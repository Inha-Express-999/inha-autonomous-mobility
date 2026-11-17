# Recovery HUD and launcher validation — 2026-11-17

- Python recovery/web-demo/physical-dispatch: 48 passed; one existing Starlette deprecation warning.
- MapClient and Presentation: standalone Roslyn compile passed. This is not a full Unity build or PlayMode test.
- Launcher CheckOnly: READY on a dedicated test database.
- Occupied port: rejected without terminating its owner.
- Corrupt database: rejected; original SHA remained unchanged.
- Unity PC_Bootstrap attempted: SERVICE_LOCATIONS_BLOCKED / Existing server could not be checked. Loopback health request timed out. Actual HUD layout and live status transitions remain unverified.
- Test server stopped afterwards. Prior durable-restart evidence database was copied, not used directly.

No new driving/restart success is claimed by this validation.
