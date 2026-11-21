# Development and packaging

## Build and run

Use Windows and the .NET 10 SDK.

```powershell
dotnet build Keypivo.slnx -c Release -m:1
dotnet run --project src/Keypivo -c Release --no-build -- --show
```

`--show` opens the tray menu immediately on startup.

Sign-in startup uses the MSIX package's Windows startup task. It is unavailable in unpackaged development runs; native UI tests use a simulated startup service and do not alter the PC's startup settings.

## Tests

The application tests use a console runner:

```powershell
dotnet run --project tests/Keypivo.Tests -c Release --no-build
dotnet run --project tests/Keypivo.UiTests -c Release --no-build
```

Run the Store publishing tests separately:

```powershell
./scripts/test-store-publishing.ps1
```

These tests use simulated devices and a fake Store CLI. They do not require a security key, usbipd-win, or Store credentials. The native UI checks render the actual WinForms setup pages to `artifacts/ui`; CI uploads them as previews. See the [verification record](verification.md) for coverage and manual checks.

GitHub Actions runs the tests and builds a validated MSIX for PRs to `develop` and `main`. Store submission is configured separately in [Store publishing](store-publishing.md).

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

The MSIX manifest and Store identity are in `packaging`. Follow [Store publishing](store-publishing.md) to build a package, complete the first submission, and enable subsequent updates from CI.
