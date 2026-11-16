# Keypivo privacy information

Last updated: November 16, 2026.

Keypivo runs locally on your Windows PC. It does not include analytics, advertising, user accounts, or a service that collects information from the app.

To display connection status and switch USB ownership, Keypivo reads device information from the locally installed usbipd-win command. This includes USB hardware identifiers, connection information, and device descriptions. Keypivo does not upload that information to its developer.

The app saves your selected vendor ID, optional product ID, and popup position in a local settings file. It can read settings from an earlier release to preserve your preferences. The Microsoft Store version may store settings in Windows' per-app storage location.

Keypivo does not request security-key PINs, access private keys, or perform signing, decryption, or authentication. usbipd-win, Windows, WSL2, and any software you use with your security key are separate products with their own behavior and privacy terms.

If you choose to report a problem through [GitHub Issues](https://github.com/rubyu/keypivo/issues), the information you submit is handled by GitHub and may be public. Do not include PINs, secrets, private keys, or unnecessary device identifiers in a report. You can also use that page to ask questions about this privacy information.
