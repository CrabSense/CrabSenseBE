# Controller port manifest (v1)

- PUT /api/controllers/{deviceCode}/ports/manifest: dedicated X-Device-Key, approved/online controller; no shared key fallback.
- GET /api/controllers/{deviceId}/ports: authenticated farm-scoped reader. Includes any existing RAS bindings matching channel.
- Full snapshots, revision >= 1; unchanged revision and equal content is idempotent (updates last report time). Reject stale/conflicting revision and duplicate IDs/channels/GPIOs. Removed rows are retained with Present=false.
- Declared presence means firmware configuration, not proof of an attached load. Detected descriptors need a hardware UID. RemoteControl is capability metadata, not command authorization.
- Existing RAS bindings, telemetry, command queue, roles and hardware kill switch are unchanged. Inventory must not automatically create a RAS mapping or energize outputs.
- Migration: 20261113143000_ControllerPortInventory. Apply BE first, then Kiosk, then provision/flash compatible hardware.
- Test: dotnet test tests/CrabSenseBE.UnitTests/CrabSenseBE.UnitTests.csproj --filter FullyQualifiedName~ControllerPortInventoryTests
- In-memory tests cover revisions, duplicate reports, removal, invalid credentials. PostgreSQL migration/rollback and cross-repo end-to-end tests remain required.
