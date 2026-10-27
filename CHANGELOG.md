# Changelog

## 0.1.0-preview.1 — source preview

- Initial C# 14 / .NET 10 Windows x64 CLI port of PowerShell v10.
- IIS URL Rewrite compatibility rule and signed MSI installation if IIS is present.
- Apache 2.4/XAMPP conditional compatibility rule and Apache syntax validation.
- XAMPP restart through the installation's stock batch files.
- User-profile WinINet temporary cache cleanup and optional next-login cleanup.
- Optional logging, `--dry-run`, idempotent installation and removal.
- Restart Babylon in its original user session only when it was already running.
- Windows GitHub Actions build and standalone unsigned preview artifact.

This is **not** a stable release. Windows build and real Apache/Babylon translation verification are pending.

- Use NuGet `Microsoft.Web.Administration` 11.1.0 with typed IIS API instead of loading the legacy .NET Framework assembly at runtime.
