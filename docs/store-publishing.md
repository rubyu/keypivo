# Microsoft Store publishing

Keypivo is packaged as an x64 MSIX containing its .NET runtime. usbipd-win and WSL2 remain separately installed prerequisites. The portable ZIP is still available for use outside the Store.

## Branches and CI

The `CI` workflow runs on PRs targeting `develop` or `main`, pushes to those branches, and manual dispatch. The **Windows build and tests** job restores and builds the solution, executes the console test runner, tests Store-publishing guards using a fake CLI, and creates and validates an MSIX. No USB device or Store credential is needed for these checks.

The **Submit Microsoft Store update** job runs only after successful checks on a push to `main`, and only when the repository variable `STORE_PUBLISH_ENABLED` is exactly `true`. PRs and `develop` never submit packages. Configure branch protection so only merged PRs can update `main`; a direct push to `main` would otherwise also trigger publishing.

On GitHub, open Settings > Rules > Rulesets (or Branches) and protect `develop` and `main`. Require a pull request, require the **Windows build and tests** check, require the branch to be up to date, and disallow force pushes and branch deletion. These repository settings are separate from workflow files.

## One-time setup

1. Register as a Windows app developer in [Partner Center](https://storedeveloper.microsoft.com/).
2. Under Apps and games > New product, choose **MSIX or PWA app** and reserve **Keypivo**.
3. The reserved product identity supplied by the publisher is already recorded in [packaging/store.json](../packaging/store.json) and the package manifest. These are public package identifiers, not credentials. Confirm them against Product management > Product identity:

| Field | Configured value |
| --- | --- |
| Store product ID | `9P9S5NCN2DQ4` |
| Package/Identity/Name | `24880rubyu.Keypivo` |
| Package/Identity/Publisher | `CN=4A475315-607B-42C7-B194-4DB633BAC95D` |
| Package/Properties/PublisherDisplayName | `rubyu` |

4. In [GitHub Actions variables](https://github.com/rubyu/keypivo/settings/variables/actions), leave `STORE_PUBLISH_ENABLED` unset or `false` until the first publication and credential setup below are complete. There is no need to duplicate the identity values as GitHub variables.
5. Download the `keypivo-msix-<commit>` artifact from a successful CI run on `develop` using the configured identity above. Its MSIX can be used for the first manual submission. Early CI runs used `Keypivo.Validation`; those older packages are only for validation and must not be submitted. Prefer the latest successful run.
6. Complete the first submission in Partner Center: availability and pricing, app properties, age rating, English listing, screenshots, privacy-policy URL, package upload, and certification notes. Upload the MSIX and set publication to occur when certification completes. The Store signs the package; this unsigned upload package is not intended for direct installation.
7. Test the installed Store package, including tray startup, settings persistence, `usbipd` discovery, UAC consent for bind/unbind, and Windows/WSL switching. Packaging validation alone does not establish this behavior. Finish the first publication so the app is live before enabling API updates.

The manifest declares `runFullTrust` for the WinForms desktop app and `allowElevation` for the user-approved administrative sharing commands. Restricted capabilities require Microsoft approval. Explain that only the separately installed usbipd command is elevated for `bind`/`unbind`; the app itself runs as the user, and switching an already shared device does not elevate. Approval is not guaranteed; address certification feedback before enabling automatic releases. See [Microsoft's capability guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations).

## API credentials

1. Associate a Microsoft Entra tenant with the Partner Center account.
2. Register an application in Microsoft Entra ID. Under Partner Center > Account settings > User management > Microsoft Entra applications, add it and assign the **Manager** role as described in [Microsoft's GitHub Actions guide](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/github-actions).
3. Create a client secret for that Entra application. Keep the **secret value**, not the secret identifier. Do not put it in source code, PRs, issues, or chat.
4. In the repository's Settings > Environments, create **microsoft-store**. Restrict deployment branches to `main`. For unattended publishing, leave required reviewers disabled.
5. Add these environment secrets:

| Secret | Source |
| --- | --- |
| `PARTNER_CENTER_TENANT_ID` | Entra Directory (tenant) ID. |
| `PARTNER_CENTER_SELLER_ID` | Partner Center seller/account identifier. |
| `PARTNER_CENTER_CLIENT_ID` | Entra Application (client) ID. |
| `PARTNER_CENTER_CLIENT_SECRET` | The Entra client secret value. |

6. After the app is live, the packaged behavior is verified, and the credentials are ready, set the **repository variable** `STORE_PUBLISH_ENABLED` to `true`.
7. Merge the next release PR from `develop` into `main`. CI tests and packages that commit, then submits that exact artifact for certification. Confirm availability in Partner Center; a successful upload is not a guarantee of approval or immediate public availability.

## Versions and submission recovery

Package versions follow `1.<run-number / 65536, rounded down>.<run-number modulo 65536>.0`. The fourth component stays zero as required for Store uploads. For ordinary run numbers this is `1.0.<run-number>.0`. Every new workflow run advances the version; re-running a run preserves it.

The publishing script checks the app identity, requires a previously published submission, rejects an existing draft or pending submission, and requires a version higher than every published package. It preserves the existing listing and pricing. It does not automatically delete a Partner Center draft, change the price, or retry a Store mutation.

If a submission is already pending, finish it or explicitly discard it in Partner Center before retrying. Do not rerun a version that has already published. If a submitted version is already live, use a new release commit/run. Keep submission changes in one place while CI publishing is active to avoid a race with manual Partner Center edits. Rotate the Entra secret before it expires. To stop automatic submissions, set `STORE_PUBLISH_ENABLED` to `false`.

## Local packaging

Install the .NET 10 SDK and Windows 10/11 SDK, then use the checked-in Store identity:

```powershell
$store = Get-Content -LiteralPath packaging/store.json -Raw | ConvertFrom-Json
./scripts/package-store.ps1 `
  -IdentityName $store.identityName `
  -Publisher $store.publisher `
  -PublisherDisplayName $store.publisherDisplayName `
  -Version '1.0.0.0'
```

The script keeps MakeAppx semantic validation enabled and checks the packaged entry point, runtime, capabilities, identity, and logo dimensions. It refuses to overwrite an existing package. `-Architecture arm64` is available for separate packaging; the current automated release targets x64 only. Supporting both architectures should use a tested bundle before broadening the Store release.

## Listing material

Draft listing text, review notes, and screenshot guidance are in [store-listing.md](store-listing.md). The [privacy information](privacy.md) can be linked using its public GitHub URL after it is merged. Review it against the version being submitted.

## References

- [First publication and subsequent CLI updates](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/overview)
- [MSStore CLI commands](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/commands)
- [Store package versions](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [MakeAppx package validation](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
