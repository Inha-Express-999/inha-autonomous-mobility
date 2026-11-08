"""PC LiDAR trial: continuous sensor-only braking; RRT remains a non-executable candidate.

Protocol 6 sends a raw range image; obstacle returns come only from the
geometry-only ground segmentation in ``lidar_ground`` (no labels or tags).
Protocol 5 hits were already ground-filtered/labelled in Unity and remain
accepted for compatibility.

Assumptions (not measured guarantees): 2 m/s짼 braking, .3 s reaction,
.5 m margin, 18 km/h ceiling. Unknown/dynamic returns close to the path
receive extra clearance; neither target transforms nor actual velocities enter.
"""
from dataclasses import dataclass
from math import cos, sin, hypot, sqrt

from campus_sim.lidar_ground import segment_scan
from campus_sim.map_coordinates import projection
from campus_sim.safety import crossing_ttc, stopping_distance


@dataclass(frozen=True)
class LidarDecision:
    stop: bool
    reason: str
    speed_cap_mps: float = 0.


# Assumption: a return lower than this above the local ground cannot be a
# standing person or a vehicle body (kerbs, wheel stops). Geometry only.
LOW_STRUCTURE_HEIGHT_M = .4


def clearance_margin(hit):
    height = getattr(hit, "height_m", None)
    if height is not None:
        return .2 if height < LOW_STRUCTURE_HEIGHT_M else .8
    return .2 if hit.entity_class == "static" else .8


def turn_sweep_blocked(turn, ego, world, margin):
    """Whether a world point meets the remaining swept body of the current turn arc.

    Returns None when the turn exposes no planned arcs (caller stays conservative).
    """
    controller = getattr(turn, "controller", None)
    center, heading = getattr(turn, "center", None), getattr(turn, "heading", None)
    arcs = getattr(controller, "arcs", None)
    if center is None or heading is None or not arcs:
        return None
    index = getattr(controller, "arc_index", 0)
    if index >= len(arcs):
        return False
    dx, dz = world[0] - center[0], world[1] - center[1]
    px, pz = dx*cos(heading) - dz*sin(heading), dx*sin(heading) + dz*cos(heading)
    half_width = ego.half_width_m + margin
    half_length = ego.half_length_m + margin
    reach = hypot(half_width, half_length)
    for sample in arcs[index][max(0, getattr(controller, "sample_index", 0) - 2):]:
        rx, rz = px - sample.x, pz - sample.z
        if abs(rx) > reach or abs(rz) > reach:
            continue
        c, s_ = cos(sample.heading_rad), sin(sample.heading_rad)
        if abs(rx*c - rz*s_) <= half_width and abs(rx*s_ + rz*c) <= half_length:
            return True
    return False


def obstacle_hits(frame):
    """Obstacle returns in the ego frame (lateral_m, forward_m, entity_class)."""
    if hasattr(frame, "ranges_mm"):
        return segment_scan(frame).obstacles
    return frame.hits


def evaluate_lidar(vehicle_id, vehicle, ego, now):
    frame = ego.lidar
    if frame is None or not frame.valid or frame.tick != ego.tick:
        vehicle.lidar_blocked = True
        vehicle.lidar_clear_frames = 0
        return LidarDecision(True, "LiDAR invalid/incomplete observation")
    hits = obstacle_hits(frame)
    speed = abs(ego.speed_mps)
    body_front = ego.rear_axle_offset_m + ego.half_length_m
    body_rear = ego.half_length_m - ego.rear_axle_offset_m
    stopping = stopping_distance(speed, .3, 2., .5)
    clearance = max(0., frame.max_range_m - max(body_front, body_rear))
    risk = False
    upcoming = []
    walked = 0.
    for a, b in zip(vehicle.route_unity, vehicle.route_unity[1:]):
        length = hypot(b[0]-a[0], b[1]-a[1])
        if walked > vehicle.progress_m + frame.max_range_m + max(body_front, body_rear):
            break
        if walked + length >= vehicle.progress_m - .5:
            upcoming.append((a,b,walked,length))
        walked += length
    # With a route ahead, the vehicle follows the route, not its momentary heading:
    # a straight sweep only holds over the reaction distance, after which the
    # corridor along the route (widened by the measured lane error) decides. On a
    # junction turn this keeps the outside kerb from reading as "straight ahead".
    follows_route = vehicle.turn is None and bool(upcoming)
    reaction = max(.5, speed * .3)
    route_horizon = max(stopping, 2. * speed)  # Same 2 s horizon as the straight TTC.
    lane_error = min(.5, max(0., getattr(vehicle, "lane_error_m", 0.) or 0.))
    # Check the measured vehicle envelope before lane/turn control. During a turn
    # the remaining swept body of the planned arc is checked in every direction
    # (forward and reverse); a turn without arc geometry keeps the radius check.
    for hit in hits:
        x, y = hit.lateral_m, hit.forward_m
        kind_margin = clearance_margin(hit)
        width = ego.half_width_m + kind_margin
        front_distance = y - body_front
        rear_distance = -y - body_rear
        near_body = abs(x) <= width and -body_rear - .5 <= y <= body_front + .5
        world = (ego.x + x*cos(ego.heading_rad) + y*sin(ego.heading_rad),
                 ego.z - x*sin(ego.heading_rad) + y*cos(ego.heading_rad))
        turn_risk = False
        if vehicle.turn is not None:
            swept = turn_sweep_blocked(vehicle.turn, ego, world, kind_margin)
            turn_risk = (hypot(x, y) <= max(body_front, body_rear) + stopping + kind_margin) if swept is None else swept
        in_forward_sweep = abs(x) <= width and y >= 0 and (not follows_route or front_distance <= reaction)
        in_reverse_sweep = abs(x) <= width and y < 0 and ego.speed_mps < -.01
        distance = front_distance if in_forward_sweep else rear_distance
        # A straight constant-relative-velocity TTC is an advisory lower-level check.
        # Dynamic motion is unknown; the expanded envelope never claims prediction.
        ttc = crossing_ttc((x, y), (0., -ego.speed_mps), max(body_front, body_rear) + kind_margin)             if not follows_route or not in_forward_sweep else float("inf")
        if near_body or turn_risk or ((in_forward_sweep or in_reverse_sweep) and (distance <= stopping or ttc <= 2.)):
            risk = True
            break
        if in_forward_sweep or in_reverse_sweep:
            clearance = min(clearance, max(0., distance))
        # Also check the upcoming curved route, using the original route corridor.
        # While turning, the onward route starts at the post-turn pose; its
        # first metres are covered by the swept turn check instead.
        for a, b, walked, length in (() if vehicle.turn is not None else upcoming):
            error, ratio, _ = projection(world, a, b)
            ahead = walked + ratio*length - vehicle.progress_m
            if 0 <= ahead <= frame.max_range_m and error <= width + lane_error:
                clearance = min(clearance, max(0., ahead - body_front))
                # Keep the straight TTC's standstill keep-out (body radius + margin)
                # so a stopped vehicle never creeps toward a return on its route.
                if hypot(x, y) <= max(body_front, body_rear) + kind_margin or ahead - body_front <= route_horizon:
                    risk = True
                    break
        if risk:
            break
    if risk:
        vehicle.lidar_blocked = True
        vehicle.lidar_clear_frames = 0
        if now - vehicle.lidar_rrt_at_s >= 1. and speed < .1:
            vehicle.lidar_rrt_at_s = now
            vehicle.lidar_rrt_status = rrt_candidate(vehicle_id, vehicle, ego, hits)
        return LidarDecision(True, "LiDAR TTC/stopping-distance risk; brake before RRT / " + vehicle.lidar_rrt_status)
    if vehicle.lidar_blocked:
        vehicle.lidar_clear_frames = vehicle.lidar_clear_frames + 1 if clearance > 1. else 0
        if vehicle.lidar_clear_frames < 5:
            return LidarDecision(True, "LiDAR waiting for five clear scans")
        vehicle.lidar_blocked = False
        vehicle.lidar_rrt_status = "NOT_REQUESTED"
    # Even a clear 15 m scan cannot authorize the previous 40 km/h setting.
    # Conservative available-distance bound includes reaction time and body offset.
    available = max(0., clearance - .5)
    cap = max(0., - .6 + sqrt(.36 + 4*available))
    return LidarDecision(False, "LiDAR observed corridor clear", min(5., cap))


def rrt_candidate(vehicle_id, vehicle, ego, hits):
    """Never authorize control from unobserved space or a geometric RRT path."""
    if not hits or any(h.entity_class != "static" for h in hits):
        return "WAIT_DYNAMIC_VALIDATION"
    # Lane width alone cannot establish a convex corridor at curves/junctions.
    # Only a straight, aligned, bounded segment may be submitted to static RRT.
    width = vehicle.lane_width_m
    if width is None:
        return "WAIT_VALIDATED_CORRIDOR"
    from campus_sim.local_planning import ConvexCorridor, plan_rrt
    from campus_sim.sensors import Detection, SensorObservation, ObservationContext
    forward = (sin(ego.heading_rad), cos(ego.heading_rad))
    segment = next(((a,b) for a,b in zip(vehicle.route_unity, vehicle.route_unity[1:])
                    if projection((ego.x,ego.z),a,b)[0] < .2 and
                    hypot(b[0]-a[0],b[1]-a[1]) >= 10 and
                    ((b[0]-a[0])*forward[0]+(b[1]-a[1])*forward[1]) / hypot(b[0]-a[0],b[1]-a[1]) > .999), None)
    if segment is None:
        return "WAIT_VALIDATED_CORRIDOR"
    # Experimental corridor is a planning preview only, not a drivable authorization.
    radius = max(ego.half_width_m, ego.half_length_m)
    corridor = ConvexCorridor(((-width/2,-3.),(width/2,-3.),(width/2,12.),(-width/2,12.)))
    detections = tuple(Detection(position_m=(h.lateral_m,h.forward_m),
        range_m=hypot(h.lateral_m,h.forward_m),radius_m=.2,entity_class="static") for h in hits)
    frame = SensorObservation(vehicle_id=vehicle_id,sensor_id="pc-lidar",sensor_type="LIDAR",
        observed_tick=ego.tick,pose_version=ego.tick,valid=True,detections=detections)
    context = ObservationContext(vehicle_id=vehicle_id,sensors={"pc-lidar":"LIDAR"},
        required_sensor_ids=frozenset({"pc-lidar"}),current_tick=ego.tick,pose_version=ego.tick,tick_duration_s=.1)
    result = plan_rrt((0.,0.),(0.,8.),corridor,(frame,),context,ego_radius_m=radius,
        max_iterations=200,budget_s=.01,seed=ego.tick)
    return result.status + "_NON_EXECUTABLE"
