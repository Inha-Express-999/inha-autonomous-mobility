"""Loopback integration-only ASGI socket interruption; production API is unchanged."""
import asyncio
import contextlib
import json
import time


class SocketFaultState:
    def __init__(self):
        self.pc_connections = set()
        self.blocked_until = 0.0

    def disconnect(self):
        self.blocked_until = time.monotonic() + 1.5
        for event in tuple(self.pc_connections):
            event.set()
        return len(self.pc_connections)


class SocketFaultHarness:
    def __init__(self, app, state):
        self.app, self.state = app, state

    async def __call__(self, scope, receive, send):
        if scope["type"] != "websocket":
            return await self.app(scope, receive, send)
        if time.monotonic() < self.state.blocked_until:
            await send({"type": "websocket.close", "code": 1013})
            return
        interrupt = asyncio.Event()

        async def observed_receive():
            message = await receive()
            if message["type"] == "websocket.receive":
                with contextlib.suppress(ValueError, TypeError):
                    payload = json.loads(message.get("text") or "{}")
                    if isinstance(payload, dict) and payload.get("role") == "PC_Operator":
                        self.state.pc_connections.add(interrupt)
            return message

        session = asyncio.create_task(self.app(scope, observed_receive, send))
        failure = asyncio.create_task(interrupt.wait())
        try:
            done, _ = await asyncio.wait((session, failure), return_when=asyncio.FIRST_COMPLETED)
            if session in done:
                await session
            else:
                session.cancel()
                with contextlib.suppress(asyncio.CancelledError):
                    await session
                await send({"type": "websocket.close", "code": 1012})
        finally:
            self.state.pc_connections.discard(interrupt)
            for task in (session, failure):
                if not task.done():
                    task.cancel()
                    with contextlib.suppress(asyncio.CancelledError):
                        await task
