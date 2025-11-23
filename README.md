# Simstarr Relay Control

<p align="center">
  <img width="388" height="262" alt="simstarr relay control thumb" src="https://github.com/user-attachments/assets/a7c07eeb-4e25-47d4-893e-26aff64d4d23" />
</p>


Simstarr Relay Control is a Windows application that integrates with Elite Dangerous to provide real-world haptic feedback using USB relays. It can trigger fog machines and other haptics based on journal updates, enhancing immersion during gameplay.

The program is designed so its features can be distributed across multiple computers. This is because The Simstarr is a custom-built motion simulator platform with specialized requirements that most users will never encounter.

Stand-Alone Mode will be the most useful for Elite Dangerous players, while Relay PC Mode by itself will be ideal for users who want a simple way to control the status of their hardware for unrelated purposes and don't need to access the game's log file. 

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

## Hardware 
This has been confirmed to work on a “SainSmart USB 4-Channel Relay Automation (5V)” purchased on Amazon. It should also work with other variants that use similar architecture but this is untested.

<img width="250" height="250" alt="image" src="https://github.com/user-attachments/assets/2b448c9e-16e8-4b8c-9c03-5e808d2e6fca" />

It's important to bridge the R20 jumper so the relays get power from the usb port, also note it does not come with a USB cable, ordering that at the same time is recommended. 
