# P0.2 consolidated acceptance / P3

Use an isolated test farm with a dedicated area, row, empty boxes and separate accounts. Do not use real production crabs or connect physical actuators. Keep VPS hardware control disabled. Complete the code/CI gates first, then perform the following once as a consolidated UAT. Record actual result, screenshots, request IDs, and pass/fail for each case.

## Release gates
- Exact current main CI green for BE, App, FE. GitHub Pages deploy green; BE Deploy VPS green with DEPLOY_OK, hardwareControlDisabled=true.
- Backup before migration; verify schema migration 20261125180000_AddMissingCrabLotSnapshot in EF migration history and nullable integer column in be.CrabLots. Check old complete snapshots and incomplete legacy snapshots; no manual production UPDATE.
- Verify supported login surfaces: System Administrator/Farm Administrator FE Web; Farm Manager Desktop/Desktop Web; Farm Staff Mobile/Mobile Web. Reject the other role combinations and cross-area mutations.

## Single end-to-end acceptance
1. SA FE: verify system/device monitoring and no access to farm operation mutation; no hardware control.
2. FA FE: create test area, active row and boxes, assign manager/staff. Put one empty box in maintenance; it cannot appear in available targets. Reject physical deletion of a previously used box or its area.
3. FM Desktop: create and scope inbound lot; staff cannot create/close it. Check remaining count and area assignments.
4. FS Mobile: scan QR, register a crab with positive weight/width/length, photos and lot; verify stable ID and QR, one open allocation, initial history. Attempt duplicate registration and wrong-area/closed-lot registration; reject. Verify no partial writes.
5. FS Mobile: record measurements (including width-only edit), feeding and care. Verify timestamp and actor; stale historical measure must not overwrite latest. Attempt direct profile moltedAt mutation; reject. 
6. FS Mobile: transfer with original sourceBoxId. Attempt stale source and occupied/maintenance destinations; reject. Confirm one current allocation and old box release.
7. FS Mobile: record non-success molt; crab remains alive/Problem, box Watch, last successful molt unchanged. Record verified successful molt; softshell and timestamp/history update. Confirm mortality requires separate workflow.
8. FS Mobile: record missing with reason and correct box. Confirm closed allocation, released box, missing distinct from dead. FM Desktop: recover same ID/QR/lot to valid same-area empty box with reason; reject terminal, closed lot, occupied target, repeated recovery and wrong role. FS reassess health.
9. FM Desktop: correct/void historical event with reason; retain original audit and no automatic current snapshot rewrite. Confirm scope on corrections.
10. FM Desktop: close test lot containing one missing and one dead; verify frozen registered/alive/dead/harvested/missing counts, closed lot unavailable for registration, partial scope hides full snapshot. Check survival/health-watch filters and missing separate from mortality. Mobile health watch excludes ended crabs.
11. Check cross-platform refresh, error handling, permission denial, QR/media authorization and history. Repeat relevant flows on hosted web variants. Confirm no active allocation duplicates.
12. Record defects and evidence; do not declare P0.2 accepted until all blocking cases pass.

## Known boundaries
This is a test plan, not evidence of manual acceptance. Direct SQL state and actual account-based UAT require authorized access. Hardware/physical commissioning is separate.
