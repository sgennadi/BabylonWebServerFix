# BabylonWebServerFix

**Windows x64 compatibility utility for Babylon Pro 12 with Microsoft IIS and Apache/XAMPP.**

**Latest stable release: [v1.0.5](https://github.com/sgennadi/BabylonWebServerFix/releases/tag/v1.0.5).** Starting the EXE without arguments, with `/?`, `-h`, or `--help` displays usage and examples without changing the system. Run `install` explicitly to apply compatibility rules.

Babylon Pro 12 uses an embedded MSHTML viewer. When it loads its interface from `http://localhost/`, a local IIS or Apache server on port 80 can intercept the request and display the web server's page instead of the Babylon interface. This utility installs compatibility rules without changing server port bindings or Babylon installation and licensing files.

## When to use the utility

**Do not install a fix if Babylon already translates correctly while IIS or Apache is running.**

Two Babylon interface-loading modes have been observed:

| Babylon interface mode | Example document address | Recommendation |
|---|---|---|
| Localhost HTTP | `http://localhost/?uid=0&ver=12000&bld=13&iev=11&lngs=14,0,15,3,1,7` | If the web server intercepts this request and Babylon fails, use the compatibility fix. |
| Local HTML file | `file:///C:/ProgramData/Babylon/LocalUI/wnd.html?uid=0&ver=12000&bld=13&iev=11&lngs=14,0,15,3,1,7` | If translation works, no compatibility fix is necessary. |

An additional temporary document may appear at `%LOCALAPPDATA%\Temp\BWebData.html`. Its exact purpose is not confirmed. Loading `wnd.html` from disk does not prove that Babylon never accesses the network.

The two interface-loading modes above are different from the utility's **two IIS rule types** described below.

## Download

Download `BabylonWebServerFix.exe` and `SHA256SUMS.txt` from [v1.0.5 Stable](https://github.com/sgennadi/BabylonWebServerFix/releases/tag/v1.0.5). Verify the checksum before deployment.

The EXE is an unsigned, self-contained .NET 10 Windows x64 executable. The target machine does not require a separate .NET runtime or PowerShell.

Run mutating commands from an elevated Command Prompt or PowerShell. The `status` command is read-only and does not require elevation.

## Quick start

Show help and examples (no changes):

```cmd
BabylonWebServerFix.exe
BabylonWebServerFix.exe /?
```


If Babylon's **localhost HTTP** mode conflicts with a local web server, inspect the current configuration before making changes:

```cmd
BabylonWebServerFix.exe status
BabylonWebServerFix.exe install --dry-run
BabylonWebServerFix.exe install
```

**Without arguments**, the EXE displays help and examples without making changes. `/?`, `--help`, and `-h` do the same. Use the explicit `install` command to apply changes.

To remove only the utility's IIS compatibility rules:

```cmd
BabylonWebServerFix.exe remove-iis
```

To remove all managed compatibility settings:

```cmd
BabylonWebServerFix.exe remove
```

Do not remove or install rules on a working computer simply to switch Babylon interface modes.

## IIS compatibility modes

### Native Request Filtering (default)

```cmd
BabylonWebServerFix.exe install --iis-mode native
```

Plain `install` selects this mode automatically. It uses built-in IIS Request Filtering and does **not** require Microsoft URL Rewrite or an MSI download.

The managed rule `Babylon12-Compatibility-Native` returns **HTTP 404.19** for a query string containing `&ver=12000&bld=13&iev=11`. This is a **site-wide query match** on the selected IIS site; it is not restricted to `localhost` or the root path. Ordinary requests without this query substring are not targeted. An initial HTTP error-page flash may occur in Babylon.

### Microsoft URL Rewrite (optional)

```cmd
BabylonWebServerFix.exe install --iis-mode rewrite
```

This rule targets the legacy Babylon localhost request more precisely and uses URL Rewrite `AbortRequest`. If `RewriteModule` is already registered, the utility reuses it without an automatic update. Otherwise, a genuine Microsoft-signed x64 URL Rewrite MSI is needed.

The utility supports switching between these IIS rule types. It installs the selected rule before removing the other **managed** rule, backs up IIS configuration before modifications, and does not uninstall the URL Rewrite module.

To supply the MSI offline, place `rewrite_amd64_en-US.msi` alongside the EXE or specify:

```cmd
BabylonWebServerFix.exe install --iis-mode rewrite --msi "C:\Installers\rewrite_amd64_en-US.msi"
```

For an updated **direct Microsoft HTTPS MSI URL**, use `--msi-url HTTPS_URL`. Do not supply a download webpage. `--msi` and `--msi-url` cannot be combined. All MSI candidates must pass Windows Authenticode/WinTrust and Microsoft publisher checks. If the module's registration cannot be determined safely, the installer does not blindly reinstall it.

`remove-iis` removes only this utility's managed IIS rules. It does not stop IIS, remove third-party modules, or clear the WinINet cache.

## Apache / XAMPP compatibility

For Apache 2.4 or XAMPP, the utility creates a conditional rule returning **HTTP 403** for the legacy Babylon localhost request. It backs up configuration and validates changes using `httpd -t`.

A running Apache Windows service or matching XAMPP console instance may be restarted when configuration changes. Unchanged rules do not trigger an unnecessary Apache restart. A previously stopped Apache remains stopped. The utility does not change its port bindings.

## Commands and options

| Command or option | Purpose |
|---|---|
| `status` | Show detected server configuration and managed rules |
| `install [--iis-mode native|rewrite]` | Install applicable compatibility rules |
| `install --dry-run` | Preview modifications without applying them |
| `remove-iis` | Remove only managed IIS compatibility rules |
| `remove` | Remove managed compatibility settings |
| `restart-apache` | Explicit Apache restart |
| `cache-only` | Perform WinINet temporary cache cleanup |
| `--skip-cache` | Do not clear the temporary cache during install |
| `--skip-babylon-restart` | Do not restart an already running Babylon process |
| `--skip-apache-restart` | Do not restart Apache automatically |
| `--site "Default Web Site"` | Select an IIS site |
| `--apache-httpd PATH` / `--apache-config PATH` | Select a nonstandard Apache installation |
| `--msi PATH` / `--msi-url HTTPS_URL` | Provide a trusted URL Rewrite installer |
| `--enable-log` / `--log-path PATH` | Opt in to an installation log and optionally choose its path |
| `--cleanup-next-logon` | Opt in to Active Setup per-user cache cleanup |
| `--help` | Display all supported arguments |

Logging and next-logon cleanup are disabled by default. Temporary WinINet cleanup preserves cookies and saved credentials, though locked files may remain. If Babylon is already running, the utility can restart it in its interactive session; it does not start Babylon when it was previously stopped.

If neither IIS nor Apache is installed, `install` makes no changes, including to the cache.

## Safety and limitations

- Native IIS filtering matches the Babylon query substring **site-wide**. Use URL Rewrite if a narrower localhost-only rule is required.
- The HTTP responses differ between IIS native filtering (404.19), IIS URL Rewrite (connection abort), and Apache (403).
- IIS is not started or restarted by the utility.
- Protect the EXE, MSI packages, Apache binaries and configuration files from modification by untrusted users before elevated execution.
- Test on the intended IIS/Apache configuration before deploying to other computers. Automated smoke tests do not replace integration testing.

See [validation matrix](docs/TEST_MATRIX.md) for detailed test scenarios.

## Build from source

Requirements: Windows and .NET 10 SDK. Visual Studio 2026 is optional.

```cmd
dotnet build BabylonWebServerFix.sln -c Release
dotnet run --project tests/BabylonWebServerFix.SmokeTests/BabylonWebServerFix.SmokeTests.csproj -c Release
dotnet publish src/BabylonWebServerFix/BabylonWebServerFix.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o dist
```

Output: `dist\BabylonWebServerFix.exe`.

## Project files

- `src/BabylonWebServerFix/` — C# CLI and Windows integration
- `tests/BabylonWebServerFix.SmokeTests/` — offline regression tests
- `.github/workflows/build.yml` — Windows build and test workflow
- `.github/workflows/stable-release.yml` — stable release workflow
- `docs/TEST_MATRIX.md` — detailed verification scenarios
- `CHANGELOG.md` — version history

## License

MIT ([LICENSE](LICENSE)). Babylon, IIS, Microsoft URL Rewrite, Apache and XAMPP are third-party products with their own licenses and trademarks. No third-party application binaries are bundled.
