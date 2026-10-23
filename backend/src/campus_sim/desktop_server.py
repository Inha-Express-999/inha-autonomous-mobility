"""PC-owned, loopback-only server. A parent stdin pipe controls graceful shutdown."""

from __future__ import annotations

import argparse
import sys
from threading import Thread

import uvicorn


def watch_parent(stream, server):
    """Shutdown on an explicit command or EOF when the owning client exits."""
    for line in stream:
        if line.strip() == "shutdown":
            break
    server.should_exit = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--parent-pipe", action="store_true")
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("port must be between 1024 and 65535")
    server = uvicorn.Server(
        uvicorn.Config(
            "campus_sim.api:app", host="127.0.0.1", port=args.port, workers=1, log_level="warning"
        )
    )
    if args.parent_pipe:
        Thread(target=watch_parent, args=(sys.stdin, server), daemon=True).start()
    server.run()


if __name__ == "__main__":
    main()
