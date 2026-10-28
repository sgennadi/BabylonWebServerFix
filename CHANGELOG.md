# Changelog

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
