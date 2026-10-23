import asyncio
import os
from contextlib import asynccontextmanager, suppress
from pathlib import Path
from threading import RLock

from fastapi import FastAPI, HTTPException, status
from fastapi.responses import RedirectResponse
from fastapi.staticfiles import StaticFiles

from campus_sim.avoidance_demo import AvoidancePreview, avoidance_preview
from campus_sim.avoidance_simulation import simulate_avoidance
from campus_sim.dispatch_comparison import DispatchComparison, compare_dispatch
from campus_sim.domain import CommandAck, CreateRequest, Landmark, RequestView
from campus_sim.service import MobilityService
from campus_sim.unity_map import unity_map
from campus_sim.web_demo import DemoControl, DemoPlan, DemoScenario, DemoSession


def create_app(service: MobilityService | None = None) -> FastAPI:
    @asynccontextmanager
    async def lifespan(app: FastAPI):
        async def advance():
            while True:
                app.state.service.step()
                if app.state.demo is not None:
                    await asyncio.to_thread(app.state.demo.advance)
                await asyncio.sleep(0.1)

        task = asyncio.create_task(advance())
        try:
            yield
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError):
                await task

    app = FastAPI(title="Inha Mobil API", version="0.4.8.0", lifespan=lifespan)
    app.state.service = service or MobilityService.from_osm_map()
    app.state.demo = None
    app.state.demo_lock = RLock()

    def demo():
        with app.state.demo_lock:
            if app.state.demo is None:
                app.state.demo = DemoSession()
            return app.state.demo

    @app.get("/", include_in_schema=False)
    def home():
        return RedirectResponse("/demo/")

    @app.get("/v1/demo/map")
    def demo_map():
        return demo().map

    @app.get("/v1/demo/state")
    def demo_state():
        return demo().state()

    @app.get("/v1/demo/unity-map")
    def demo_unity_map():
        session = demo()
        with session.lock:
            return unity_map(session.service)

    @app.get("/v1/desktop/info")
    def desktop_info():
        session = demo()
        with session.lock:
            return {
                "service_id": "inha-mobil-local-demo",
                "schema_version": 1,
                "map_schema_version": 1,
                "project_version": app.version,
                "process_id": os.getpid(),
                "run_id": session.run_id,
                "geometry_hash": unity_map(session.service)["geometry_hash"],
                "simulation_only": True,
            }

    @app.post("/v1/demo/control")
    def demo_control(command: DemoControl):
        return demo().control(command)

    @app.post("/v1/demo/planning")
    def demo_planning(command: DemoPlan):
        try:
            return demo().compare_planning(command)
        except ValueError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error

    @app.post("/v1/demo/avoidance")
    def demo_avoidance(command: AvoidancePreview):
        return avoidance_preview(command)

    @app.post("/v1/demo/avoidance-simulation")
    def demo_avoidance_simulation(command: AvoidancePreview):
        return simulate_avoidance(command)

    @app.post("/v1/demo/dispatch-comparison")
    def demo_dispatch_comparison(command: DispatchComparison):
        try:
            current = ()
            if command.source == "current":
                session = demo()
                with session.lock:
                    current = tuple(
                        r.model_copy(deep=True) for r in session.service.requests.values()
                    )
            return compare_dispatch(command, current)
        except ValueError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error

    @app.post("/v1/demo/reset")
    def demo_reset():
        return demo().reset()

    @app.post("/v1/demo/scenario")
    def demo_scenario(command: DemoScenario):
        return demo().scenario(command.name)

    @app.post("/v1/demo/requests", response_model=CommandAck, status_code=201)
    def demo_request(command: CreateRequest):
        try:
            return demo().submit(command)
        except ValueError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error

    @app.post("/v1/demo/requests/{request_id}/cancel", response_model=CommandAck)
    def demo_cancel(request_id: str):
        session = demo()
        try:
            with session.lock:
                return session.service.cancel_request(
                    request_id, f"web-cancel-{request_id}", "web-demo"
                )
        except KeyError as error:
            raise HTTPException(status_code=404, detail="request not found") from error
        except ValueError as error:
            raise HTTPException(status_code=409, detail=str(error)) from error

    app.mount("/demo", StaticFiles(directory=Path(__file__).parent / "web", html=True), name="demo")

    @app.get("/health")
    def health() -> dict[str, str]:
        return {"status": "ok", "schema_version": "3"}

    @app.get("/v1/landmarks", response_model=list[Landmark])
    def list_landmarks() -> list[Landmark]:
        return list(app.state.service.landmarks.values())

    @app.get("/v1/debug/virtual-state")
    def virtual_state() -> dict:
        return app.state.service.virtual_state()

    @app.post("/v1/requests", response_model=CommandAck, status_code=status.HTTP_201_CREATED)
    def create_request(command: CreateRequest) -> CommandAck:
        try:
            return app.state.service.create_request(command)
        except ValueError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error

    @app.get("/v1/owners/{owner_id}/requests", response_model=list[RequestView])
    def list_owner_requests(owner_id: str) -> list[RequestView]:
        return app.state.service.requests_for_owner(owner_id)

    @app.post("/v1/requests/{request_id}/cancel", response_model=CommandAck)
    def cancel_request(request_id: str, command_id: str, owner_id: str) -> CommandAck:
        try:
            return app.state.service.cancel_request(request_id, command_id, owner_id)
        except KeyError as error:
            raise HTTPException(status_code=404, detail="request not found") from error
        except ValueError as error:
            raise HTTPException(status_code=409, detail=str(error)) from error

    return app


app = create_app()
