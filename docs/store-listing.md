# Store listing

## Product name

Keypivo

## Short description

Switch your security key between Windows and WSL2 from the system tray.

## Description

Use your USB security key on Windows or WSL2 with a quick switch from the system tray. See its current connection at a glance and get notified when it changes.

Requires usbipd-win and WSL2 to be installed separately. The key is available to one operating system at a time.

## Features

- Switch between Windows and WSL2 from the system tray
- View connection status and receive change notifications
- Choose your key using USB vendor and product IDs
- Place the menu at the bottom right or center of your screen

## Certification notes

We request the allowElevation capability to support two user-initiated operations that require administrator privileges:

- Enable sharing: When the user selects “Switch to WSL” for a device that is not yet shared, Keypivo runs usbipd bind.
- Remove sharing: When the user selects “Stop sharing this key” in Settings, Keypivo runs usbipd unbind.

For these operations, Keypivo requests elevation for the external usbipd.exe process through the standard Windows UAC mechanism.

Testing requires Windows, usbipd-win, a running WSL2 distribution, and a USB security key. Default matching uses VID 1050 with no PID restriction; the IDs can be changed in Settings. The device is exclusively available to Windows or to the USB-IP client. The application only manages USB ownership; it does not implement OpenPGP or SSH authentication.

## Screenshots

| Image | Caption |
| --- | --- |
| [Keypivo](images/keypivo.png) | Switch your security key between Windows and WSL2 from the tray. |

## Privacy and support

- [Privacy policy](privacy.md)
- [Support](https://github.com/rubyu/keypivo/issues)
