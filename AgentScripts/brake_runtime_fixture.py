"""Test-owned instance of the actual schema-v3 app, bound only to loopback."""

import asyncio
import socket
import sys
from pathlib import Path

import uvicorn

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend" / "src"))


async def main():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        listener.listen()
        print(listener.getsockname()[1], flush=True)
        server = uvicorn.Server(uvicorn.Config("campus_sim.api:app", log_level="error"))
        try:
            await asyncio.wait_for(server.serve(sockets=[listener]), timeout=30)
        except TimeoutError:
            server.should_exit = True


if __name__ == "__main__":
    asyncio.run(main())
