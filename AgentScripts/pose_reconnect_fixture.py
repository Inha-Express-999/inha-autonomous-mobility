"""Own loopback server: close first physical socket after its first response."""
import asyncio
import json
import os
import socket
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
os.chdir(ROOT)  # Resolve map source paths against the real repository, including Hub launches.
sys.path[:0] = [str(ROOT / "backend/src"), str(ROOT / "tmp/python-deps")]
import uvicorn
from campus_sim.api import create_app

app = create_app()
connections = 0
samples = []
demo = "--operator-demo" in sys.argv or "--restart-demo" in sys.argv
silence = "--silence" in sys.argv
moving_stop = "--moving-stop" in sys.argv


@app.get("/__test/poses")
def poses():
    return {"samples": samples}


async def wrapped(scope, receive, send):
    global connections
    if scope["type"] != "websocket":
        return await app(scope, receive, send)
    connections += 1
    generation = connections
    if moving_stop and generation > 1:
        await receive()
        await send({"type": "websocket.close", "code": 1012})
        return
    closed = False
    speed = 0.0

    async def read():
        nonlocal speed
        if closed:
            return {"type": "websocket.disconnect", "code": 1012}
        message = await receive()
        if message["type"] == "websocket.receive" and message.get("text"):
            pose = json.loads(message["text"])
            speed = pose["speed_mps"]
            if len(samples) < 100:
                samples.append({"connection": generation, "x": pose["x"], "z": pose["z"], "tick": pose["tick"]})
        return message

    async def write(message):
        nonlocal closed
        if silence and generation == 1 and speed >= 1.5 and message["type"] == "websocket.send":
            await asyncio.sleep(2)
        if generation == 1 and (moving_stop or demo or silence) and message["type"] == "websocket.send":
            command = json.loads(message["text"])
            command.update(target_speed_kmh=10, steering_angle_rad=0, emergency_stop=False, status="DRIVING")
            message = {**message, "text": json.dumps(command)}
        await send(message)
        if "--passive" in sys.argv or demo or silence or (moving_stop and speed < 1.5):
            return
        if generation == 1 and message["type"] == "websocket.send" and not closed:
            closed = True
            await send({"type": "websocket.close", "code": 1012})

    await app(scope, read, write)


async def main():
    with socket.socket() as listener:
        port = int(sys.argv[sys.argv.index("--port") + 1]) if "--port" in sys.argv else 0
        listener.bind(("127.0.0.1", port))
        listener.listen()
        print(listener.getsockname()[1], flush=True)
        server = uvicorn.Server(uvicorn.Config(wrapped, log_level="error"))
        try:
            await asyncio.wait_for(server.serve(sockets=[listener]), 40)
        except TimeoutError:
            server.should_exit = True


if __name__ == "__main__":
    asyncio.run(main())
