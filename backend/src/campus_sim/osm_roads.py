"""Shared cached OSM topology and conservative access filtering."""

import math
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from itertools import pairwise


@dataclass(frozen=True)
class Segment:
    way_id: str
    index: int
    u: str
    v: str
    tags: dict
    directions: tuple[int, ...]

    @property
    def key(self):
        return self.way_id, self.index


def read_network(path, coordinates):
    osm = ET.parse(path).getroot()
    nodes = {
        n.attrib["id"]: coordinates.from_wgs84(float(n.attrib["lon"]), float(n.attrib["lat"]))
        for n in osm.findall("node")
    }
    restrictions = set()
    for relation in osm.findall("relation"):
        tags = {t.attrib["k"]: t.attrib["v"] for t in relation.findall("tag")}
        if tags.get("type") == "restriction":
            restrictions.update(
                m.attrib["ref"] for m in relation.findall("member") if m.attrib["type"] == "way"
            )
    blocked_nodes = set()
    for node in osm.findall("node"):
        tags = {t.attrib["k"]: t.attrib["v"] for t in node.findall("tag")}
        if tags.get("barrier") in {"bollard", "block", "cycle_barrier", "fence", "wall"} or any(
            tags.get(k) in {"no", "private"}
            for k in ("access", "vehicle", "motor_vehicle", "motorcar")
        ):
            blocked_nodes.add(node.attrib["id"])
    segments, excluded = [], []
    for way in osm.findall("way"):
        tags = {t.attrib["k"]: t.attrib["v"] for t in way.findall("tag")}
        if "highway" not in tags:
            continue
        refs = [n.attrib["ref"] for n in way.findall("nd")]
        reason = None
        if any(ref not in nodes for ref in refs):
            reason = "MISSING_NODE"
        elif any(
            tags.get(k) not in {None, "yes", "permissive", "designated"}
            for k in ("access", "vehicle", "motor_vehicle", "motorcar")
        ):
            reason = "EXPLICIT_ACCESS_RESTRICTION"
        elif way.attrib["id"] in restrictions:
            reason = "TURN_RESTRICTION_REQUIRES_STATEFUL_ROUTER"
        elif any(
            k.startswith(("access:", "vehicle:", "motor_vehicle:", "motorcar:", "oneway:"))
            for k in tags
        ):
            reason = "CONDITIONAL_OR_DIRECTIONAL_RESTRICTION"
        elif tags.get("oneway") not in {None, "yes", "1", "true", "-1", "no", "0", "false"}:
            reason = "UNSUPPORTED_ONEWAY"
        if "maxspeed" in tags:
            try:
                speed = float(tags["maxspeed"])
                if not math.isfinite(speed) or speed <= 0:
                    reason = "UNSUPPORTED_MAXSPEED"
            except ValueError:
                reason = "UNSUPPORTED_MAXSPEED"
        if reason:
            excluded.append({"osm_way_id": way.attrib["id"], "reason": reason, "tags": tags})
            continue
        oneway = tags.get("oneway")
        directions = (
            (-1,)
            if oneway == "-1"
            else (1,)
            if oneway in {"yes", "1", "true"}
            or (oneway is None and tags.get("junction") == "roundabout")
            else (1, -1)
        )
        for index, (u, v) in enumerate(pairwise(refs)):
            if (
                u not in blocked_nodes
                and v not in blocked_nodes
                and math.dist(nodes[u], nodes[v]) > 1e-6
            ):
                segments.append(Segment(way.attrib["id"], index, u, v, tags, directions))
    return nodes, segments, excluded
