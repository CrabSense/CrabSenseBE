# Non-sales BE audit follow-up

Scope: owner excluded sales/payment/delivery/price lists. This PR does not modify them. Keep VPS FE/App-only, hardware control disabled.

Changes: reject invalid transaction types and rounded zero weights; require area ownership on harvest-linked frozen lots and barcode registration; cap cumulative linked inbound across lots against confirmed harvest totals, serialized using existing harvest concurrency token; require barcode weight total to match current lot; serialize barcode registration with inventory updates. Manual lots without harvest links remain supported for separately documented stock. Existing legacy discrepancies require reconciliation, not guessed corrections.

AI media analysis returns 501 until real inference exists; current box rules remain available with explicit rule-score semantics. Dashboard water component measures fresh sensor coverage without an artificial 70% floor, not chemical quality. Device telemetry cannot alter administrative maintenance or MAC, and validates battery and status. Sensor replay lookup includes sensor identity; DB unique dedup and concurrent replay still need a migration with legacy duplicate reconciliation.

Remaining: item-level frozen outbound ledger, global vs area setting contract, actual AI/camera/notification/backup/deployment workers, offline E2E, provider and physical commissioning. No sales workflow extension and no hardware enablement. No new DB field in this first PR; existing harvest/frozen concurrency tokens reused.

## Second commit — schema and API

Migration 20261112120000_TrackFrozenItemsAndSensorReplay adds FrozenCrabItem.StockState (Available/Dispatched/Unverified), DispatchedAt, OutboundTransactionId with FK, and WaterMeasurement.EdgeEventKey with a unique partial index scoped to SensorId. Legacy items are Available only if no recorded outbound and count/weight match current stock; otherwise Unverified. No legacy sensor rows are removed. New event IDs hash to a bounded key; preexisting source lookup still prevents replay.

GET /api/frozen-lots/{lotId}/crab-items lists area-scoped barcodes. POST /api/frozen-lots/{lotId}/crab-items/reconcile is FM only, accepts availableItemIds and reason, checks physical count/weight, records audit without inventing historical dispatch dates. Outbound execute adds frozenCrabItemIds; required for tracked lots; linked harvest lots require barcode registration before dispatch. Aggregate-only manual lots retain existing workflow. New inbound into a barcode-registered lot is rejected: create a new lot. No sales, payment or delivery changes.

FE/App must implement barcode selection, reconcile UI, and render 501 for media inference. Deployment must use CI and migrations before API consumption.

## Farm Settings ownership

Migration 20261112130000_ScopeFarmSettingsToArea adds nullable FarmingAreaId, FK, and unique per-area key index. Existing global rows stay unassigned, preserved, and hidden from area-scoped APIs because no owner evidence exists. A Farm Admin assigned exactly one area may omit the area for backward compatibility; multi-area admins must specify farmingAreaId for single-key read/write/delete. List returns only assigned areas, optionally filtered. Technical settings remain excluded. Legacy values must be reviewed by the owner before recreating scoped settings; no automatic reassignment.

## Scheduled backup

Migration 20261112140000_TrackScheduledBackupRuns adds LastAttemptSlotUtc, LastCompletedAtUtc, LastResult and LastBackupFile. Legacy enabled schedules are disabled until SA explicitly enables. Daily/Weekly (Monday) at HH:mm in Farm:TimeZone; current-period catch-up once, persisted atomic claim prevents duplicate slot; failure is recorded, next slot retries. Backup uses existing pg_dump and validates with pg_restore; Linux directory must be 0700, files 0600; retention only removes API-owned crabsense_be_ archives, never predeploy backups. No automatic restore or scheduled hardware activity. An interrupted Started slot is not retried automatically; next scheduled slot can run. Test runtime backup with staging credentials before enabling in production.

## Notification provider hardening

FCM now uses HTTP v1 and service-account OAuth2. Configure Fcm:ProjectId and Fcm:CredentialsJson or Fcm:CredentialsPath securely; the legacy ServerKey is ignored. No credentials supplied or push sent during audit. Telegram/Zalo validate provider response instead of HTTP 200 alone; Zalo token sent as header. Provider errors no longer log raw response/token. Push tests target authenticated SA's own token; unsupported channels fail. Migration 20261112150000_IsolateActivePushTokens deactivates ambiguous active tokens shared by multiple accounts and adds partial unique index. Users must unregister/rotate token when switching accounts; no automatic reassignment. Email provider and scoped Telegram/Zalo routing require confirmed destination/provider configuration; global destinations remain blocked for area-specific events.

## Regression and deployment verification

Added PostgreSQL assertions for per-area settings uniqueness, per-sensor replay key uniqueness, active push-token isolation, outbound FK and backup execution columns. Added API tests for tracked outbound/replay, historical barcode reconciliation, harvest receipt capacity after previous outbound and cross-area settings denial. Backup files use a .partial path until archive verification and atomic rename. CI must pass all clean/legacy migration tests before merge. No sales source changed. Existing FE/App clients need the API contract changes described above; test against VPS only after the new deployment succeeds.

## Platform configuration truthfulness

Camera configuration validates camera identity, schedule JSON, retention/FPS/storage limits and returns runtimeStatus=NotIntegrated until an actual recording worker exists. AI service registration validates names/version, confidence, FPS, batch size and class JSON; the system service summary cannot infer Running from a configuration row. Rollback only references distinct previously recorded successful versions, not requested deployments. Media storage is not reported healthy from local disk alone. Heuristic AI has confidence=0 (no model result), with a separate ruleScore in ResultJson. External camera, inference and orchestration workers remain blocked by missing approved runtime contracts and credentials; do not fabricate execution.

## Mobile sync and condition evidence follow-up

Offline create_crab now delegates to the online registration service using the requested UUID, but server-generated code/QR and normal weight/carapace, capacity, area, allocation and history requirements. Existing clients must send positive weightGram, carapaceWidthMm and carapaceLengthMm. Updates/transfer reject stale or invalid timestamps as conflicts instead of acknowledging them as applied; optional baseServerUpdatedAt is the server timestamp from pull; transfer requires sourceBoxId. Batch max 100, duplicate keys rejected, conflict keys persisted, and response conflicts describes refresh/retry. Box status cannot contradict current allocation. Pull cursor validates entity type. Condition submit uses authenticated inspector identity and same-box registered photo checks, not client-supplied actor. No schema change in this follow-up. Legacy untyped queue/download remain 501 intentionally; app must use scoped pull/batch and private media content routes. E2E offline client testing still required.

Condition measurements no longer convert heuristic pre_molt into actual Molting or clear Quarantined. Measured weight and prediction history remain; harvest recommendation is an inspection reminder, not authorization to harvest. Duplicate offline creation under a different key conflicts; exact replay remains idempotent. Tests cover actor spoofing, cross-box evidence and preservation of quarantine.

## Online farm-state follow-up

Box configuration and operational writes validate occupancy against live crabs/open allocations, reject inconsistent status and record authenticated actor in history. Ordinary crab profile rejects nonpositive measurements/future molt time and cannot clear quarantine. Weight history rejects terminal/missing crabs and future/pre-stocking measurements, binds registered private media under existing guard, records authenticated operator, and does not overwrite newer weight snapshots with older measurements. No schema changes or sales changes. Legacy contradictory box occupancy is rejected until corrected via allocation workflow. Quarantine release needs a separately defined audited workflow if required by the farm.

## PR follow-up: allocation and molting integrity

Fix permission regression fixture: empty box may be marked maintenance, but fabricated active occupancy returns 409; mock allocation repository explicitly. Molting creation requires crab area access independently of submitted box, exact current box, active lifecycle, valid dates and positive measurements. Historical edits/void retain corrections but do not rewrite current lifecycle/weight (a fresh observed profile correction is required). Transfer checks source ownership even when snapshot is missing and records actual actor rather than area admin; preserve quarantine/molt display in destination. An open allocation cannot be closed through history editing. No schema/sales/IoT changes. CI required; previous PR40 merged prematurely and test failed.

## Concurrent state changes and report scope

Migration 20261112160000_ProtectFarmStateConcurrency adds bigint ConcurrencyVersion to Boxes and Crabs, defaults legacy rows to 0 and advances in PostgreSQL BEFORE UPDATE triggers. EF uses database-generated concurrency tokens, so stale writes fail 409 and rollback associated history rather than resurrect a crab or erase a concurrent allocation. This applies to ordinary farm operations and existing sales references without extending sales business logic. Added DB trigger/stale-write SQL assertion, EF metadata tests, and authenticated cross-area molt regression. Current-snapshot daily crab report now scopes by current box, falling back to latest allocation for unplaced records, not any old allocation. Historical movement-based daily reports would require event-time reconstruction; this change deliberately avoids exposing current state outside the assigned area.

## Observation integrity follow-up

PR42 candidate: mortality only accepts active lifecycle (Alive/Molting/Quarantined), validates cause/time and positive measurements; feeding operations preserve quarantine and do not overwrite a newer observation with an older event, resolve box-only historical events from allocation records, check registered evidence, record actor and prevent retargeting a confirmed operation; manual inspection uses authenticated operator name and validates weight/time; training rejects future/pre-stocking events, negative feed/duration and observation before linked feeding; alert acknowledge and processing retries do not regress or reset active incident state. No database migration or sales/IoT changes. Historical operation read scope and explicit event-time cross-box validation still need further regression review; not a full end-to-end acceptance claim.
