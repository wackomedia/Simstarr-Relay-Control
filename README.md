# Simstarr Relay Control

## Overview
Simstarr Relay Control is a Windows Forms application (.NET 10, C# 14) used to monitor Elite Dangerous journal events and drive external relay-controlled hardware (fog machine + 3 additional relays). It supports three operating modes to split responsibilities across PCs or run everything locally.

## Operating Modes
1. Stand Alone
   - Local relay hardware (USB FTDI) + local journal monitoring.
   - Heat warnings/damage auto-trigger fog blasts (3s / 5s) with cooldown.
2. Relay PC
   - Only drives hardware; no journal monitoring.
   - Optionally hosts embedded HTTP API (if EMBED_HTTP_SERVER defined).
3. Game PC
   - Monitors journals only and forwards fog/relay commands to a remote Relay PC.

Mode selection persists across runs (stored in settings JSON under %APPDATA%\Simstarr).

## Core Components
- MainForm: UI, mode orchestration, hotkeys, cooldown logic, embedded server startup.
- RelayController:
  - Initializes FTDI device.
  - Maintains relay bitmask and state.
  - Methods: SetRelay, PulseRelay, FogBlast (relay 0 by default), Dispose/Cleanup.
- JournalWatcher:
  - Background tail of newest journal file.
  - Raises events (HeatWarning, HeatDamage) used for automated fog activation.
- EventForwarder:
  - HTTP client targeting http://<host>:<port>/api/relay
  - Actions: ping, fog, setRelay.
  - Adds optional X-Auth-Token header if token provided.

## Embedded HTTP Server (conditional)
Compiled only if EMBED_HTTP_SERVER is defined.  
Endpoint: POST /api/relay  
Accepted JSON actions:
- {"action":"ping"}
- {"action":"fog","relayIndex":0,"durationMs":3000}
- {"action":"setRelay","relayIndex":2,"state":true}

Responses include JSON result or error text. When hardware absent, returns simulated responses.

## Settings Persistence
Stored in settings.json:
- JournalPath
- RelayAddress (normalized to http:// form)
- AppMode ("StandAlone", "Relay", "Game")

Loaded once at process start; saved on mode change, address edits, and application close.

## Hotkeys (global, registered when running)
Ctrl+Alt+1  Fog relay 1 (3s)  
Ctrl+Alt+6  Fog relay 1 (5s) (optional)  
Ctrl+Alt+2  Toggle relay 2  
Ctrl+Alt+3  Toggle relay 3  
Ctrl+Alt+4  Toggle relay 4  
Ctrl+Alt+5  Start / Stop  
NumPad 1–5 duplicates above (for alternate keyboards)  
(Uses MOD_NOREPEAT to suppress auto-repeat.)

## Cooldown Logic
Fog activation (manual or auto) enforces a 5s cooldown (ActivationCooldown). Attempts during cooldown are logged and ignored.

## Relay UI Indicators
Relay buttons show ON/OFF state. Fog buttons change color for duration of active blast. Relay mode displays all local IPv4 addresses and the exact string for Game PC to enter (ip:5000 plus full URL).

## Forwarding Behavior (Game PC)
On HeatWarning / HeatDamage:
- Sends fog command to Relay PC via EventForwarder.
- Locally updates fog UI if forwarded successfully.
Manual fog and relay actions also forward if not in a local hardware mode.

## Error Handling & Logging
All significant actions append timestamped lines to the log textbox:
- Initialization status
- Hotkey registration failures (includes Win32 error codes)
- Forwarding success/failure
- Cooldown rejections
- Journal discovery and parsing notices
- Embedded server requests (when enabled)

## Build / Run Requirements
- .NET 10 SDK
- Windows (global hotkeys & WinForms dependencies)
- FTDI drivers installed for hardware relay device (RelayController)
- Elite Dangerous journal directory configured (Stand Alone / Game modes)

## Extensibility Notes
- Additional journal events can be wired by adding new events in JournalWatcher and handlers in MainForm.
- More relay actions (patterns) can be layered over PulseRelay.
- Security: If exposed beyond LAN, add authentication enforcement inside embedded server (currently optional token only for outbound forwarder).

## Troubleshooting
- Hotkeys not firing: ensure app is running (Start pressed), no other app has conflicting global combinations, check log for Win32 error details.
- Fog not triggering on Game PC: verify RelayAddress resolves and ping succeeds; confirm Relay PC started first.
- No journal events: verify correct Elite Dangerous folder, presence of *.journal* files, and game running.

## License / Attribution
Add preferred license information here.

## Encoding / Unicode Note
This README was sanitized to plain ASCII (replaced non-breaking hyphens and dashes). If save errors persist, ensure file encoding is UTF-8 without BOM:
1. In **File > Save As**, click the arrow on the Save button and choose **Save with Encoding**.
2. Select UTF-8.
