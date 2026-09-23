from __future__ import annotations

from enum import StrEnum

from pydantic import BaseModel, Field, model_validator


class ServiceType(StrEnum):
    PASSENGER = "PASSENGER"
    CARGO = "CARGO"


class RequestStatus(StrEnum):
    CREATED = "CREATED"
    VALIDATED = "VALIDATED"
    QUEUED = "QUEUED"
    ASSIGNED = "ASSIGNED"
    PICKUP_SERVICE = "PICKUP_SERVICE"
    IN_TRANSIT = "IN_TRANSIT"
    DROPOFF_SERVICE = "DROPOFF_SERVICE"
    COMPLETED = "COMPLETED"
    REJECTED = "REJECTED"
    CANCELLED = "CANCELLED"
    EXPIRED = "EXPIRED"
    FAILED = "FAILED"


class ReasonCode(StrEnum):
    UNKNOWN = "UNKNOWN"
    NO_ACCESSIBLE_ALTERNATIVE = "NO_ACCESSIBLE_ALTERNATIVE"
    VEHICLE_FAILURE = "VEHICLE_FAILURE"


class ServiceNeeds(BaseModel):
    requires_step_free: bool = False
    wheelchair_slots: int = Field(default=0, ge=0)
    boarding_assistance: bool = False

    @model_validator(mode="after")
    def wheelchair_requires_step_free(self) -> ServiceNeeds:
        if self.wheelchair_slots and not self.requires_step_free:
            raise ValueError("wheelchair_slots requires requires_step_free=true")
        return self


class CreateRequest(BaseModel):
    command_id: str = Field(min_length=1, max_length=128)
    owner_id: str = Field(min_length=1, max_length=128)
    service_type: ServiceType
    pickup_landmark_id: str = Field(min_length=1)
    dropoff_landmark_id: str = Field(min_length=1)
    service_needs: ServiceNeeds = Field(default_factory=ServiceNeeds)
    party_size: int = Field(default=1, ge=0)
    cargo_kg: float = Field(default=0, ge=0)
    latest_arrival_s: float | None = Field(default=None, ge=0)

    @model_validator(mode="after")
    def validate_service_payload(self) -> CreateRequest:
        if self.pickup_landmark_id == self.dropoff_landmark_id:
            raise ValueError("pickup and dropoff landmarks must differ")
        if self.service_type is ServiceType.PASSENGER:
            if self.party_size < 1 or self.cargo_kg != 0:
                raise ValueError("a passenger request needs party_size >= 1 and cargo_kg = 0")
            if self.service_needs.wheelchair_slots > self.party_size:
                raise ValueError("wheelchair_slots cannot exceed party_size")
        elif self.party_size != 0 or self.cargo_kg <= 0:
            raise ValueError("a cargo request needs party_size = 0 and cargo_kg > 0")
        return self


class RequestView(BaseModel):
    id: str
    owner_id: str
    service_type: ServiceType
    status: RequestStatus
    created_s: float
    pickup_landmark_id: str
    dropoff_landmark_id: str
    pickup_stop_id: str | None = None
    dropoff_stop_id: str | None = None
    service_needs: ServiceNeeds
    party_size: int
    cargo_kg: float
    latest_arrival_s: float | None = None
    eta_s: float | None = None
    vehicle_id: str | None = None
    reason: ReasonCode = ReasonCode.UNKNOWN


class CommandAck(BaseModel):
    command_id: str
    accepted: bool
    request: RequestView


class Landmark(BaseModel):
    id: str
    name: str
    stop_ids: list[str]


class Stop(BaseModel):
    id: str
    landmark_id: str
    step_free_access: bool


class Vehicle(BaseModel):
    id: str
    passenger_capacity: int
    wheelchair_slots: int
    cargo_capacity_kg: float
    available: bool = True
