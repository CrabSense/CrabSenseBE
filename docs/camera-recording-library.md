# Camera recording library API

All routes require System Administrator (Platform). The library restricts video media to Camera devices, excludes crab/box media, inconsistent related entity IDs and references from AI detections, inspections or observations. General /api/media authorization remains unchanged.

- GET /api/cameras/recordings?deviceId=...&farmingAreaId=...&farmingRowId=...&from=...&to=...&search=...&page=1&pageSize=25
  - from inclusive, to exclusive; ISO 8601 with offset; filters use media CreatedAt (not capture time), current Camera location (not historical location).
  - page starts at 1, pageSize 1–200; response data has items, totalCount, page, pageSize, totalPages, totalBytes, cameraCount. Legacy calls without new filters/paging still return up to 200 rows.
- GET /api/cameras/recordings/{id}: detail; missing or out-of-scope = 404.
- GET /api/cameras/recordings/{id}/content: authenticated inline bytes, supports HTTP Range (206/416), private/no-store.
- GET /api/cameras/recordings/{id}/download: authenticated attachment, supports Range.
- DELETE /api/cameras/recordings/{id}?confirm=true: explicit confirmation, storage deletion then metadata removal and audit. Failed storage deletion keeps metadata and records failed audit. Shared storage-key references are blocked. Provider mismatch returns 409; no public link generated.

Playback requires Authorization: Bearer on every request, including ranges. Browser video cannot automatically attach a bearer header; FE must use an authenticated fetch/blob for bounded files or a dedicated authenticated streaming client. Do not pass JWTs in URLs. Drive/S3 implementations spool to a temporary seekable file per request; range semantics are supported but large files can be expensive. Storage and DB are not a distributed transaction: if physical deletion succeeds but DB commit fails, metadata can remain; audit request persists for reconciliation.

Legacy media does not store actual capture start/end, duration or recording mode; the fields are null and source is LegacyMedia. Do not infer from current recording settings. A Camera Agent and recording metadata ingestion contract are still required for actual capture, retention and source attribution. No schema changes in this API increment.
