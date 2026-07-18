# CrabSenseBE

> Backend API + PostgreSQL cho **CrabSense / CrabGuardian** — phần mềm vận hành trại nuôi cua lột.

Repo này triển khai **Member 1 (BE/DB)** theo workbook `CrabGuardian_Spec_Workbook.xlsx` (sheet `03_Module`, `06_API`, `04_Bang_DB`).

## Vai trò trong hệ thống

| Mục | Nội dung (Excel) |
|-----|------------------|
| Module | MOD-AUTH, MOD-FARM, MOD-HARVEST, MOD-FROZEN, MOD-TRACE, MOD-MARKET, MOD-SALES, MOD-SYS (+ API cho IoT/AI) |
| FR chính | FR-1, FR-3, FR-6, FR-7, FR-8, FR-10, FR-A |
| Platform | ASP.NET Core API + PostgreSQL (Swagger) |
| DB | 28 nhóm bảng — script `01` → `05` (`CrabSense_Full.sql`) |

## 11 module sản phẩm (sheet 03_Module)

1. **Authentication & User** — JWT, RBAC: `admin` / `operator` / `viewer` / `system_admin` / `sales`
2. **Farm Management** — Area / Row / Box / Crab / Lot / Batch + **Operation Log**
3. **IoT Monitoring** — API ingest sensor + Device management (firmware/battery/RSSI)
4. **AI Decision Support** — nhận detection, inspection, feedback, recommendation, dataset
5. **Harvest Management** — `harvest_voucher` / `harvest_line` + softshell window
6. **Frozen Inventory** — `frozen_lot` + expiry alert
7. **QR Traceability** — `qr_code` + `traceability_link` (public lookup)
8. **Marketplace** — Quotation → Order → Packing → Shipping → Completed + `delivery`
9. **Customer & Sales** — customer types, price M/L, payment, invoice (`payment_status`, không cần cổng TT online)
10. **Reports & Analytics** — harvest / inventory / sales / mortality / WQ / AI accuracy
11. **System Administration** — farm_settings, Notification Center, alert thresholds

## Database (sheet 04_Bang_DB — 28 nhóm)

| STT | Bảng | Nhóm |
|-----|------|------|
| 1 | app_user | Auth (+ role sales) |
| 2 | water_system / zone / flow | RAS |
| 3–5 | sensor*, device, water_measurement | IoT |
| 6–10 | crop_batch, farming_*, box/crab/lot, operation_log, feed/molting… | Nuôi |
| 11–12 | alert_threshold / alert / notification* | Cảnh báo + Notify Center |
| 13–18 | video_*, ai_* | Video + AI Decision |
| 19–21 | harvest_*, frozen_*, qr/trace | TH + Đông + QR |
| 22–28 | customer, price_*, sales_order*, delivery, payment/invoice, inventory_txn/pre_order, farm_settings | Sales / Marketplace / System |

Nguồn schema: `Cap_Ras/database/CrabSense_Full.sql` (gộp script 01+02+03+05 + seed).

## API (sheet 06_API)

Contract chuẩn JSON:

```json
{ "success": true, "message": null, "data": { } }
```

Nhóm endpoint chính (xem full trong Excel `06_API` ~176 API):

- `/api/auth/*`, `/api/users/*`
- `/api/farming-areas`, `/api/farming-rows`, `/api/boxes`, `/api/crabs`, `/api/crab-lots`
- `/api/iot/sensor-data`, `/api/sensors`, `/api/devices`, `/api/water-systems`
- `/api/alerts`, `/api/alert-thresholds`, `/api/notifications`, `/api/notification-channels`
- `/api/videos`, `/api/ai/detections|feedback|recommendations|datasets`, `/api/inspections`
- `/api/harvest-vouchers`, `/api/frozen-lots`, `/api/qr-codes`, `/api/traceability/{code}`
- `/api/customers`, `/api/price-lists`, `/api/sales-orders`, `/api/deliveries`, `/api/payments`, `/api/invoices`
- `/api/operation-logs`, `/api/reports/*`, `/api/dashboard/overview`, `/api/settings`

## Stack đề xuất

- ASP.NET Core 8 (N-Layer: Api / Application / Domain / Infrastructure)
- PostgreSQL 14+
- JWT + RBAC
- Docker Compose (API + Postgres)

## Liên kết repo anh em

| Repo | Vai trò |
|------|---------|
| [CrabSenseFE](https://github.com/CrabSense/CrabSenseFE) | Web admin/operator/sales |
| [CrabSenseApp](https://github.com/CrabSense/CrabSenseApp) | Mobile hiện trường |
| [CrabSenseKiosk](https://github.com/CrabSense/CrabSenseKiosk) | Desktop / wallboard |
| [CrabSenseIot](https://github.com/CrabSense/CrabSenseIot) | ESP32 / gateway firmware |
| [CrabSenseAIWaterQualityAnalysis](https://github.com/CrabSense/CrabSenseAIWaterQualityAnalysis) | AI Decision + phân tích WQ |

## Nguồn Excel

`CrabGuardian_Spec_Workbook.xlsx` — sheets: `01_Chuc_nang_FR`, `03_Module`, `04_Bang_DB`, `06_API`, `08_Vai_tro`.
