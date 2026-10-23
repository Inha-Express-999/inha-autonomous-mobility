"""Configurable energy assumptions for the time-only simulator, not measured specs."""

from dataclasses import dataclass

from pydantic import BaseModel, ConfigDict, Field, model_validator

from campus_sim.domain import RequestView, ServiceType


class BatteryPolicy(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False, frozen=True)

    capacity_wh: float = Field(gt=0)
    initial_wh: float = Field(ge=0)
    wh_per_m: float = Field(gt=0)
    auxiliary_power_w: float = Field(ge=0)
    reserve_fraction: float = Field(ge=0.15, lt=1)
    charge_trigger_fraction: float = Field(ge=0, lt=1)
    charging_power_w: float = Field(gt=0)
    passenger_load_fraction: float = Field(ge=0)
    cargo_load_fraction_per_kg: float = Field(ge=0)
    charge_when_idle: bool = False

    @model_validator(mode="after")
    def validate_initial(self):
        if self.initial_wh > self.capacity_wh:
            raise ValueError("initial energy exceeds capacity")
        return self

    @property
    def reserve_wh(self) -> float:
        return self.capacity_wh * self.reserve_fraction

    def load_factor(self, request: RequestView) -> float:
        return 1 + (
            request.party_size * self.passenger_load_fraction
            if request.service_type is ServiceType.PASSENGER
            else request.cargo_kg * self.cargo_load_fraction_per_kg
        )


@dataclass
class BatteryState:
    remaining_wh: float
    consumed_wh: float = 0.0
    charged_wh: float = 0.0


@dataclass
class ChargeSession:
    vehicle_id: str
    started_at_s: float
    ends_at_s: float
    initial_wh: float
    completed: bool = False
    interrupted: bool = False
