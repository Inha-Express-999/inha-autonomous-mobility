"""PC LiDAR trial: continuous sensor-only braking; RRT remains a non-executable candidate.

Assumptions (not measured guarantees): 2 m/s짼 braking, .3 s reaction,
.5 m margin, 18 km/h ceiling. Unknown/dynamic returns close to the path
receive extra clearance; neither target transforms nor actual velocities enter.
"""
from dataclasses import dataclass
from math import cos, sin, hypot, sqrt

from campus_sim.map_coordinates import projection
from campus_sim.safety import crossing_ttc, stopping_distance


@dataclass(frozen=True)
class LidarDecision:
    stop: bool
    reason: str
    speed_cap_mps: float = 0.


def evaluate_lidar(vehicle_id, vehicle, ego, now):
    frame = ego.lidar
    if frame is None or not frame.valid or frame.tick != ego.tick:
        vehicle.lidar_blocked = True
        vehicle.lidar_clear_frames = 0
        return LidarDecision(True, "LiDAR invalid/incomplete observation")
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
    # Check the measured vehicle envelope before lane/turn control. During a turn,
    # all nearby directions matter, including reversing into a return.
    for hit in frame.hits:
        x, y = hit.lateral_m, hit.forward_m
        kind_margin = .2 if hit.entity_class == "static" else .8
        width = ego.half_width_m + kind_margin
        front_distance = y - body_front
        rear_distance = -y - body_rear
        near_body = abs(x) <= width and -body_rear - .5 <= y <= body_front + .5
        turn_risk = vehicle.turn is not None and hypot(x, y) <= max(body_front, body_rear) + stopping + kind_margin
        in_forward_sweep = abs(x) <= width and y >= 0
        in_reverse_sweep = abs(x) <= width and y < 0 and ego.speed_mps < -.01
        distance = front_distance if in_forward_sweep else rear_distance
        # A straight constant-relative-velocity TTC is an advisory lower-level check.
        # Dynamic motion is unknown; the expanded envelope never claims prediction.
        ttc = crossing_ttc((x, y), (0., -ego.speed_mps), max(body_front, body_rear) + kind_margin)
        if near_body or turn_risk or ((in_forward_sweep or in_reverse_sweep) and (distance <= stopping or ttc <= 2.)):
            risk = True
            break
        if in_forward_sweep or in_reverse_sweep:
            clearance = min(clearance, max(0., distance))
        # Also check the upcoming curved route, using the original route corridor.
        world = (ego.x + x*cos(ego.heading_rad) + y*sin(ego.heading_rad),
                 ego.z - x*sin(ego.heading_rad) + y*cos(ego.heading_rad))
        for a, b, walked, length in upcoming:
            error, ratio, _ = projection(world, a, b)
            ahead = walked + ratio*length - vehicle.progress_m
            if 0 <= ahead <= frame.max_range_m and error <= width:
                clearance = min(clearance, max(0., ahead - body_front))
                if ahead - body_front <= stopping:
                    risk = True
                    break
        if risk:
            break
    if risk:
        vehicle.lidar_blocked = True
        vehicle.lidar_clear_frames = 0
        if now - vehicle.lidar_rrt_at_s >= 1. and speed < .1:
            vehicle.lidar_rrt_at_s = now
            vehicle.lidar_rrt_status = rrt_candidate(vehicle_id, vehicle, ego, frame.hits)
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
