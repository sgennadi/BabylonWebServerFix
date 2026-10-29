using System.Text.RegularExpressions;

namespace BabylonWebServerFix;

public sealed record CliOptions
{
    public string Action { get; private set; } = "status";
    public bool Help { get; private set; } = false;
    public string Site { get; private set; } = "Default Web Site";
    public string IisMode { get; private set; } = "native";
    public string? MsiPath { get; private set; }
    public Uri? MsiUrl { get; private set; }
    public string? ApacheHttpdPath { get; private set; }
    public string? ApacheConfigPath { get; private set; }
    public string CacheVersion { get; private set; } = "1,0,0,0";
    public bool EnableLog { get; private set; }
    public string? LogPath { get; private set; }
    public bool CleanupNextLogon { get; private set; }
    public bool SkipCache { get; private set; }
    public bool SkipBabylonRestart { get; private set; }
    public bool SkipApacheRestart { get; private set; }
    public bool DryRun { get; private set; }
    public int TrayWaitSeconds { get; private set; } = 0;

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        if (args.Length == 0 || (args.Length == 1 && args[0] is "/?" or "--help" or "-h"))
        {
            o.Help = true;
            return o;
        }
        int i = 0;
        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            o.Action = args[0].ToLowerInvariant();
            i++;
        }
        if (o.Action is not ("install" or "status" or "remove" or "remove-iis" or "cache-only" or "restart-apache" or "tray-init"))
            throw new ArgumentException("Unknown command: " + o.Action);

        string NeedValue()
        {
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {args[i - 1]}");
            return args[i];
        }
        bool iisModeExplicit = false;
        for (; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--site": o.Site = NeedValue(); break;
                case "--iis-mode":
                    o.IisMode = NeedValue().ToLowerInvariant();
                    iisModeExplicit = true;
                    break;
                case "--msi": o.MsiPath = NeedValue(); break;
                case "--msi-url": o.MsiUrl = RewriteInstallerPolicy.ParseMicrosoftDownloadUrl(NeedValue()); break;
                case "--apache-httpd": o.ApacheHttpdPath = NeedValue(); break;
                case "--apache-config": o.ApacheConfigPath = NeedValue(); break;
                case "--cache-version": o.CacheVersion = NeedValue(); break;
                case "--log-path": o.LogPath = NeedValue(); break;
                case "--enable-log": o.EnableLog = true; break;
                case "--cleanup-next-logon": o.CleanupNextLogon = true; break;
                case "--skip-cache": o.SkipCache = true; break;
                case "--skip-babylon-restart": o.SkipBabylonRestart = true; break;
                case "--skip-apache-restart": o.SkipApacheRestart = true; break;
                case "--dry-run": o.DryRun = true; break;
                case "--wait-seconds":
                    if (!int.TryParse(NeedValue(), out var seconds) || seconds < 0 || seconds > 300)
                        throw new ArgumentException("--wait-seconds must be between 0 and 300");
                    o.TrayWaitSeconds = seconds;
                    break;
                case "--help": case "-h": case "/?": o.Help = true; break;
                default: throw new ArgumentException("Unknown option: " + args[i]);
            }
        }
        // A supplied MSI path or URL is an intentional request for URL Rewrite.
        // Honor it when --iis-mode was not explicitly specified.
        if (!iisModeExplicit && (o.MsiPath is not null || o.MsiUrl is not null))
            o.IisMode = "rewrite";
        if (string.IsNullOrWhiteSpace(o.Site)) throw new ArgumentException("--site must not be empty");
        if (iisModeExplicit && o.IisMode == "native" && (o.MsiPath is not null || o.MsiUrl is not null))
            throw new ArgumentException("MSI installer options require --iis-mode rewrite, not native.");
        if (o.IisMode is not ("rewrite" or "native")) throw new ArgumentException("--iis-mode must be rewrite or native");
        if (o.MsiPath is not null && o.MsiUrl is not null) throw new ArgumentException("--msi and --msi-url cannot be combined");
        if (o.TrayWaitSeconds != 0 && o.Action != "tray-init") throw new ArgumentException("--wait-seconds is only valid for tray-init");
        if (o.LogPath != null && !o.EnableLog) throw new ArgumentException("--log-path requires --enable-log");
        if (o.CleanupNextLogon && o.Action is not ("install" or "cache-only"))
            throw new ArgumentException("--cleanup-next-logon is only valid for install / cache-only");
        if (o.SkipCache && o.Action != "install") throw new ArgumentException("--skip-cache is only valid for install");
        if (o.SkipCache && o.CleanupNextLogon) throw new ArgumentException("--skip-cache conflicts with --cleanup-next-logon");
        if (!Regex.IsMatch(o.CacheVersion, @"^\d{1,5},\d{1,5},\d{1,5},\d{1,5}$"))
            throw new ArgumentException("--cache-version must look like 1,0,0,0");
        return o;
    }

    public static string Usage => """
        BabylonWebServerFix - IIS / Apache compatibility for Babylon Pro 12

          BabylonWebServerFix.exe                   Show help (no changes)
          BabylonWebServerFix.exe /?                Show help (no changes)
          BabylonWebServerFix.exe install           [options]
          BabylonWebServerFix.exe status            [options]
          BabylonWebServerFix.exe remove            [options]
          BabylonWebServerFix.exe remove-iis        [options]
          BabylonWebServerFix.exe cache-only        [options]
          BabylonWebServerFix.exe restart-apache    [options]
          BabylonWebServerFix.exe tray-init          [--wait-seconds 0..300] [--dry-run]

        Options:
          --site "Default Web Site"     IIS site name
          --iis-mode native|rewrite     IIS Request Filtering (default, no MSI)
                                        or Microsoft IIS URL Rewrite (explicit)
          --msi PATH                    Offline Microsoft-signed MSI (local / network share)
          --msi-url HTTPS_URL           Updated direct Microsoft .msi download URL
          --apache-httpd PATH           Exact Apache httpd.exe if detection is ambiguous
          --apache-config PATH          Apache httpd.conf when nonstandard
          --skip-cache                  Install without clearing cache
          --skip-babylon-restart        Leave Babylon running
          --skip-apache-restart         Do not restart Apache automatically
          --cleanup-next-logon         Opt-in Active Setup per-user cleanup
          --cache-version 1,0,0,0      Version of Active Setup registration
          --enable-log                  Create optional log (OFF by default)
          --log-path PATH               Custom log path, requires --enable-log
          --wait-seconds 45             Wait up to 45 seconds for Babylon (tray-init only)
          --dry-run                     Show intended changes without changing anything
          /?, -h, --help                Help

        Examples:
          BabylonWebServerFix.exe
          BabylonWebServerFix.exe /?
          BabylonWebServerFix.exe status
          BabylonWebServerFix.exe install --dry-run
          BabylonWebServerFix.exe install
          BabylonWebServerFix.exe install --iis-mode rewrite
          BabylonWebServerFix.exe remove-iis
          BabylonWebServerFix.exe tray-init --wait-seconds 45

        No arguments or a help switch displays help without changing anything.
        An elevated Windows x64 terminal is required for mutating actions.
        If neither IIS nor Apache is present: do nothing (including cache cleanup).
        Default IIS mode: native Request Filtering, no MSI download.
        --iis-mode rewrite enables Microsoft URL Rewrite (or use --msi / --msi-url).
        Existing RewriteModule is reused; it is never automatically upgraded.
        Switching modes removes only the other Babylon compatibility rule.
        Native IIS query blocking is site-wide and returns HTTP 404.19.
        remove-iis cleans only the managed IIS rules, not Apache or cache.
        """;
}
