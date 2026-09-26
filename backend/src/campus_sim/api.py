import asyncio
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, HTTPException, WebSocket, status

from campus_sim import __version__
from campus_sim.domain import CommandAck, CreateRequest, Landmark, RequestView
from campus_sim.energy import EnergyFleet
from campus_sim.realtime import serve_client_socket
from campus_sim.service import MobilityService


def create_app(
    service: MobilityService | None = None,
    *,
    map_path: str | Path | None = None,
    energy_config_path: str | Path | None = None,
) -> FastAPI:
    if service is not None and map_path is not None:
        raise ValueError("provide a service instance or map_path, not both")
    service_instance = service or (
        MobilityService.from_synthetic_graph(map_path, active_fleet=True)
        if map_path is not None
        else MobilityService.synthetic_fleet_fixture()
    )

    if energy_config_path is not None:
        if service_instance.energy is not None:
            raise ValueError("energy model already configured")
        energy = EnergyFleet.from_config(energy_config_path)
        if (service_instance.graph is None or energy.map_version != service_instance.graph.map_version
                or not energy.charger_nodes <= {n.id for n in service_instance.graph.nodes}
                or not set(service_instance.vehicle_runtime) <= energy.policies.keys()):
            raise ValueError("energy map, charger or active fleet mismatch")
        service_instance.energy = energy

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        stop_event = asyncio.Event()
        clock_task = asyncio.create_task(service_instance.run_clock(stop_event, fixed_dt_s=0.05))
        app.state.clock_task = clock_task
        try:
            yield
        finally:
            stop_event.set()
            try:
                await clock_task
            finally:
                if service_instance.coordination is not None:
                    await service_instance.coordination.close()

    app = FastAPI(
        title="Inha Autonomous Mobility API",
        version=__version__,
        lifespan=lifespan,
    )
    app.state.service = service_instance

    @app.websocket("/v1/client/ws")
    async def client_socket(websocket: WebSocket) -> None:
        await serve_client_socket(websocket, app.state.service)

    @app.get("/health")
    async def health() -> dict[str, object]:
        graph = app.state.service.graph
        return {
            "status": "ok",
            "project_version": __version__,
            "schema_version": "3",
            "energy_model_status": "SYNTHETIC_MODEL" if app.state.service.energy is not None else "DISABLED",
            "map_version": graph.map_version if graph is not None else None,
            "map_data_status": graph.data_status if graph is not None else None,
            "node_count": len(graph.nodes) if graph is not None else 0,
            "edge_count": len(graph.edges) if graph is not None else 0,
        }

    @app.get("/v1/landmarks", response_model=list[Landmark])
    async def list_landmarks() -> list[Landmark]:
        return list(app.state.service.landmarks.values())

    @app.post("/v1/requests", response_model=CommandAck, status_code=status.HTTP_201_CREATED)
    async def create_request(command: CreateRequest) -> CommandAck:
        try:
            return app.state.service.create_request(command)
        except ValueError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error

    @app.get("/v1/owners/{owner_id}/requests", response_model=list[RequestView])
    async def list_owner_requests(owner_id: str) -> list[RequestView]:
        return app.state.service.requests_for_owner(owner_id)

    @app.post("/v1/requests/{request_id}/cancel", response_model=CommandAck)
    async def cancel_request(request_id: str, command_id: str, owner_id: str) -> CommandAck:
        try:
            return app.state.service.cancel_request(request_id, command_id, owner_id)
        except KeyError as error:
            raise HTTPException(status_code=404, detail="request not found") from error
        except ValueError as error:
            raise HTTPException(status_code=409, detail=str(error)) from error

    return app


app = create_app()
