# RAS automation safety contract

Automatic relay commands remain disabled until
`RasAutomation:FirmwareInterlockVerified=true` and the node rule contains
`"firmwareInterlock": true`.

The firmware must independently enforce:

- a watchdog timeout that moves each channel to its configured safe state;
- channel-specific safe state (the backend does not assume every relay is safe when OFF);
- maximum continuous run time for pumps and valves;
- physical high/low float interlocks even when the network or backend is unavailable;
- relay-state reporting after every accepted command, including command/delivery identity.

The backend does not retry an acknowledged command while relay-state evidence is
missing. It marks automation `unknown`, then `unsafe` after
`RelayStateTimeoutSeconds`. Missing, stale, future-dated, cross-device,
cross-system, inactive, or contradictory float evidence never turns a pump on.
