# Store listing draft

## Product name

Keypivo

## Short description

Switch your security key between Windows and WSL2 from the system tray.

## Description

Keypivo is a small Windows tray utility that helps you switch a USB security key between Windows and WSL2. View its connection status, switch with one click, and receive a notification when its state changes.

Choose the device by vendor ID and an optional product ID. If several connected devices match, Keypivo selects the first one. Place the popup at the bottom right of your screen or in the center.

Keypivo requires usbipd-win and WSL2 to be installed separately. Keep a WSL2 terminal open when switching to WSL. USB access belongs to one operating system at a time. GnuPG, SSH agents, smart-card services, and Linux USB permissions must be configured separately.

Keypivo does not access private keys, request PINs, or perform cryptographic operations. Its interface is English-only.

## Features

- Compact system tray menu
- Switch between Windows and WSL2
- Connection status and change notifications
- Configurable USB vendor and product IDs
- Bottom-right or centered popup

## Certification notes

This WinForms desktop utility runs at normal user privileges. It invokes a separately installed usbipd-win executable to list USB devices and attach or detach a matching security key. The `runFullTrust` capability is required for the desktop application. The `allowElevation` capability supports explicit user-consented UAC prompts for initial `usbipd bind` and optional `usbipd unbind`. The app never bypasses UAC, stores administrator credentials, installs a driver, or performs unattended elevation. It does not elevate for normal attach/detach operations.

Testing requires Windows, usbipd-win, a running WSL2 distribution, and a USB security key. Default matching uses VID 1050 with no PID restriction; the IDs can be changed in Settings. The device is exclusively available to Windows or to the USB-IP client. The application only manages USB ownership; it does not implement OpenPGP or SSH authentication.

## Screenshots

Use the supplied Keypivo image for the Store listing:

| Image | Size | Caption |
| --- | --- | --- |
| [Keypivo](images/keypivo.png) | 1672 × 941 | View your security key status and switch from the tray. |

This PNG meets the desktop minimum dimensions of **1366 × 768**. Submit it as the listing screenshot; only this image is maintained in the repository. See [Microsoft's screenshot requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images).

## Privacy and support

Use [privacy.md](privacy.md) for the privacy-policy content and the [GitHub issues page](https://github.com/rubyu/keypivo/issues) for support. Confirm the public URLs are accessible before submitting them to Partner Center.
