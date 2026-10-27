"""Prepare a low-speed turnaround before an otherwise unavailable lane route."""
from dataclasses import dataclass, replace
import math

from campus_sim.turnaround import TurnController


@dataclass
class PhysicalTurn:
    controller: TurnController
    center: tuple
    heading: float

    def update(self, ego):
        rear=(ego.x+ego.rear_axle_offset_m*math.sin(ego.heading_rad),
              ego.z+ego.rear_axle_offset_m*math.cos(ego.heading_rad))
        dx,dz=rear[0]-self.center[0],rear[1]-self.center[1]
        x=dx*math.cos(self.heading)-dz*math.sin(self.heading)
        z=dx*math.sin(self.heading)+dz*math.cos(self.heading)
        return self.controller.update(x,z,ego.heading_rad-self.heading,ego.speed_mps)


def prepare_turn(session, vehicle_id, command):
    """Plan within a collinear authored road strip, then prove onward routing.

    Never extends a road or changes the observed pose. The temporary endpoint
    pose is used only to plan the route after the manoeuvre.
    """
    vehicle=session.vehicles[vehicle_id]
    ego=vehicle.ego
    if ego.schema_version != 4 or command.driving_profile != 'campus_fast_no_obstacles':
        return False
    profiles=session.lanes.by_edge
    coordinates=session.coordinates
    segments=[]
    for edge in session.session.service.graph.edges:
        if edge.id not in profiles or not session.lanes.feasible(edge,ego.half_width_m):
            continue
        points=[coordinates.to_unity(*p) for p in edge.polyline]
        for a,b in zip(points,points[1:]):
            length=math.dist(a,b)
            if length>.1:
                segments.append((a,b,((b[0]-a[0])/length,(b[1]-a[1])/length),
                                 profiles[edge.id]['authored_road_width_m']))
    rear=(ego.x+ego.rear_axle_offset_m*math.sin(ego.heading_rad),
          ego.z+ego.rear_axle_offset_m*math.cos(ego.heading_rad))
    for a,b,direction,width in segments:
        heading=math.atan2(direction[0],direction[1])
        if abs(math.atan2(math.sin(heading-ego.heading_rad),math.cos(heading-ego.heading_rad)))>.05:
            continue
        right=(direction[1],-direction[0])
        lateral=sum((rear[j]-a[j])*right[j] for j in (0,1))
        if not .1<lateral<width/2-ego.half_width_m-.2:
            continue
        center=tuple(rear[j]-lateral*right[j] for j in (0,1))
        intervals=[]
        for start,end,d,w in segments:
            if w+1e-6<width or sum(d[j]*direction[j] for j in (0,1))<.9995:
                continue
            if max(abs(sum((p[j]-center[j])*right[j] for j in (0,1))) for p in (start,end))>.05:
                continue
            intervals.append(tuple(sorted(sum((p[j]-center[j])*direction[j] for j in (0,1)) for p in (start,end))))
        merged=[]
        for low,high in sorted(intervals):
            if merged and low<=merged[-1][1]+.01:
                merged[-1]=(merged[-1][0],max(high,merged[-1][1]))
            else:
                merged.append((low,high))
        space=next(((low,high) for low,high in merged if low<0<high),None)
        if space is None:
            continue
        controller=None
        # Prefer steering reserve over an almost full-lock manoeuvre. The larger
        # radius also lengthens the short reverse arc, allowing pose correction.
        # Every option must still pass the full swept-body corridor check.
        for radius_factor in (2., 1.5, 1.35, 1.15):
            try:
                controller=TurnController(wheelbase_m=ego.wheelbase_m,max_steering_rad=ego.max_steering_rad,
                    lane_offset_m=lateral,road_half_width_m=width/2-.05,
                    forward_space_m=space[1],rear_space_m=-space[0],
                    minimum_radius_m=radius_factor*ego.wheelbase_m/math.tan(ego.max_steering_rad),
                    half_width_m=ego.half_width_m,half_length_m=ego.half_length_m)
                break
            except ValueError:
                pass
        if controller is None:
            continue
        final_rear=tuple(center[j]-lateral*right[j] for j in (0,1))
        final_root=tuple(final_rear[j]+ego.rear_axle_offset_m*direction[j] for j in (0,1))
        predicted=ego.model_copy(update=dict(x=final_root[0],z=final_root[1],heading_rad=heading-math.pi,speed_mps=0.))
        planned=replace(vehicle,ego=predicted,last_command_id=None,turn=None)
        session.vehicles[vehicle_id]=planned
        try:
            session.mission(vehicle_id,command,_allow_turn=False,_preview=True)
        except ValueError:
            continue
        finally:
            session.vehicles[vehicle_id]=vehicle
        # Keep measured localization/owner/watchdog state; adopt only mission data.
        retained={name:getattr(vehicle,name) for name in ('ego','token','tick','received_s','connected')}
        vehicle.__dict__.update(planned.__dict__)
        vehicle.__dict__.update(retained)
        vehicle.turn=PhysicalTurn(controller,center,heading)
        vehicle.status='MANEUVER'
        vehicle.reason='Three-point turn before onward lane route'
        return True
    return False


def prefer_shorter_turn(session, vehicle_id, command):
    """Compare a feasible turn against the forward mission without moving ego.

    Use cruise-time distance and explicitly pay for low-speed manoeuvring plus
    six seconds for stops/acceleration. This is a selection cost, not an ETA.
    """
    vehicle = session.vehicles[vehicle_id]
    if vehicle.ego.schema_version != 4 or command.driving_profile != 'campus_fast_no_obstacles':
        return False
    candidate = replace(vehicle, last_command_id=None, turn=None)
    session.vehicles[vehicle_id] = candidate
    try:
        if not prepare_turn(session, vehicle_id, command):
            return False
    finally:
        session.vehicles[vehicle_id] = vehicle
    distance = lambda points: sum(math.dist(a,b) for a,b in zip(points,points[1:]))
    turn_distance = sum(sum(math.hypot(b.x-a.x,b.z-a.z) for a,b in zip(arc,arc[1:]))
                        for arc in candidate.turn.controller.arcs)
    cruise = command.cruise_speed_kmh / 3.6
    forward_cost = distance(vehicle.route_unity) / cruise
    turn_cost = distance(candidate.route_unity) / cruise + turn_distance / .55 + 6.
    if turn_cost + 1. >= forward_cost:
        return False
    vehicle.__dict__.update(candidate.__dict__)
    vehicle.reason = 'Shorter lane route including low-speed turnaround cost'
    return True
