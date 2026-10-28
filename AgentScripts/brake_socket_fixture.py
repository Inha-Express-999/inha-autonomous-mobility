"""Loopback-only transport failure fixture; no runtime schema or driving authority."""

import asyncio

from websockets.asyncio.server import serve


async def peer(socket):
    await socket.send("TRANSPORT_TEST_READY")
    await socket.wait_closed()


async def main():
    async with serve(peer, "127.0.0.1", 0, max_size=1024) as server:
        print(server.sockets[0].getsockname()[1], flush=True)
        # Bound lifetime even if the Unity test runner crashes before its cleanup.
        await asyncio.sleep(30)


if __name__ == "__main__":
    asyncio.run(main())
