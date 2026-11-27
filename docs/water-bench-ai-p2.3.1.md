# P2.3.1 — Water bench AI assessment preflight

GET /api/areas/{areaId}/water-bench/batches/{stationId}/{bootId}/{batchId}/results/ai-readiness

Farm-scoped read-only metadata preflight per selected analyte. The response is not cached.
Statuses distinguish terminal evidence, missing/ambiguous media, receipt integrity, calibration/camera mapping, private media identity, duplicate media and existing assessments.
Only operator_confirmation_required supplies the scoped receipt's calibration ID, camera, unit, lot, algorithm, reaction and salinity bounds. It does not expose signing tokens or storage keys.
The POST assessment still requires explicit operator conditions and downloads/hash-checks image bytes before calling the signed AI service. No automatic publication, dosing, NH3 conversion, or inference from preflight.
The metadata preflight does not verify actual sample conditions, media content, current AI availability or physical chemistry. Pending P2.3 follow-up: service contract/integration tests and batch orchestration; laboratory validation remains required.
