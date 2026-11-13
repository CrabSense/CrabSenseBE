# Controller inventory integration — canonical main schema
Uses existing DiscoverControllerPorts migration and /api/iot/controllers/capabilities, not a second port table.
Kiosk forwards a signed firmware manifest as the canonical capability DTO. Existing sensor and RAS foreign keys remain intact.
Same descriptor preserves commissioning status when an unrelated port is appended; changed/removed ports are Pending/Disabled.
Command inventoryRevision is stamped at enqueue and checked again at lease, Kiosk and firmware.
OFF can target a current reported output even before RAS assignment; ON still requires Verified exact RAS binding and physical firmware interlocks.
PUT /api/iot/controllers/{deviceCode}/ports/observations stores signed GPIO state via the authenticated Kiosk. Not proof of load current.
Global hardware lock remains unchanged. No live actuation or production migration is performed by CI.
