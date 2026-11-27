# P2.3.2 — Supervised batch assessment orchestration

POST /api/areas/{areaId}/water-bench/batches/{stationId}/{bootId}/{batchId}/results/batch-assessment

FarmManage only, body {"samples":[{"analyte":"NO2","assessment":{"requestId":"<uuid>","action":"estimate","reason":"operator confirmed sample","calibrationId":"<uuid>","reagentLot":"lot","reactionSeconds":300,"salinityPpt":15,"sampleSource":"RAS_RETURN","operatorConditionsConfirmed":true}}]}.
Manual selects 1–3 analytes in the completed firmware batch; scheduled requires all three. IDs and analytes must be unique. Reuse exact per-sample request IDs and body on retry. Each sample has its own transaction, reuses existing single-assessment checks, and remains review-required. Expected sample failures are isolated; overall completed/partial/failed means request processing, not chemical validation. An unexpected infrastructure failure or cancellation may end the HTTP request after some samples commit; retry identical IDs and inspect persisted results.
This increment deliberately dispatches sequentially on one EF context. True parallel inference using independent scoped DB work or a bounded AI batch adapter is a follow-up, not claimed here. No firmware actions, automatic chemistry, result publication or fallback. 
