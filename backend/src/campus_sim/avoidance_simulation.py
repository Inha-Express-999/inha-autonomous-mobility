"""Isolated synthetic sensor/kinematic experiment, never campus vehicle control.

World state only generates fixture observations and evaluates swept collisions.
The controller consumes observations, a static RRT path and ego localization.
"""

from math import atan2, cos, dist, hypot, isfinite, pi, sin, sqrt

from campus_sim.avoidance_demo import AvoidancePreview
from campus_sim.local_planning import ConvexCorridor, plan_rrt, segment_distance
from campus_sim.safety import assess_safety
from campus_sim.sensors import Detection, ObservationContext, SensorObservation


def simulate_avoidance(command: AvoidancePreview) -> dict:
    dt, duration, radius, margin = 0.05, 30.0, 0.5, 0.6
    corridor = ConvexCorridor(((-4, -2), (4, -2), (4, 14), (-4, 14)))
    position, heading, speed = (
        (0.0, 0.0),
        0.0,
        command.speed_mps if command.mode == "crossing" else 0.0,
    )
    obstacle_velocity = (-1.2, 0.0) if command.mode == "crossing" else (0.0, 0.0)

    def observations(tick, obstacle, ego, yaw, ego_speed):
        dx, dy = obstacle[0] - ego[0], obstacle[1] - ego[1]
        local = (dx * cos(yaw) - dy * sin(yaw), dx * sin(yaw) + dy * cos(yaw))
        vx, vy = (
            obstacle_velocity[0] - ego_speed * sin(yaw),
            obstacle_velocity[1] - ego_speed * cos(yaw),
        )
        relative = (vx * cos(yaw) - vy * sin(yaw), vx * sin(yaw) + vy * cos(yaw))
        detection = Detection(
            position_m=local,
            range_m=hypot(*local),
            radius_m=command.radius,
            entity_class="static" if command.mode == "static" else "pedestrian",
            relative_velocity_mps=relative,
        )
        context = ObservationContext(
            vehicle_id="LAB",
            sensors={"lidar": "LIDAR", "radar": "RADAR"},
            required_sensor_ids=frozenset({"lidar", "radar"}),
            current_tick=tick + 10,
            pose_version=tick + 10,
            tick_duration_s=dt,
        )
        frames = tuple(
            SensorObservation(
                vehicle_id="LAB",
                sensor_id=name,
                sensor_type=kind,
                observed_tick=tick + 2 if command.stale else tick + 10,
                pose_version=tick + 10,
                valid=True,
                detections=(detection,) if hypot(*local) <= 20 and name == "lidar" else (),
            )
            for name, kind in (("lidar", "LIDAR"), ("radar", "RADAR"))
        )
        return frames, context

    initial_obstacle = (command.x, command.y)
    frames, context = observations(0, initial_obstacle, position, heading, 0)
    rrt = plan_rrt(
        position,
        (0, 12),
        corridor,
        frames,
        context,
        ego_radius_m=radius,
        margin_m=margin,
        seed=command.seed,
    )
    raw = rrt.path
    path = []
    if raw:
        # Shortcut only complete collision-free segments, not arbitrary corners.
        index = 0
        path = [raw[0]]
        while index < len(raw) - 1:
            next_index = index + 1
            for candidate in range(len(raw) - 1, index, -1):
                if (
                    segment_distance(initial_obstacle, raw[index], raw[candidate])
                    > radius + margin + command.radius
                ):
                    next_index = candidate
                    break
            index = next_index
            path.append(raw[index])
    elif command.mode == "crossing":
        path = [(0, 0), (0, 12)]
    frames_out = []
    waypoint = 1
    clear_s, arrived, collision = 0.0, False, False
    minimum_clearance = float("inf")
    for tick in range(int(duration / dt) + 1):
        t = tick * dt
        obstacle = (command.x + obstacle_velocity[0] * t, command.y + obstacle_velocity[1] * t)
        frames, context = observations(tick, obstacle, position, heading, speed)
        safety = assess_safety(frames, context, speed_mps=speed, ego_radius_m=radius)
        # A stopped vehicle checks intended forward motion before restarting.
        prospective_frames, _ = observations(tick, obstacle, position, heading, command.speed_mps)
        prospective = assess_safety(
            prospective_frames, context, speed_mps=command.speed_mps, ego_radius_m=radius
        )
        hazard = safety.action == "STOP" or (
            command.mode == "crossing" and prospective.action == "STOP"
        )
        clear_s = 0 if hazard else clear_s + dt
        goal_distance = dist(position, (0, 12))
        if path:
            while waypoint < len(path) - 1 and dist(position, path[waypoint]) < 0.3:
                waypoint += 1
            target = path[waypoint]
            desired = atan2(target[0] - position[0], target[1] - position[1])
            error = (desired - heading + pi) % (2 * pi) - pi
            omega = max(-1.2, min(1.2, error * 2.5))
            wanted = min(command.speed_mps, sqrt(2 * 1.5 * max(0, goal_distance - 0.15)))
            wanted *= max(0, cos(error)) ** 2
            if abs(error) > 0.3:
                wanted = min(wanted, 0.65)
        else:
            wanted, omega = 0.0, 0.0
        if hazard or clear_s < 1 or arrived or collision:
            wanted = 0.0
        elif safety.action == "YIELD":
            wanted = min(wanted, 0.8)
        if goal_distance < 0.25 and speed < 0.15:
            arrived = True
            wanted = 0.0
        action = (
            "ARRIVED"
            if arrived
            else (
                "COLLISION"
                if collision
                else "BRAKING"
                if hazard and speed > 0.05
                else "WAITING"
                if wanted == 0
                else "DRIVING"
            )
        )
        clearance = dist(position, obstacle) - radius - command.radius
        minimum_clearance = min(minimum_clearance, clearance)
        ttc = safety.minimum_ttc_s
        frames_out.append(
            {
                "time_s": t,
                "position": position,
                "heading_rad": heading,
                "speed_mps": speed,
                "obstacle": obstacle,
                "action": action,
                "safety": safety.action,
                "ttc_s": ttc if isfinite(ttc) else None,
                "restart_ttc_s": prospective.minimum_ttc_s
                if isfinite(prospective.minimum_ttc_s)
                else None,
                "stopping_distance_m": safety.stopping_distance_m,
                "clearance_m": clearance,
                "sensor_valid": not command.stale,
            }
        )
        if tick == int(duration / dt):
            break
        # Bounded acceleration/deceleration; no instantaneous velocity reset.
        next_speed = speed + max(-3 * dt, min(1 * dt, wanted - speed))
        next_heading = heading + omega * dt
        travelled = (speed + next_speed) / 2 * dt
        next_position = (
            position[0] + travelled * sin((heading + next_heading) / 2),
            position[1] + travelled * cos((heading + next_heading) / 2),
        )
        next_obstacle = (
            obstacle[0] + obstacle_velocity[0] * dt,
            obstacle[1] + obstacle_velocity[1] * dt,
        )
        relative_a = (position[0] - obstacle[0], position[1] - obstacle[1])
        relative_b = (next_position[0] - next_obstacle[0], next_position[1] - next_obstacle[1])
        swept_clearance = segment_distance((0, 0), relative_a, relative_b) - radius - command.radius
        minimum_clearance = min(minimum_clearance, swept_clearance)
        if swept_clearance <= 0 or not corridor.contains(next_position, radius):
            collision = True
        position, heading, speed = next_position, next_heading, next_speed
        if collision or (arrived and speed < 1e-6):
            # Include the final integrated state, including any collision failure.
            frames_out.append(
                {
                    **frames_out[-1],
                    "time_s": t + dt,
                    "position": position,
                    "obstacle": next_obstacle,
                    "speed_mps": speed,
                    "clearance_m": dist(position, next_obstacle) - radius - command.radius,
                    "action": "COLLISION" if collision else "ARRIVED",
                }
            )
            break
    return {
        "source": "synthetic_sensor_kinematic_experiment",
        "simulation_only": True,
        "unity_connected": False,
        "dt_s": dt,
        "corridor": corridor.vertices,
        "goal": (0, 12),
        "path": path,
        "tree_edges": rrt.tree_edges,
        "rrt_status": rrt.status,
        "rrt_iterations": rrt.iterations,
        "radius_m": radius,
        "obstacle_radius_m": command.radius,
        "margin_m": margin,
        "obstacle_velocity_mps": obstacle_velocity,
        "desired_speed_mps": command.speed_mps,
        "frames": frames_out,
        "arrived": arrived,
        "collision": collision,
        "minimum_clearance_m": minimum_clearance,
        "duration_s": frames_out[-1]["time_s"],
    }
