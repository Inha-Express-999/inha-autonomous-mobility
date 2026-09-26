# Observed identity transport and deduplication

2026-09-26, Unity 6000.3.21f1. EditMode 17/17; moving-pedestrian PlayMode 1/1.
Python/MapData 168/168 and Ruff passed. EditMode checks repeated rays share a
visible object's ID and a wall occludes both its class and ID. The live service
accepts the extended sensor messages and completes passenger/cargo/recreation
checks. Python tests cover deduplication, anonymous returns, invalid/expired/future
frames, context mixing and malformed IDs. Exact source hashes and XML are retained.

No zone coverage/density, person re-identification, real-world identity, full crowd
integration or performance target is proven. Existing allocation/deprecation
warnings remain. Optional wire-field compatibility is documented in DATA_CONTRACT.
