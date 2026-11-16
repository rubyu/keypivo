# Verification record

Date: 2026-11-16

## Current results

| Check | Result |
| --- | --- |
| .NET SDK | 10.0.203, Windows x64. |
| Release build | All three projects compiled with zero warnings and zero errors. |
| Product naming | User-facing text and assembly product metadata use Keypivo. Previous identifiers remain only for settings compatibility and single-instance coordination. |
| Binary naming | `keypivo.exe`, `keypivo.dll`, and `keypivo.core.dll` use lowercase filenames. The package is `keypivo-win.zip`. |
| Text checks | Application text, README, and documentation remain English-only and use Security key for the device. |
| Automated tests | All 47 application tests passed on GitHub's Windows runner, including 15 switching-verification regressions and two settings-upgrade cases. Local execution remains blocked by Windows application control; the remote run supplies the runtime evidence. |
| Store publishing guards | Seven fake-CLI tests passed locally: successful submission, missing first publication, pending draft, duplicate version, wrong identity, missing credentials, and query failure. No Store API was called. |
| MSIX packaging | Self-contained x64 packages passed Windows SDK MakeAppx semantic validation and payload/manifest/logo checks. A version 1.0.0.0 package was also built using the supplied Store identity `24880rubyu.Keypivo` and publisher `CN=4A475315-607B-42C7-B194-4DB633BAC95D`. Store certification is still required. |
| GitHub CI | [Run 43847629036](https://github.com/rubyu/keypivo/actions/runs/43847629036) passed: 47/47 application tests, 7/7 publishing guard tests, and MSIX build/validation. Store submission was correctly skipped on the PR. Latest checks are on [PR #1](https://github.com/rubyu/keypivo/pull/1). |
| Native GUI and actual USB switching | Pending verification in an environment that permits the generated assemblies to run. |
| usbipd CLI | The standard installation was previously confirmed as version 5.3.0, with working state output and supported commands. It is not on PATH. |

No application-control settings, certificate trust settings, or USB connection settings were changed during verification. The supplied listing image documents the menu presentation but does not establish switching behavior. A successful build does not establish runtime correctness.

## Automated coverage

The original 20 scenarios cover:

- USB JSON parsing, malformed schemas, exact IDs, first-match ordering, and disconnected records.
- Windows availability, persistent sharing, forced sharing, and client attachment.
- First-time bind followed by attach; idempotent attach and detach; preservation of sharing.
- Toggle after an external change; detach followed by unbind.
- Canceled elevation, partial failures, delayed state updates, and false command success.
- Device removal or replacement during an operation.
- Missing dependencies, unavailable devices, error recovery, concurrent operations, and cancellation.
- Command arguments and elevation; stdout/stderr draining; subprocess timeout and cancellation.

Ten additional scenarios cover:

1. Custom VID, optional PID, exact matching, and first-match ordering.
2. Case normalization, leading zeros, malformed IDs, and invalid display positions.
3. Defaults, save/restart persistence, and replacement without stale temporary files.
4. Corrupt settings fallback without overwriting the original file.
5. Partial settings files with defaults for missing fields.
6. Rejected saves preserving existing configuration.
7. Changing filters without mutating USB; the next operation targets the new selection.
8. Nonmatching PID preventing all USB operations.
9. Filter changes being rejected during an operation.
10. Bottom-right and centered placement, negative monitor coordinates, and bounds clamping.

Two settings-upgrade scenarios verify reading an earlier configuration without modifying it, saving to the new location, and giving the new configuration precedence even when it is corrupt.

Fifteen switching-verification regressions cover:

- Temporary missing devices and disconnected persisted entries after detach; no transient status events.
- An expected state followed by a brief disappearance resetting the stability window.
- Original-device tracking through bind and attach when enumeration order changes.
- USB re-enumeration after unbind.
- A replacement device at the same bus never receiving the original key's unbind command.
- Late recovery requiring the original instance ID and bus ID, with one completion event.
- A renewed disappearance restarting late-recovery confirmation.
- Actual command failures remaining distinct from recoverable verification timeouts.
- Delayed bind completion neither confirming attachment nor triggering another command.
- The verification deadline canceling a slow state query and releasing the operation lock.
- Explicit cancellation remaining cancellation, not a verification timeout.
- Regular polls, duplicate operations, and filter edits being excluded during verification.
- Filter changes abandoning an old pending confirmation.
- A new action superseding an old pending confirmation.
- State-query failures remaining visible errors.

The regression tests use shortened polling and stability intervals. Production defaults are a 10-second verification deadline, a 500 ms pause between reads, and a one-second stability window. These tests passed remotely; actual hardware switching and the installed Store package still require manual verification.

Run:

```powershell
dotnet build Keypivo.slnx -c Release -m:1
dotnet run --project tests/Keypivo.Tests -c Release --no-build
```

## Native UI acceptance checklist

Use a Windows environment that permits the app to run, with usbipd-win and WSL2 installed.

1. Start with `--show`. The compact popup contains **Security key**, status, switching, **Refresh status**, **Settings**, and **Exit**. Confirm that Settings is immediately below Refresh status.
2. Confirm that every app-owned label, notification, dialog, and validation message is English. There is no language picker or old product-specific branding.
3. Confirm that the initial position is the primary display's bottom-right working area, above the taskbar. Clicking outside or pressing Escape hides the popup without exiting.
4. Open Settings. Defaults are VID `1050`, empty PID, and **Bottom right**. Save **Center**, reopen the popup and Settings, and restart the app to verify persistence.
5. Enter a different valid VID and PID. Save and confirm selection updates without changing any USB connection. Empty PID must match every product with the selected VID.
6. Try invalid VID and PID values. The dialog should stay open, show a concise English validation message, and preserve the previous saved settings. Cancel must discard edits.
7. With multiple matching connected devices, operate only on the first device in the JSON array. A nonmatching PID must disable switching.
8. Switch an unshared key to WSL. Cancel UAC once, then retry and approve. Confirm the device with `usbipd list` and Linux `lsusb`.
9. Switch back to Windows. During USB re-enumeration the popup must stay on **Switching…**, with no **Not connected** or failure notification. After stable Windows availability, one completion notification should appear. The sharing registration should remain. Repeated switching on a shared device should not require UAC.
10. Use **Stop sharing this key** in Settings. Verify detach and unbind. The link must be disabled for unsaved edits and when no shared key is selected.
11. Confirm notifications for actual changes, external attach/detach, and unplugging. Unchanged polls should not repeatedly notify.
12. Confirm errors are concise on the popup and expanded only through **View details**. A failed save must leave the Settings dialog open.
13. Check 100%, 150%, and 200% display scaling: no clipped title, no empty scrollable areas, and readable buttons. Check keyboard navigation and high-contrast mode.
14. Exit and confirm the tray icon disappears while the USB connection stays unchanged. A second launch must not create another running instance.
15. Remove the selected key immediately after a switching command. Verification should end within its 10-second deadline with **Not confirmed**, without repeating the command or operating on another key. Reconnect the original key at the same port in the requested state; after stable regular polling the warning should clear and one completion notification should appear without clicking Refresh status.
16. Cause a genuine command failure, such as canceling UAC. It should show **Action failed**; later status polls must not silently clear that command error as though it were a confirmation timeout.
17. Start `keypivo.exe`. Confirm Keypivo in the popup, Settings title, notifications, and file product metadata, with a K-shaped tray icon. With no new settings file, confirm that an earlier configuration is loaded; saving must write to `%LOCALAPPDATA%\Keypivo\settings.json` without changing the earlier file.

OpenPGP and SSH authentication are separate checks. The app does not generate keys, request PINs, or sign data.
