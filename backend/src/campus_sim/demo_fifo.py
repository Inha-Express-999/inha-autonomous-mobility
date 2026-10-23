"""Run with python -m campus_sim.demo_fifo; no network or real-time wait."""

import argparse
import json

from campus_sim.domain import CreateRequest
from campus_sim.service import MobilityService


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument(
        "--synthetic", action="store_true", help="Use the old invented six-stop graph"
    )
    mode.add_argument("--extensions", action="store_true", help="Queue the five added destinations")
    mode.add_argument("--battery", action="store_true", help="Queue 18 trips to show recharging")
    args = parser.parse_args()
    service = (
        MobilityService.synthetic_fixture(clock=lambda: 0.0)
        if args.synthetic
        else MobilityService.from_osm_map(clock=lambda: 0.0)
    )
    destinations = (
        ("hitech", "dorm_1", "dorm_2", "dorm_3", "inha_station")
        if args.extensions
        else ("hitech",) * 18
        if args.battery
        else ("library",) * 3
    )
    for index, destination in enumerate(destinations, 1):
        service.create_request(
            CreateRequest(
                command_id=f"demo-{index}",
                owner_id="demo",
                service_type="PASSENGER",
                pickup_landmark_id="main_gate",
                dropoff_landmark_id=destination,
            )
        )

    def show():
        state = service.virtual_state()
        print(
            json.dumps(
                {
                    "synthetic": True,
                    "synthetic_geometry": state["synthetic_geometry"],
                    "routing_mode": state["routing_mode"],
                    "time_s": state["simulation_time_s"],
                    "battery_model": state["battery_model"],
                    "batteries": state["batteries"],
                    "vehicle_fifo": state["vehicle_fifo"],
                    "charging_sessions": state["charging_sessions"],
                    "requests": [
                        {
                            "id": r["id"],
                            "status": r["status"],
                            "vehicle": r["vehicle_id"],
                            "destination": r["dropoff_landmark_id"],
                        }
                        for r in state["requests"]
                    ],
                    "trips": [
                        {
                            "vehicle": t["vehicle_id"],
                            "phase": t["phase"],
                            "returns_at_s": t["returns_at_s"],
                            "round_trip_duration_s": t["total_duration_s"],
                        }
                        for t in state["trips"]
                    ],
                },
                ensure_ascii=False,
            )
        )

    show()
    first = next(iter(service.trips.values()))
    service.step(first.dropoff_at_s)
    show()
    while True:
        events = [t.returns_at_s for t in service.trips.values() if not t.returned]
        events.extend(c.ends_at_s for c in service.charges if not c.completed)
        if not events:
            break
        service.step(min(events))
        show()


if __name__ == "__main__":
    main()
