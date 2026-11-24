# Media storage layout

New Google Drive uploads are routed by the backend. Existing MediaAsset IDs, StorageKey values, and URLs remain unchanged; no migration, move, or deletion is performed.

Configure distinct public and private folder IDs: `GoogleDrive:SharedFolderId` and `GoogleDrive:PrivateFolderId` (or `GOOGLE_DRIVE_SHARED_FOLDER_ID` and `GOOGLE_DRIVE_PRIVATE_FOLDER_ID`). The private root must be accessible to the service account and must not have public permissions. For private uploads use a Drive configuration with writable storage quota (e.g. Shared Drive). Apps Script is only used for small explicitly public uploads. Do not enable public sharing on the private folder to resolve quota problems.

| Root | Contents |
| --- | --- |
| 01_NhapHang | Inbound lots |
| 02_NuoiCua | Farm, area, row, box, crab and related events |
| 03_GhiHinh | Camera videos, area/row/camera/YYYY/MM/DD/source |
| 04_PhanTichNuoc | Existing water analysis run or batch/chemical/Raw |
| 05_ThuHoach | Harvest evidence |
| 06_KhoDongLanh | Frozen lots |
| 07_ThietBi | Equipment |
| 08_NguoiDung | User media |
| 09_AI | Reserved for future AI dataset/prediction contracts |
| 10_HeThong | Logs |
| 99_TamThoi | Unassociated images |

Dates in new camera and water paths are UTC. Camera runtime uploads derive location, start time and mode from the server-side execution and schedule; the agent need not know the path. A water upload linked to an already-created run resolves by its batch and analyte. Samples uploaded before a run exists still use their existing upload contract; linking or reorganizing these samples requires a separate batch-aware upload feature. No new automatic deletion policy is introduced by this change. Existing camera retention policies remain authoritative. Public legacy upload routes retain their existing sharing behavior.

Before deployment: confirm distinct public/private roots, service-account quota and permissions, test a public image, a private crab image and a scheduled MP4; verify download via authenticated API and verify old files remain readable.
