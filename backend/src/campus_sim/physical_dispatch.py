"""Selectable dispatch driven by Unity observations, not virtual travel time."""
from dataclasses import dataclass
import math
from uuid import uuid4

from campus_sim.dispatch import has_capacity, FifoDispatch, GreedyDispatch, HungarianDispatch
from campus_sim.domain import RequestStatus
from campus_sim.battery import ChargeSession
from campus_sim.unity_drive import AutoMission


@dataclass
class ObservedTrip:
    request_id: str
    vehicle_id: str
    home_stop: str
    phase: str = 'TO_PICKUP'
    mission_id: str | None = None
    dwell_s: float = 0.
    last_tick: int = -1
    last_wall: float | None = None
    last_position: tuple | None = None
    budget_wh: float = 0.
    consumed_wh: float = 0.
    returned: bool = False
    reason: str = ''


class PhysicalDispatcher:
    boarding_s = 3.  # Explicit simulation service-time assumption.
    alighting_s = 3.

    def __init__(self, session):
        self.session = session
        self.jobs = {}
        self.events = []
        self.retry_at = 0.
        self.ready_order = {}
        self.ready_sequence = 0
        self.opportunity_count = 0
        self.estimates = {}
        self.energy_observations = {}
        self.charging = {}
        self.charges = []
        self.diagnostics = {}

    def _stop_charge(self, vehicle_id, *, interrupted=False):
        charge=self.charging.pop(vehicle_id,None)
        if charge:
            charge.completed=True;charge.interrupted=interrupted
            charge.ends_at_s=self.session.clock.read()

    def _energy_step(self, now):
        """Use the web's battery policy, with observed distance and home presence."""
        s=self.session;service=s.service;policy=service.battery_policy
        if policy is None:return
        for identifier,vehicle in s.physical.vehicles.items():
            if not self._fresh(vehicle,now):
                self.energy_observations.pop(identifier,None)
                self._stop_charge(identifier,interrupted=True)
                continue
            ego=vehicle.ego;previous=self.energy_observations.get(identifier)
            if previous and previous[0]==vehicle.token and previous[1]==ego.tick:continue
            position=(ego.x,ego.z)
            dt=distance=0.
            if previous and previous[0]==vehicle.token:
                dt=min(.2,max(0.,now-previous[2]));distance=math.dist(position,previous[3])
            self.energy_observations[identifier]=(vehicle.token,ego.tick,now,position)
            job=next((j for j in self.jobs.values() if j.vehicle_id==identifier and not j.returned),None)
            battery=service.batteries[identifier]
            if job or vehicle.status in {'DRIVING','MANEUVER'} or abs(ego.speed_mps)>.05:
                load=policy.load_factor(service.requests[job.request_id]) if job and job.phase=='TO_DROPOFF' else 1.
                used=distance*policy.wh_per_m*load+(dt*policy.auxiliary_power_w/3600 if not s.clock.paused else 0)
                used=min(battery.remaining_wh,used)
                battery.remaining_wh-=used;battery.consumed_wh+=used
                if job:job.consumed_wh+=used
                self._stop_charge(identifier,interrupted=True)
                if battery.remaining_wh<=policy.reserve_wh:
                    vehicle.status='OFF_ROUTE';vehicle.reason='Physical model battery reserve reached'
                continue
            home=s.physical.coordinates.to_unity(*service.graph.nodes[service.home_nodes[identifier]])
            at_home=vehicle.status in {'PARKED','ARRIVED'} and math.dist(position,home)<=4 and abs(ego.speed_mps)<.05
            if not at_home:
                self._stop_charge(identifier,interrupted=True);continue
            if s.clock.paused:continue
            if battery.remaining_wh>=policy.capacity_wh:
                self._stop_charge(identifier);continue
            if identifier not in self.charging:
                if not policy.charge_when_idle and battery.remaining_wh>policy.capacity_wh*policy.charge_trigger_fraction:continue
                started=s.clock.read()
                charge=ChargeSession(identifier,started,started+(policy.capacity_wh-battery.remaining_wh)*3600/policy.charging_power_w,battery.remaining_wh)
                self.charging[identifier]=charge;self.charges.append(charge)
            gain=min(policy.capacity_wh-battery.remaining_wh,dt*policy.charging_power_w/3600)
            battery.remaining_wh+=gain;battery.charged_wh+=gain
            if battery.remaining_wh>=policy.capacity_wh:self._stop_charge(identifier)

    def busy(self, vehicle_id):
        return any(j.vehicle_id==vehicle_id and not j.returned for j in self.jobs.values())

    def _command(self, stop):
        return AutoMission(run_id=self.session.run_id,command_id=uuid4().hex,stop_id=stop,
                           driving_profile='campus_fast_no_obstacles',cruise_speed_kmh=40)

    def _fresh(self, vehicle, now):
        return (vehicle.connected and vehicle.ego is not None and
                now-vehicle.received_s<=self.session.physical.stale_s and
                vehicle.ego.sensor_valid and vehicle.ego.sensor_age_s<=.2)

    def _event(self, job, phase):
        job.phase=phase
        self.events.append(dict(request_id=job.request_id,vehicle_id=job.vehicle_id,
                                phase=phase,time_s=self.session.clock.read()))
        self.events[:]=self.events[-2000:]

    def _estimate(self, vehicle_id, request):
        service=self.session.service
        physical=self.session.physical
        homes=[stop for stop,node in service.stop_nodes.items() if node==service.home_nodes[vehicle_id]]
        if not homes:
            homes=['home:'+vehicle_id]
        pose=physical.vehicles[vehicle_id].ego
        energy=0.; durations=[]
        for leg,stop in enumerate((request.pickup_stop_id,request.dropoff_stop_id,homes[0])):
            plan=physical.preview_mission(vehicle_id,self._command(stop),origin=pose)
            points=plan.route_unity
            length=sum(math.dist(a,b) for a,b in zip(points,points[1:]))
            turn_length=0.
            if plan.turn:
                turn_length=sum(sum(math.hypot(b.x-a.x,b.z-a.z) for a,b in zip(arc,arc[1:]))
                                for arc in plan.turn.controller.arcs)
            durations.append(length/(40/3.6)+turn_length/.55+(6 if plan.turn else 0))
            length+=turn_length
            policy=service.battery_policy
            if policy:
                load=policy.load_factor(request) if leg==1 else 1.
                # Conservative planning allowance at manoeuvring speed. Actual
                # model energy is debited from observed distance and wall time.
                energy+=length*policy.wh_per_m*load+policy.auxiliary_power_w*(length/.55+6)/3600
            if len(points)>1:
                a,b=points[-2:]
                pose=pose.model_copy(update=dict(x=b[0],z=b[1],speed_mps=0.,
                    heading_rad=math.atan2(b[0]-a[0],b[1]-a[1])))
        if service.battery_policy and energy+service.battery_policy.reserve_wh>service.batteries[vehicle_id].remaining_wh:
            raise ValueError('WAITING_PHYSICAL_BATTERY')
        return homes[0],energy,durations

    def _preflight(self, vehicle_id, request):
        home,energy,_=self._estimate(vehicle_id,request)
        return home,energy

    def _start_leg(self, job, stop, phase):
        try:
            self.session.physical.mission(job.vehicle_id,self._command(stop))
        except ValueError as error:
            job.reason=str(error)
            return False
        job.mission_id=self.session.physical.vehicles[job.vehicle_id].mission_id
        job.dwell_s=0.
        job.reason=''
        self._event(job,phase)
        return True

    def step(self):
        s=self.session; service=s.service; p=s.physical; now=p.clock()
        self._energy_step(now)
        for job in list(self.jobs.values()):
            if job.returned:
                continue
            vehicle=p.vehicles.get(job.vehicle_id)
            request=service.requests[job.request_id]
            if vehicle is None or not self._fresh(vehicle,now):
                job.last_wall=None
                job.dwell_s=0.
                job.reason='WAITING_FRESH_PHYSICS'
                continue
            ego=vehicle.ego
            if job.reason=='WAITING_FRESH_PHYSICS':
                job.reason=''
            if ego.tick==job.last_tick:
                continue
            dt=0. if job.last_wall is None else max(0.,now-job.last_wall)
            job.last_wall=now; job.last_tick=ego.tick
            position=(ego.x,ego.z)
            distance=0. if job.last_position is None else math.dist(position,job.last_position)
            job.last_position=position
            if vehicle.status=='OFF_ROUTE':
                job.reason=vehicle.reason
                continue
            if s.clock.paused:
                continue
            # Reconnect replaces the physical owner and loses the old route.
            # Resume the same leg from the new measured pose; never duplicate it.
            if vehicle.mission_id!=job.mission_id:
                stop=(request.pickup_stop_id if job.phase in {'TO_PICKUP','PICKUP_SERVICE'} else
                      request.dropoff_stop_id if job.phase in {'TO_DROPOFF','DROPOFF_SERVICE'} else job.home_stop)
                self._start_leg(job,stop,'TO_PICKUP' if job.phase=='PICKUP_SERVICE' else
                                'TO_DROPOFF' if job.phase=='DROPOFF_SERVICE' else job.phase)
                continue
            if vehicle.status!='ARRIVED' or abs(ego.speed_mps)>=.05:
                job.dwell_s=0.
                continue
            if job.phase=='TO_PICKUP':
                if request.status==RequestStatus.CANCELLED:
                    self._start_leg(job,job.home_stop,'RETURNING')
                else:
                    request.status=RequestStatus.PICKUP_SERVICE
                    self._event(job,'PICKUP_SERVICE')
            elif job.phase=='PICKUP_SERVICE':
                job.dwell_s+=min(dt,.2)
                if job.dwell_s>=self.boarding_s and self._start_leg(job,request.dropoff_stop_id,'TO_DROPOFF'):
                    request.status=RequestStatus.IN_TRANSIT
            elif job.phase=='TO_DROPOFF':
                request.status=RequestStatus.DROPOFF_SERVICE
                self._event(job,'DROPOFF_SERVICE')
            elif job.phase=='DROPOFF_SERVICE':
                job.dwell_s+=min(dt,.2)
                if job.dwell_s>=self.alighting_s:
                    request.status=RequestStatus.COMPLETED; request.eta_s=None
                    self._event(job,'RETURN_PENDING')
                    self._start_leg(job,job.home_stop,'RETURNING')
            elif job.phase=='RETURN_PENDING':
                self._start_leg(job,job.home_stop,'RETURNING')
            elif job.phase=='RETURNING':
                job.returned=True
                self._event(job,'READY')
        if now<self.retry_at:
            return
        self.retry_at=now+1.
        for identifier,vehicle in p.vehicles.items():
            if (identifier not in self.ready_order and not self.busy(identifier) and
                self._fresh(vehicle,now) and vehicle.status in {'PARKED','ARRIVED'}):
                self.ready_sequence+=1
                self.ready_order[identifier]=self.ready_sequence
        queued=[r for r in service.requests.values()
                if r.id in service.external_request_ids and r.status==RequestStatus.QUEUED]
        available=[service.vehicles[i].model_copy(update={'available':True}) for i,v in p.vehicles.items()
                   if not self.busy(i) and self._fresh(v,now) and
                   v.status in {'PARKED','ARRIVED'} and abs(v.ego.speed_mps)<=.05]
        self.diagnostics = dict(queued=len(queued), available=[v.id for v in available],
                                cached_candidates=len(self.estimates))
        # A three-leg preview is relatively expensive. Evaluate one candidate
        # per scheduler tick, letting WebSockets refresh localization in between.
        # Cache only cost/feasibility; starting the leg still plans from fresh ego.
        # Planning can hold the session lock longer than the observation TTL.
        # Keep feasibility snapshots across that temporary gap; otherwise an
        # HTTP poll before the next WebSocket update discards all progress.
        # Freshness still gates `available` and mission(), and reuse below
        # validates the current pose and vehicle geometry.
        active_keys={(i,r.id) for i,v in p.vehicles.items() for r in queued
                     if v.connected and not self.busy(i) and v.status in {'PARKED','ARRIVED'}}
        self.estimates={k:e for k,e in self.estimates.items() if k in active_keys}
        if len(available)>1:
            for request in queued:
                for vehicle in available:
                    if not has_capacity(vehicle,request) or not service._stops_accessible(vehicle,request): continue
                    ego=p.vehicles[vehicle.id].ego; key=(vehicle.id,request.id)
                    signature=(ego.wheelbase_m,ego.max_steering_rad,ego.half_width_m,ego.half_length_m,ego.rear_axle_offset_m)
                    cached=self.estimates.get(key)
                    # Unity world/local Transform round trips jitter by a few
                    # float ULPs even at rest. This is a numerical cache tolerance
                    # (0.1 mm / 0.0001 rad), not a relaxed route safety margin.
                    if cached and math.hypot(cached[0].x-ego.x,cached[0].z-ego.z)<.25 and abs(
                        math.atan2(math.sin(cached[0].heading_rad-ego.heading_rad),math.cos(cached[0].heading_rad-ego.heading_rad)))<.03 and all(
                            a == b or (a is not None and b is not None and math.isclose(a,b,rel_tol=0.,abs_tol=1e-4))
                            for a,b in zip(cached[1],signature)):
                        if cached[2] is not None or now-cached[4]<1: continue
                    try: estimate,reason=self._estimate(vehicle.id,request),''
                    except ValueError as error: estimate,reason=None,str(error)
                    self.estimates[key]=(ego,signature,estimate,reason,now)
                    self.retry_at=p.clock()+.1
                    service.waiting_reasons[request.id]=reason or 'EVALUATING_PHYSICAL_CANDIDATES'
                    return
        estimates={}
        def feasible(vehicle,request):
            key=(vehicle.id,request.id)
            if key not in estimates:
                estimates[key]=None
                if has_capacity(vehicle,request) and service._stops_accessible(vehicle,request):
                    if len(available)>1:
                        cached=self.estimates[key];estimates[key]=cached[2]
                        if cached[3]: service.waiting_reasons[request.id]=cached[3]
                    else:
                        try: estimates[key]=self._estimate(vehicle.id,request)
                        except ValueError as error: service.waiting_reasons[request.id]=str(error)
                    if estimates[key] is not None and service.battery_policy and (
                        estimates[key][1]+service.battery_policy.reserve_wh>service.batteries[vehicle.id].remaining_wh):
                        estimates[key]=None;service.waiting_reasons[request.id]='WAITING_PHYSICAL_BATTERY'
            return estimates[key] is not None
        now_s=s.clock.read()
        def cost(vehicle,request):
            _,energy,times=estimates[vehicle.id,request.id]
            late=0 if request.latest_arrival_s is None else max(0,now_s+sum(times[:2])-request.latest_arrival_s)
            recharge=energy*3600/service.battery_policy.charging_power_w if service.battery_policy else 0
            scarce=120 if vehicle.wheelchair_slots and not request.service_needs.wheelchair_slots and any(
                r.service_needs.wheelchair_slots for r in queued) else 0
            return times[0]+.5*times[1]+2*late+recharge+scarce
        def priority(request):
            base=3 if request.service_needs.requires_step_free or request.service_needs.boarding_assistance else (
                2 if request.service_type.value=='PASSENGER' else 1)
            completion=min((sum(e[2][:2]) for (v,r),e in estimates.items() if r==request.id and e),default=0)
            deadline=0 if request.latest_arrival_s is None else 2*max(0,min(2,1-(request.latest_arrival_s-now_s-completion)/600))
            return -(base+max(0,now_s-request.created_s)/120+deadline),request.created_s,request.id
        if service.dispatch_algorithm=='fifo':
            assignments=FifoDispatch().assign(queued,available,feasible,vehicle_order=self.ready_order)
        else:
            policy=GreedyDispatch() if service.dispatch_algorithm=='greedy' else HungarianDispatch()
            assignments=policy.assign(queued,available,feasible,cost=cost,priority=priority,
                                      now_s=now_s,opportunity_count=self.opportunity_count)
        for assignment in assignments:
            request=service.requests[assignment.request_id]; identifier=assignment.vehicle_id
            vehicle=p.vehicles[identifier]; home,budget,_=estimates[identifier,request.id]
            job=ObservedTrip(request.id,identifier,home,budget_wh=budget,
                last_wall=now,last_position=(vehicle.ego.x,vehicle.ego.z))
            if self._start_leg(job,request.pickup_stop_id,'TO_PICKUP'):
                self._stop_charge(identifier,interrupted=True)
                self.jobs[request.id]=job; self.opportunity_count+=1
                self.ready_order.pop(identifier,None)
                request.status=RequestStatus.ASSIGNED; request.vehicle_id=identifier; request.eta_s=None
                service.waiting_reasons.pop(request.id,None)
            else:
                service.waiting_reasons[request.id]=job.reason
                self.diagnostics['start_failure']=job.reason
