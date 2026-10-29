# Changelog

## 1.0.6 — stable release (2026-10-29)

- Added opt-in `tray-init` command to minimize a visible Babylon main window into the tray in the current user's interactive session.
- Added `--wait-seconds 0..300` for login-time Babylon start; existing `--dry-run` mode is respected.
- No registry/file edits, license data copying, or changes to IIS/Apache when running `tray-init`.
- Keeps existing web-server compatibility behavior from 1.0.5 unchanged.
- Added CLI parser smoke tests and documentation; GitHub Actions Windows x64 build and test validation.


## 1.0.5 — stable release

- No-argument execution displays help and examples without modifying the system.
- Added Windows-style `/?` along with `-h` and `--help`.
- Compatibility changes require explicit `install` command.
- Updated documentation and CLI smoke tests; IIS/Apache compatibility behavior is unchanged.


## 1.0.4 — stable release (2026-10-28)

- This stable release includes the preceding 1.0.1–1.0.3 development changes: MSI-source hardening, MSI-free native IIS filtering, `remove-iis`, and safe bidirectional migration between native filtering and URL Rewrite.
- Harden RewriteModule detection: query Microsoft.Web.Administration first, then cross-check with read-only `appcmd list config /section:system.webServer/globalModules` when the managed API reports no module.
- If appcmd confirms RewriteModule is registered, reuse it and skip MSI download; emit a diagnostic warning describing the disagreement.
- Refuse a blind MSI download if the module-registration query fails; preserve the configured native rule and avoid unnecessary IIS changes.
- Add seven offline regression tests for appcmd output parsing, including real-world registration syntax, comment, error, and look-alike cases.
- Keep default MSI-free native filtering and bidirectional rule migration unchanged. The cause of the earlier 09:33 redownload remains unconfirmed.
- Workstation verification: after `status` showed native installed/Rewrite absent, `remove-iis` backed up IIS and removed the native rule; a second `status` confirmed both project-managed IIS rules absent while IIS stayed Started. No web-server module uninstallation was attempted.
- Validated on BASE-IMAGE: v1.0.4 `install --iis-mode rewrite --dry-run` recognized the registered module, skipped MSI download, previewed the Rewrite/native migration, and completed successfully. The appcmd fallback and actual reinstall avoidance after switching still need separate validation.


## 1.0.3 — unreleased development (2026-10-28)

- Make the built-in, MSI-free IIS Request Filtering mode the default for ordinary `install` and no-argument execution.
- Confirmed on a test workstation: Babylon Pro 12 successfully translates with IIS native filtering active; a brief initial error-page flash was observed and remains a known limitation.
- Convert successful native filter setup message from WARN to INFO.
- Add bidirectional migration: switch to native only after its rule is installed, removing the old Babylon URL Rewrite rule; switch to URL Rewrite only after that rule is installed, then removing the native Babylon filtering rule.
- Preserve Microsoft URL Rewrite module itself (no module uninstall or unnecessary download); make `--iis-mode rewrite` opt-in, while explicit `--msi` or `--msi-url` selects Rewrite.
- Add CLI and regression tests for default mode, explicit alternatives and conflicting option combinations.
- Workstation evidence on 2026-10-28: both directions of automatic managed IIS rule migration completed without errors (Native → Rewrite at 09:33 and Rewrite → Native at 09:34). Reuse of a previously installed RewriteModule still needs diagnosis: the 09:33 run downloaded the MSI again.


## 1.0.2 — unreleased development (2026-10-28)

- Opt-in `--iis-mode native` uses built-in IIS Request Filtering without downloading/installing URL Rewrite.
- Creates a named, removable filter for the Babylon 12 query substring, with HTTP 404.19 and IIS configuration backup.
- Add `remove-iis` to remove only managed IIS compatibility rules, preserving Apache and WinINet.
- Show both IIS compatibility rules in `status`; preserve URL Rewrite as the default.
- The native filter is intentionally experimental until translation is verified on a workstation.


## 1.0.1 — unreleased development (2026-10-28)

- Add `--msi-url` to handle future official Microsoft URL Rewrite downloads without recompiling the utility.
- Prefer the offline Microsoft-signed MSI beside the EXE, with explicit `--msi` support for UNC deployment shares.
- Fail closed with actionable offline installation instructions if the download URL is unavailable; do not use third-party mirrors.
- Validate HTTPS Microsoft download hosts for URL overrides and limit download size.
- Add regression tests for offline priority, UNC paths, updated HTTPS URL, invalid hosts and conflicting sources.
- Existing registered IIS RewriteModule remains untouched regardless of module version.


## 1.0.0 — first stable release (2026-10-28)

- Promote the tested Babylon Pro 12 / IIS / Apache-XAMPP compatibility workflow to the first stable Windows x64 CLI release.
- Preserve v0.1.3 behavior (no invasive changes to the IIS rule, Apache restart, Babylon restart, or WinINet cleanup code).
- Package a self-contained .NET 10 `BabylonWebServerFix.exe` and an independent SHA-256 checksum asset.
- Document supported test configurations, known limitations and verification steps.
- Keep logging and next-logon cleanup disabled by default.


## 0.1.3-preview (2026-10-28)

- Replace the runtime PowerShell signer check with native Windows WinTrust verification plus Microsoft publisher identity validation for IIS URL Rewrite MSI files.
- Keep MSI verification fail-closed; reject invalid or non-Microsoft signatures and add negative signature and publisher-name regression tests.
- Rework README for an existing GitHub repository, with EXE-only CMD examples, download/build instructions and no obsolete repository-creation steps.
- Clarify preview verification limits and service restart behavior; retain prior PowerShell source only as a historical reference under legacy/.
- Update preview-release workflow to use CMD rather than PowerShell commands.


## 0.1.2-preview (2026-10-28)

- Fix idempotence: repeated `install` no longer restarts Apache when the managed include and rule are unchanged.
- Replace timestamp-based Apache reload inference with exact configuration state.
- Add four offline regression tests for already-installed/missing/outdated managed Apache configuration.
- Confirmed on one Windows test workstation: XAMPP listening on port 80 and Babylon Pro 12 translating English to Hebrew after restart. A broader HTTP-compatibility test is still needed.
- Retain explicit `restart-apache` and automatic Babylon restart behavior.

## 0.1.1-preview (2026-10-28)

- Fix XAMPP console restart: bundled apache_stop.bat may contain an unresolved @@BITROCK_INSTALLDIR@@ token, and apache_start.bat may block in the foreground.
- Stop only processes matching the selected XAMPP executable, user and session; never use global taskkill by process name.
- Restart the validated Apache binary with explicit -d and -f arguments, then confirm a TCP/80 listener.
- Attempt to restart Apache with restored configuration after a failed update.
- Retain IIS, Babylon restart and WinINet cache behavior.


## 0.1.0-preview.1 — source preview

- Initial C# 14 / .NET 10 Windows x64 CLI port of PowerShell v10.
- IIS URL Rewrite compatibility rule and signed MSI installation if IIS is present.
- Apache 2.4/XAMPP conditional compatibility rule and Apache syntax validation.
- XAMPP restart through the installation's stock batch files.
- User-profile WinINet temporary cache cleanup and optional next-login cleanup.
- Optional logging, `--dry-run`, idempotent installation and removal.
- Restart Babylon in its original user session only when it was already running.
- Windows GitHub Actions build and standalone unsigned preview artifact.

This remains a **preview**. Windows CI builds and a single workstation Apache/Babylon translation test have passed; broader IIS and Apache integration testing remains outstanding.

- Use NuGet `Microsoft.Web.Administration` 11.1.0 with typed IIS API instead of loading the legacy .NET Framework assembly at runtime.
