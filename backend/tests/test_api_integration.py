from __future__ import annotations

import unittest

from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from campus_sim.api import create_app
from campus_sim.realtime import MAX_MESSAGE_BYTES
from campus_sim.service import MobilityService


class ApiIntegrationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.service = MobilityService.synthetic_fixture()
        self.client = TestClient(create_app(self.service))

    def tearDown(self) -> None:
        self.client.close()

    def test_http_health_landmarks_create_owner_filter_and_cancel(self) -> None:
        assert self.service.graph is not None
        self.assertEqual(self.client.get("/health").status_code, 200)
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
