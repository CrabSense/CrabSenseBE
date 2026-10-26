# CrabSense Local UI (kiosk / máy local)

Static UI để test API CrabSense trên máy local.

## Chạy

1. API: `http://localhost:5080` (project `src/CrabSenseBE.Api`)
2. UI:

```powershell
.\start.ps1
```

Mở `http://localhost:5500` — nhập user/pass thật trên form. Demo account (`owner`, `staff`, ...)
chỉ tồn tại khi bật `Auth:SeedDemoUsers:Enabled=true` + `Auth:SeedDemoUsers:Password`.

Các script `seed-*` / `verify-*` đọc credential từ env, không hardcode:

```powershell
$env:CRABSENSE_USER='...'; $env:CRABSENSE_PASSWORD='...'
$env:CRABSENSE_EDGE_KEY='...'   # bắt buộc cho POST /api/iot/sensor-data*
```

## Seed IoT demo (optional)

```powershell
.\seed-demo-iot.ps1
```
