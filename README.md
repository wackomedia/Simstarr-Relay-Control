# Simstarr Relay Control

Simstarr Relay Control is a Windows application that integrates with Elite Dangerous to provide real-world haptic feedback via USB relays. It can trigger fog machines or other devices based on journal updates, enhancing immersion during gameplay. 

The Simstarr is an advanced motion simulator platform with special needs, so this program can separate game and hardware features of the app to multiple computers. Stand Alone mode will be most useful to you normies.

## What It Does
- Watches the Elite Dangerous journal file for heat warnings / heat damage.
- Activates relay 1 for 3 sec for WARNING and 5 sec for DAMAGE, then forces cooldown for 5 seconds.
- ON/OFF control for three extra relays currently not yet assigned to real-life devices.
- Provides global hotkeys so apps like Touch Portal and Voice Attack can trigger actions.

## Hotkeys
Ctrl+Alt+1  Fog (3s)  
Ctrl+Alt+6  Fog (5s)  
Ctrl+Alt+2  Relay 2 toggle  
Ctrl+Alt+3  Relay 3 toggle  
Ctrl+Alt+4  Relay 4 toggle  
Ctrl+Alt+5  Start / Stop application

(Numpad 1–5 duplicates these hotkeys.)

## Modes
1. Stand Alone – one app instance for everything.
2. Relay PC – The PC that has the hardware, separate from game.
3. Game PC – Reads game journal and sends commands to a Relay PC.

## Disclaimer
This app is open source, feel free to modify it for your own needs. It was built in Visual Studio with Copilot AI.
