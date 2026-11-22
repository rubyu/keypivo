# Store listing

## Product name

Keypivo

## Short description

Switch your security key between Windows and WSL2 from the system tray.

## Description

Use your USB security key on Windows or WSL2 with a quick switch from the system tray. See its current connection at a glance and get notified when it changes.

Requires usbipd-win and WSL2 to be installed separately. Initial sharing setup requires administrator access. The key is available to one operating system at a time.

## Features

- Switch between Windows and WSL2 from the system tray
- View connection status and receive change notifications
- Choose your key using USB vendor and product IDs
- Place the menu at the bottom right or center of your screen

## Certification notes

Keypivo runs as a standard-user desktop application and declares `runFullTrust`. It does not request `allowElevation` or launch elevated processes.

The app uses the separately installed usbipd-win tool to read device state and run `attach --wsl` and `detach` at normal user privileges. Installation, `bind`, and `unbind` are performed manually by the user outside Keypivo. Setup pages show the required commands and offer clipboard copying and read-only status checks. Keypivo does not execute these setup commands or open an administrator terminal.

After completing each manual step, select **Check installation** or **Check status**. A confirmed result returns to the normal tray menu. An incomplete step keeps its instructions visible. Normal Windows/WSL switching is available from the menu after the key is shared.

Testing requires Windows, usbipd-win, a running WSL2 distribution, and a USB security key. Default matching uses VID 1050 with no PID restriction; the IDs can be changed in Settings. The device is exclusively available to Windows or to the USB-IP client. The application only manages USB ownership; it does not implement OpenPGP or SSH authentication.

## Screenshots

| Image | Caption |
| --- | --- |
| [Keypivo](images/keypivo.png) | Switch your security key between Windows and WSL2 from the tray. |

## Privacy and support

- [Privacy policy](privacy.md)
- [Support](https://github.com/rubyu/keypivo/issues)
