"""Measure a bounded localhost WebSocket snapshot run against the real ASGI server."""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import platform
import socket
import statistics
import time
from collections import Counter
from contextlib import AsyncExitStack
from hashlib import sha256
from pathlib import Path

import uvicorn
import websockets
from campus_sim import __version__
from campus_sim.api import create_app
from websockets.asyncio.client import connect
from websockets.exceptions import ConnectionClosed


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    index = fraction * (len(ordered) - 1)
    lower = int(index)
    upper = min(lower + 1, len(ordered) - 1)
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (index - lower)


async def measure(clients: int, snapshots: int, requests_per_client: int = 0) -> dict:
    run_started = time.perf_counter()
    repository = Path(__file__).resolve().parents[2]
    source_files = [
        "AgentScripts/LoadTest/ws_snapshot_load.py",
        "backend/src/campus_sim/api.py",
        "backend/src/campus_sim/realtime.py",
        "backend/src/campus_sim/service.py",
        "configs/crowd.json",
    ]
    app = create_app(max_ws_clients=clients)
    config = uvicorn.Config(app, log_level="error", access_log=False)
    server = uvicorn.Server(config)
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.bind(("127.0.0.1", 0))
    listener.listen(128)
    listener.setblocking(False)
    port = listener.getsockname()[1]
    task = asyncio.create_task(server.serve(sockets=[listener]))
    try:
        async with asyncio.timeout(10):
            while not server.started:
                if task.done():
                    await task
                    raise RuntimeError("server exited before startup")
                await asyncio.sleep(0.01)
        uri = f"ws://127.0.0.1:{port}/v1/client/ws"
        connect_times_ms: list[float] = []
        command_ack_ms: list[float] = []
        intervals_ms: list[float] = []
        observed_sequences: list[list[int]] = []
        async with AsyncExitStack() as stack:
            connections = []
            for index in range(clients):
                started = time.perf_counter()
                connection = await stack.enter_async_context(connect(uri, proxy=None))
                await connection.send(json.dumps({
                    "type": "subscribe", "schemaVersion": 3,
                    "projectVersion": __version__, "role": "Mobile_Passenger",
                    "subscriberId": f"load-client-{index}",
                }))
                connected = json.loads(await asyncio.wait_for(connection.recv(), timeout=5))
                if connected.get("type") != "connected":
                    raise RuntimeError(f"client {index} was not connected: {connected}")
                connect_times_ms.append((time.perf_counter() - started) * 1000)
                connections.append(connection)
            if app.state.active_ws_clients != clients:
                raise RuntimeError("active client count disagrees with connected clients")

            async with connect(uri, proxy=None) as excess:
                rejected = json.loads(await asyncio.wait_for(excess.recv(), timeout=5))
                if rejected != {"type": "error", "code": "connection_limit_reached"}:
                    raise RuntimeError(f"excess connection was not rejected: {rejected}")
                try:
                    await excess.recv()
                except ConnectionClosed as closed:
                    if closed.code != 1013:
                        raise RuntimeError(f"unexpected rejection close code: {closed.code}") from closed
                else:
                    raise RuntimeError("excess connection remained open")

            owned_request_ids: list[str] = []
            if requests_per_client:
                graph = app.state.service.graph
                if graph is None:
                    raise RuntimeError("load scenario requires a route graph")
                landmarks = [node.landmark_id for node in graph.nodes if node.landmark_id]
                pickup, dropoff = landmarks[:2]
                started_at = [0.0] * clients

                async def send_request(index: int) -> None:
                    started_at[index] = time.perf_counter()
                    await connections[index].send(json.dumps({
                        "type": "create_request", "messageId": f"load-request-{index}",
                        "serviceType": "PASSENGER", "pickupLandmarkId": pickup,
                        "dropoffLandmarkId": dropoff, "partySize": 1,
                    }))

                await asyncio.gather(*(send_request(index) for index in range(clients)))

                async def receive_ack(index: int) -> str:
                    while True:
                        frame = json.loads(await asyncio.wait_for(
                            connections[index].recv(), timeout=10
                        ))
                        if frame.get("type") == "snapshot":
                            continue
                        if (frame.get("type") != "command_ack"
                                or frame.get("messageId") != f"load-request-{index}"
                                or not frame.get("accepted")):
                            raise RuntimeError(f"client {index} request rejected: {frame}")
                        command_ack_ms.append((time.perf_counter() - started_at[index]) * 1000)
                        return frame["request"]["id"]

                owned_request_ids = await asyncio.gather(
                    *(receive_ack(index) for index in range(clients))
                )
                if len(set(owned_request_ids)) != clients:
                    raise RuntimeError("request IDs were not unique")
                if len(app.state.service.requests) != clients:
                    raise RuntimeError("accepted request count disagrees with service state")

            measurement_start_s = app.state.service.now_s() + 0.3

            async def receive_snapshots(index: int) -> list[int]:
                connection = connections[index]
                previous_at = None
                sequences = []
                while len(sequences) < snapshots:
                    frame = json.loads(await asyncio.wait_for(connection.recv(), timeout=5))
                    if frame.get("type") != "snapshot":
                        raise RuntimeError(f"unexpected frame: {frame.get('type')}")
                    if frame["snapshot"]["simulationTimeS"] < measurement_start_s:
                        continue
                    if requests_per_client:
                        visible = {item["id"] for item in frame["snapshot"]["requests"]}
                        if visible != {owned_request_ids[index]}:
                            raise RuntimeError(
                                f"client {index} saw wrong requests: {sorted(visible)}"
                            )
                    sequence = frame["snapshot"]["sequence"]
                    if sequences and sequence <= sequences[-1]:
                        raise RuntimeError("snapshot sequence did not increase")
                    sequences.append(sequence)
                    received_at = time.perf_counter()
                    if previous_at is not None:
                        intervals_ms.append((received_at - previous_at) * 1000)
                    previous_at = received_at
                return sequences

            observed_sequences = await asyncio.gather(
                *(receive_snapshots(index) for index in range(clients))
            )
        await asyncio.sleep(0)
        if app.state.active_ws_clients != 0:
            raise RuntimeError("WebSocket slots were not reclaimed")
        return {
            "result": "completed",
            "project_version": __version__,
            "python": platform.python_version(),
            "platform": platform.platform(),
            "logical_cpu_count": os.cpu_count(),
            "websockets": websockets.__version__,
            "clients": clients,
            "snapshots_per_client": snapshots,
            "requests_per_client": requests_per_client,
            "accepted_requests": len(owned_request_ids),
            "command_ack_ms_p95": round(percentile(command_ack_ms, 0.95), 2)
            if command_ack_ms else None,
            "mobile_request_projection_valid": True if requests_per_client else None,
            "request_status_counts": dict(sorted(Counter(
                request.status.value for request in app.state.service.requests.values()
            ).items())),
            "received_snapshots": sum(map(len, observed_sequences)),
            "interval_samples": len(intervals_ms),
            "snapshot_interval_ms_p50": round(statistics.median(intervals_ms), 2),
            "snapshot_interval_ms_p95": round(percentile(intervals_ms, 0.95), 2),
            "connect_ms_p95": round(percentile(connect_times_ms, 0.95), 2),
            "excess_client_rejected": True,
            "slots_reclaimed": True,
            "clock": app.state.service.clock_telemetry(),
            "elapsed_s": round(time.perf_counter() - run_started, 2),
            "scope": "localhost, one Python process, mobile snapshots"
            + (" with one passenger request per client" if requests_per_client else " without requests")
            + ", without Unity",
            "source_sha256": {
                name: sha256((repository / name).read_bytes()).hexdigest()
                for name in source_files
            },
        }
    finally:
        server.should_exit = True
        await asyncio.wait_for(task, timeout=10)
        listener.close()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--clients", type=int, default=50)
    parser.add_argument("--snapshots", type=int, default=10)
    parser.add_argument("--requests-per-client", type=int, choices=(0, 1), default=0)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.clients < 1 or args.snapshots < 2:
        parser.error("clients must be positive and snapshots must be at least two")
    report = asyncio.run(measure(args.clients, args.snapshots, args.requests_per_client))
    content = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output is None:
        print(content, end="")
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(content, encoding="utf-8")
        print(args.output)


if __name__ == "__main__":
    main()
