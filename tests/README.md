# Tests

Guards for the things this repo has actually got wrong: publishing from the
wrong branch, shipping a dependency that was supposedly removed, changing the
auth stack by accident, and a listing whose screenshots 404.

## Running

```powershell
./tests/Run-Tests.ps1
```

Requires **Pester 5+** (Windows ships 3.4.0, which cannot run these):

```powershell
Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser -Force -SkipPublisherCheck
```

Everything is offline-safe by default: the live-org tests skip unless
connection strings are set, and the packaging gate skips when there is no
`.vsix` to inspect.

| Switch | Effect |
| --- | --- |
| `-VsixPath <path>` | Run the packaging gate against a specific package instead of the newest under `Extension/VSIX` |
| `-Offline` | Skip tests tagged `Network` (the screenshot URL checks) |
| `-Output Detailed` | Per-test output |

## What each file covers

| File | Needs | Covers |
| --- | --- | --- |
| `Manifest.Tests.ps1` | network (image checks only) | Publisher/extension id unchanged, manifest version ahead of what is live, task ids never change, task versions never go backwards, no leftover Innofactor branding, every `overview.md` image resolves |
| `MinifyJS.Tests.ps1` | node + npm | `script.ps1` copies the lockfile and uses `npm ci`; a real `npm ci` + `gulp minify` run over a fixture strips `console`/`debugger`, preserves ES2020, and emits valid JS; `npm audit` is clean |
| `Vsix.Tests.ps1` | a built `.vsix` | No DotNetZip/Ionic.Zip, ADAL pinned to `3.19.50615.2240` with its `.Platform` satellite, every task has its cmdlet and the VstsTaskSdk, MinifyJS ships its lockfile, task ids survive packaging |
| `Integration.Tests.ps1` | a real org, **Windows PowerShell 5.1** | `Find-CrmUser` against online and on-prem |
| `Test-Connection.ps1` | a real org, **Windows PowerShell 5.1** | Same check without a Pester dependency — use this for a quick manual verification |

## Expected failures against the old package

`Run-Tests.ps1` defaults to the newest `.vsix` in `Extension/VSIX`. If that is
still the published **9.0.95**, the gate correctly fails two assertions —
9.0.95 really does ship `DotNetZip.dll` in four tasks, and it predates the
gulp ESM migration so it has `gulpfile.cjs` rather than `gulpfile.mjs`. Both
clear once you rebuild and repack. That is the gate doing its job, not a
broken test.

## Verifying a live connection

Pester 5+ often ends up installed only for pwsh 7, while these cmdlets must be exercised under
**Windows PowerShell 5.1** — the host an Azure DevOps agent uses. `Test-Connection.ps1` avoids
that mismatch by using no test framework at all:

```powershell
# in a Windows PowerShell 5.1 window
$env:CRM_ONLINE_CONNSTR = 'AuthType=ClientSecret;Url=https://...;ClientId=...;ClientSecret=...'
.\tests\Test-Connection.ps1

$env:CRM_ONPREM_CONNSTR = 'AuthType=AD;Url=http://crmserver/org;Domain=...;Username=...;Password=...'
.\tests\Test-Connection.ps1 -Target OnPrem
```

It reads the connection string from the environment so it never lands on a command line or in
shell history, defaults to the *packaged* task payload rather than `bin/Release`, and reports
which ADAL version actually loaded. It deliberately does not pass `-Verbose`, because the cmdlet
logs the full connection string at that level.

## Before publishing

1. Clean every `Cmdlets/*/bin` and `obj`, then rebuild in Release — otherwise
   stale DLLs (this is exactly how `DotNetZip.dll` kept shipping) get copied
   into the package by `pack.ps1`.
2. `./Extension/pack.ps1`
3. `./tests/Run-Tests.ps1 -VsixPath ./Extension/VSIX/<new>.vsix` — must be green.
4. Run `Integration.Tests.ps1` under Windows PowerShell 5.1 against **both** an
   online and an on-prem org. Online is what validates the ADAL pin.

## Updating the baseline

`fixtures/published-baseline.json` records the task ids and versions of the
currently published package. After a successful publish, regenerate it from
the new `.vsix` so the "never go backwards" checks track the new floor.
