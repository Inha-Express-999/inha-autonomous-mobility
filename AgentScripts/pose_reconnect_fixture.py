"""Own loopback server: close first physical socket after its first response."""
import asyncio
import json
import socket
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT / "backend/src"), str(ROOT / "tmp/python-deps")]
import uvicorn
from campus_sim.api import create_app

app = create_app()
connections = 0
samples = []


@app.get("/__test/poses")
def poses():
    return {"samples": samples}


async def wrapped(scope, receive, send):
    global connections
    if scope["type"] != "websocket":
        return await app(scope, receive, send)
    connections += 1
    generation = connections
    closed = False

    async def read():
        if closed:
            return {"type": "websocket.disconnect", "code": 1012}
        message = await receive()
        if message["type"] == "websocket.receive" and message.get("text"):
            pose = json.loads(message["text"])
            if len(samples) < 100:
                samples.append({"connection": generation, "x": pose["x"], "z": pose["z"], "tick": pose["tick"]})
        return message

    async def write(message):
        nonlocal closed
        await send(message)
        if generation == 1 and message["type"] == "websocket.send" and not closed:
            closed = True
            await send({"type": "websocket.close", "code": 1012})

    await app(scope, read, write)


async def main():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        listener.listen()
        print(listener.getsockname()[1], flush=True)
        server = uvicorn.Server(uvicorn.Config(wrapped, log_level="error"))
        try:
            await asyncio.wait_for(server.serve(sockets=[listener]), 40)
        except TimeoutError:
            server.should_exit = True


if __name__ == "__main__":
    asyncio.run(main())
