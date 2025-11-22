# Simstarr Relay Control

A small Windows app that reacts to Elite Dangerous ship heat events by activating a fog machine and up to three extra relays. Use it for ambient effects while playing.

## What It Does
- Watches the Elite Dangerous journal for heat warnings / heat damage.
- Lets you manually trigger a fog blast or toggle relays.
- Can run everything on one PC or split “game” and “relay hardware” across two PCs.
- Provides global hotkeys for quick activation.

## Modes (choose on first run)
1. Stand Alone – Game journal + local relay hardware.
2. Relay PC – Only drives attached relay hardware (fog + relays).
3. Game PC – Reads game journal and sends commands to a separate Relay PC.

## Basic Use
1. Install FTDI driver for your USB relay device (if using hardware).
2. Start the app; pick a mode.
3. Point it at your Elite Dangerous journals folder (Stand Alone / Game PC).
4. (If split setup) Enter the Relay PC address (shown on the Relay PC screen).
5. Press Start.
6. Use buttons or hotkeys to trigger fog / toggle relays.

## Hotkeys
Ctrl+Alt+1  Fog (3s)  
Ctrl+Alt+6  Fog (5s)  
Ctrl+Alt+2  Relay 2 toggle  
Ctrl+Alt+3  Relay 3 toggle  
Ctrl+Alt+4  Relay 4 toggle  
Ctrl+Alt+5  Start / Stop  

(Numpad 1–5 duplicates these.)

Fog has a short cooldown; attempts during cooldown are ignored.

## Requirements
- Windows
- .NET 10 runtime
- Elite Dangerous installed (journals available)
- USB relay (optional if just experimenting)

## Troubleshooting (Quick)
- Fog not firing (split setup): Make sure Relay PC is running first; hit Ping.
- No journal events: Folder path wrong or game not running.
- Hotkeys fail: Another app using them or app not in Started state.

## Privacy & Network
If you expose the Relay PC beyond your LAN, add your own authentication (current build uses optional outbound token only).

## License
Add a license section here.

Enjoy the smoke!
