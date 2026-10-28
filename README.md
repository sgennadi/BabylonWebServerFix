# BabylonWebServerFix

A Windows x64 command-line utility that helps **Babylon Pro 12** coexist with local development web servers **Microsoft IIS** and **Apache HTTP Server / XAMPP** on the same workstation.

> **Preview (v0.1.2). Windows CI passes, and Apache + Babylon translation were observed together on one test workstation; broader compatibility still needs validation.** The included PowerShell v10 is retained as the previously tested implementation. Check functionality on one disposable/test workstation before any classroom or enterprise rollout.

## Why it exists

Babylon 12 uses an embedded MSHTML (`Internet Explorer_Server`) window that can load an internal document under `http://localhost/?uid=...&ver=12000&bld=13&iev=11...`. If a local web server responds to that URL, Babylon may display the IIS/Apache response instead of its translation interface. Clearing the legacy WinINet cache restored the interface in the original investigation; a narrowly matched IIS URL Rewrite **AbortRequest** rule was subsequently tested successfully on a single machine.

This utility transfers that workflow from PowerShell to a single, self-contained .NET CLI. It does **not** change IIS port 80, IP bindings, hostnames, DHCP, Babylon's executable, or license data.

### Compatibility approach

| Web server | Managed rule | Important limitation |
|---|---|---|
| IIS | `Babylon12-Localhost-Compatibility` with URL Rewrite `AbortRequest` | Requires Microsoft IIS URL Rewrite 2.1. May install the official signed x64 MSI when IIS exists. |
| Apache 2.4, including XAMPP | A scoped Apache `<LocationMatch>` / `<If>` rule | Returns **HTTP 403**, not a TCP abort. A test workstation successfully translated with Babylon Pro 12 while XAMPP Apache listened on TCP/80. The HTTP 403 behavior still needs separate verification. |
| Neither installed | No operation | Does not clear cache or install anything. |

The match is limited to the exact HTTP host `localhost` (optionally port 80), root path `/` and Babylon 12 query prefix `uid=...&ver=12000&bld=13&iev=11`. All other requests should be left alone. Please independently verify behavior with your applications before distribution.

## Download and build

Requirements for **building**: Windows and .NET **10 SDK** (C# 14; Visual Studio 2026 is supported). The build restores the official `Microsoft.Web.Administration` 11.1.0 NuGet package for .NET, rather than loading the legacy .NET Framework DLL from IIS. Internet access to NuGet is needed for the first restore. No additional .NET runtime needs to be installed on target machines when published self-contained.

```powershell
dotnet build BabylonWebServerFix.sln -c Release
dotnet run --project tests/BabylonWebServerFix.SmokeTests/BabylonWebServerFix.SmokeTests.csproj -c Release
dotnet publish src/BabylonWebServerFix/BabylonWebServerFix.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o dist
```

The output is `dist/BabylonWebServerFix.exe`. **The generated executable is unsigned** until signed by a trusted publisher. The GitHub Actions build creates an unsigned `win-x64` artifact on Windows.

## Usage

Run from an **elevated terminal** on a test workstation. The executable does not bypass a configured execution policy because it is a native .NET app, not a `.ps1`.

```cmd
BabylonWebServerFix.exe install
BabylonWebServerFix.exe status
BabylonWebServerFix.exe remove
BabylonWebServerFix.exe cache-only
BabylonWebServerFix.exe restart-apache
BabylonWebServerFix.exe install --dry-run
BabylonWebServerFix.exe install --enable-log
BabylonWebServerFix.exe install --cleanup-next-logon
BabylonWebServerFix.exe install --skip-apache-restart --skip-babylon-restart
```

Other flags: `--site "Default Web Site"`, `--msi PATH`, `--apache-httpd PATH`, `--apache-config PATH`, `--skip-cache`, `--log-path PATH` (requires `--enable-log`), `--cache-version 1,0,0,0` and `--help`.

**Defaults:** no log file, no scheduled cleanup at user logon. Temporary WinINet files of non-special local and domain user profiles are cleaned immediately when IIS or Apache is detected and `install`/`cache-only` runs. The current token's WinINet index is cleared with the standard Windows API entrypoint. Locked cache files can remain: choose `--cleanup-next-logon` only when needed.

### Automatic restarts

- **Babylon:** only if already running, and only if an interactive, non-elevated restart in the original user session can be prepared safely. Otherwise it is left alone. `--skip-babylon-restart` opts out.
- **Apache as Windows service:** uses `httpd -k restart -n SERVICE`.
- **XAMPP console Apache:** starts the selected `httpd.exe` directly with `-d ROOT -f CONFIG`. Bundled XAMPP BATs are not called because they may contain unresolved installation placeholders or block in the foreground. Restarts only Apache processes from the selected executable in the **same user and session**, and verifies TCP/80. Console processes are forcibly terminated during this operation; existing HTTP requests can be interrupted. Use `--skip-apache-restart` to opt out. A previously stopped Apache remains stopped. Repeating `install` with an unchanged managed rule does **not** restart Apache; use `restart-apache` for an explicit reload.
- **IIS:** it is not started or restarted automatically.

> **Security:** run the tool only from a trusted directory and ensure the XAMPP `.bat` files and Apache configuration are not writable by untrusted users before using an elevated administrator account. Do not place a downloaded executable in an untrusted student-writable share.

### Existing installations and idempotence

- The IIS rule name and exact match expressions are retained from the previously tested PowerShell versions.
- Already-installed rules are recognized to avoid duplicates.
- IIS configuration is backed up with `appcmd add backup` before changing the rule.
- Apache `httpd.conf` is backed up, modifications validated with `httpd -t` and rolled back on validation or restart failure.
- `remove` deletes only the utility's managed rules and the optional one-time cleanup registration, **not** the web servers, URL Rewrite MSI, or deleted temporary files.
- A downloaded URL Rewrite MSI is verified for Microsoft Authenticode signature and deleted after the install attempt. Explicit/local MSI files are never removed.
- No configuration or cache modification if neither IIS nor Apache is detected.

## Deployment and limitations

1. Make a VM snapshot or record an IIS/Apache backup on one test workstation.
2. Build and run `BabylonWebServerFix.exe status` and `install --dry-run`.
3. Test `install` and check a real translation in Babylon with the relevant local web server running on port 80.
4. **Verify the Apache behavior separately**: a `403` response is not the same as IIS `AbortRequest`; the Apache rule may not fix Babylon on every host.
5. Test `remove`, reinstall, reboots, active student sessions, XAMPP restarts, multiple users and cache cleanup behavior before deploying through GPO or imaging.

The source build requires Windows and .NET 10. GitHub Actions compiles the project on Windows and runs smoke tests; a green build does **not** validate full IIS/XAMPP/Babylon integration. Always validate on a test workstation before deployment.

## Repository layout

- `src/BabylonWebServerFix` — C# CLI and Windows integrations
- `tests/BabylonWebServerFix.SmokeTests` — dependency-free offline smoke tests
- `.github/workflows/build.yml` — Windows build and unsigned single-file artifact
- `.github/workflows/release.yml` — manual preview release from an existing tag
- `docs/TEST_MATRIX.md` — validation checklist
- `legacy/Babylon-IIS-Apache-AllUsers-v10.ps1` — reference deployment script

## Release process

After creating an empty GitHub repository named `BabylonWebServerFix`, push these source files to its `main` branch. GitHub Actions builds the `win-x64` artifact. After a green build and a successful real-Windows test, create a prerelease tag and run the **Preview release** workflow. Do not mark this release as stable until IIS, Apache and Babylon have been validated on actual target machines.

## License

MIT (see `LICENSE`). Babylon, XAMPP, Apache HTTP Server, Microsoft IIS and Microsoft URL Rewrite are third-party products with their own licenses and trademarks. No third-party binaries are bundled.

### Preparing your GitHub repository

Create an **empty** repository named `BabylonWebServerFix` on your GitHub account (do not initialize it with its own README, `.gitignore` or license). Extract this archive, open a terminal in the `BabylonWebServerFix` directory, then run:

```cmd
git init -b main
git add .
git commit -m "Initial .NET 10 preview migration"
git remote add origin https://github.com/sgennadi/BabylonWebServerFix.git
git push -u origin main
```

Replace `sgennadi` with your GitHub account. Wait for **Windows build and smoke tests** to pass before distributing any generated executable. The project intentionally does not bundle a compiled executable or Microsoft MSI.
