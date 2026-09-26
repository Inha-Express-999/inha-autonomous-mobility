"""Real service/transport with one vehicle on a short, explicitly synthetic graph."""
from pathlib import Path

from campus_sim.api import create_app
from campus_sim.service import MobilityService

ROOT = Path(__file__).resolve().parents[2]
app = create_app(MobilityService.from_synthetic_graph(
    ROOT / "maps/fixtures/physics-integration-3.json"
))
