"""Sensor-only swept-body queries over the already authorised static route.

No obstacle identities, transforms or true dynamic velocities are used. Samples
are padded for translation/heading changes; this is not a new avoidance planner.
"""
from math import atan2, ceil, cos, hypot, sin, tan

from campus_sim.map_coordinates import projection


def _pose(x, z, heading, distance, padding=0.):
    return x, z, sin(heading), cos(heading), distance, padding


def body_extents(ego):
    """Root-relative Collider measurements; older clients retain the conservative bound."""
    if getattr(ego, "body_front_m", None) is not None and getattr(ego, "body_rear_m", None) is not None:
        return ego.body_front_m, ego.body_rear_m
    return ego.rear_axle_offset_m + ego.half_length_m, ego.half_length_m - ego.rear_axle_offset_m


def _append_rotation(poses, x, z, previous_heading, heading, distance, radius):
    # Cover a corner's intermediate body orientations instead of inflating the
    # entire rectangle by a bounding circle of the whole heading change.
    delta = atan2(sin(heading - previous_heading), cos(heading - previous_heading))
    count = max(1, ceil(abs(delta) / .087))  # <= five degrees between poses
    padding = .1 + radius * abs(sin(delta / (2 * count)))
    for i in range(1, count + 1):
        poses.append(_pose(x, z, previous_heading + delta * i / count, distance, padding))


def motion_sweep(vehicle, ego, horizon):
    """Return root poses in increasing travel distance, or None for unknown turns."""
    poses = [_pose(ego.x, ego.z, ego.heading_rad, 0.)]
    radius = hypot(ego.half_width_m, max(body_extents(ego)))
    if vehicle.turn is not None:
        turn = vehicle.turn
        if not hasattr(turn, "controller"):
            return None  # Legacy/test turn: preserve the previous fail-closed circle.
        controller = turn.controller
        if controller.arc_index >= len(controller.arcs):
            return poses
        arc = controller.arcs[controller.arc_index]
        rear = (ego.x + ego.rear_axle_offset_m * sin(ego.heading_rad),
                ego.z + ego.rear_axle_offset_m * cos(ego.heading_rad))
        dx, dz = rear[0] - turn.center[0], rear[1] - turn.center[1]
        local = (dx * cos(turn.heading) - dz * sin(turn.heading),
                 dx * sin(turn.heading) + dz * cos(turn.heading))
        indices = range(max(0, controller.sample_index - 2), min(len(arc), controller.sample_index + 30))
        nearest = min(indices, key=lambda i: hypot(arc[i].x - local[0], arc[i].z - local[1]))
        walked, emitted, previous_heading = 0., -.2, ego.heading_rad
        previous = arc[nearest]
        for i in range(nearest, len(arc)):
            sample = arc[i]
            walked += hypot(sample.x - previous.x, sample.z - previous.z)
            previous = sample
            if walked > horizon:
                break
            if walked - emitted < .2 and i != len(arc) - 1:
                continue
            heading = turn.heading + sample.heading_rad
            rx = turn.center[0] + sample.x * cos(turn.heading) + sample.z * sin(turn.heading)
            rz = turn.center[1] - sample.x * sin(turn.heading) + sample.z * cos(turn.heading)
            _append_rotation(poses, rx - ego.rear_axle_offset_m * sin(heading),
                             rz - ego.rear_axle_offset_m * cos(heading), previous_heading, heading, walked, radius)
            emitted, previous_heading = walked, heading
        return poses

    route = vehicle.route_unity
    if not route or ego.speed_mps < -.01:
        direction = -1 if ego.speed_mps < -.01 else 1
        for i in range(1, ceil(horizon / .2) + 1):
            d = min(horizon, i * .2)
            poses.append(_pose(ego.x + direction * d * sin(ego.heading_rad),
                               ego.z + direction * d * cos(ego.heading_rad), ego.heading_rad, d, .1))
        return poses

    # The caller updates route progress after safety. Reproject only locally,
    # so a loop/intersection cannot jump to another lap or use last tick's pose.
    walked, candidates = 0., []
    for a, b in zip(route, route[1:]):
        length = hypot(b[0] - a[0], b[1] - a[1])
        error, ratio, _ = projection((ego.x, ego.z), a, b)
        along = walked + ratio * length
        if vehicle.progress_m - 1 <= along <= vehicle.progress_m + 5:
            candidates.append((error, along))
        walked += length
    start = min(candidates)[1] if candidates else vehicle.progress_m
    if getattr(ego, "steering_angle_rad", None) is not None and getattr(ego, "steering_rate_rad_s", 0.) > 0:
        return _tracking_sweep(vehicle, ego, start, horizon, radius)
    walked, previous_heading = 0., ego.heading_rad
    for a, b in zip(route, route[1:]):
        length = hypot(b[0] - a[0], b[1] - a[1])
        if length < 1e-6:
            continue
        low, high = max(start, walked), min(start + horizon, walked + length)
        if high >= low:
            heading = atan2(b[0] - a[0], b[1] - a[1])
            count = max(1, ceil((high - low) / .2))
            for i in range(count + 1):
                along = low + (high - low) * i / count
                ratio = (along - walked) / length
                x, z = a[0] + ratio * (b[0] - a[0]), a[1] + ratio * (b[1] - a[1])
                if i == 0:
                    _append_rotation(poses, x, z, previous_heading, heading, max(0., along - start), radius)
                else:
                    poses.append(_pose(x, z, heading, max(0., along - start), .1))
            previous_heading = heading
        walked += length
        if walked > start + horizon:
            break
    return poses


def _tracking_sweep(vehicle, ego, start, horizon, radius):
    """Forecast the existing pursuit command with measured ego steering/rate.

    Waypoint tangent changes are not in-place rotations of the vehicle. This
    short bicycle rollout uses the same pursuit target and curvature preview
    as unity_drive, with root/rear-axle conversion and bounded steering slew.
    """
    segments, events, walked = [], [], 0.
    route = vehicle.route_unity
    cruise = getattr(vehicle, "cruise_speed_mps", 5.)
    speed = abs(ego.speed_mps)
    preview = max(cruise * 2.5, speed * 2.5, speed * speed / 4 + 5)
    fast = getattr(vehicle, "driving_profile", "campus_lidar") in {"campus_fast_no_obstacles", "campus_lidar"}
    for i, (a, b) in enumerate(zip(route, route[1:])):
        length = hypot(b[0] - a[0], b[1] - a[1])
        if length > 1e-6 and walked + length >= start - 1 and walked <= start + horizon + preview + 8:
            segments.append((a, b, walked, length))
        walked += length
        if i + 2 < len(route):
            c = route[i + 2]
            denominator = length * hypot(c[0] - b[0], c[1] - b[1]) * hypot(c[0] - a[0], c[1] - a[1])
            curvature = 2 * abs((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])) / denominator if denominator > 1e-6 else 0.
            if curvature > (.001 if fast else .03):
                events.append(walked)
    if not segments:
        return [_pose(ego.x, ego.z, ego.heading_rad, 0.)]
    length = walked
    poses = [_pose(ego.x, ego.z, ego.heading_rad, 0.)]
    x, z, heading, steering, progress = ego.x, ego.z, ego.heading_rad, ego.steering_angle_rad, start
    rear_x = x + ego.rear_axle_offset_m * sin(heading)
    rear_z = z + ego.rear_axle_offset_m * cos(heading)
    distance = 0.
    while distance < horizon:
        candidates = []
        for a, b, along, segment_length in segments:
            error, ratio, _ = projection((x, z), a, b)
            candidate = along + ratio * segment_length
            if progress - 1 <= candidate <= progress + 2:
                candidates.append((error, candidate))
        if candidates:
            progress = max(progress, min(candidates)[1])
        if length - progress <= .65:
            break  # Arrival braking; never invent travel past the mission goal.
        curved = any(progress - 1 <= event <= progress + preview for event in events)
        lookahead = 2.5 if curved else max(3., min(8., 2 + speed * .9))
        target_distance = min(length, progress + lookahead)
        target = route[-1]
        for a, b, along, segment_length in segments:
            if along + segment_length >= target_distance:
                ratio = max(0., min(1., (target_distance - along) / segment_length))
                target = (a[0] + ratio * (b[0] - a[0]), a[1] + ratio * (b[1] - a[1]))
                break
        dx, dz = target[0] - x, target[1] - z
        lateral, forward = dx * cos(heading) - dz * sin(heading), dx * sin(heading) + dz * cos(heading)
        if forward < -.2:
            break  # The actual controller brakes OFF_ROUTE for this condition.
        desired = atan2(2 * ego.wheelbase_m * lateral, max(dx * dx + dz * dz, .1))
        desired = max(-ego.max_steering_rad, min(ego.max_steering_rad, desired))
        step = min(.2, horizon - distance)
        slew = ego.steering_rate_rad_s * step / max(speed, .55)
        steering += max(-slew, min(slew, desired - steering))
        curvature = tan(steering) / ego.wheelbase_m
        new_heading = heading + curvature * step
        if abs(curvature) > 1e-8:
            rear_x += (cos(heading) - cos(new_heading)) / curvature
            rear_z += (sin(new_heading) - sin(heading)) / curvature
        else:
            rear_x += step * sin(heading)
            rear_z += step * cos(heading)
        x, z = rear_x - ego.rear_axle_offset_m * sin(new_heading), rear_z - ego.rear_axle_offset_m * cos(new_heading)
        distance += step
        padding = .1 + radius * abs(sin((new_heading - heading) / 2))
        poses.append(_pose(x, z, new_heading, distance, padding))
        heading = new_heading
    return poses


def collision_distance(world, poses, half_width, front, rear, margin):
    """First sampled body overlap along travel; None means outside this sweep."""
    reach = hypot(half_width + margin, max(front, rear) + margin) + 1.
    for px, pz, sh, ch, distance, padding in poses:
        dx, dz = world[0] - px, world[1] - pz
        if abs(dx) > reach + padding or abs(dz) > reach + padding:
            continue
        lateral, forward = dx * ch - dz * sh, dx * sh + dz * ch
        # Euclidean clearance around a rectangle has rounded corners. Expanding
        # both axes and then taking the full corner invents extra diagonal risk.
        lateral_gap = max(0., abs(lateral) - half_width)
        longitudinal_gap = max(0., forward - front, -rear - forward)
        if lateral_gap * lateral_gap + longitudinal_gap * longitudinal_gap <= (margin + padding) ** 2:
            return max(0., distance - .1)
    return None
