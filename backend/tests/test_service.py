from __future__ import annotations

import asyncio
import json
import unittest

from campus_sim.domain import CreateRequest, RequestStatus, ServiceNeeds
from campus_sim.realtime import handle_client_message, make_snapshot
from campus_sim.service import MobilityService


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


class MobilityServiceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.service = MobilityService.synthetic_fixture()

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
        directed_segments = list(zip(route, route[1:]))
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

    def test_request_type_without_an_active_vehicle_is_rejected_clearly(self) -> None:
        assert self.service.graph is not None
        command = CreateRequest(
            command_id="cargo-command",
            owner_id="operator",
            service_type="CARGO",
            pickup_landmark_id=self.service.graph.nodes[0].landmark_id,
            dropoff_landmark_id=self.service.graph.nodes[1].landmark_id,
            party_size=0,
            cargo_kg=5.0,
        )
        with self.assertRaisesRegex(ValueError, "no active synthetic vehicle"):
            self.service.create_request(command)

    def test_runtime_respects_edge_speed_limit(self) -> None:
        assert self.service.graph is not None
        edges = list(self.service.graph.edges)
        edges[0] = edges[0].model_copy(update={"allowed_speed_mps": 2.0})
        self.service.graph = self.service.graph.model_copy(update={"edges": edges})
        request = self.service.create_request(passenger_command(self.service))
        self.assertEqual(request.request.eta_s, 50.0)
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

    def test_operator_cannot_issue_mobile_commands(self) -> None:
        socket = FakeWebSocket()
        closed = asyncio.run(
            handle_client_message(
                socket,
                self.service,
                "PC_Operator",
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
