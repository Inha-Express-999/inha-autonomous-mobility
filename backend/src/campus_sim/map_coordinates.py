"""Explicit WGS84 / AEQD / existing Unity legacy-grid adapter.

Unity authoring uses the formula in AgentScripts/prepare_campus.py. Do not
reinterpret its X/Z as AEQD: the two projections differ by up to ~0.69 m here.
"""

import math
from math import cos, isfinite, radians

from pyproj import CRS, Transformer

ORIGIN_LAT = 37.4506
ORIGIN_LON = 126.6535
CRS_LOCAL = "+proj=aeqd +lat_0=37.4506 +lon_0=126.6535 +datum=WGS84 +units=m +no_defs"


class MapCoordinates:
    def __init__(self):
        crs = CRS.from_proj4(CRS_LOCAL)
        self._forward = Transformer.from_crs("EPSG:4326", crs, always_xy=True)
        self._inverse = Transformer.from_crs(crs, "EPSG:4326", always_xy=True)

    @staticmethod
    def _finite(*values):
        if not all(isfinite(v) for v in values):
            raise ValueError("coordinates must be finite")

    def from_wgs84(self, lon, lat):
        self._finite(lon, lat)
        if not -180 <= lon <= 180 or not -90 <= lat <= 90:
            raise ValueError("invalid WGS84 coordinate")
        return self._forward.transform(lon, lat, errcheck=True)

    def from_unity(self, x, z):
        self._finite(x, z)
        return self.from_wgs84(
            ORIGIN_LON + x / (111320 * cos(radians(ORIGIN_LAT))),
            ORIGIN_LAT + z / 110980,
        )

    def to_unity(self, east, north):
        self._finite(east, north)
        lon, lat = self._inverse.transform(east, north, errcheck=True)
        return (
            (lon - ORIGIN_LON) * 111320 * cos(radians(ORIGIN_LAT)),
            (lat - ORIGIN_LAT) * 110980,
        )

    @staticmethod
    def wgs84_to_unity(lon, lat):
        return (
            (lon - ORIGIN_LON) * 111320 * cos(radians(ORIGIN_LAT)),
            (lat - ORIGIN_LAT) * 110980,
        )


def projection(point, a, b):
    delta = (b[0] - a[0], b[1] - a[1])
    squared = delta[0] ** 2 + delta[1] ** 2
    ratio = (
        max(0.0, min(1.0, sum((point[i] - a[i]) * delta[i] for i in (0, 1)) / squared))
        if squared
        else 0.0
    )
    projected = tuple(a[i] + ratio * delta[i] for i in (0, 1))
    return math.dist(point, projected), ratio, projected
