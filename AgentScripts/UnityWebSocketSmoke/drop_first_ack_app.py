"""Temporary ASGI app for the Unity C# WebSocket smoke test."""

import asyncio
from contextlib import asynccontextmanager

from campus_sim.realtime import serve_client_socket
from campus_sim.service import MobilityService
from fastapi import FastAPI, WebSocket

service = MobilityService.synthetic_fixture()
ack_state = {"dropped": False}


class DropFirstAckSocket:
    """Close the first command connection after applying, before returning its ACK."""

    def __init__(self, socket: WebSocket) -> None:
        self.socket = socket

    async def accept(self) -> None:
        await self.socket.accept()

    async def receive_text(self) -> str:
        return await self.socket.receive_text()

    async def close(self, **kwargs: object) -> None:
        await self.socket.close(**kwargs)

    async def send_json(self, message: dict[str, object]) -> None:
        request = message.get("request")
        is_mobile_request = isinstance(request, dict) and request.get("ownerId") == "ws-smoke-passenger"
        if message.get("type") == "command_ack" and is_mobile_request and not ack_state["dropped"]:
            ack_state["dropped"] = True
            await self.socket.close(code=1011, reason="smoke test drops first ACK")
            return
        await self.socket.send_json(message)


@asynccontextmanager
async def lifespan(_app: FastAPI):
    stop_event = asyncio.Event()
    clock_task = asyncio.create_task(service.run_clock(stop_event, fixed_dt_s=0.05))
    try:
        yield
    finally:
        stop_event.set()
        await clock_task


app = FastAPI(lifespan=lifespan)


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok"}


@app.websocket("/v1/client/ws")
async def client_socket(websocket: WebSocket) -> None:
    await serve_client_socket(DropFirstAckSocket(websocket), service)
