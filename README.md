# BabylonWebServerFix

**Windows x64 utility for Babylon Pro 12 compatibility with Microsoft IIS and Apache/XAMPP.**

> **Latest verified published stable release: v1.0.0.** Version v1.0.4 exists in source, but its GitHub Actions publication failed with HTTP 403; it is not yet a verified published release. It defaults to built-in IIS Request Filtering without URL Rewrite, includes safe bidirectional migration between native filtering and URL Rewrite, and reuses a registered RewriteModule without unnecessary MSI download. Both migration directions and `remove-iis` were verified on BASE-IMAGE. Ordinary site responses, the appcmd disagreement fallback path, and the brief initial Babylon error flash still need checking.

Babylon 12 embeds an MSHTML-based viewer that may request `http://localhost/?uid=...&ver=12000&bld=13&iev=11...`. A local web server can intercept that URL and replace Babylon's interface with a site response. This tool configures narrowly scoped server rules and optionally cleans temporary WinINet files without changing IP addresses, port bindings, Babylon installation files or licenses.

## When to use this tool: Babylon UI modes (verified 2026-10-28)

Babylon Pro 12 has been observed using **two different MSHTML document URLs**. These are *runtime observations*, not a reliable product version check; the LocalUI HTML file itself has a 2023 timestamp.

| Mode | Embedded document URL | What to do |
|---|---|---|
| Legacy localhost HTTP | `http://localhost/?uid=0&ver=12000&bld=13&iev=11&lngs=14,0,15,3,1,7` | If IIS/Apache listens on port 80 and Babylon shows the web server page or translation breaks, this compatibility utility can be used. |
| Local-file HTML | `file://C:\\ProgramData\\Babylon\\LocalUI\\wnd.html?uid=0&ver=12000&bld=13&iev=11&lngs=14,0,15,3,1,7` | If translation works with IIS/Apache running **without** the compatibility rules, **do not run install**. Nothing needs fixing. |

A second local document was observed: `%LOCALAPPDATA%\\Temp\\BWebData.html` (user profile path varies). The local-file mode still uses MSHTML, and bundled JavaScript contains remote HTTP service URLs. Loading the main UI from a file **does not prove that Babylon makes zero network connections**, nor does the presence of `wnd.html` alone prove a fixed build.

**Real-world validation (BASE-IMAGE, 2026-10-28):** IIS was running on TCP/80; both managed IIS rules were absent after `remove-iis`; Babylon displayed a local-file UI and translation worked normally. No compatibility fix was needed on that workstation. This confirms the result for the tested installation, not for every version of Babylon.

**Decision rule:** First try Babylon translation while IIS/Apache is running and with no BabylonWebServerFix rules. If it works, leave the system alone. If the old `http://localhost/?uid=...` behavior causes a conflict, run `status`, then `install --dry-run`, and only then `install` as administrator. Do not switch modes solely because the file `LocalUI\\wnd.html` exists.

### Two IIS compatibility implementations (only for the problematic localhost HTTP mode)

The two *Babylon UI loading modes* above are distinct from the tool's **two IIS rule implementations**:

- **Native Request Filtering (default):** `BabylonWebServerFix.exe install`; no separate MSI, sends HTTP 404.19 for Babylon query substring. Matches site-wide query strings (not only localhost) and may flash an HTTP error briefly.
- **Microsoft URL Rewrite (opt-in):** `BabylonWebServerFix.exe install --iis-mode rewrite`; aborts the targeted localhost request; needs a registered Microsoft RewriteModule, which is reused if already present.

Apache/XAMPP has its own conditional HTTP 403 compatibility rule. Neither IIS implementation is needed when updated Babylon works normally without it.

## Download the Windows EXE

1. Open **[v1.0.0 Stable Release](https://github.com/sgennadi/BabylonWebServerFix/releases/tag/v1.0.0)** and download `BabylonWebServerFix.exe` and optional `SHA256SUMS.txt` from **Assets**.
2. Verify the SHA-256 checksum before deploying to multiple machines.
3. Run the executable from an **elevated Command Prompt (Run as administrator)** on a test workstation.

The latest development build artifacts are also available through [Windows build and smoke tests](https://github.com/sgennadi/BabylonWebServerFix/actions/workflows/build.yml). Prefer the versioned stable release for deployment.

The EXE is **unsigned** until a code-signing certificate is configured. GitHub Actions produces a self-contained .NET 10 single-file build; target machines do not need a separate .NET runtime or PowerShell scripts.

## Offline IIS URL Rewrite installation and future updates

The Microsoft IIS page currently publishes **URL Rewrite 2.1**. When explicit `--iis-mode rewrite` is selected, the utility checks for an existing registered `RewriteModule` using Microsoft.Web.Administration and a read-only appcmd fallback. **If present, it does not download or upgrade the module.** If those checks disagree, a warning identifies the inconsistency while the existing registration takes precedence. If appcmd fails and registration is uncertain, the tool refuses to download/reinstall blindly. Compatibility of a future Microsoft release must still be tested.

If the installer URL is blocked, moved or retired, supply the **genuine Microsoft-signed x64 MSI** using one of these methods:

1. **Recommended for deployments:** place `rewrite_amd64_en-US.msi` beside `BabylonWebServerFix.exe` on the deployment share. It is used automatically when IIS needs the module. No Microsoft download is required.
2. Supply a local or network path to the Microsoft-signed MSI using `--msi`. No specific server path is required; the preferred method is simply placing the MSI beside the EXE.
3. If Microsoft publishes a **new direct MSI URL**, use `BabylonWebServerFix.exe install --msi-url "https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi"` with the newer official direct link substituted when it changes, without recompiling the EXE. Do not pass a webpage URL.

Selection: use either an explicit `--msi` (local/UNC path) or an explicit `--msi-url` (Microsoft HTTPS); otherwise the utility prefers an MSI beside the EXE, then the embedded Microsoft URL. `--msi` and `--msi-url` cannot be combined. If a remote download fails and no offline source is provided, the tool stops with an actionable error; it **never silently installs an untrusted package**. All candidate MSI files are validated by Windows Authenticode/WinTrust and Microsoft publisher identity before installation, and the resulting IIS module must register as `RewriteModule`.

For network deployments, make the shared MSI and EXE read-only to students; only administrators should be able to replace installers.

## Default IIS mode: no URL Rewrite MSI

v1.0.4 uses IIS's built-in **Request Filtering** by default. This mode needs **no separate Microsoft URL Rewrite MSI** and does not download anything.

```cmd
BabylonWebServerFix.exe status
BabylonWebServerFix.exe install --dry-run
BabylonWebServerFix.exe install
```

Its named rule (`Babylon12-Compatibility-Native`) matches `&ver=12000&bld=13&iev=11` in the request query and returns HTTP **404.19**. The request is denied for *all paths and hostnames on the selected IIS site* if it contains the same substring. The normal `http://localhost/` without that query does not match. Unlike URL Rewrite, Request Filtering cannot limit this rule to just the Babylon request on localhost and the root path.

Babylon Pro 12 translation **worked on a test workstation with IIS running** after installing the native rule on 2026-10-28. A brief flash of an error page with red text occurred at initial launch before Babylon displayed its normal window. This may be due to the HTTP error response and has not been eliminated. An ordinary `http://localhost/` page still needs explicit HTTP verification.

### Switching back to Microsoft URL Rewrite

```cmd
BabylonWebServerFix.exe install --iis-mode rewrite
```

This installs or reuses Microsoft URL Rewrite, creates the managed Rewrite rule, and **then removes the managed native filtering rule**, so the two rules do not remain active together. **Verified on BASE-IMAGE on 2026-10-28:** an official Microsoft MSI was downloaded and installed successfully (exit 0), the Rewrite rule was created, the native rule was removed, Babylon restarted, and the user confirmed translation works. If the MSI download or installation fails, the existing native rule is intended to remain in place; this failure path has not yet been tested on a workstation. To use an offline installer, place the signed MSI beside the EXE or use `--msi PATH`; supplying `--msi` or `--msi-url` without `--iis-mode` automatically selects Rewrite. **Note:** on 2026-10-28 at 09:33, the utility downloaded the MSI despite an earlier successful installation. A later `appcmd list config /section:system.webServer/globalModules` command positively listed `RewriteModule`. The registration state at 09:33 is not independently known; v1.0.4 adds an appcmd fallback check and diagnostic output to prevent a redundant reinstall when appcmd currently confirms the module. On 2026-10-28, a v1.0.4 `install --iis-mode rewrite --dry-run` test on BASE-IMAGE correctly detected the registered module and printed `IIS RewriteModule is already registered; reusing existing module without MSI download.` It previewed Rewrite rule installation and native-rule removal, with no IIS changes. This confirms the normal managed-API detection path, **not** the fallback path for disagreements between managed API and appcmd; the original redownload cause remains unknown.

Switching back to default native filtering is equally simple:

```cmd
BabylonWebServerFix.exe install
```

The native rule is installed or verified **before** the managed URL Rewrite rule is removed. No Microsoft module is deliberately uninstalled; only this project's rules are changed. Both migrations back up IIS configuration before changes. **On 2026-10-28 at 09:34, this exact reverse migration was confirmed in a real BASE-IMAGE log**: native rule installed, previous Rewrite rule removed and IIS remained Started.

`status` reports both managed IIS rule types. `remove-iis` removes both managed IIS rules without touching Apache or the WinINet cache. **Verified on BASE-IMAGE (2026-10-28):** `remove-iis` backed up IIS, removed the installed native rule, and a subsequent `status` confirmed both managed rules absent; IIS remained Started. This was a native-only removal test (the Rewrite rule was already absent). `remove` also removes the application's other managed settings. IIS Request Filtering must be enabled as a Windows IIS feature.

**After `remove-iis`, Babylon compatibility is no longer configured for IIS.** To restore the default MSI-free behavior, run `BabylonWebServerFix.exe` (or `BabylonWebServerFix.exe install`). To reinstall the URL Rewrite rule explicitly, use `BabylonWebServerFix.exe install --iis-mode rewrite`.

**Deployment note:** this behavior is implemented in the v1.0.4 source code but its release publication has not succeeded. **Both migration directions passed on BASE-IMAGE** (Native → Rewrite at 09:33 and Rewrite → Native at 09:34 on 2026-10-28), and the earlier Rewrite migration also passed a live Babylon translation check. Repeated-install idempotence, error-path rollback, the appcmd disagreement fallback path, and normal localhost page responses still need checking.

## Quick start (Command Prompt / CMD)

Open a terminal as administrator, change to the directory containing the EXE, and run:

```cmd
BabylonWebServerFix.exe status
BabylonWebServerFix.exe install --dry-run
BabylonWebServerFix.exe install
```

**No arguments** also runs `install`:

```cmd
BabylonWebServerFix.exe
```

Other supported commands:

```cmd
BabylonWebServerFix.exe restart-apache
BabylonWebServerFix.exe cache-only
BabylonWebServerFix.exe remove
BabylonWebServerFix.exe remove-iis
BabylonWebServerFix.exe install --iis-mode native
BabylonWebServerFix.exe install --skip-apache-restart
BabylonWebServerFix.exe install --skip-babylon-restart
BabylonWebServerFix.exe install --skip-cache
BabylonWebServerFix.exe install --enable-log
BabylonWebServerFix.exe install --cleanup-next-logon
BabylonWebServerFix.exe --help
```

Run `--help` for the complete option list. Notable options: `--site "Default Web Site"`, `--msi PATH`, `--apache-httpd PATH`, `--apache-config PATH`, `--log-path PATH` (requires `--enable-log`), `--cache-version 1,0,0,0`, and `--dry-run`.

### Default behavior

- If neither IIS nor Apache is installed: **do nothing**, including no cache cleanup.
- If IIS is installed: **default native Request Filtering requires no MSI and no download**. Choose `--iis-mode rewrite` explicitly to use Microsoft URL Rewrite. When the mode changes, the project installs the target rule first, then removes its old managed IIS rule. Optional MSI downloads still require valid Microsoft Authenticode signatures through Windows WinTrust; downloaded MSI files are removed after installation.
- If Apache 2.4 is installed: configure the managed Apache rule, back up `httpd.conf`, validate using `httpd -t`, and restart a running Apache when configuration changed. An unchanged rule does **not** trigger a restart.
- If Babylon is running: stop it before cache cleanup and restart it in the original interactive user session when a safe non-elevated restart can be arranged. If it was not running, leave it stopped.
- Clear temporary WinINet web files for non-special Windows user profiles; preserve cookies and saved credentials. Locked files are reported and may remain.
- **Logging is off** by default. **Next-logon cleanup is off** by default. Both require explicit opt-in.

### Web server rule behavior

| Server | Managed behavior | Validation notes |
|---|---|---|
| IIS (default: native) | Built-in Request Filtering, HTTP **404.19** on Babylon query substring | Translation confirmed on one test workstation without URL Rewrite; broad site-wide query match and initial error flash remain limitations. |
| IIS (`--iis-mode rewrite`) | URL Rewrite `AbortRequest` on the exact Babylon 12 `localhost` request | Previously verified on a test workstation; needs signed MSI only when RewriteModule is not already installed. |
| Apache / XAMPP | Conditional HTTP **403** for the same narrow request | HTTP 403 is not equivalent to IIS connection abort. Translation worked on one test PC while XAMPP listened on TCP/80; explicit HTTP rule behavior remains to be tested. |
| Neither | No-op | No server software is installed. |

The Apache and IIS URL Rewrite rules match `localhost` (optionally `:80`), the `/` path and the Babylon 12 query prefix. The default IIS native filter matches a substring across the *entire selected site's query strings*, regardless of host and path. Ordinary `http://localhost/` requests without the specified query are not targeted.

### Process and service management

- **IIS** is never started or restarted by this tool.
- **Apache Windows service** uses its service-specific restart command.
- **Console XAMPP Apache** is restarted directly using its selected `httpd.exe` and original `httpd.conf`. It does **not** call XAMPP start/stop BATs. Only processes belonging to that executable, user, and interactive session are targeted. Active connections may be interrupted.
- A previously stopped Apache remains stopped. For a forced reload use `BabylonWebServerFix.exe restart-apache`.
- **Babylon** is only restarted if it was already running; use `--skip-babylon-restart` to opt out.

**Security:** run the utility only from trusted local directories. Ensure Apache executables, configurations and installers are not writable by untrusted accounts before running elevated. MSI verification can reject packages when Windows cannot validate signature trust or certificate revocation.

## Build from source (.NET 10)

Requirements: .NET 10 SDK on Windows; Visual Studio 2026 is optional.

```cmd
dotnet build BabylonWebServerFix.sln -c Release
dotnet run --project tests/BabylonWebServerFix.SmokeTests/BabylonWebServerFix.SmokeTests.csproj -c Release
dotnet publish src/BabylonWebServerFix/BabylonWebServerFix.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o dist
```

The resulting EXE is `dist\BabylonWebServerFix.exe`. NuGet restore requires internet access for the first build. The application itself does not launch `.ps1` files or `powershell.exe`.

## Testing and limitations

Real-world verification on 2026-10-28 confirmed Babylon translation with both Apache/XAMPP and an active IIS configuration. For v1.0.4, native IIS filtering, both IIS rule-migration directions, RewriteModule reuse in dry-run, and `remove-iis` were exercised on BASE-IMAGE; Windows CI passed 65 offline smoke tests. Run `status`, `install --dry-run`, `install` and a repeat `install` on a **test** workstation before broader distribution. Verify Babylon translates, Apache serves a normal `http://localhost/` page, the managed request receives the intended behavior, and IIS/Apache preserve their port bindings.

Read the [test matrix](docs/TEST_MATRIX.md). A green CI build checks compilation and smoke tests, **not** complete IIS/Apache integration. The stable release is intended for the tested IIS and XAMPP compatibility workflow; validate any other configuration before broad production deployment.

## Repository layout

- `src/BabylonWebServerFix/` — C# CLI and Windows API integrations
- `tests/BabylonWebServerFix.SmokeTests/` — offline regression tests
- `.github/workflows/build.yml` — Windows x64 CI build and artifact
- `.github/workflows/release.yml` — optional preview releases from existing tags
- `.github/workflows/stable-release.yml` — versioned stable release build, executable and checksum
- `docs/TEST_MATRIX.md` — functional validation checklist

## Releases

**Latest verified published stable: [v1.0.0 Stable](https://github.com/sgennadi/BabylonWebServerFix/releases/tag/v1.0.0).** The [v0.1.3 Preview](https://github.com/sgennadi/BabylonWebServerFix/releases/tag/v0.1.3-preview.1) is also available. **v1.0.4 source code and tests are in main, but no v1.0.4 downloadable stable release has been verified** because GitHub Actions failed creating the release with HTTP 403 (`Resource not accessible by integration`). Do not advertise a v1.0.4 EXE in Releases until it is actually published. Stable packages include an unsigned EXE and a SHA-256 checksum file. The manual stable-release workflow requires an existing tag for future releases; new versions should be tested on Windows before publishing.

## License

MIT ([LICENSE](LICENSE)). Babylon, XAMPP, Apache HTTP Server, Microsoft IIS and Microsoft URL Rewrite are third-party products with their own licenses and trademarks. No third-party application binaries are bundled.
