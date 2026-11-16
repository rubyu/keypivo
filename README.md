# Keypivo

Switch a USB security key between Windows and WSL2 from your system tray.

![Keypivo tray menu](docs/images/keypivo.png)

## Getting started

You need Windows 10 or 11, [usbipd-win](https://github.com/dorssel/usbipd-win) 5.x, and an installed WSL2 distribution.

Install usbipd-win:

```powershell
winget install --interactive --exact dorssel.usbipd-win
```

Extract the Keypivo ZIP and run `keypivo.exe`. Portable packages without a bundled runtime also require the .NET 10 Desktop Runtime.

## Usage

1. Connect your security key and open a WSL2 terminal.
2. Click the Keypivo tray icon.
3. Choose **Switch to WSL** or **Switch to Windows**.

The menu shows the current connection status, and notifications let you know when it changes. Use **Refresh status** to check again.

Click outside the menu or press Escape to dismiss it. **Exit** closes Keypivo and leaves the key's connection unchanged.

The key can be used by one operating system at a time. While connected to WSL2, it is unavailable to Windows apps.

The first switch to WSL may request administrator permission to share the device. Returning to Windows keeps it shared for future use. To remove sharing, choose **Stop sharing this key** in Settings.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| Vendor ID (VID) | `1050` | Four hexadecimal digits identifying the device vendor. |
| Product ID (PID) | Empty | Optional four-digit hexadecimal ID. Leave empty to match any product from the selected vendor. |
| Display position | Bottom right | Show the menu and Settings at the bottom right or center of the primary display. |

If several connected devices match, Keypivo selects the first one. Changing the IDs does not disconnect a key that is already attached.

## Troubleshooting

- **Setup required:** install usbipd-win, then select **Refresh status**.
- **Not connected:** check the connection and VID/PID settings.
- **Action failed:** use **View details** to see the error. When switching to WSL, keep a WSL2 terminal open.
- **Not confirmed:** leave the key connected while Keypivo continues checking. If the message persists, open **View details** before retrying.
- **GPG or SSH cannot use the key:** configure GnuPG, SSH agents, smart-card services, and Linux device permissions separately.

## More information

- [Development and packaging](docs/development.md)
- [Microsoft Store publishing](docs/store-publishing.md)
- [Privacy](docs/privacy.md)
- [MIT License](LICENSE)
