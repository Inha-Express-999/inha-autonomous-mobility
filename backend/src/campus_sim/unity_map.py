"""Read-only Unity projection of the web demo's authoritative static road graph."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from campus_sim import __version__
from campus_sim.map_builder import write_json
from campus_sim.map_coordinates import MapCoordinates
from campus_sim.service import MobilityService


def unity_map(service: MobilityService) -> dict:
    coordinates = MapCoordinates()

    def point(xy):
        x, z = coordinates.to_unity(*xy)
        return {"x": x, "y": 0.0, "z": z}

    nodes = [
        {"id": identifier, "position": point(xy)}
        for identifier, xy in sorted(service.graph.nodes.items())
    ]
    edges = [
        {"u": edge.u, "v": edge.v, "key": edge.key}
        for edge in sorted(service.graph.edges, key=lambda edge: edge.id)
    ]
    hubs = [
        {
            "id": identifier,
            "name": hub["name"],
            "node_id": hub["node_id"],
            "position": point(service.graph.nodes[hub["node_id"]]),
            "vehicle_ids": sorted(
                vehicle for vehicle, home in service.home_hubs.items() if home["id"] == identifier
            ),
        }
        for identifier, hub in sorted({h["id"]: h for h in service.home_hubs.values()}.items())
    ]
    stops = [
        {
            "id": stop.id,
            "name": service.landmarks[stop.landmark_id].name,
            "landmark_id": stop.landmark_id,
            "node_id": service.stop_nodes[stop.id],
            "position": point(service.graph.nodes[service.stop_nodes[stop.id]]),
        }
        for stop in sorted(service.stops.values(), key=lambda stop: stop.id)
    ]
    geometry = {"nodes": nodes, "edges": edges, "hubs": hubs, "stops": stops}
    content = json.dumps(geometry, sort_keys=True, ensure_ascii=False, allow_nan=False)
    return {
        "schema_version": 1,
        "project_version": __version__,
        "map_version": service.graph.map_version,
        "geometry_hash": hashlib.sha256(content.encode("utf-8")).hexdigest(),
        "coordinate_system": "unity_legacy_x_east_z_north_m",
        "height_mode": "project_to_unity_surface_visual_only",
        "simulation_only": True,
        "operationally_verified": False,
        "vehicle_actuation": False,
        "attribution": "© OpenStreetMap contributors · ODbL 1.0",
        **geometry,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    write_json(args.output, unity_map(MobilityService.from_osm_map()))


if __name__ == "__main__":
    main()
