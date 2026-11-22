# Development and packaging

## Build and run

Use Windows and the .NET 10 SDK.

```powershell
dotnet build Keypivo.slnx -c Release -m:1
dotnet run --project src/Keypivo -c Release --no-build -- --show
```

`--show` opens the tray menu immediately on startup.

Sign-in startup uses the MSIX package's Windows startup task. It is unavailable in unpackaged development runs; native UI tests use a simulated startup service and do not alter the PC's startup settings.

Notification preferences are saved with the other settings. Missing preferences default to on. If the settings document is unreadable, defaults apply; when only another setting is invalid, readable notification preferences are preserved during recovery. The initial status notification uses **Current status at launch**, independently of later connection-change preferences.

## Tests

The application tests use a console runner:

```powershell
dotnet run --project tests/Keypivo.Tests -c Release --no-build
dotnet run --project tests/Keypivo.UiTests -c Release --no-build
```

These tests use simulated devices. They do not require a security key, usbipd-win, or Store credentials. The native UI checks render the actual WinForms setup pages to `artifacts/ui`; CI uploads them as previews. See the [verification record](verification.md) for coverage and manual checks.

GitHub Actions runs the tests and builds a validated MSIX for PRs to `develop` and `main`, pushes to those branches, and manual runs. Store updates are submitted manually in Partner Center; see [Store publishing](store-publishing.md).

## Portable packages

```powershell
./scripts/publish.ps1
```

The script builds, runs the application tests, and creates `artifacts/publish` and `artifacts/keypivo-win.zip`. Move an existing publish directory aside before running it again.

To include the .NET runtime:

```powershell
./scripts/publish.ps1 -SelfContained -Runtime win-x64
```

Use `win-arm64` for ARM64. The first self-contained build may need to download runtime packs. `-SkipTests` is available when the environment prevents test execution; packages built with this option have not passed those checks.

Portable binaries are unsigned. Deployment to a PC with application-control policies may require signing through an approved process.

## Store packages

The MSIX manifest and Store identity are in `packaging`. Follow [Store publishing](store-publishing.md) to download a CI package or build one locally, then submit an update in Partner Center.
