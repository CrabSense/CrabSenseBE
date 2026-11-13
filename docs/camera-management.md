# Camera management contract

System Administrator only: /api/cameras (list/create), /api/cameras/{id} (detail/update/delete), /enable, /disable, /health, /capabilities, /test-connection, /events, /recording (GET/PUT), /api/cameras/recording-schedules, /api/cameras/recordings, /api/system/device-locations.

Camera metadata continues to use Devices. Location lookup exposes only IDs, codes, names, active flags and child rows. Existing /api/devices routes remain compatible. Camera Online is derived from authenticated device heartbeat and a 15-minute freshness window; enabling returns Offline until a new heartbeat. It does not prove the stream is playable.

Use explicit RTSP(S) or HTTP(S) stream URLs, HTTP(S) snapshot URLs. URLs with userinfo, query or fragment are rejected on write and hidden on read; legacy values remain in the database. Do not embed credentials in paths either. Sources requiring credentials or query tokens must be migrated to a future authenticated Camera Agent. No server-side camera URL proxy is implemented. Camera test returns NotIntegrated, not a live stream verdict.

Weekly schedule JSON (day 1=Monday, 7=Sunday; same-day non-overlapping HH:mm slots; split overnight ranges into separate days):
```json
{"timezone":"Asia/Ho_Chi_Minh","slots":[{"day":1,"start":"08:00","end":"17:00"}]}
```

This is a configuration contract, not a recording engine. Runtime remains NotIntegrated for enabled modes. History lists technical video media associated with cameras, not evidence that scheduled recording executed. Camera deletion is blocked when media or AI history is linked. Existing legacy credentials require operator remediation; no migration deletes them. Camera restart/firmware/agent streaming/retention remain unimplemented.

Deployment order: BE before FE when introducing the location lookup or camera enable endpoint.
