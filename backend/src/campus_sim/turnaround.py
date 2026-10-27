"""Bounded three-point turns on a straight, authored simulation road.

This is static manoeuvre geometry, not an executable control command. Local X
points right and Z forward at entry. All sampled body corners must fit inside
the supplied road rectangle. Dynamic obstacle checking is deliberately separate.
"""
from dataclasses import dataclass
import math


@dataclass(frozen=True)
class TurnSample:
    x: float
    z: float
    heading_rad: float
    direction: int
    curvature: float


def three_point_turn(*, lane_offset_m, road_half_width_m, forward_space_m,
                     rear_space_m, minimum_radius_m, half_width_m, half_length_m,
                     clearance_m=.2, sample_step_m=.05):
    """Return three arcs (forward/ reverse/ forward), each including its cusp.

    The pose reference is the bicycle model's rear axle centre. Body half extents
    must conservatively enclose the whole vehicle about that point (not its COM).
    Endpoint is the opposite lane at the same longitudinal coordinate, facing
    back along the road. Cusp endpoints require a complete stop before reversing.
    Lengths/widths are caller-supplied; no real road permission is inferred.
    """
    values = (lane_offset_m, road_half_width_m, forward_space_m, rear_space_m,
              minimum_radius_m, half_width_m, half_length_m, clearance_m, sample_step_m)
    if any(not math.isfinite(v) for v in values):
        raise ValueError("Turn dimensions must be finite")
    if (lane_offset_m < 0 or clearance_m < 0 or
            min(road_half_width_m, forward_space_m, rear_space_m,
                minimum_radius_m, half_width_m, half_length_m, sample_step_m) <= 0):
        raise ValueError("Invalid turn dimensions")
    radius = minimum_radius_m
    # With symmetric outside arcs alpha and middle arc pi-2*alpha, net
    # displacement is 4R*cos(alpha)-2R = -2*lane_offset.
    cosine = (radius-lane_offset_m)/(2*radius)
    if not 0 < cosine <= .5:
        raise ValueError("Lane separation requires another manoeuvre")
    alpha = math.acos(cosine)
    arcs = ((1, -1/radius, alpha), (-1, 1/radius, math.pi-2*alpha), (1, -1/radius, alpha))
    x, z, heading = lane_offset_m, 0., 0.
    result = []
    # Bound the unsampled corner motion as well: a corner moves no faster than
    # (1 + body_radius/R) times centreline travel along these constant arcs.
    corner_radius = math.hypot(half_width_m, half_length_m)
    sampling_margin = sample_step_m * (1 + corner_radius/radius) / 2
    margin = clearance_m + sampling_margin
    for direction, curvature, angle in arcs:
        length = radius*angle
        count = max(1, math.ceil(length/sample_step_m))
        samples = []
        for i in range(count+1):
            theta = heading + direction*curvature*length*i/count
            px = x + (math.cos(heading)-math.cos(theta))/curvature
            pz = z + (math.sin(theta)-math.sin(heading))/curvature
            for right in (-half_width_m, half_width_m):
                for front in (-half_length_m, half_length_m):
                    cx = px + right*math.cos(theta) + front*math.sin(theta)
                    cz = pz - right*math.sin(theta) + front*math.cos(theta)
                    if (abs(cx)+margin > road_half_width_m or
                            cz+margin > forward_space_m or cz-margin < -rear_space_m):
                        raise ValueError("Swept vehicle footprint exceeds authored road space")
            samples.append(TurnSample(px,pz,theta,direction,curvature))
        result.append(samples)
        last = samples[-1]
        x,z,heading = last.x,last.z,last.heading_rad
    return result


class TurnController:
    """Track a validated turn in its local rear-axle frame at walking speed.

    update consumes measured localization and speed, never elapsed-time replay.
    Every cusp holds the brake for three consecutive stopped observations.
    """
    def __init__(self, *, wheelbase_m, max_steering_rad, **geometry):
        if not (math.isfinite(wheelbase_m) and wheelbase_m > 0 and
                math.isfinite(max_steering_rad) and 0 < max_steering_rad < math.pi/2):
            raise ValueError("Invalid vehicle steering geometry")
        if geometry['minimum_radius_m'] < wheelbase_m/math.tan(max_steering_rad):
            raise ValueError("Turn radius exceeds vehicle steering capability")
        self.arcs = three_point_turn(**geometry)
        self.geometry = geometry
        self.wheelbase = wheelbase_m
        self.max_steering = max_steering_rad
        self.arc_index = 0
        self.sample_index = 0
        self.stopped_samples = 0
        self.status = 'MANEUVER'
        self.reason = 'Following three-point turn'

    def brake(self, reason, *, failed=False):
        self.reason = reason
        if failed:
            self.status = 'OFF_ROUTE'
        return dict(target_speed_kmh=0., steering_angle_rad=0., emergency_stop=True,
                    status=self.status, reason=self.reason)

    def measured_cusp_arc(self, x, z, heading, original):
        """Fit the next stopped manoeuvre from measured SE(2), within the same corridor.

        Cubic tangents preserve body headings in both travel directions. Reject
        curves exceeding steering capability or the checked swept-body space.
        This changes only the plan; it never modifies measured localization.
        """
        end=original[-1]; direction=original[0].direction
        distance=math.hypot(end.x-x,end.z-z)
        limit=math.tan(self.max_steering)/self.wheelbase*.95
        g=self.geometry
        candidates=[]
        for start_factor in (.3,1/3,.35,.4,.5,.65,.8):
            for end_factor in (.3,1/3,.35,.4,.5,.65,.8):
                p0=(x,z); p3=(end.x,end.z)
                p1=(x+direction*math.sin(heading)*distance*start_factor,
                    z+direction*math.cos(heading)*distance*start_factor)
                p2=(end.x-direction*math.sin(end.heading_rad)*distance*end_factor,
                    end.z-direction*math.cos(end.heading_rad)*distance*end_factor)
                polygon=math.dist(p0,p1)+math.dist(p1,p2)+math.dist(p2,p3)
                count=max(20,math.ceil(polygon/.025)); samples=[]
                margin=.2+.025*(1+math.hypot(g['half_width_m'],g['half_length_m'])*limit)/2
                valid=True
                for i in range(count+1):
                    t=i/count; u=1-t
                    pos=tuple(u**3*p0[j]+3*u*u*t*p1[j]+3*u*t*t*p2[j]+t**3*p3[j] for j in (0,1))
                    d=tuple(3*u*u*(p1[j]-p0[j])+6*u*t*(p2[j]-p1[j])+3*t*t*(p3[j]-p2[j]) for j in (0,1))
                    dd=tuple(6*u*(p2[j]-2*p1[j]+p0[j])+6*t*(p3[j]-2*p2[j]+p1[j]) for j in (0,1))
                    norm=math.hypot(*d)
                    if norm<1e-5:
                        valid=False; break
                    curvature=direction*(d[1]*dd[0]-d[0]*dd[1])/norm**3
                    theta=math.atan2(direction*d[0],direction*d[1])
                    if abs(curvature)>limit:
                        valid=False; break
                    for right in (-g['half_width_m'],g['half_width_m']):
                        for front in (-g['half_length_m'],g['half_length_m']):
                            px=pos[0]+right*math.cos(theta)+front*math.sin(theta)
                            pz=pos[1]-right*math.sin(theta)+front*math.cos(theta)
                            if abs(px)+margin>g['road_half_width_m'] or pz+margin>g['forward_space_m'] or pz-margin < -g['rear_space_m']:
                                valid=False
                    if not valid: break
                    samples.append(TurnSample(*pos,theta,direction,curvature))
                if valid:
                    length=sum(math.hypot(b.x-a.x,b.z-a.z) for a,b in zip(samples,samples[1:]))
                    candidates.append((length,samples))
        return min(candidates,key=lambda item:item[0])[1] if candidates else None

    def update(self, x, z, heading, speed_mps):
        if self.status in {'DONE','OFF_ROUTE'}:
            return self.brake(self.reason)
        if not all(math.isfinite(v) for v in (x,z,heading,speed_mps)):
            return self.brake('Non-finite turn localization', failed=True)
        g=self.geometry
        # Check the actual swept body as well as the planned one. Stay inside
        # the static authored corridor even when localization deviates.
        for right in (-g['half_width_m'],g['half_width_m']):
            for front in (-g['half_length_m'],g['half_length_m']):
                px=x+right*math.cos(heading)+front*math.sin(heading)
                pz=z-right*math.sin(heading)+front*math.cos(heading)
                if (abs(px)+.1 > g['road_half_width_m'] or
                    pz+.1 > g['forward_space_m'] or pz-.1 < -g['rear_space_m']):
                    return self.brake('Turn body outside authored corridor', failed=True)
        arc=self.arcs[self.arc_index]
        # No crossing into another arc or skipping an entire manoeuvre loop.
        end_index=min(len(arc),self.sample_index+30)
        nearest=min(range(max(0,self.sample_index-2),end_index),
                    key=lambda i: math.hypot(arc[i].x-x,arc[i].z-z))
        error=math.hypot(arc[nearest].x-x,arc[nearest].z-z)
        if error > .3:
            return self.brake('Turn tracking error exceeds 0.3m', failed=True)
        self.sample_index=max(self.sample_index,nearest)
        end=arc[-1]
        distance=math.hypot(end.x-x,end.z-z)
        angle_error=abs(math.atan2(math.sin(end.heading_rad-heading),math.cos(end.heading_rad-heading)))
        if distance < .07 or self.sample_index == len(arc)-1:
            if angle_error > .15:
                return self.brake('Turn cusp heading mismatch', failed=True)
            if abs(speed_mps) < .03:
                self.stopped_samples += 1
            else:
                self.stopped_samples = 0
            if self.stopped_samples >= 3:
                if self.arc_index+1 < len(self.arcs):
                    revised=self.measured_cusp_arc(x,z,heading,self.arcs[self.arc_index+1])
                    if revised is None:
                        original=self.arcs[self.arc_index+1]
                        start=original[0]
                        start_heading_error=abs(math.atan2(math.sin(start.heading_rad-heading),math.cos(start.heading_rad-heading)))
                        if math.hypot(start.x-x,start.z-z)>.1 or start_heading_error>.08:
                            return self.brake('Next turn arc infeasible from measured cusp',failed=True)
                        revised=original
                    self.arcs[self.arc_index+1]=revised
                self.arc_index += 1
                self.sample_index = self.stopped_samples = 0
                if self.arc_index == len(self.arcs):
                    self.status='DONE'
            return self.brake('Turn complete' if self.status == 'DONE' else 'Stop before changing direction')
        self.stopped_samples = 0
        # Track position AND tangent in the rear-axle frame. In a short reverse
        # arc, point pursuit can reach its endpoint with the wrong body heading.
        # Reverse the heading-feedback sign when travelling backwards.
        target=arc[nearest]
        dx,dz=target.x-x,target.z-z
        lateral=dx*math.cos(target.heading_rad)-dz*math.sin(target.heading_rad)
        heading_error=math.atan2(math.sin(target.heading_rad-heading),math.cos(target.heading_rad-heading))
        curvature=target.curvature + .8*lateral + target.direction*1.2*heading_error
        steering=math.atan(self.wheelbase*curvature)
        steering=max(-self.max_steering,min(self.max_steering,steering))
        remaining=sum(math.hypot(b.x-a.x,b.z-a.z) for a,b in zip(arc[self.sample_index:],arc[self.sample_index+1:]))
        speed=min(.55,math.sqrt(max(.01,2*.5*(remaining-.04))))
        self.reason='Following three-point turn'
        return dict(target_speed_kmh=arc[0].direction*speed*3.6,
                    steering_angle_rad=steering,emergency_stop=False,
                    status='MANEUVER',reason=self.reason)
