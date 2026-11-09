"""PC LiDAR trial: continuous sensor-only braking; RRT remains a non-executable candidate.

Assumptions (not measured guarantees): 2 m/s짼 braking, .3 s reaction,
.5 m margin, 18 km/h ceiling. Unknown/dynamic returns close to the path
receive extra clearance; neither target transforms nor actual velocities enter.
"""
from dataclasses import dataclass
from math import cos, sin, hypot, sqrt

from campus_sim.map_coordinates import projection
from campus_sim.safety import stopping_distance
from campus_sim.lidar_corridor import body_extents, collision_distance, motion_sweep


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
    # Synthetic assumptions, not measured braking guarantees.
    weather_range = {"clear": 15., "rain": 12., "snow": 8., "fog": 6.}[frame.weather]
    deceleration = {"dry": 2., "wet": 1.2, "snow": .6, "ice": 0., "unknown": 0.}[frame.road_condition]
    deceleration = min(deceleration, {"clear": 2., "rain": 1.2, "snow": .6, "fog": 2.}[frame.weather])
    if not frame.observation_complete or deceleration <= 0:
        vehicle.lidar_blocked = True
        vehicle.lidar_clear_frames = 0
        return LidarDecision(True, "LiDAR incomplete weather observation or unsupported road condition")
    environment = (frame.weather, frame.road_condition, frame.max_range_m)
    if vehicle.lidar_environment is not None and vehicle.lidar_environment != environment:
        vehicle.lidar_blocked = True
        vehicle.lidar_clear_frames = 0
    vehicle.lidar_environment = environment
    effective_range = min(frame.max_range_m, weather_range)
    reaction = .3 if frame.weather == "clear" and frame.road_condition == "dry" else .5
    margin = .5 if reaction == .3 else 1.
    speed = abs(ego.speed_mps)
    body_front, body_rear = body_extents(ego)
    stopping = stopping_distance(speed, reaction, deceleration, margin)
    clearance = max(0., effective_range - max(body_front, body_rear))
    risk = stopping > clearance
    sweep = motion_sweep(vehicle, ego, effective_range)
    # Check the measured vehicle envelope before lane/turn control. During a turn,
    # all nearby directions matter, including reversing into a return.
    for hit in frame.hits:
        x, y = hit.lateral_m, hit.forward_m
        kind_margin = .2 if hit.entity_class == "static" else .8
        width = ego.half_width_m + kind_margin
        near_body = abs(x) <= width and -body_rear - .5 <= y <= body_front + .5
        turn_risk = sweep is None and hypot(x, y) <= hypot(ego.half_width_m, max(body_front, body_rear)) + stopping + kind_margin
        # A falling ego speed is not evidence that a pedestrian has crossed.
        # Once yielding, retain the stop until observed pedestrian returns leave
        # the forward/route corridor, then require the usual five clear scans.
        pedestrian_hold = vehicle.lidar_blocked and hit.entity_class == "pedestrian"
        if pedestrian_hold and abs(x) <= width and y >= 0:
            # Crossing actors have unknown motion. Preserve the existing hold
            # until they also leave the current forward strip, even if a future
            # curve would geometrically pass them on one side.
            risk = True
            break
        # Keep the currently occupied body and the reaction-time motion envelope
        # independent of the route. A steering change cannot instantly avert these.
        reaction_gap = y - body_front if ego.speed_mps >= 0 else -y - body_rear
        reaction_risk = speed > .01 and abs(x) <= width and 0 <= reaction_gap <= speed * reaction + .1
        if near_body or turn_risk or reaction_risk:
            risk = True
            break
        # TTC is travel to first swept-body overlap on the authorised path, not
        # collision with a long circular body continuing along the current tangent.
        world = (ego.x + x*cos(ego.heading_rad) + y*sin(ego.heading_rad),
                 ego.z - x*sin(ego.heading_rad) + y*cos(ego.heading_rad))
        distance = collision_distance(world, sweep, ego.half_width_m, body_front, body_rear, kind_margin) if sweep is not None else None
        if distance is not None:
            clearance = min(clearance, distance)
            if pedestrian_hold or distance <= stopping or (speed > .01 and distance / speed <= 2.):
                risk = True
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
        # Reuse the risk decision above. A separate 1 m release threshold can
        # deadlock a stopped car with a safe, TTC-limited creeping command.
        # Unsafe returns already reset this counter in the risk branch.
        vehicle.lidar_clear_frames += 1
        if vehicle.lidar_clear_frames < 5:
            return LidarDecision(True, "LiDAR waiting for five clear scans")
        vehicle.lidar_blocked = False
        vehicle.lidar_rrt_status = "NOT_REQUESTED"
    # Even a clear 15 m scan cannot authorize the previous 40 km/h setting.
    # Conservative available-distance bound includes reaction time and body offset.
    available = max(0., clearance - margin)
    ar = deceleration * reaction
    cap = max(0., -ar + sqrt(ar * ar + 2 * deceleration * available))
    # The restart/approach cap must obey the same TTC horizon as the stop check.
    # Otherwise a stationary scan releases the brake then commands an unsafe speed.
    return LidarDecision(False, f"LiDAR observed corridor clear / {frame.weather} / {frame.road_condition}", min(5., cap, clearance / 2.25))


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
