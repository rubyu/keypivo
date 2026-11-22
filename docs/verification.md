# Verification record

Date: 2026-11-22

## Automated checks

CI builds the solution, runs application and native UI tests, and builds and validates the MSIX. Test logs, native form renders, and the package are uploaded as workflow artifacts. No real USB operation or Store submission is involved in any CI run.

Application coverage includes:

- Exact VID/PID matching, first-match selection, settings persistence and migration, parsing errors, and window placement.
- Manual installation, sharing, and unsharing checks without executing setup commands or automatically attaching afterward.
- Tracking the original key through enumeration changes; refusing clipboard copying after removal, replacement, a bus change, or an unsuccessful query.
- Requiring Windows availability before unsharing, including forced-sharing states.
- Stable attach/detach confirmation through temporary USB disappearance, bounded timeouts, late recovery, cancellation, and concurrent-operation guards.
- Normal-privilege process arguments, stdout/stderr handling, timeouts, and cancellation.

Native UI checks cover setup routing, command visibility, copy guards, incomplete-check feedback, returning to the normal menu, menu actions, and control bounds. Background confirmation leaves instructions visible until the user checks explicitly. There is no separate completion page or switching action on an instruction page.

Popup checks exercise native foreground focus and ordering above another topmost window, reopening, dismissal, and releasing topmost status while instructions or Settings stay open. The attached state must read **Connected to WSL** in the menu and the shared text used by notifications and the tray tooltip. These checks use ordinary test windows; the Windows notification-area overflow still requires the manual check below.

Header checks cover equally spaced Refresh, Settings, and Exit icons, their accessible names and tooltips, action routing, busy-state guards, keyboard order, and 100/150/200% body-text layout. Custom icon and primary-button painting must cover stale pixels and preserve partial repaint bounds.

Polling checks exercise the application context with a delayed fake device read. Unchanged automatic reads must leave controls enabled and cause no menu or setup layout events. A user action waits for a pending read and runs once; explicit refresh, setup navigation, changed snapshots, and shutdown remain functional.

Startup UI checks use a simulated Windows startup service: on/off save, cancellation, unchanged settings, user/policy restrictions, read and update failures, and concurrent external changes. They never register a startup task on the test PC. Package checks require exactly one default-enabled `KeypivoStartup` task targeting `keypivo.exe` and reject immediate-install registration.

Notification checks cover defaults and older settings, individual preferences, the master switch retaining choices, persistence, recovery, and state mapping. Native checks exercise category navigation, control bounds, Save/Cancel, validation from another page, and accessible switch actions. Delivery checks capture notification requests instead of showing Windows balloons and exercise startup, every connection state, operation failures, confirmation timeouts, late recovery, live preference changes, and duplicate suppression.

Rendering checks verify that switches repaint their full background, erase old labels, and respect a partial paint clip. Settings navigation and scrolling are exercised with body text at 100%, 150%, and 200% of its default size. CI also captures the visible menu and Settings window directly from the Windows desktop, alongside the control-rendered previews. Physical monitor DPI changes remain part of manual acceptance.

Device-detail checks cover offline USB ID lookups, vendor/product scoping, malformed IDs, class/interface sections, and unknown-name fallbacks. Native checks cover compact/full toggling, aligned value columns, the divider after Vendor, live connection and sharing states, clearing removed devices, unchanged polling, 100/150/200% body text, and scrolling long descriptions within the working area.

Package checks require `runFullTrust`, reject `allowElevation`, and validate the identity, runtime, entry point, license, third-party notices, and logos.

Local Release compilation is supported. Runtime execution on the development PC is blocked by Windows application control (`0x800711C7`), so runtime evidence comes from GitHub's Windows CI. Application-control and USB settings are not changed to run tests. See the latest PR check results for the exact tested commit.

```powershell
dotnet build Keypivo.slnx -c Release -m:1
dotnet run --project tests/Keypivo.Tests -c Release --no-build
dotnet run --project tests/Keypivo.UiTests -c Release --no-build
```

## Manual acceptance checklist

Use the installed package on Windows with a security key and WSL2. Automated tests do not establish actual hardware or Store-package behavior.

1. Without usbipd-win, choose **Set up…**. Copy and run the installation command outside Keypivo. **Other installation options** opens Microsoft Learn. **Check installation** returns to the menu only after the tool can be queried.
2. For an unshared key, choose **Set up for WSL…**. Run the displayed bind command in administrator PowerShell. **Check status** returns to the normal menu without attaching the key. An unfinished step keeps the instructions visible with concise feedback.
3. Open a WSL2 terminal and switch a shared key to WSL, then Windows. During USB re-enumeration, the status stays **Switching…** without a transient failure notification. Verify the actual connection using `usbipd list` and Linux `lsusb`.
4. In Settings, **Stop sharing this key** is disabled while attached, with **Switch to Windows first.** Return to Windows, open the instructions, run the unbind command manually, and check status. Unsaved settings also disable the link.
5. Unplug or replace the key while instructions are open. Copy must be disabled until the same key is connected at the same port and a fresh check succeeds. No command may target a replacement key.
6. Move focus to PowerShell: instructions remain open. Back returns to the menu; Escape hides the popup; reopening it resumes the instructions. Keypivo must never launch an installer, PowerShell, or an administrator consent prompt.
7. Verify VID/PID filtering, first-device selection, saved display position, state-change notifications, and Exit leaving USB ownership unchanged. A second launch must not create another running instance.
8. Check keyboard navigation, high-contrast mode, and 100%, 150%, and 200% display scaling for readable, unclipped controls.
9. Disconnect a key after a switching command. Confirmation must time out without repeating the command or operating on another key. Reconnecting the original key in the expected state should clear the confirmation warning after stable polling. A genuine command failure must remain distinct from a confirmation timeout.
10. Put the tray icon in the hidden-icons overflow. Left-click and right-click it repeatedly: Keypivo must open in front, receive keyboard focus, and dismiss on an outside click or Escape. Repeat with the icon directly on the taskbar. While instructions stay open, switching to PowerShell must leave PowerShell in front; reopening Keypivo must bring the instructions forward again. Confirm Settings remains usable.
11. Attach the key to WSL. The menu, notification, and tray tooltip must say **Connected to WSL**. Detach it and confirm **Available on Windows** appears again.
12. Leave the menu and each setup page open across several two-second status checks. Buttons must not flash or lose keyboard focus, and the window must not pulse or move. Actual state changes and explicit actions must still update the display.
13. Install the Store package and open it once. Verify **Start at sign-in** is checked. Sign out and back in: Keypivo should start in the tray without opening the menu or changing USB ownership. Uncheck the setting, Save, and repeat: the app should no longer start. Enable it again and confirm the next sign-in starts it.
14. Disable Keypivo in Windows Settings > Apps > Startup. Reopen Keypivo Settings: the checkbox must show the Windows state, and the Windows-settings link must allow the user to re-enable it. Confirm that relaunches and updates preserve the disabled state. Test policy-controlled enabled/disabled states where available.
15. In each notification category, turn individual notifications off, Save, and trigger their events. The menu must still update while only the selected Windows notifications are shown. Turn off **Show notifications**, Save, and verify all notifications stop. Re-enable it and confirm individual choices remain unchanged. Restart the app and verify persistence; Cancel must discard unsaved edits. Check that **Current status at launch** controls only the initial status message.

OpenPGP and SSH authentication are separate checks. Keypivo does not generate keys, request PINs, or sign data. Store certification remains a separate review.
