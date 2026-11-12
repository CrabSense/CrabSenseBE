# BE audit follow-up and VPS verification

The new customer scope is intentional. New customers belong to one farming area. For legacy customers with orders in exactly one valid area, migration backfills that area; customers with ambiguous or missing evidence remain unassigned and are excluded from standalone customer listing/editing. Their existing orders are preserved. Do not guess ownership. An authorized multi-area user supplies farmingAreaId for customer upsert; select customerId for explicit edits or sales orders. Never merge by name alone.

Water snapshot/detail are read-only. A hosted worker advances simulation and checks hardware deadlines without sending physical commands. Only completed runs with completion timestamps contribute to latest metrics. Hardware start and device restart remain blocked until verified firmware/dispatcher support.

Security Policy controls refresh-token lifetime on login/refresh (1–90 days) and known-account failure alerts (1–20 failures per five-minute window). Access-token TTL is independently configured; existing refresh expiry is not retroactively changed. Database restore confirmation cannot be disabled. Security alert counts reflect threshold events in the last 24 hours.

CI publishes linux-x64, builds the EF bundle using non-production configuration, and exercises it on disposable clean and legacy PostgreSQL databases. No production secret or VPS is required for CI.

VPS cutover remains disabled unless EDGE_PROTOCOL_V2_READY=true. Verify firmware V2, FE authenticated media and customer contract, safe relay states, backup restore and production data ownership before enabling. The deployment workflow checks exact current main push CI, quiesces old API before final backup, retains default local media in /var/lib/crabsense/media-storage, and uses environment-based EF configuration. It restarts the old application only for failures before migration starts; post-migration automatic rollback requires matching schema stamp and Edge V2. Never restore production database automatically.

Remaining work: real inference service, hardware water dispatcher, safe reboot protocol, platform deploy/AI orchestration, backup scheduling, external notification routing/providers, old public media audit, full client/API E2E, performance/load and security scanning, and approved sales/payment/delivery contracts. No hardware execution, VPS mutation or production database change was performed by this PR.
