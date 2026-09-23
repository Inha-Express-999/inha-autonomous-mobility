from fastapi import FastAPI, HTTPException, status

from campus_sim.domain import CommandAck, CreateRequest, Landmark, RequestView
from campus_sim.service import MobilityService


def create_app(service: MobilityService | None = None) -> FastAPI:
    app = FastAPI(title="Inha Autonomous Mobility API", version="0.1.6.0")
    app.state.service = service or MobilityService.synthetic_fixture()

    @app.get("/health")
    def health() -> dict[str, str]:
        return {"status": "ok", "schema_version": "3"}

    @app.get("/v1/landmarks", response_model=list[Landmark])
    def list_landmarks() -> list[Landmark]:
        return list(app.state.service.landmarks.values())

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
