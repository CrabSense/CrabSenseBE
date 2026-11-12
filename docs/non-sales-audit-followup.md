# Non-sales BE audit follow-up

Scope: owner excluded sales/payment/delivery/price lists. This PR does not modify them. Keep VPS FE/App-only, hardware control disabled.

Changes: reject invalid transaction types and rounded zero weights; require area ownership on harvest-linked frozen lots and barcode registration; cap cumulative linked inbound across lots against confirmed harvest totals, serialized using existing harvest concurrency token; require barcode weight total to match current lot; serialize barcode registration with inventory updates. Manual lots without harvest links remain supported for separately documented stock. Existing legacy discrepancies require reconciliation, not guessed corrections.

AI media analysis returns 501 until real inference exists; current box rules remain available with explicit rule-score semantics. Dashboard water component measures fresh sensor coverage without an artificial 70% floor, not chemical quality. Device telemetry cannot alter administrative maintenance or MAC, and validates battery and status. Sensor replay lookup includes sensor identity; DB unique dedup and concurrent replay still need a migration with legacy duplicate reconciliation.

Remaining: item-level frozen outbound ledger, global vs area setting contract, actual AI/camera/notification/backup/deployment workers, offline E2E, provider and physical commissioning. No sales workflow extension and no hardware enablement. No new DB field in this first PR; existing harvest/frozen concurrency tokens reused.

## Second commit — schema and API

Migration 20261112120000_TrackFrozenItemsAndSensorReplay adds FrozenCrabItem.StockState (Available/Dispatched/Unverified), DispatchedAt, OutboundTransactionId with FK, and WaterMeasurement.EdgeEventKey with a unique partial index scoped to SensorId. Legacy items are Available only if no recorded outbound and count/weight match current stock; otherwise Unverified. No legacy sensor rows are removed. New event IDs hash to a bounded key; preexisting source lookup still prevents replay.

GET /api/frozen-lots/{lotId}/crab-items lists area-scoped barcodes. POST /api/frozen-lots/{lotId}/crab-items/reconcile is FM only, accepts availableItemIds and reason, checks physical count/weight, records audit without inventing historical dispatch dates. Outbound execute adds frozenCrabItemIds; required for tracked lots; linked harvest lots require barcode registration before dispatch. Aggregate-only manual lots retain existing workflow. New inbound into a barcode-registered lot is rejected: create a new lot. No sales, payment or delivery changes.

FE/App must implement barcode selection, reconcile UI, and render 501 for media inference. Deployment must use CI and migrations before API consumption.
