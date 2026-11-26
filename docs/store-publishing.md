# Microsoft Store publishing

Store updates are submitted manually in [Partner Center](https://partner.microsoft.com/dashboard). After CI succeeds on `main`, the validated MSIX is published to [GitHub Releases](https://github.com/rubyu/keypivo/releases). No Entra application or Store credentials are required.

## Update the Store app

1. Merge the release PR from `develop` into `main`.
2. Wait for [CI](https://github.com/rubyu/keypivo/actions/workflows/ci.yml), including **Publish GitHub Release**, to succeed on that `main` commit.
3. Open the new [GitHub Release](https://github.com/rubyu/keypivo/releases) and download `keypivo-<version>-x64.msix` from **Assets**. `SHA256SUMS.txt` provides its checksum. The release tag identifies the source commit; build details are available in GitHub Actions.
4. In Partner Center, open **Keypivo** and create an update submission. Upload the MSIX in **Packages**, with a version higher than the packages already published.
5. Review the existing listing, availability, and [certification notes](store-listing.md#certification-notes), then submit the update for certification and choose when it should be published.

Microsoft Store signs the package. The unsigned MSIX is for Store submission, not direct installation. Publishing a GitHub Release does not submit or update the Store listing. Store availability depends on certification and the publication schedule selected in Partner Center.

## CI and versions

The `CI` workflow runs on PRs targeting `develop` or `main`, pushes to those branches, and manual runs. It builds the solution, runs application and native UI tests, and creates and validates an x64 MSIX containing the .NET runtime. Test logs, UI previews, and the package are uploaded as artifacts. No USB device or Store credential is needed.

Only successful pushes and manual runs on `main` publish a GitHub Release, tagged `v<package-version>`. The release job downloads the MSIX built in that same CI run, uploads it and its checksum to a draft, then publishes the completed release. PRs and `develop` runs never publish releases. Publishing uses the workflow's built-in GitHub token; no additional secret is needed.

Public release notes contain the Microsoft Store install link and GitHub's generated list of changes since the previous release. Submission instructions remain in this document.

Package versions follow `1.<run-number / 65536, rounded down>.<run-number modulo 65536>.0`. For ordinary run numbers this is `1.0.<run-number>.0`. A new workflow run advances the version; re-running an existing run preserves it. If you need another package, select **Run workflow** on `main` to start a new run.

If release uploading fails, rerun the failed job to resume its draft. Rerunning an already published version retains its existing files. GitHub Release assets remain available after the temporary Actions artifacts expire.

Check the version against Partner Center before uploading, particularly if a previous package was built locally. The fourth component must remain zero. Do not reuse an already published version.

## Store identity

The public package identifiers are recorded in [packaging/store.json](../packaging/store.json) and the package manifest.

| Field | Configured value |
| --- | --- |
| Store product ID | `9P9S5NCN2DQ4` |
| Package/Identity/Name | `24880rubyu.Keypivo` |
| Package/Identity/Publisher | `CN=4A475315-607B-42C7-B194-4DB633BAC95D` |
| Package/Properties/PublisherDisplayName | `rubyu` |

## Local packaging

Install the .NET 10 SDK and Windows 10/11 SDK, then use the checked-in Store identity and a version higher than the currently published package:

```powershell
$store = Get-Content -LiteralPath packaging/store.json -Raw | ConvertFrom-Json
$version = Read-Host 'Package version (higher than the published version, ending in .0)'
./scripts/package-store.ps1 `
  -IdentityName $store.identityName `
  -Publisher $store.publisher `
  -PublisherDisplayName $store.publisherDisplayName `
  -Version $version
```

The package is written to `artifacts/store`. The script keeps MakeAppx semantic validation enabled and checks the packaged entry point, runtime, capabilities, identity, license, startup task, and logos. It refuses to overwrite an existing package. `-Architecture arm64` is available for separate packaging; CI currently builds x64 only.

## Package behavior and listing material

usbipd-win and WSL2 remain separately installed prerequisites. The manifest declares `runFullTrust` for the desktop app and excludes `allowElevation`. Keypivo runs at normal user privileges; users perform installation and sharing commands outside the app.

The package declares a default-enabled `windows.startupTask`. After the app's first launch, Windows starts it in the tray at sign-in. **Start at sign-in** in Settings controls that task; Windows user and policy restrictions remain authoritative.

Test the installed Store package using the [manual acceptance checklist](verification.md#manual-acceptance-checklist). Listing text, screenshots, and certification notes are in [store-listing.md](store-listing.md). Use [privacy.md](privacy.md) for the privacy-policy link.

## References

- [Store package requirements and versions](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [MakeAppx package validation](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
