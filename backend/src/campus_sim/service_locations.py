"""Validated Editor-authored service anchors, separate from display models."""

from __future__ import annotations

import math

from campus_sim.map_coordinates import projection

EMPTY = {"schema_version": 1, "stops": [], "hubs": []}


def validate_locations(document):
    if not isinstance(document, dict) or set(document) != {"schema_version", "stops", "hubs"}:
        raise ValueError("service locations require schema_version, stops and hubs")
    if type(document["schema_version"]) is not int or document["schema_version"] != 1:
        raise ValueError("unsupported service location schema")
    result = {"schema_version": 1}
    for kind in ("stops", "hubs"):
        if not isinstance(document[kind], list):
            raise TypeError(f"{kind} must be an array")
        ids = set()
        for row in document[kind]:
            if not isinstance(row, dict) or set(row) != {"id", "x", "z"}:
                raise ValueError(f"invalid {kind} anchor")
            if not isinstance(row["id"], str) or not row["id"] or row["id"] in ids:
                raise ValueError(f"invalid/duplicate {kind} id")
            ids.add(row["id"])
            if any(
                type(row[k]) not in (float, int) or not math.isfinite(row[k]) for k in ("x", "z")
            ):
                raise ValueError(f"nonfinite {kind} position: {row['id']}")
        result[kind] = {r["id"]: r for r in document[kind]}
    return result


def snap_location(row, coordinates, nodes, segments, cuts):
    anchor = coordinates.from_unity(row["x"], row["z"])
    gap, index, ratio, xy = min(
        (distance, i, ratio, xy)
        for i, s in enumerate(segments)
        for distance, ratio, xy in [projection(anchor, nodes[s["u"]], nodes[s["v"]])]
    )
    if gap > 3:
        raise ValueError(
            f"{row['id']}: move within 3m of an existing vehicle road (gap {gap:.2f}m)"
        )
    segment = segments[index]
    length = math.dist(nodes[segment["u"]], nodes[segment["v"]])
    existing = next(
        (
            node
            for fraction, node in cuts.get(index, {}).items()
            if abs(ratio - fraction) * length <= 0.01
        ),
        None,
    )
    if ratio * length <= 0.01:
        node = segment["u"]
    elif (1 - ratio) * length <= 0.01:
        node = segment["v"]
    elif existing is not None:
        node = existing
    else:
        ratio = round(ratio, 9)
        node = f"split:{segment['key']}:{ratio}"
        xy = tuple(
            nodes[segment["u"]][k] + ratio * (nodes[segment["v"]][k] - nodes[segment["u"]][k])
            for k in (0, 1)
        )
        nodes[node] = xy
        cuts.setdefault(index, {})[ratio] = node
    return node, nodes[node], segment
