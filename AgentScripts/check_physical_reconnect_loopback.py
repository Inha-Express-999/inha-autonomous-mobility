"""Real loopback protocol-5 reconnect; synthetic poses, no Unity Physics claim."""
import argparse
import asyncio
import json
import math
import sys
from pathlib import Path
from urllib.request import urlopen

import websockets
from campus_sim.map_coordinates import MapCoordinates

ROOT = Path(__file__).resolve().parents[1]


async def run(forced_disconnect=False):
    process = await asyncio.create_subprocess_exec(
        sys.executable, "-u", str(ROOT / "AgentScripts" / (
            "pose_reconnect_fixture.py" if forced_disconnect else "brake_runtime_fixture.py")),
        stdout=asyncio.subprocess.PIPE,
    )
    try:
        port = int(await asyncio.wait_for(process.stdout.readline(), 8))
        base = f"http://127.0.0.1:{port}"

        def get(path):
            with urlopen(base + path, timeout=3) as response:
                return json.load(response)

        for attempt in range(30):
            try:
                info = await asyncio.to_thread(get, "/v1/desktop/info")
                break
            except OSError:
                if attempt == 29:
                    raise
                await asyncio.sleep(.1)
        uri = f"ws://127.0.0.1:{port}/v1/demo/unity/vehicles/V01/control"
        for tick, x, z in [(1, 70.07, -159.69), (2, 70.57, -159.19)]:
            pose = {"schema_version": 5, "run_id": info["run_id"],
                "geometry_hash": info["geometry_hash"], "tick": tick, "x": x, "z": z,
                "heading_rad": math.radians(-60), "speed_mps": 0, "wheelbase_m": 1.3,
                "max_steering_rad": .52, "half_width_m": .46, "rear_axle_offset_m": -.65,
                "half_length_m": 1.55, "sensor_valid": True, "sensor_age_s": 0,
                "obstacle_detection_enabled": True, "rays": [],
                "lidar": {"tick": tick, "ray_count": 2520, "max_range_m": 15, "valid": True, "hits": []}}
            async with websockets.connect(uri, open_timeout=3) as socket:
                await socket.send(json.dumps(pose))
                command = json.loads(await asyncio.wait_for(socket.recv(), 3))
                assert command["tick"] == tick and command["run_id"] == info["run_id"]
                assert command["emergency_stop"] and command["target_speed_kmh"] == 0
                state = await asyncio.to_thread(get, "/v1/demo/state")
                actual = next(v["position"] for v in state["vehicles"] if v["id"] == "V01")
                expected = MapCoordinates().from_unity(x, z)
                assert all(abs(a-b) < 1e-6 for a, b in zip(actual, expected))
                if forced_disconnect and tick == 1:
                    await asyncio.wait_for(socket.wait_closed(), 3)
                    assert socket.close_code == 1012, socket.close_code
            await asyncio.sleep(.15)
        if forced_disconnect:
            recorded = (await asyncio.to_thread(get, "/__test/poses"))["samples"]
            first_new = next(p for p in recorded if p["connection"] == 2)
            assert first_new == {"connection": 2, "tick": 2, "x": 70.57, "z": -159.19}
        print(json.dumps({"status": "ok", "connections": 2, "latest_tick": 2,
            "server_forced_disconnect": forced_disconnect,
            "fresh_pose_visible": True, "stopped_after_reconnect": True,
            "evidence": "REAL_LOOPBACK_SYNTHETIC_POSES", "unity_physics_verified": False}))
    finally:
        if process.returncode is None:
            process.terminate()
        try:
            await asyncio.wait_for(process.wait(), 5)
        except TimeoutError:
            process.kill()
            await asyncio.wait_for(process.wait(), 5)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--forced-disconnect", action="store_true")
    args = parser.parse_args()
    asyncio.run(asyncio.wait_for(run(args.forced_disconnect), 25))
