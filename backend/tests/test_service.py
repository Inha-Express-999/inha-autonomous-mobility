from __future__ import annotations

import asyncio
import json
import unittest
from dataclasses import replace
from itertools import pairwise
from unittest.mock import patch

from fastapi import WebSocketDisconnect
from pydantic import ValidationError

from campus_sim.domain import (
    CreateRequest,
    EgoLocalization,
    MapPosition,
    RequestStatus,
    SensorObservation,
    ServiceNeeds,
    ServiceType,
)
from campus_sim.realtime import handle_client_message, make_snapshot, serve_client_socket
from campus_sim.service import (
    DispatchPriorityPolicy,
    MobilityService,
    PlanningPolicy,
    SafetyPolicy,
)


def passenger_command(
    service: MobilityService,
    command_id: str = "command-1",
    *,
    owner_id: str = "fixture-passenger",
    pickup_index: int = 1,
    dropoff_index: int = 0,
    party_size: int = 1,
    service_needs: ServiceNeeds | None = None,
) -> CreateRequest:
    assert service.graph is not None
    pickup = service.graph.nodes[pickup_index]
    dropoff = service.graph.nodes[dropoff_index]
    return CreateRequest(
        command_id=command_id,
        owner_id=owner_id,
        service_type="PASSENGER",
        pickup_landmark_id=pickup.landmark_id,
        dropoff_landmark_id=dropoff.landmark_id,
        party_size=party_size,
        service_needs=service_needs or ServiceNeeds(),
    )


class FakeWebSocket:
    def __init__(self) -> None:
        self.messages: list[dict] = []

    async def send_json(self, message: dict) -> None:
        self.messages.append(message)


class SnapshotCadenceWebSocket:
    def __init__(self) -> None:
        self.messages: list[dict] = []
        self.snapshot_times: list[float] = []
        self.subscribed = False

    async def accept(self) -> None:
        pass

    async def receive_text(self) -> str:
        if self.snapshot_times and len(self.snapshot_times) >= 3:
            raise WebSocketDisconnect()
        if not self.subscribed:
            self.subscribed = True
            return json.dumps(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.2.4.0",
                    "role": "PC_Operator",
                }
            )
        await asyncio.Future()
        raise AssertionError("unreachable")

    async def send_json(self, message: dict) -> None:
        self.messages.append(message)
        if message["type"] == "snapshot":
            self.snapshot_times.append(asyncio.get_running_loop().time())


class MobilityServiceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.service = MobilityService.synthetic_fixture()

    def test_dispatch_policy_loads_pickup_and_dropoff_service_times(self) -> None:
        policy = DispatchPriorityPolicy.from_default_config()
        self.assertEqual(policy.pickup_service_s, 2.0)
        self.assertEqual(policy.dropoff_service_s, 2.0)
        self.assertEqual(policy.assignment_algorithm, "greedy")

    def test_dispatch_policy_rejects_negative_service_time(self) -> None:
        config = {
            "assignment_algorithm": "greedy",
            "request_priority": {
                "base_priority": {"passenger": 2, "cargo": 1, "mobility_needs": 3},
                "aging_interval_s": 120,
                "deadline_weight": 2,
                "deadline_window_s": 600,
            },
            "fairness": {"wait_s": 600, "every_n_assignments": 3},
            "service_times_s": {"pickup": -1, "dropoff": 2},
        }
        with (
            patch("pathlib.Path.read_text", return_value=json.dumps(config)),
            self.assertRaisesRegex(ValueError, "finite and non-negative"),
        ):
            DispatchPriorityPolicy.from_default_config()

    def test_hungarian_batch_dispatch_assigns_distinct_compatible_vehicles(self) -> None:
        fleet = MobilityService.synthetic_fleet_fixture()
        fleet.dispatch_priority_policy = replace(
            fleet.dispatch_priority_policy,
            assignment_algorithm="hungarian",
        )
        for vehicle in fleet.vehicles.values():
            vehicle.available = False

        first = fleet.create_request(
            passenger_command(fleet, "hungarian-first", pickup_index=0, dropoff_index=1)
        ).request
        second = fleet.create_request(
            passenger_command(
                fleet,
                "hungarian-second",
                owner_id="hungarian-owner-2",
                pickup_index=5,
                dropoff_index=4,
            )
        ).request
        assert first is not None and second is not None
        self.assertEqual(first.status, RequestStatus.QUEUED)
        self.assertEqual(second.status, RequestStatus.QUEUED)

        for vehicle in fleet.vehicles.values():
            vehicle.available = True
        fleet._dispatch_queued_requests()

        self.assertEqual(first.status, RequestStatus.ASSIGNED)
        self.assertEqual(second.status, RequestStatus.ASSIGNED)
        self.assertEqual(len({first.vehicle_id, second.vehicle_id}), 2)
        self.assertNotEqual(first.vehicle_id, "V03")
        self.assertNotEqual(second.vehicle_id, "V03")

    def test_safety_policy_loads_documented_assumptions_from_config(self) -> None:
        self.assertEqual(
            self.service.safety_policy,
            SafetyPolicy(
                sensor_stale_after_s=0.3,
                resume_clear_s=1.0,
                reaction_time_s=0.2,
                emergency_decel_mps2=2.0,
                margin_m=1.0,
            ),
        )

    def test_planning_policy_loads_replan_interval_and_improvement_gate(self) -> None:
        self.assertEqual(
            self.service.planning_policy,
            PlanningPolicy(
                global_replan_interval_s=5.0,
                improvement_ratio=0.1,
                improvement_s=15.0,
            ),
        )

    def test_request_traverses_pickup_transit_and_completion(self) -> None:
        created = self.service.create_request(passenger_command(self.service))
        self.assertEqual(created.request.status, RequestStatus.ASSIGNED)
        self.assertGreater(created.request.eta_s or 0, 0)

        self.service.advance(20.0)
        self.assertEqual(created.request.status, RequestStatus.PICKUP_SERVICE)
        self.service.advance(2.0)
        self.assertEqual(created.request.status, RequestStatus.IN_TRANSIT)
        self.service.advance(20.0)
        self.assertEqual(created.request.status, RequestStatus.DROPOFF_SERVICE)
        self.service.advance(2.0)
        self.assertEqual(created.request.status, RequestStatus.COMPLETED)
        self.assertTrue(self.service.vehicles["V01"].available)
        self.assertEqual(self.service.vehicle_runtime["V01"].mission_state, "IDLE")

    def test_eta_means_remaining_time_until_destination_arrival(self) -> None:
        request = self.service.create_request(passenger_command(self.service)).request
        assert request is not None
        runtime = self.service.vehicle_runtime["V01"]
        trip_eta_s = self.service._route_duration_between_stops(
            request.pickup_stop_id or "",
            request.dropoff_stop_id or "",
            service_type=ServiceType.PASSENGER,
            requires_step_free=False,
        )
        expected_initial_eta = self.service._route_duration(runtime) + 2.0 + trip_eta_s
        self.assertAlmostEqual(request.eta_s or 0.0, expected_initial_eta, delta=0.1)

        self.service.advance(20.0)
        self.assertEqual(request.status, RequestStatus.PICKUP_SERVICE)
        self.assertAlmostEqual(request.eta_s or 0.0, trip_eta_s + 2.0, delta=0.1)
        self.service.advance(1.0)
        self.assertAlmostEqual(request.eta_s or 0.0, trip_eta_s + 1.0, delta=0.1)

        self.service.advance(1.0)
        self.assertEqual(request.status, RequestStatus.IN_TRANSIT)
        self.assertAlmostEqual(
            request.eta_s or 0.0,
            self.service._route_duration(runtime),
            delta=0.1,
        )
        self.service.advance(20.0)
        self.assertEqual(request.status, RequestStatus.DROPOFF_SERVICE)
        self.assertEqual(request.eta_s, 0.0)

    def test_request_rejects_unreachable_dropoff_leg_before_assignment(self) -> None:
        assert self.service.graph is not None
        pickup_node = self.service.graph.nodes[1].id
        directed_edges = [
            edge for edge in self.service.graph.edges if edge.from_node != pickup_node
        ]
        self.service.graph = self.service.graph.model_copy(update={"edges": directed_edges})

        with self.assertRaisesRegex(ValueError, "dropoff_route_unavailable"):
            self.service.create_request(
                passenger_command(self.service, pickup_index=1, dropoff_index=0)
            )

        self.assertEqual(self.service.requests, {})
        self.assertTrue(self.service.vehicles["V01"].available)

    def test_fixed_clock_advances_without_snapshot_requests(self) -> None:
        async def run_until_three_ticks() -> None:
            stop_event = asyncio.Event()
            clock_task = asyncio.create_task(self.service.run_clock(stop_event))
            deadline = asyncio.get_running_loop().time() + 1.0
            while (
                self.service.simulation_time_s < 0.15
                and asyncio.get_running_loop().time() < deadline
            ):
                await asyncio.sleep(0.005)
            stop_event.set()
            await clock_task

        asyncio.run(run_until_three_ticks())
        self.assertGreaterEqual(self.service.simulation_time_s, 0.15)
        self.assertLess(self.service.simulation_time_s, 0.25)

    def test_snapshot_reads_do_not_advance_authoritative_simulation(self) -> None:
        request = self.service.create_request(passenger_command(self.service))
        self.service.advance(0.05)
        simulation_time = self.service.now_s()
        position = (
            self.service.vehicle_runtime["V01"].x,
            self.service.vehicle_runtime["V01"].y,
        )

        make_snapshot(self.service, "PC_Operator", None, 1)
        make_snapshot(self.service, "Mobile_Passenger", request.request.owner_id, 2)

        self.assertEqual(self.service.now_s(), simulation_time)
        self.assertEqual(
            (self.service.vehicle_runtime["V01"].x, self.service.vehicle_runtime["V01"].y),
            position,
        )

    def test_ego_localization_is_map_scoped_and_rejects_stale_ticks(self) -> None:
        assert self.service.graph is not None
        observation = EgoLocalization(
            vehicle_id="V01",
            session_id="test-session",
            observed_tick=12,
            map_version=self.service.graph.map_version,
            position=MapPosition(x=-3.5, y=7.25, z=0.4),
            heading_rad=-0.25,
            speed_mps=1.8,
        )
        self.assertTrue(self.service.record_ego_localization(observation))
        self.assertFalse(self.service.record_ego_localization(observation))
        self.assertEqual(self.service.ego_localizations["V01"].observed_tick, 12)
        self.assertEqual(self.service.ego_localizations["V01"].position.x, -3.5)
        restarted_session = observation.model_copy(
            update={"session_id": "restarted-session", "observed_tick": 0}
        )
        self.assertTrue(self.service.record_ego_localization(restarted_session))
        runtime = self.service.vehicle_runtime["V01"]
        self.assertEqual((runtime.x, runtime.y, runtime.z), (-3.5, 7.25, 0.4))
        self.assertEqual(runtime.heading_rad, -0.25)
        self.assertEqual(runtime.pose_source, "unity_localization")
        self.service.advance(0.05)
        self.assertEqual((runtime.x, runtime.y), (-3.5, 7.25))
        self.service.advance(0.51)
        self.assertTrue(self.service.ego_localization_is_stale("V01"))
        stale_snapshot = make_snapshot(self.service, "PC_Operator", None, 1)
        self.assertEqual(stale_snapshot["vehicles"][0]["motionState"], "REPLANNING")
        self.assertEqual(stale_snapshot["vehicles"][0]["reason"], "STALE_LOCALIZATION")
        with self.assertRaisesRegex(ValueError, "stale_localization"):
            self.service.create_request(passenger_command(self.service))

        with self.assertRaisesRegex(ValueError, "map_version_mismatch"):
            self.service.record_ego_localization(
                observation.model_copy(update={"observed_tick": 13, "map_version": "unknown-map"})
            )
        with self.assertRaisesRegex(ValueError, "unknown_vehicle"):
            self.service.record_ego_localization(observation.model_copy(update={"vehicle_id": "ghost"}))
        with self.assertRaisesRegex(ValueError, "vehicle_runtime_unavailable"):
            self.service.record_ego_localization(observation.model_copy(update={"vehicle_id": "V02"}))

    def test_sensor_observation_is_bound_to_fresh_ego_pose_and_deduplicated(self) -> None:
        assert self.service.graph is not None
        pose = EgoLocalization(
            vehicle_id="V01",
            session_id="sensor-session",
            observed_tick=10,
            map_version=self.service.graph.map_version,
            position=MapPosition(x=0.0, y=0.0, z=0.0),
            heading_rad=0.0,
            speed_mps=0.0,
        )
        self.assertTrue(self.service.record_ego_localization(pose))
        frame = SensorObservation.model_validate(
            {
                "vehicleId": "V01",
                "sensorId": "front-lidar",
                "sensorType": "LIDAR_2D",
                "sessionId": "sensor-session",
                "mapVersion": self.service.graph.map_version,
                "observedTick": 4,
                "egoPoseTick": 10,
                "valid": True,
                "detections": [
                    {
                        "rangeM": 5.0,
                        "bearingRad": 0.2,
                        "localPositionM": {
                            "x": 4.900332889,
                            "y": 0.993346654,
                            "z": 0.0,
                        },
                        "entityClass": "PEDESTRIAN",
                    }
                ],
            }
        )

        self.assertTrue(self.service.record_sensor_observation(frame))
        self.assertFalse(self.service.record_sensor_observation(frame))
        self.assertEqual(
            self.service.sensor_observations[("V01", "front-lidar")].detections[0].entity_class,
            "PEDESTRIAN",
        )
        with self.assertRaisesRegex(ValueError, "ego_pose_tick_mismatch"):
            self.service.record_sensor_observation(
                frame.model_copy(update={"observed_tick": 5, "ego_pose_tick": 9})
            )
        with self.assertRaisesRegex(ValueError, "session_mismatch"):
            self.service.record_sensor_observation(
                frame.model_copy(update={"observed_tick": 5, "session_id": "other-session"})
            )

        for index in range(7):
            self.assertTrue(
                self.service.record_sensor_observation(
                    frame.model_copy(update={
                        "sensor_id": f"aux-{index}",
                        "observed_tick": 5,
                    })
                )
            )
        with self.assertRaisesRegex(ValueError, "sensor_stream_limit_exceeded"):
            self.service.record_sensor_observation(
                frame.model_copy(update={"sensor_id": "overflow", "observed_tick": 5})
            )

        self.assertTrue(
            self.service.record_ego_localization(
                pose.model_copy(update={"session_id": "new-session", "observed_tick": 0})
            )
        )
        self.assertFalse(
            any(vehicle_id == "V01" for vehicle_id, _ in self.service.sensor_observations)
        )

        self.service.advance(0.51)
        with self.assertRaisesRegex(ValueError, "stale_localization"):
            self.service.record_sensor_observation(frame.model_copy(update={"observed_tick": 5}))

    def test_sensor_observation_rejects_inconsistent_geometry_and_lidar_velocity(self) -> None:
        observation = {
            "vehicleId": "V01",
            "sensorId": "front-sensor",
            "sensorType": "LIDAR_2D",
            "sessionId": "sensor-session",
            "mapVersion": "synthetic-map-v1",
            "observedTick": 1,
            "egoPoseTick": 1,
            "valid": True,
            "detections": [
                {
                    "rangeM": 5.0,
                    "bearingRad": 0.2,
                    "localPositionM": {"x": 4.900332889, "y": 0.993346654, "z": 0.0},
                    "entityClass": "UNKNOWN",
                }
            ],
        }
        radar_frame = {
            **observation,
            "sensorType": "RADAR",
            "detections": [
                {**observation["detections"][0], "relativeSpeedMps": -1.5}
            ],
        }
        self.assertEqual(
            SensorObservation.model_validate(radar_frame).detections[0].relative_speed_mps,
            -1.5,
        )

        for changes in (
            {"detections": [{**observation["detections"][0], "rangeM": 6.0}]},
            {"detections": [{**observation["detections"][0], "bearingRad": 1.0}]},
            {
                "detections": [
                    {**observation["detections"][0], "relativeSpeedMps": 0.5}
                ]
            },
            {"detections": observation["detections"] * 65},
        ):
            with self.subTest(changes=changes), self.assertRaises(ValidationError):
                SensorObservation.model_validate({**observation, **changes})

    def test_localized_safety_fails_closed_on_missing_stale_invalid_and_close_sensor_data(self) -> None:
        assert self.service.graph is not None
        start = self.service.graph.nodes[0].position_m
        runtime = self.service.vehicle_runtime["V01"]
        session_id = "safety-session"
        sensor_tick = 0

        def report_sensor(*, valid: bool = True, detections: list[dict] | None = None) -> None:
            nonlocal sensor_tick
            tick = sensor_tick
            pose = EgoLocalization(
                vehicle_id="V01",
                session_id=session_id,
                observed_tick=tick,
                map_version=self.service.graph.map_version,
                position=MapPosition(x=start.x, y=start.y, z=0.0),
                heading_rad=0.0,
                speed_mps=runtime.speed_mps,
            )
            self.assertTrue(self.service.record_ego_localization(pose))
            frame = SensorObservation.model_validate(
                {
                    "vehicleId": "V01",
                    "sensorId": "front-lidar",
                    "sensorType": "LIDAR_2D",
                    "sessionId": session_id,
                    "mapVersion": self.service.graph.map_version,
                    "observedTick": tick,
                    "egoPoseTick": tick,
                    "valid": valid,
                    "detections": detections or [],
                }
            )
            self.assertTrue(self.service.record_sensor_observation(frame))
            sensor_tick += 1

        self.service._refresh_sensor_safety("V01", runtime)
        self.assertEqual(runtime.safety_motion_state, "REPLANNING")
        self.assertEqual(runtime.safety_reason, "SENSOR_DATA_STALE")

        report_sensor(valid=False)
        self.service._refresh_sensor_safety("V01", runtime)
        self.assertEqual(runtime.safety_motion_state, "EMERGENCY_STOP")
        self.assertEqual(runtime.safety_reason, "SENSOR_INVALID")

        report_sensor()
        self.service._refresh_sensor_safety("V01", runtime)
        self.assertEqual(runtime.safety_reason, "SAFETY_RESUME_HOLD")
        for _ in range(11):
            self.service.simulation_time_s = round(self.service.simulation_time_s + 0.1, 3)
            report_sensor()
            self.service._refresh_sensor_safety("V01", runtime)
        self.assertIsNone(runtime.safety_motion_state)

        runtime.speed_mps = 1.0
        close_detection = {
            "rangeM": 1.0,
            "bearingRad": 0.0,
            "localPositionM": {"x": 1.0, "y": 0.0, "z": 0.0},
            "entityClass": "STATIC_OBSTACLE",
        }
        report_sensor(detections=[close_detection])
        self.service._refresh_sensor_safety("V01", runtime)
        self.assertEqual(runtime.safety_motion_state, "EMERGENCY_STOP")
        self.assertEqual(runtime.safety_reason, "OBSTACLE_STOP")

        self.service.simulation_time_s = round(self.service.simulation_time_s + 0.31, 3)
        report_sensor()
        self.service.simulation_time_s = round(self.service.simulation_time_s + 0.31, 3)
        self.service._refresh_sensor_safety("V01", runtime)
        self.assertEqual(runtime.safety_motion_state, "REPLANNING")
        self.assertEqual(runtime.safety_reason, "SENSOR_DATA_STALE")

    def test_localized_safety_requires_sensor_frame_for_latest_ego_pose_tick(self) -> None:
        assert self.service.graph is not None
        start = self.service.graph.nodes[0].position_m
        runtime = self.service.vehicle_runtime["V01"]
        session_id = "pose-sync-session"

        def pose(tick: int) -> EgoLocalization:
            return EgoLocalization(
                vehicle_id="V01",
                session_id=session_id,
                observed_tick=tick,
                map_version=self.service.graph.map_version,
                position=MapPosition(x=start.x, y=start.y, z=0.0),
                heading_rad=0.0,
                speed_mps=0.0,
            )

        def frame(tick: int) -> SensorObservation:
            return SensorObservation(
                vehicle_id="V01",
                sensor_id="front-lidar",
                sensor_type="LIDAR_2D",
                session_id=session_id,
                map_version=self.service.graph.map_version,
                observed_tick=tick,
                ego_pose_tick=tick,
                valid=True,
                detections=[],
            )

        self.assertTrue(self.service.record_ego_localization(pose(1)))
        self.assertTrue(self.service.record_sensor_observation(frame(1)))
        self.service._refresh_sensor_safety("V01", runtime)
        self.assertEqual(runtime.safety_reason, "SAFETY_RESUME_HOLD")

        # New localization arrives before its corresponding sensor scan.
        self.assertTrue(self.service.record_ego_localization(pose(2)))
        self.assertEqual(runtime.safety_motion_state, "REPLANNING")
        self.assertEqual(runtime.safety_reason, "SENSOR_DATA_STALE")

        self.assertTrue(self.service.record_sensor_observation(frame(2)))
        self.assertEqual(runtime.safety_motion_state, "EMERGENCY_STOP")
        self.assertEqual(runtime.safety_reason, "SAFETY_RESUME_HOLD")

    def test_localization_drives_request_arrival_and_completion(self) -> None:
        assert self.service.graph is not None
        start = self.service.graph.nodes[0].position_m

        def report(position, tick: int, heading: float = 0.0) -> bool:
            assert self.service.graph is not None
            accepted = self.service.record_ego_localization(
                EgoLocalization(
                    vehicle_id="V01",
                    session_id="physics-session",
                    observed_tick=tick,
                    map_version=self.service.graph.map_version,
                    position=MapPosition(x=position.x, y=position.y, z=0.0),
                    heading_rad=heading,
                    speed_mps=0.0,
                )
            )
            if accepted:
                self.service.record_sensor_observation(
                    SensorObservation(
                        vehicle_id="V01",
                        sensor_id="front-lidar",
                        sensor_type="LIDAR_2D",
                        session_id="physics-session",
                        map_version=self.service.graph.map_version,
                        observed_tick=tick,
                        ego_pose_tick=tick,
                        valid=True,
                        detections=[],
                    )
                )
            return accepted

        self.assertTrue(
            report(start, 1)
        )
        ack = self.service.create_request(passenger_command(self.service, pickup_index=1))
        request = ack.request
        self.assertIsNotNone(request)
        runtime = self.service.vehicle_runtime["V01"]
        pickup_position = self.service.graph.nodes[1].position_m
        self.assertTrue(report(pickup_position, 2, 0.5))
        for tick in range(70):
            self.service.advance(0.05)
            self.assertTrue(report(pickup_position, tick + 3, 0.5))
        self.assertEqual(request.status, RequestStatus.IN_TRANSIT)

        dropoff_position = self.service.graph.nodes[0].position_m
        self.assertTrue(report(dropoff_position, 73, 1.0))
        self.service.advance(0.05)
        self.assertEqual(request.status, RequestStatus.DROPOFF_SERVICE)
        for tick in range(74, 114):
            self.assertTrue(report(dropoff_position, tick, 1.0))
            self.service.advance(0.05)
        self.assertEqual(request.status, RequestStatus.COMPLETED)
        self.assertEqual(runtime.pose_source, "unity_localization")

    def test_localized_request_at_current_node_enters_pickup_service(self) -> None:
        assert self.service.graph is not None
        pickup_position = self.service.graph.nodes[0].position_m
        observation_tick = 1

        def report_stopped_pose() -> None:
            nonlocal observation_tick
            self.assertTrue(
                self.service.record_ego_localization(
                    EgoLocalization(
                        vehicle_id="V01",
                        session_id="physics-session",
                        observed_tick=observation_tick,
                        map_version=self.service.graph.map_version,
                        position=MapPosition(
                            x=pickup_position.x, y=pickup_position.y, z=0.0
                        ),
                        heading_rad=0.0,
                        speed_mps=0.0,
                    )
                )
            )
            self.service.record_sensor_observation(
                SensorObservation(
                    vehicle_id="V01",
                    sensor_id="front-lidar",
                    sensor_type="LIDAR_2D",
                    session_id="physics-session",
                    map_version=self.service.graph.map_version,
                    observed_tick=observation_tick,
                    ego_pose_tick=observation_tick,
                    valid=True,
                    detections=[],
                )
            )
            observation_tick += 1

        report_stopped_pose()
        request = self.service.create_request(
            passenger_command(self.service, pickup_index=0, dropoff_index=1)
        ).request
        assert request is not None
        for _ in range(70):
            self.service.advance(0.05)
            report_stopped_pose()
        self.assertEqual(request.status, RequestStatus.IN_TRANSIT)
        runtime = self.service.vehicle_runtime["V01"]
        self.assertGreaterEqual(len(runtime.route_points), 2)

    def test_localized_route_does_not_invent_off_graph_connector(self) -> None:
        assert self.service.graph is not None
        start = self.service.graph.nodes[0].position_m
        self.service.record_ego_localization(
            EgoLocalization(
                vehicle_id="V01",
                session_id="physics-session",
                observed_tick=1,
                map_version=self.service.graph.map_version,
                position=MapPosition(x=start.x, y=start.y, z=0.0),
                heading_rad=0.0,
                speed_mps=0.0,
            )
        )
        self.service.vehicle_runtime["V01"].x += 5.0
        with self.assertRaisesRegex(ValueError, "ego_pose_off_graph_node"):
            self.service.create_request(passenger_command(self.service, pickup_index=1))

    def test_localized_route_eta_tracks_monotonic_observed_progress(self) -> None:
        assert self.service.graph is not None
        start = self.service.graph.nodes[0].position_m
        self.service.record_ego_localization(
            EgoLocalization(
                vehicle_id="V01",
                session_id="route-progress-session",
                observed_tick=1,
                map_version=self.service.graph.map_version,
                position=MapPosition(x=start.x, y=start.y, z=0.0),
                heading_rad=0.0,
                speed_mps=0.0,
            )
        )
        request = self.service.create_request(
            passenger_command(self.service, pickup_index=1, dropoff_index=0)
        ).request
        assert request is not None
        self.service.advance(0.05)
        initial_eta_s = request.eta_s
        runtime = self.service.vehicle_runtime["V01"]
        first, second = runtime.route_points[:2]
        midpoint = MapPosition(
            x=(first[0] + second[0]) / 2,
            y=(first[1] + second[1]) / 2,
            z=0.0,
        )

        def report(position: MapPosition, tick: int, speed_mps: float) -> None:
            self.assertTrue(
                self.service.record_ego_localization(
                    EgoLocalization(
                        vehicle_id="V01",
                        session_id="route-progress-session",
                        observed_tick=tick,
                        map_version=self.service.graph.map_version,
                        position=position,
                        heading_rad=0.0,
                        speed_mps=speed_mps,
                    )
                )
            )

        report(midpoint, 2, 1.0)
        observed_progress_m = runtime.route_progress_m
        self.service.advance(0.05)
        self.assertGreater(observed_progress_m, 0.0)
        self.assertLess(request.eta_s or 0.0, initial_eta_s or 0.0)

        report(MapPosition(x=start.x, y=start.y, z=0.0), 3, 0.0)
        self.service.advance(0.05)
        self.assertEqual(runtime.route_progress_m, observed_progress_m)

    def test_localized_vehicle_cannot_cancel_while_moving(self) -> None:
        assert self.service.graph is not None
        start = self.service.graph.nodes[0].position_m
        self.service.record_ego_localization(
            EgoLocalization(
                vehicle_id="V01",
                session_id="physics-session",
                observed_tick=1,
                map_version=self.service.graph.map_version,
                position=MapPosition(x=start.x, y=start.y, z=0.0),
                heading_rad=0.0,
                speed_mps=0.0,
            )
        )
        request = self.service.create_request(passenger_command(self.service)).request
        assert request is not None
        moving_position = self.service.graph.nodes[0].position_m
        self.service.record_ego_localization(
            EgoLocalization(
                vehicle_id="V01",
                session_id="physics-session",
                observed_tick=2,
                map_version=self.service.graph.map_version,
                position=MapPosition(x=moving_position.x + 5.0, y=moving_position.y, z=0.0),
                heading_rad=1.57,
                speed_mps=1.0,
            )
        )
        with self.assertRaisesRegex(ValueError, "vehicle_not_stopped_at_route_node"):
            self.service.cancel_request(request.id, "cancel-moving", request.owner_id)
        self.assertEqual(request.status, RequestStatus.ASSIGNED)
        self.assertFalse(self.service.vehicles["V01"].available)

    def test_websocket_accepts_ego_localization_only_for_operator(self) -> None:
        assert self.service.graph is not None
        payload = {
            "type": "ego_localization",
            "vehicleId": "V01",
            "sessionId": "operator-session",
            "observedTick": 9,
            "mapVersion": self.service.graph.map_version,
            "position": {"x": 15.0, "y": 20.0, "z": 0.2},
            "headingRad": 1.25,
            "speedMps": 2.0,
        }
        operator_socket = FakeWebSocket()
        asyncio.run(
            handle_client_message(
                operator_socket, self.service, "PC_Operator", None, json.dumps(payload)
            )
        )
        self.assertEqual(operator_socket.messages[0]["type"], "ego_localization_ack")
        self.assertTrue(operator_socket.messages[0]["accepted"])
        self.assertEqual(operator_socket.messages[0]["sessionId"], "operator-session")
        self.assertEqual(self.service.ego_localizations["V01"].position.y, 20.0)

        mobile_socket = FakeWebSocket()
        asyncio.run(
            handle_client_message(
                mobile_socket, self.service, "Mobile_Passenger", "mobile-1", json.dumps(payload)
            )
        )
        self.assertFalse(mobile_socket.messages[0]["accepted"])
        self.assertEqual(mobile_socket.messages[0]["errorCode"], "role_not_allowed")

        malformed_socket = FakeWebSocket()
        asyncio.run(
            handle_client_message(
                malformed_socket,
                self.service,
                "PC_Operator",
                None,
                json.dumps({**payload, "position": {"x": float("nan"), "y": 0, "z": 0}}),
            )
        )
        self.assertEqual(malformed_socket.messages[0]["code"], "invalid_ego_localization")

    def test_websocket_idle_snapshot_cadence_is_ten_hertz(self) -> None:
        socket = SnapshotCadenceWebSocket()
        asyncio.run(serve_client_socket(socket, self.service))

        self.assertEqual(len(socket.snapshot_times), 3)
        intervals = [
            right - left
            for left, right in pairwise(socket.snapshot_times)
        ]
        self.assertTrue(all(interval >= 0.075 for interval in intervals))
        self.assertTrue(all(interval < 0.2 for interval in intervals))

    def test_step_free_routing_avoids_non_step_free_short_edge(self) -> None:
        command = passenger_command(
            self.service,
            dropoff_index=4,
            service_needs=ServiceNeeds(requires_step_free=True, wheelchair_slots=1),
        )
        result = self.service.create_request(command)
        self.assertEqual(result.request.pickup_stop_id, "fixture_stop_2")
        self.assertEqual(result.request.dropoff_stop_id, "fixture_stop_5")
        # n1→n2→n5 is shorter but e25 is not step-free; the selected route takes the accessible loop.
        route = self.service.vehicle_runtime["V01"].route_points
        directed_segments = list(pairwise(route))
        self.assertNotIn(((100.0, 0.0), (100.0, 120.0)), directed_segments)

    def test_duplicate_command_is_idempotent_but_scoped_to_owner(self) -> None:
        first = self.service.create_request(passenger_command(self.service, "same-id"))
        duplicate = self.service.create_request(passenger_command(self.service, "same-id"))
        self.service.advance(20.0)
        late_duplicate = self.service.create_request(passenger_command(self.service, "same-id"))
        other_owner = self.service.create_request(
            passenger_command(self.service, "same-id", owner_id="another-passenger")
        )
        self.assertEqual(first.request.id, duplicate.request.id)
        self.assertEqual(late_duplicate.request.status, RequestStatus.ASSIGNED)
        self.assertNotEqual(first.request.id, other_owner.request.id)
        self.assertEqual(other_owner.request.status, RequestStatus.QUEUED)

    def test_reusing_create_command_id_with_different_payload_is_rejected(self) -> None:
        first = passenger_command(self.service, "same-id")
        self.service.create_request(first)
        changed = passenger_command(self.service, "same-id", dropoff_index=2)
        with self.assertRaisesRegex(ValueError, "command_id_reused_with_different_payload"):
            self.service.create_request(changed)

    def test_rejected_create_command_still_reserves_its_idempotency_key(self) -> None:
        impossible = passenger_command(self.service, "reserved-id", party_size=5)
        with self.assertRaisesRegex(ValueError, "no vehicle can satisfy"):
            self.service.create_request(impossible)
        changed = passenger_command(self.service, "reserved-id", party_size=4)
        with self.assertRaisesRegex(ValueError, "command_id_reused_with_different_payload"):
            self.service.create_request(changed)

    def test_reusing_cancel_command_id_for_another_request_is_rejected(self) -> None:
        first = self.service.create_request(passenger_command(self.service, "create-a"))
        self.service.cancel_request(first.request.id, "same-cancel-id", "fixture-passenger")
        other = self.service.create_request(
            passenger_command(self.service, "create-b", owner_id="fixture-passenger")
        )
        with self.assertRaisesRegex(ValueError, "command_id_reused_with_different_payload"):
            self.service.cancel_request(
                other.request.id, "same-cancel-id", "fixture-passenger"
            )

    def test_cancel_active_request_preserves_current_position_and_dispatches_queue(self) -> None:
        first = self.service.create_request(passenger_command(self.service, "first"))
        second = self.service.create_request(
            passenger_command(self.service, "second", owner_id="passenger-2", dropoff_index=2)
        )
        self.assertEqual(second.request.status, RequestStatus.QUEUED)
        self.service.advance(3.0)
        runtime = self.service.vehicle_runtime["V01"]
        stopped_position = (runtime.x, runtime.y)

        self.service.cancel_request(first.request.id, "cancel-first", "fixture-passenger")

        self.assertEqual(first.request.status, RequestStatus.CANCELLED)
        self.assertEqual((runtime.x, runtime.y), stopped_position)
        self.assertEqual(second.request.status, RequestStatus.ASSIGNED)
        self.assertEqual(second.request.vehicle_id, "V01")
        self.assertEqual(runtime.request_id, second.request.id)

    def test_capacity_impossible_request_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "no vehicle can satisfy"):
            self.service.create_request(passenger_command(self.service, party_size=5))

    def test_cargo_request_traverses_pickup_transit_and_completion(self) -> None:
        assert self.service.graph is not None
        command = CreateRequest(
            command_id="cargo-command",
            owner_id="operator",
            service_type="CARGO",
            pickup_landmark_id=self.service.graph.nodes[1].landmark_id,
            dropoff_landmark_id=self.service.graph.nodes[0].landmark_id,
            party_size=0,
            cargo_kg=5.0,
        )
        result = self.service.create_request(command)
        self.assertEqual(result.request.status, RequestStatus.ASSIGNED)
        self.assertEqual(result.request.service_type.value, "CARGO")

        self.service.advance(20.0)
        self.assertEqual(result.request.status, RequestStatus.PICKUP_SERVICE)
        self.service.advance(2.0)
        self.assertEqual(result.request.status, RequestStatus.IN_TRANSIT)
        self.service.advance(20.0)
        self.assertEqual(result.request.status, RequestStatus.DROPOFF_SERVICE)
        self.service.advance(2.0)
        self.assertEqual(result.request.status, RequestStatus.COMPLETED)
        self.assertTrue(self.service.vehicles["V01"].available)

    def test_cargo_request_over_capacity_is_rejected(self) -> None:
        assert self.service.graph is not None
        command = CreateRequest(
            command_id="cargo-over-capacity",
            owner_id="operator",
            service_type="CARGO",
            pickup_landmark_id=self.service.graph.nodes[1].landmark_id,
            dropoff_landmark_id=self.service.graph.nodes[0].landmark_id,
            party_size=0,
            cargo_kg=21.0,
        )
        with self.assertRaisesRegex(ValueError, "no vehicle can satisfy"):
            self.service.create_request(command)

    def test_runtime_respects_edge_speed_limit(self) -> None:
        assert self.service.graph is not None
        edges = list(self.service.graph.edges)
        edges[0] = edges[0].model_copy(update={"allowed_speed_mps": 2.0})
        self.service.graph = self.service.graph.model_copy(update={"edges": edges})
        request = self.service.create_request(passenger_command(self.service))
        assert request.request is not None
        runtime = self.service.vehicle_runtime["V01"]
        dropoff_duration = self.service._route_duration_between_stops(
            request.request.pickup_stop_id or "",
            request.request.dropoff_stop_id or "",
            service_type=request.request.service_type,
            requires_step_free=False,
        )
        self.assertEqual(
            request.request.eta_s,
            round(self.service._route_duration(runtime) + 2.0 + dropoff_duration, 1),
        )
        self.service.advance(1.0)
        runtime = self.service.vehicle_runtime["V01"]
        self.assertEqual(runtime.x, 2.0)
        self.assertEqual(runtime.speed_mps, 2.0)

    def test_queued_compatible_request_is_assigned_after_previous_completion(self) -> None:
        first = self.service.create_request(passenger_command(self.service, "first"))
        second = self.service.create_request(
            passenger_command(self.service, "second", owner_id="passenger-2", dropoff_index=2)
        )
        self.assertEqual(second.request.status, RequestStatus.QUEUED)
        for delta_s in (20.0, 2.0, 20.0, 2.0):
            self.service.advance(delta_s)
        self.assertEqual(first.request.status, RequestStatus.COMPLETED)
        self.assertEqual(second.request.status, RequestStatus.ASSIGNED)
        self.assertEqual(second.request.vehicle_id, "V01")

    def test_queued_mobility_and_deadline_priority_precedes_older_cargo(self) -> None:
        assert self.service.graph is not None
        first = self.service.create_request(passenger_command(self.service, "active"))
        self.service.simulation_time_s = 120.0

        cargo_pickup = self.service.graph.nodes[2]
        cargo_dropoff = self.service.graph.nodes[3]
        older_cargo = self.service.create_request(
            CreateRequest(
                command_id="older-cargo",
                owner_id="cargo-owner",
                service_type=ServiceType.CARGO,
                pickup_landmark_id=cargo_pickup.landmark_id,
                dropoff_landmark_id=cargo_dropoff.landmark_id,
                party_size=0,
                cargo_kg=5.0,
            )
        )
        urgent_passenger = self.service.create_request(
            CreateRequest(
                command_id="urgent-mobility-passenger",
                owner_id="mobility-owner",
                service_type=ServiceType.PASSENGER,
                pickup_landmark_id=cargo_pickup.landmark_id,
                dropoff_landmark_id=cargo_dropoff.landmark_id,
                service_needs=ServiceNeeds(requires_step_free=True, wheelchair_slots=1),
                party_size=1,
                latest_arrival_s=300.0,
            )
        )
        self.assertEqual(older_cargo.request.status, RequestStatus.QUEUED)
        self.assertEqual(urgent_passenger.request.status, RequestStatus.QUEUED)

        for delta_s in (20.0, 2.0, 20.0, 2.0):
            self.service.advance(delta_s)

        self.assertEqual(first.request.status, RequestStatus.COMPLETED)
        self.assertEqual(urgent_passenger.request.status, RequestStatus.ASSIGNED)
        self.assertEqual(urgent_passenger.request.vehicle_id, "V01")
        self.assertEqual(older_cargo.request.status, RequestStatus.QUEUED)

    def test_synthetic_fleet_assigns_and_completes_three_independent_missions(self) -> None:
        fleet = MobilityService.synthetic_fleet_fixture()
        wheelchair = fleet.create_request(
            passenger_command(
                fleet,
                "fleet-wheelchair",
                pickup_index=1,
                dropoff_index=0,
                service_needs=ServiceNeeds(requires_step_free=True, wheelchair_slots=1),
            )
        ).request
        passenger = fleet.create_request(
            passenger_command(
                fleet,
                "fleet-passenger",
                owner_id="fleet-passenger-2",
                pickup_index=3,
                dropoff_index=2,
            )
        ).request
        cargo_pickup = fleet.graph.nodes[4]
        cargo_dropoff = fleet.graph.nodes[5]
        cargo = fleet.create_request(
            CreateRequest(
                command_id="fleet-cargo",
                owner_id="fleet-cargo-owner",
                service_type=ServiceType.CARGO,
                pickup_landmark_id=cargo_pickup.landmark_id,
                dropoff_landmark_id=cargo_dropoff.landmark_id,
                party_size=0,
                cargo_kg=5.0,
            )
        ).request

        assert wheelchair is not None and passenger is not None and cargo is not None
        self.assertEqual(set(fleet.vehicle_runtime), {"V01", "V02", "V03"})
        self.assertEqual(
            {wheelchair.vehicle_id, passenger.vehicle_id, cargo.vehicle_id},
            {"V01", "V02", "V03"},
        )
        self.assertEqual(
            {wheelchair.status, passenger.status, cargo.status},
            {RequestStatus.ASSIGNED},
        )

        for _ in range(120):
            fleet.advance(1.0)
            if all(
                request.status is RequestStatus.COMPLETED
                for request in (wheelchair, passenger, cargo)
            ):
                break
        self.assertEqual(
            {wheelchair.status, passenger.status, cargo.status},
            {RequestStatus.COMPLETED},
        )
        self.assertTrue(all(vehicle.available for vehicle in fleet.vehicles.values()))

    def test_synthetic_fleet_dispatches_queued_work_to_newly_available_compatible_vehicle(self) -> None:
        fleet = MobilityService.synthetic_fleet_fixture()
        first_passenger = fleet.create_request(
            passenger_command(fleet, "active-v01", pickup_index=0, dropoff_index=1)
        ).request
        second_passenger = fleet.create_request(
            passenger_command(
                fleet,
                "active-v02",
                owner_id="active-v02-owner",
                pickup_index=2,
                dropoff_index=3,
            )
        ).request
        active_cargo_pickup = fleet.graph.nodes[5]
        active_cargo_dropoff = fleet.graph.nodes[4]
        active_cargo = fleet.create_request(
            CreateRequest(
                command_id="active-v03",
                owner_id="active-v03-owner",
                service_type=ServiceType.CARGO,
                pickup_landmark_id=active_cargo_pickup.landmark_id,
                dropoff_landmark_id=active_cargo_dropoff.landmark_id,
                party_size=0,
                cargo_kg=5.0,
            )
        ).request

        waiting_cargo_pickup = fleet.graph.nodes[1]
        waiting_cargo_dropoff = fleet.graph.nodes[2]
        waiting_cargo = fleet.create_request(
            CreateRequest(
                command_id="waiting-cargo",
                owner_id="waiting-cargo-owner",
                service_type=ServiceType.CARGO,
                pickup_landmark_id=waiting_cargo_pickup.landmark_id,
                dropoff_landmark_id=waiting_cargo_dropoff.landmark_id,
                party_size=0,
                cargo_kg=5.0,
            )
        ).request
        urgent_pickup = fleet.graph.nodes[3]
        urgent_dropoff = fleet.graph.nodes[2]
        urgent_mobility = fleet.create_request(
            CreateRequest(
                command_id="waiting-wheelchair",
                owner_id="waiting-wheelchair-owner",
                service_type=ServiceType.PASSENGER,
                pickup_landmark_id=urgent_pickup.landmark_id,
                dropoff_landmark_id=urgent_dropoff.landmark_id,
                service_needs=ServiceNeeds(requires_step_free=True, wheelchair_slots=1),
                party_size=1,
                latest_arrival_s=240.0,
            )
        ).request

        assert all(
            request is not None
            for request in (first_passenger, second_passenger, active_cargo, waiting_cargo, urgent_mobility)
        )
        self.assertEqual(
            {first_passenger.vehicle_id, second_passenger.vehicle_id, active_cargo.vehicle_id},
            {"V01", "V02", "V03"},
        )
        self.assertEqual(waiting_cargo.status, RequestStatus.QUEUED)
        self.assertEqual(urgent_mobility.status, RequestStatus.QUEUED)

        for _ in range(60):
            fleet.advance(1.0)
        self.assertEqual(urgent_mobility.status, RequestStatus.ASSIGNED)
        self.assertEqual(urgent_mobility.vehicle_id, "V01")
        self.assertEqual(waiting_cargo.vehicle_id, "V03")

        for _ in range(250):
            fleet.advance(1.0)
            if (
                waiting_cargo.status is RequestStatus.COMPLETED
                and urgent_mobility.status is RequestStatus.COMPLETED
            ):
                break
        self.assertEqual(waiting_cargo.status, RequestStatus.COMPLETED)
        self.assertEqual(urgent_mobility.status, RequestStatus.COMPLETED)
        self.assertEqual(waiting_cargo.vehicle_id, "V03")

    def test_fairness_reserves_each_third_assignment_for_long_waiting_compatible_request(self) -> None:
        service = MobilityService.synthetic_fixture()
        first = service.create_request(
            passenger_command(service, "fairness-first", pickup_index=0, dropoff_index=1)
        ).request
        queued_passenger = service.create_request(
            passenger_command(
                service,
                "fairness-second",
                owner_id="fairness-second-owner",
                pickup_index=1,
                dropoff_index=0,
            )
        ).request
        assert first is not None and queued_passenger is not None

        for _ in range(180):
            if queued_passenger.status is RequestStatus.ASSIGNED:
                break
            service.advance(1.0)
        self.assertEqual(first.status, RequestStatus.COMPLETED)
        self.assertEqual(queued_passenger.status, RequestStatus.ASSIGNED)
        self.assertEqual(service.assignments_since_fairness, 2)

        for _ in range(180):
            if queued_passenger.status is RequestStatus.DROPOFF_SERVICE:
                break
            service.advance(1.0)
        self.assertEqual(queued_passenger.status, RequestStatus.DROPOFF_SERVICE)

        cargo_pickup = service.graph.nodes[2]
        cargo_dropoff = service.graph.nodes[3]
        older_cargo = service.create_request(
            CreateRequest(
                command_id="fairness-aged-cargo",
                owner_id="fairness-aged-cargo-owner",
                service_type=ServiceType.CARGO,
                pickup_landmark_id=cargo_pickup.landmark_id,
                dropoff_landmark_id=cargo_dropoff.landmark_id,
                party_size=0,
                cargo_kg=5.0,
            )
        ).request
        recent_mobility = service.create_request(
            passenger_command(
                service,
                "fairness-recent-mobility",
                owner_id="fairness-recent-mobility-owner",
                pickup_index=3,
                dropoff_index=2,
                service_needs=ServiceNeeds(requires_step_free=True, wheelchair_slots=1),
            )
        ).request
        assert older_cargo is not None and recent_mobility is not None
        self.assertEqual(older_cargo.status, RequestStatus.QUEUED)
        self.assertEqual(recent_mobility.status, RequestStatus.QUEUED)
        completed_at_s = service.now_s() + 2.0
        older_cargo.created_s = completed_at_s - 600.0
        recent_mobility.created_s = completed_at_s - 599.0

        service.advance(2.0)

        self.assertEqual(older_cargo.status, RequestStatus.ASSIGNED)
        self.assertEqual(recent_mobility.status, RequestStatus.QUEUED)
        self.assertEqual(older_cargo.vehicle_id, "V01")
        self.assertEqual(service.assignments_since_fairness, 0)

    def test_cancel_wrong_owner_and_cancel_after_pickup_are_rejected(self) -> None:
        request = self.service.create_request(passenger_command(self.service))
        with self.assertRaises(KeyError):
            self.service.cancel_request(request.request.id, "cancel-wrong-owner", "intruder")
        self.service.advance(20.0)
        with self.assertRaisesRegex(ValueError, "current state"):
            self.service.cancel_request(request.request.id, "cancel-too-late", "fixture-passenger")

    def test_snapshot_contains_live_route_and_isolated_mobile_requests(self) -> None:
        request = self.service.create_request(passenger_command(self.service))
        self.service.advance(1.0)
        snapshot = make_snapshot(self.service, "Mobile_Passenger", "fixture-passenger", 7)
        self.assertEqual(snapshot["sequence"], 7)
        self.assertEqual(snapshot["mapVersion"], self.service.graph.map_version)
        self.assertEqual(snapshot["requests"][0]["id"], request.request.id)
        self.assertEqual(snapshot["vehicles"][0]["id"], "V01")
        self.assertEqual(snapshot["vehicles"][0]["missionState"], "TO_PICKUP")
        self.assertNotEqual(snapshot["vehicles"][0]["position"]["x"], 0.0)
        self.assertGreaterEqual(len(snapshot["routes"][0]["polyline"]), 2)
        self.assertEqual(
            len(snapshot["routes"][0]["segmentSpeedsMps"]),
            len(snapshot["routes"][0]["polyline"]) - 1,
        )
        self.assertTrue(all(speed > 0 for speed in snapshot["routes"][0]["segmentSpeedsMps"]))
        private_snapshot = make_snapshot(self.service, "Mobile_Passenger", "other-user", 8)
        self.assertEqual(private_snapshot["requests"], [])
        self.assertEqual(private_snapshot["vehicles"], [])

    def test_completed_passenger_does_not_see_vehicle_reassigned_to_another_owner(self) -> None:
        first = self.service.create_request(passenger_command(self.service, "first"))
        for delta_s in (20.0, 2.0, 20.0, 2.0):
            self.service.advance(delta_s)
        self.assertEqual(first.request.status, RequestStatus.COMPLETED)
        second = self.service.create_request(
            passenger_command(self.service, "second", owner_id="passenger-2")
        )
        self.assertEqual(second.request.status, RequestStatus.ASSIGNED)

        former_owner = make_snapshot(self.service, "Mobile_Passenger", "fixture-passenger", 10)
        current_owner = make_snapshot(self.service, "Mobile_Passenger", "passenger-2", 11)
        self.assertEqual(former_owner["vehicles"], [])
        self.assertEqual(former_owner["routes"], [])
        self.assertEqual(current_owner["vehicles"][0]["requestId"], second.request.id)

    def test_websocket_create_ack_and_snapshot_share_service_state(self) -> None:
        assert self.service.graph is not None
        payload = {
            "type": "create_request",
            "messageId": "client-message-1",
            "pickupLandmarkId": self.service.graph.nodes[1].landmark_id,
            "dropoffLandmarkId": self.service.graph.nodes[2].landmark_id,
            "serviceNeeds": {"requiresStepFree": False, "wheelchairSlots": 0},
        }
        socket = FakeWebSocket()
        closed = asyncio.run(
            handle_client_message(
                socket,
                self.service,
                "Mobile_Passenger",
                "mobile-1",
                json.dumps(payload),
            )
        )
        self.assertFalse(closed)
        self.assertEqual(socket.messages[0]["type"], "command_ack")
        self.assertTrue(socket.messages[0]["accepted"])
        self.assertEqual(socket.messages[0]["request"]["status"], "ASSIGNED")
        self.assertEqual(
            make_snapshot(self.service, "Mobile_Passenger", "mobile-1", 1)["requests"][0]["ownerId"],
            "mobile-1",
        )

    def test_websocket_reused_message_id_with_changed_payload_is_rejected(self) -> None:
        assert self.service.graph is not None
        socket = FakeWebSocket()
        first_payload = {
            "type": "create_request",
            "messageId": "same-message",
            "pickupLandmarkId": self.service.graph.nodes[1].landmark_id,
            "dropoffLandmarkId": self.service.graph.nodes[2].landmark_id,
            "serviceNeeds": {"requiresStepFree": False, "wheelchairSlots": 0},
        }
        changed_payload = {
            **first_payload,
            "dropoffLandmarkId": self.service.graph.nodes[3].landmark_id,
        }
        for payload in (first_payload, changed_payload):
            asyncio.run(
                handle_client_message(
                    socket,
                    self.service,
                    "Mobile_Passenger",
                    "mobile-1",
                    json.dumps(payload),
                )
            )
        self.assertTrue(socket.messages[0]["accepted"])
        self.assertFalse(socket.messages[1]["accepted"])
        self.assertEqual(socket.messages[1]["errorCode"], "idempotency_conflict")
        self.assertEqual(len(self.service.requests), 1)

    def test_unrecognized_role_cannot_issue_commands(self) -> None:
        socket = FakeWebSocket()
        closed = asyncio.run(
            handle_client_message(
                socket,
                self.service,
                "Unknown_Role",
                None,
                json.dumps({"type": "create_request", "messageId": "operator-command"}),
            )
        )
        self.assertFalse(closed)
        self.assertEqual(socket.messages[0]["errorCode"], "role_not_allowed")

    def test_invalid_same_landmark_command_is_rejected_without_mutating_state(self) -> None:
        assert self.service.graph is not None
        landmark_id = self.service.graph.nodes[0].landmark_id
        socket = FakeWebSocket()
        asyncio.run(
            handle_client_message(
                socket,
                self.service,
                "Mobile_Passenger",
                "mobile-1",
                json.dumps({
                    "type": "create_request",
                    "messageId": "invalid-same-stop",
                    "pickupLandmarkId": landmark_id,
                    "dropoffLandmarkId": landmark_id,
                }),
            )
        )
        self.assertFalse(socket.messages[0]["accepted"])
        self.assertEqual(socket.messages[0]["errorCode"], "invalid_request")
        self.assertEqual(self.service.requests, {})


if __name__ == "__main__":
    unittest.main()
