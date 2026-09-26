from __future__ import annotations

import sys
import time
import unittest
from pathlib import Path
from unittest.mock import patch

from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from campus_sim.api import create_app
from campus_sim.cli import main as cli_main
from campus_sim.realtime import MAX_MESSAGE_BYTES
from campus_sim.road_graph import RoadGraphLoadError
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]


class ApiIntegrationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.service = MobilityService.synthetic_fixture()
        self.client = TestClient(create_app(self.service))
        self.client.__enter__()

    def tearDown(self) -> None:
        self.client.__exit__(None, None, None)

    def test_server_clock_advances_without_connected_clients(self) -> None:
        time.sleep(0.13)
        self.assertGreaterEqual(self.service.now_s(), 0.1)
        self.assertLess(self.service.now_s(), 0.3)

    def test_app_can_start_from_an_alternate_synthetic_graph(self) -> None:
        benchmark_map = ROOT / "maps/fixtures/campus-synthetic-benchmark-11.json"
        app = create_app(map_path=benchmark_map)
        self.assertEqual(set(app.state.service.vehicle_runtime), {"V01", "V02", "V03"})
        with TestClient(app) as client:
            health = client.get("/health").json()
            self.assertEqual(health["project_version"], "0.3.0.0")
            self.assertEqual(health["schema_version"], "3")
            self.assertEqual(health["map_version"], "synthetic-benchmark-11node-v1")
            self.assertEqual(health["map_data_status"], "SYNTHETIC_FIXTURE")
            self.assertEqual(health["node_count"], 11)
            self.assertGreater(health["edge_count"], 0)
            landmarks = client.get("/v1/landmarks").json()
            self.assertEqual(len(landmarks), 11)
            self.assertEqual(landmarks[0]["id"], "benchmark_landmark_01")
            created = client.post(
                "/v1/requests",
                json={
                    "command_id": "alternate-map-create",
                    "owner_id": "alternate-map-test",
                    "service_type": "PASSENGER",
                    "pickup_landmark_id": "benchmark_landmark_02",
                    "dropoff_landmark_id": "benchmark_landmark_01",
                    "party_size": 1,
                },
            )
            self.assertEqual(created.status_code, 201)
            self.assertEqual(created.json()["request"]["status"], "ASSIGNED")

    def test_default_app_uses_the_three_vehicle_synthetic_fleet(self) -> None:
        app = create_app()
        self.assertEqual(set(app.state.service.vehicle_runtime), {"V01", "V02", "V03"})

    def test_default_websocket_assigns_three_clients_to_compatible_vehicles(self) -> None:
        app = create_app()
        with TestClient(app) as client:
            service = app.state.service
            assert service.graph is not None
            pickup = service.graph.nodes[1].landmark_id
            dropoff = service.graph.nodes[0].landmark_id
            cases = (
                (
                    "Mobile_Passenger",
                    "mobility-client",
                    "PASSENGER",
                    {"requiresStepFree": True, "wheelchairSlots": 1},
                ),
                (
                    "Mobile_Passenger",
                    "standard-client",
                    "PASSENGER",
                    {"requiresStepFree": False, "wheelchairSlots": 0},
                ),
                ("PC_Operator", None, "CARGO", None),
            )
            assigned_vehicle_ids: list[str] = []

            for index, (role, subscriber_id, service_type, service_needs) in enumerate(cases, start=1):
                with client.websocket_connect("/v1/client/ws") as websocket:
                    subscription = {
                        "type": "subscribe",
                        "schemaVersion": 3,
                        "projectVersion": "0.3.0.0",
                        "role": role,
                    }
                    if subscriber_id is not None:
                        subscription["subscriberId"] = subscriber_id
                    websocket.send_json(subscription)
                    self.assertEqual(websocket.receive_json()["type"], "connected")
                    initial_snapshot = websocket.receive_json()
                    self.assertEqual(initial_snapshot["type"], "snapshot")

                    command = {
                        "type": "create_request",
                        "messageId": f"three-client-request-{index}",
                        "serviceType": service_type,
                        "pickupLandmarkId": pickup,
                        "dropoffLandmarkId": dropoff,
                    }
                    if service_needs is not None:
                        command["serviceNeeds"] = service_needs
                    else:
                        command["cargoKg"] = 4.0
                    websocket.send_json(command)

                    ack = websocket.receive_json()
                    snapshot = websocket.receive_json()
                    self.assertEqual(ack["type"], "command_ack")
                    self.assertTrue(ack["accepted"], ack)
                    self.assertEqual(snapshot["type"], "snapshot")
                    self.assertEqual(snapshot["snapshot"]["role"], role)
                    if subscriber_id is not None:
                        self.assertEqual(snapshot["snapshot"]["subscriberId"], subscriber_id)
                        self.assertEqual(len(snapshot["snapshot"]["requests"]), 1)
                        self.assertEqual(
                            snapshot["snapshot"]["requests"][0]["id"], ack["request"]["id"]
                        )
                    else:
                        self.assertEqual(len(snapshot["snapshot"]["requests"]), 3)
                    assigned_vehicle_ids.append(ack["request"]["vehicleId"])

            self.assertEqual(assigned_vehicle_ids, ["V01", "V02", "V03"])
            self.assertEqual(len(service.requests), 3)
            self.assertEqual(set(service.vehicle_runtime), set(assigned_vehicle_ids))

    def test_app_map_path_rejects_candidate_only_map_data(self) -> None:
        candidate_map = ROOT / "maps/candidates/inha-campus-osm-road-candidates.json"
        with self.assertRaises(RoadGraphLoadError):
            create_app(map_path=candidate_map)

    def test_app_rejects_both_service_and_map_path(self) -> None:
        with self.assertRaisesRegex(ValueError, "not both"):
            create_app(
                self.service,
                map_path=ROOT / "maps/fixtures/campus-synthetic-6.json",
            )

    def test_serve_cli_passes_selected_synthetic_graph_to_api(self) -> None:
        benchmark_map = ROOT / "maps/fixtures/campus-synthetic-benchmark-11.json"
        with patch.object(
            sys,
            "argv",
            ["campus-sim", "serve", "--map", str(benchmark_map), "--port", "8999"],
        ), patch("uvicorn.run") as run_server:
            cli_main()

        app = run_server.call_args.args[0]
        self.assertEqual(app.state.service.graph.map_version, "synthetic-benchmark-11node-v1")
        self.assertEqual(run_server.call_args.kwargs["port"], 8999)

    def test_http_health_landmarks_create_owner_filter_and_cancel(self) -> None:
        assert self.service.graph is not None
        health_response = self.client.get("/health")
        self.assertEqual(health_response.status_code, 200)
        self.assertEqual(health_response.json()["map_version"], self.service.graph.map_version)
        self.assertEqual(health_response.json()["map_data_status"], "SYNTHETIC_FIXTURE")
        self.assertEqual(health_response.json()["node_count"], len(self.service.graph.nodes))
        landmarks_response = self.client.get("/v1/landmarks")
        self.assertEqual(landmarks_response.status_code, 200)
        self.assertEqual(len(landmarks_response.json()), 6)

        create_response = self.client.post(
            "/v1/requests",
            json={
                "command_id": "http-create-1",
                "owner_id": "passenger-http",
                "service_type": "PASSENGER",
                "pickup_landmark_id": self.service.graph.nodes[1].landmark_id,
                "dropoff_landmark_id": self.service.graph.nodes[0].landmark_id,
                "party_size": 1,
            },
        )
        self.assertEqual(create_response.status_code, 201)
        created = create_response.json()["request"]
        self.assertEqual(created["status"], "ASSIGNED")
        self.assertEqual(
            self.client.get("/v1/owners/passenger-http/requests").json()[0]["id"],
            created["id"],
        )
        self.assertEqual(self.client.get("/v1/owners/other/requests").json(), [])

        foreign_cancel = self.client.post(
            f"/v1/requests/{created['id']}/cancel",
            params={"command_id": "http-cancel-foreign", "owner_id": "other"},
        )
        self.assertEqual(foreign_cancel.status_code, 404)
        cancelled = self.client.post(
            f"/v1/requests/{created['id']}/cancel",
            params={"command_id": "http-cancel-1", "owner_id": "passenger-http"},
        )
        self.assertEqual(cancelled.status_code, 200)
        self.assertEqual(cancelled.json()["request"]["status"], "CANCELLED")

    def test_websocket_subscribe_ping_create_ack_and_snapshot(self) -> None:
        assert self.service.graph is not None
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.2.1.1",
                    "role": "Mobile_Passenger",
                    "subscriberId": "mobile-asgi-1",
                }
            )
            connected = websocket.receive_json()
            first_snapshot = websocket.receive_json()
            self.assertEqual(connected["type"], "connected")
            self.assertFalse(connected["authenticated"])
            self.assertEqual(first_snapshot["type"], "snapshot")
            self.assertEqual(first_snapshot["snapshot"]["sequence"], 0)

            websocket.send_json({"type": "ping"})
            self.assertEqual(websocket.receive_json()["type"], "pong")
            websocket.receive_json()  # Snapshot follows each handled client message.

            websocket.send_json(
                {
                    "type": "create_request",
                    "messageId": "mobile-asgi-command-1",
                    "pickupLandmarkId": self.service.graph.nodes[1].landmark_id,
                    "dropoffLandmarkId": self.service.graph.nodes[0].landmark_id,
                    "serviceNeeds": {"requiresStepFree": False, "wheelchairSlots": 0},
                }
            )
            ack = websocket.receive_json()
            snapshot = websocket.receive_json()
            self.assertEqual(ack["type"], "command_ack")
            self.assertTrue(ack["accepted"])
            self.assertEqual(ack["messageId"], "mobile-asgi-command-1")
            self.assertEqual(snapshot["snapshot"]["requests"][0]["id"], ack["request"]["id"])
            self.assertEqual(snapshot["snapshot"]["role"], "Mobile_Passenger")

    def test_operator_websocket_records_ego_localization(self) -> None:
        assert self.service.graph is not None
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.3.0.0",
                    "role": "PC_Operator",
                }
            )
            self.assertEqual(websocket.receive_json()["type"], "connected")
            websocket.receive_json()
            websocket.send_json(
                {
                    "type": "ego_localization",
                    "vehicleId": "V01",
                    "sessionId": "integration-session",
                    "observedTick": 42,
                    "mapVersion": self.service.graph.map_version,
                    "position": {"x": 15.0, "y": -20.0, "z": 0.35},
                    "headingRad": 1.25,
                    "speedMps": 2.0,
                }
            )
            ack = websocket.receive_json()
            self.assertEqual(ack["type"], "ego_localization_ack")
            self.assertTrue(ack["accepted"])
            self.assertEqual(ack["sessionId"], "integration-session")
            self.assertEqual(ack["observedTick"], 42)
            self.assertEqual(self.service.ego_localizations["V01"].position.y, -20.0)
            snapshot = websocket.receive_json()
            vehicle = next(item for item in snapshot["snapshot"]["vehicles"] if item["id"] == "V01")
            self.assertEqual(vehicle["position"], {"x": 15.0, "y": -20.0, "z": 0.35})
            self.assertEqual(vehicle["headingRad"], 1.25)

    def test_operator_websocket_accepts_sensor_frame_for_current_ego_pose(self) -> None:
        assert self.service.graph is not None
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.3.0.0",
                    "role": "PC_Operator",
                }
            )
            self.assertEqual(websocket.receive_json()["type"], "connected")
            websocket.receive_json()
            websocket.send_json(
                {
                    "type": "ego_localization",
                    "vehicleId": "V01",
                    "sessionId": "sensor-ws-session",
                    "observedTick": 8,
                    "mapVersion": self.service.graph.map_version,
                    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
                    "headingRad": 0.0,
                    "speedMps": 0.0,
                }
            )
            self.assertTrue(websocket.receive_json()["accepted"])
            websocket.receive_json()

            frame = {
                "type": "sensor_observation",
                "vehicleId": "V01",
                "sensorId": "front-lidar",
                "sensorType": "LIDAR_2D",
                "sessionId": "sensor-ws-session",
                "mapVersion": self.service.graph.map_version,
                "observedTick": 3,
                "egoPoseTick": 8,
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
            websocket.send_json(frame)
            ack = websocket.receive_json()
            self.assertEqual(ack["type"], "sensor_observation_ack")
            self.assertTrue(ack["accepted"])
            self.assertEqual(ack["sensorId"], "front-lidar")
            websocket.receive_json()
            self.assertEqual(
                len(self.service.sensor_observations[("V01", "front-lidar")].detections), 1
            )

            websocket.send_json({**frame, "observedTick": 3})
            duplicate_ack = websocket.receive_json()
            self.assertFalse(duplicate_ack["accepted"])
            self.assertEqual(duplicate_ack["errorCode"], "stale_observation")
            websocket.receive_json()

            websocket.send_json(
                {**frame, "observedTick": 4, "egoPoseTick": 7}
            )
            mismatch_ack = websocket.receive_json()
            self.assertFalse(mismatch_ack["accepted"])
            self.assertEqual(mismatch_ack["errorCode"], "ego_pose_tick_mismatch")

        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.3.0.0",
                    "role": "Mobile_Passenger",
                    "subscriberId": "sensor-injection-check",
                }
            )
            self.assertEqual(websocket.receive_json()["type"], "connected")
            websocket.receive_json()
            websocket.send_json(frame)
            forbidden_ack = None
            for _ in range(10):
                message = websocket.receive_json()
                if message.get("type") == "sensor_observation_ack":
                    forbidden_ack = message
                    break
                self.assertEqual(message.get("type"), "snapshot")
            self.assertIsNotNone(forbidden_ack, "The mobile role must receive a sensor rejection ACK.")
            self.assertFalse(forbidden_ack["accepted"])
            self.assertEqual(forbidden_ack["errorCode"], "role_not_allowed")
            self.assertEqual(
                len(self.service.sensor_observations[("V01", "front-lidar")].detections), 1
            )

    def test_operator_websocket_localization_completes_synthetic_trip(self) -> None:
        assert self.service.graph is not None
        pickup = self.service.graph.nodes[0].position_m
        dropoff = self.service.graph.nodes[1].position_m
        tick = 0
        sensor_tick = 0

        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.3.0.0",
                    "role": "PC_Operator",
                }
            )
            self.assertEqual(websocket.receive_json()["type"], "connected")
            websocket.receive_json()

            def report(position) -> dict:
                nonlocal tick, sensor_tick
                pose_tick = tick
                websocket.send_json(
                    {
                        "type": "ego_localization",
                        "vehicleId": "V01",
                        "sessionId": "full-trip-integration",
                        "observedTick": pose_tick,
                        "mapVersion": self.service.graph.map_version,
                        "position": {
                            "x": position.x,
                            "y": position.y,
                            "z": 0.0,
                        },
                        "headingRad": 0.0,
                        "speedMps": 0.0,
                    }
                )
                tick += 1
                ack = websocket.receive_json()
                self.assertEqual(ack["type"], "ego_localization_ack")
                self.assertTrue(ack["accepted"])
                websocket.send_json(
                    {
                        "type": "sensor_observation",
                        "vehicleId": "V01",
                        "sensorId": "front-lidar",
                        "sensorType": "LIDAR_2D",
                        "sessionId": "full-trip-integration",
                        "mapVersion": self.service.graph.map_version,
                        "observedTick": sensor_tick,
                        "egoPoseTick": pose_tick,
                        "valid": True,
                        "detections": [],
                    }
                )
                sensor_tick += 1
                sensor_ack = websocket.receive_json()
                self.assertEqual(sensor_ack["type"], "sensor_observation_ack")
                self.assertTrue(sensor_ack["accepted"])
                return websocket.receive_json()["snapshot"]

            report(pickup)
            websocket.send_json(
                {
                    "type": "create_request",
                    "messageId": "full-trip-request",
                    "serviceType": "PASSENGER",
                    "pickupLandmarkId": self.service.graph.nodes[0].landmark_id,
                    "dropoffLandmarkId": self.service.graph.nodes[1].landmark_id,
                    "partySize": 1,
                }
            )
            command_ack = websocket.receive_json()
            self.assertEqual(command_ack["type"], "command_ack")
            self.assertTrue(command_ack["accepted"])
            request_id = command_ack["request"]["id"]
            snapshot = websocket.receive_json()["snapshot"]
            pickup_vehicle = next(item for item in snapshot["vehicles"] if item["id"] == "V01")
            pickup_route_id = pickup_vehicle["routeId"]
            self.assertTrue(pickup_route_id)

            for _ in range(60):
                request = next(item for item in snapshot["requests"] if item["id"] == request_id)
                if request["status"] == "IN_TRANSIT":
                    break
                snapshot = report(pickup)
            self.assertEqual(request["status"], "IN_TRANSIT")
            vehicle = next(item for item in snapshot["vehicles"] if item["id"] == "V01")
            self.assertEqual(vehicle["motionState"], "DRIVING")
            route_id = vehicle["routeId"]
            self.assertNotEqual(
                route_id,
                pickup_route_id,
                "A newly planned dropoff route must use a fresh route ID.",
            )
            route = next(item for item in snapshot["routes"] if item["id"] == route_id)
            self.assertGreaterEqual(len(route["polyline"]), 2)

            for _ in range(60):
                snapshot = report(dropoff)
                request = next(item for item in snapshot["requests"] if item["id"] == request_id)
                if request["status"] == "COMPLETED":
                    break
            self.assertEqual(request["status"], "COMPLETED")
            vehicle = next(item for item in snapshot["vehicles"] if item["id"] == "V01")
            self.assertEqual(vehicle["missionState"], "IDLE")

    def test_operator_websocket_creates_passenger_and_cargo_and_cancels(self) -> None:
        assert self.service.graph is not None
        pickup = self.service.graph.nodes[1].landmark_id
        dropoff = self.service.graph.nodes[0].landmark_id
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.3.0.0",
                    "role": "PC_Operator",
                }
            )
            self.assertEqual(websocket.receive_json()["type"], "connected")
            websocket.receive_json()

            websocket.send_json(
                {
                    "type": "create_request",
                    "messageId": "operator-passenger-1",
                    "serviceType": "PASSENGER",
                    "pickupLandmarkId": pickup,
                    "dropoffLandmarkId": dropoff,
                    "partySize": 2,
                    "serviceNeeds": {
                        "requiresStepFree": True,
                        "wheelchairSlots": 1,
                        "boardingAssistance": False,
                    },
                }
            )
            passenger_ack = websocket.receive_json()
            passenger_snapshot = websocket.receive_json()
            self.assertTrue(passenger_ack["accepted"])
            self.assertEqual(passenger_ack["request"]["ownerId"], "pc-operator")
            self.assertEqual(passenger_ack["request"]["partySize"], 2)
            self.assertEqual(passenger_snapshot["snapshot"]["role"], "PC_Operator")

            websocket.send_json(
                {
                    "type": "create_request",
                    "messageId": "operator-cargo-1",
                    "serviceType": "CARGO",
                    "pickupLandmarkId": pickup,
                    "dropoffLandmarkId": dropoff,
                    "partySize": 0,
                    "cargoKg": 5.0,
                }
            )
            cargo_ack = websocket.receive_json()
            websocket.receive_json()
            self.assertTrue(cargo_ack["accepted"])
            self.assertEqual(cargo_ack["request"]["serviceType"], "CARGO")
            self.assertEqual(cargo_ack["request"]["cargoKg"], 5.0)
            self.assertEqual(cargo_ack["request"]["partySize"], 0)

            websocket.send_json(
                {
                    "type": "cancel_request",
                    "messageId": "operator-cancel-cargo-1",
                    "requestId": cargo_ack["request"]["id"],
                }
            )
            cancel_ack = websocket.receive_json()
            websocket.receive_json()
            self.assertTrue(cancel_ack["accepted"])
            self.assertEqual(cancel_ack["request"]["status"], "CANCELLED")

    def test_mobile_websocket_cannot_create_cargo(self) -> None:
        assert self.service.graph is not None
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.3.0.0",
                    "role": "Mobile_Passenger",
                    "subscriberId": "mobile-cargo-denied",
                }
            )
            self.assertEqual(websocket.receive_json()["type"], "connected")
            websocket.receive_json()
            websocket.send_json(
                {
                    "type": "create_request",
                    "messageId": "mobile-cargo-1",
                    "serviceType": "CARGO",
                    "pickupLandmarkId": self.service.graph.nodes[1].landmark_id,
                    "dropoffLandmarkId": self.service.graph.nodes[0].landmark_id,
                    "partySize": 0,
                    "cargoKg": 2.0,
                }
            )
            ack = websocket.receive_json()
            self.assertFalse(ack["accepted"])
            self.assertEqual(ack["errorCode"], "role_not_allowed")

    def test_websocket_rejects_invalid_schema_then_closes(self) -> None:
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 99,
                    "projectVersion": "0.2.1.1",
                    "role": "PC_Operator",
                }
            )
            self.assertEqual(websocket.receive_json()["code"], "unsupported_schema_version")
            with self.assertRaises(WebSocketDisconnect) as raised:
                websocket.receive_json()
            self.assertEqual(raised.exception.code, 1008)

    def test_websocket_requires_mobile_subscriber_id(self) -> None:
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.2.1.1",
                    "role": "Mobile_Passenger",
                }
            )
            self.assertEqual(websocket.receive_json()["code"], "subscriber_required")
            with self.assertRaises(WebSocketDisconnect):
                websocket.receive_json()

    def test_websocket_closes_oversized_command(self) -> None:
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            websocket.send_json(
                {
                    "type": "subscribe",
                    "schemaVersion": 3,
                    "projectVersion": "0.2.1.1",
                    "role": "PC_Operator",
                }
            )
            self.assertEqual(websocket.receive_json()["type"], "connected")
            websocket.receive_json()
            websocket.send_text("x" * (MAX_MESSAGE_BYTES + 1))
            self.assertEqual(websocket.receive_json()["code"], "message_too_large")
            with self.assertRaises(WebSocketDisconnect) as raised:
                websocket.receive_json()
            self.assertEqual(raised.exception.code, 1009)

    def test_websocket_subscribe_timeout_sends_error_and_closes(self) -> None:
        with self.client.websocket_connect("/v1/client/ws") as websocket:
            self.assertEqual(websocket.receive_json()["code"], "subscribe_timeout")
            with self.assertRaises(WebSocketDisconnect) as raised:
                websocket.receive_json()
            self.assertEqual(raised.exception.code, 1008)


if __name__ == "__main__":
    unittest.main()
