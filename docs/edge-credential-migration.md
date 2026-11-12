# Edge credential migration

Do not disable `EdgeSync:AllowLegacySharedKey` until every active controller
has completed this process.

1. Inventory devices whose `CredentialHash` is null. Confirm their kiosk,
   farming area, status, and owner; do not auto-assign unknown devices.
2. For one kiosk at a time, approve each controller through the existing
   provisioning workflow. Approval creates a per-device secret and stores only
   its SHA-256 hash.
3. Deliver the one-time secret to that controller and configure it as
   `X-Device-Key`. Never log or persist the plaintext secret in Backend config.
4. Verify pending-command, ACK, sensor-ingest, relay-state, and water-analysis
   calls with the device credential. Verify another device cannot reuse it.
5. Revoke and investigate a device if the secret is lost or ownership is
   uncertain. Re-approval rotates the credential.
6. After the inventory query reports no active device with a null
   `CredentialHash`, set `EdgeSync:AllowLegacySharedKey=false` in a staged
   environment, observe it, and then apply the same setting in production
   through the normal reviewed deployment.

Rollback is configuration-only: temporarily re-enable the legacy key while
finishing provisioning. Do not clear device credential hashes or rotate all
devices in bulk.

Private Google Drive media also requires a non-public
`GoogleDrive:PrivateFolderId`. The shared/public folder must not be reused for
internal media.
