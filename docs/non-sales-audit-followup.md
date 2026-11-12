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
