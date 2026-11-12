# Role, area-policy and Edge protocol 2 rollout

## Database and API changes

- Four roles and multi-area CrabLot remain unchanged. The Staff–Manager–Area composite foreign key rejects mismatched assignments and removal of a Manager assignment with dependent Staff. The migration aborts on legacy mismatches; review them manually instead of fabricating assignments.
- `Problem` is a health condition, not quarantine. Existing numeric `Quarantined=3` and historical rows are retained. Terminal/missing lifecycle cannot be resurrected by a condition update.
- Global NotificationRules (`farmingAreaId: null`) are defaults. Environmental rules may be overridden per assigned area; `PUT /api/notification-rules/{typeCode}` requires `farmingAreaId` for `sensor_threshold`, `tank_overflow`, and `tank_empty`. A Farm Administrator cannot edit another area's rules or disable area isolation. GET returns defaults plus permitted overrides, with `farmingAreaId` in each row; UI should select area override first, then default. Technical rules remain platform-owned. Scoped external channels still require dedicated recipient routing; no area alert is sent to a global external destination.
- The migration preserves global rules; it does not guess separate historical settings. Notification settings never disable physical firmware interlocks.

## Edge firmware contract (required before real relay control)

The API refuses command polling and ACK without the header `X-Edge-Protocol: 2` (HTTP 409 after device authentication). The header is a version declaration, not proof of firmware safety. Continue using the device-specific `X-Device-Key`; migrate away from legacy shared credentials.

`GET /api/edge/commands/pending?deviceCode=ESP32-01` returns commands with `commandSequence` (positive signed 64-bit integer), `deliveryAttemptId`, `expiresAt`, and the usual command ID/channel. PostgreSQL allocates a monotonically increasing global sequence under a per-device transaction lock. A newer command supersedes pending/delivered older commands for the same channel; a new alloff supersedes all older pending/delivered commands on that device. An older alloff may still affect channels not overridden by newer commands. Its execution MUST therefore be checked per channel.

Firmware requirements:

1. Use a durable high-water mark per relay channel (NVS or equivalent). On receipt of a command, reject expired commands and any sequence below the persisted channel high-water mark. Duplicate sequence: do not actuate again; report the actual relay state and ACK the current attempt if valid.
2. Persist the new sequence before actuating, serialize execution, and use explicit on/off whenever possible. Never automatically replay a toggle after reboot or ACK loss. If a reboot interrupts actuation, reconcile physical relay state and request a fresh desired-state command. The sequence is a fence, not proof that the relay actuated.
3. For alloff, apply only to channels with a lower high-water mark; do not overwrite newer channel commands. Persist each affected channel independently before actuation. Validate channel configuration.
4. ACK `POST /api/edge/commands/{id}/ack` with the same protocol and device headers and the current `deliveryAttemptId`. Use `status: "acknowledged"` or `"failed"`. Send authenticated relay-state observations separately. Do not treat ACK as physical flow evidence.
5. Firmware must enforce independent high/low float interlocks, dry-run/overflow protection, safe boot and connectivity-loss behavior. Protocol 2 alone is not a safety certification.

## Cutover and rollback

- Do not enable RAS automation or connect live pumps until firmware contract and fail-safe tests pass.
- Schedule a controlled cutover with relay outputs in the verified safe state. The migration marks old pending/delivered commands superseded, retains history, and assigns historical sequences. Reissue desired states after firmware upgrade.
- Check for invalid legacy Staff assignments before deploying:
  ```sql
  SELECT s."Id", s."StaffUserId", s."ManagerUserId", s."FarmingAreaId"
  FROM be."FarmStaffAssignments" s
  LEFT JOIN be."FarmManagerAssignments" m
    ON m."ManagerUserId" = s."ManagerUserId"
   AND m."FarmingAreaId" = s."FarmingAreaId"
  WHERE m."Id" IS NULL;
  ```
- **Do not use application-only rollback to a protocol-1 binary after this migration.** Old code cannot enforce area overrides or the firmware version gate. The migration Down refuses to discard scoped policies or ordered command history. Recover with a compatible application or a verified database backup and a coordinated firmware/device safe-state procedure.
- BE repository contains no ESP32 firmware source. Firmware must be implemented and tested in its own repository; this PR changes only the BE contract and persistence.


## Follow-up hardening

- Farm operations: Staff records and may edit own entries within 24 hours; Managers read/approve through dedicated audited workflows. Training feeding/observation writes are Staff-only; label confirmation is Manager-only. Personal reminders remain available to farm roles, with area assignment checks on every read/write.
- Critical environmental notifications always go in-app to assigned farm roles, even if optional rules or preferences are disabled. External-only sends create a hidden delivery anchor and record sent/failed without exposing an in-app notification. Email/FCM still require a configured provider; no fake success. Area-scoped external destinations are intentionally not routed to global chat IDs.
- Crab/harvest photos: new uploads are private, using authenticated /api/media/{id}/content URLs. Frontend must load with JWT. Pending upload is owned by its uploader, then attached in the registration transaction. JPEG, PNG, WebP are accepted after signature validation. Legacy links without registered metadata are no longer server-fetched. Existing public links are not automatically revoked; review historical storage permissions separately.
- QR public data includes frozenLots and grades, with Mixed for heterogeneous grades. The old singular lot fields are null when several lots exist.
- VPS workflow is gated by repository Actions variable EDGE_PROTOCOL_V2_READY=true. Do not set it until firmware/FE and safe-state cutover are verified. The old application stops before migration; a failed health check will not restart a protocol-1 package on the upgraded schema. Do not restore production data automatically.
