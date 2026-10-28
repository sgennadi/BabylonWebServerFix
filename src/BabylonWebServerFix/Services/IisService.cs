using Microsoft.Web.Administration;

namespace BabylonWebServerFix.Services;

internal sealed class IisService(CliOptions options, DeployLog log)
{
    private readonly CliOptions _options = options;
    private readonly DeployLog _log = log;
    public static bool IsInstalled => WindowsSystem.ServiceInstalled("W3SVC");
    private string Inetsrv => Path.Combine(WindowsSystem.WindowsDirectory, "System32", "inetsrv");

    public async Task ApplyAsync(string mode)
    {
        if (!IsInstalled) return;
        // The .NET Core implementation of Microsoft.Web.Administration is shipped
        // with the utility. The IIS server feature and appcmd must exist locally.
        if (!File.Exists(Path.Combine(Inetsrv, "appcmd.exe")))
        {
            _log.Warn("IIS scripting components (appcmd.exe) unavailable; no IIS changes made.");
            return;
        }
        using (var manager = new ServerManager())
        {
            var site = manager.Sites[_options.Site];
            if (site is null) throw new InvalidOperationException("IIS site not found: " + _options.Site);
            _log.Info($"IIS site: {_options.Site}; state={site.State}");
            foreach (var binding in site.Bindings)
                _log.Info($"Binding: {binding.Protocol} {binding.BindingInformation}");

            var moduleFound = await DetectRewriteModuleAsync(manager);
            var native = new IisNativeRequestFiltering(_options, _log);
            if (mode == "status")
            {
                if (moduleFound) ApplyRule(manager, "status");
                else _log.Info("IIS RewriteModule not installed.");
                native.Apply(manager, "status");
                return;
            }
            if (mode == "remove")
            {
                if (moduleFound) ApplyRule(manager, "remove");
                else _log.Info("IIS URL Rewrite compatibility rule absent (module not installed).");
                // CommitChanges updates configuration; reload before a second edit.
                using var fresh = new ServerManager();
                native.Apply(fresh, "remove");
                return;
            }
            if (_options.IisMode == "native")
            {
                // Install the new rule before removing the older managed rule.
                // If the new rule fails, the previous URL Rewrite rule remains.
                bool rewriteOwned = moduleFound && HasManagedRewriteRule(manager);
                native.Apply(manager, "install");
                if (rewriteOwned)
                {
                    if (_options.DryRun)
                        _log.Info("Would remove previous Babylon URL Rewrite rule after native setup.");
                    else
                    {
                        using var afterNative = new ServerManager();
                        ApplyRule(afterNative, "remove");
                        _log.Info("IIS compatibility migrated from URL Rewrite to native Request Filtering.");
                    }
                }
                return;
            }
            if (moduleFound)
            {
                // The explicit URL Rewrite choice takes priority. Configure
                // that rule first, then clean up our native filtering rule.
                ApplyRule(manager, "install");
                using var afterRewrite = new ServerManager();
                native.Apply(afterRewrite, "remove");
                return;
            }
            if (_options.DryRun)
            {
                _log.Info("Would install signed Microsoft IIS URL Rewrite from offline MSI or HTTPS and configure compatibility rule.");
                native.Apply(manager, "remove");
                return;
            }
        }

        // Release IIS configuration handles before running an MSI, which may
        // register a new schema and module. Reopen after successful installation.
        await EnsureRewriteAsync();
        using var updated = new ServerManager();
        if (!await DetectRewriteModuleAsync(updated))
            throw new InvalidOperationException("RewriteModule not registered after MSI; restart Windows and retry.");
        ApplyRule(updated, mode);
        // After successfully installing URL Rewrite, remove only the
        // Babylon-managed native filter. The native rule stays intact
        // whenever URL Rewrite installation or verification fails.
        using var afterRewriteInstall = new ServerManager();
        new IisNativeRequestFiltering(_options, _log).Apply(afterRewriteInstall, "remove");
    }

    private void ApplyRule(ServerManager manager, string mode)
    {
        var configuration = manager.GetApplicationHostConfiguration();
        var section = configuration.GetSection("system.webServer/rewrite/rules", _options.Site);
        var rules = section.GetCollection();
        ConfigurationElement? existing = null;
        foreach (ConfigurationElement rule in rules)
        {
            if (!string.Equals(Convert.ToString(rule.GetAttributeValue("name")), HostPolicy.RuleName, StringComparison.Ordinal)) continue;
            existing = rule;
            break;
        }

        if (mode == "status")
        {
            _log.Info(existing is null ? "IIS rule absent." :
                $"IIS rule present: {HostPolicy.RuleName}; enabled={existing.GetAttributeValue("enabled")}");
            return;
        }
        if (mode == "remove" && existing is null) { _log.Info("IIS rule already absent."); return; }
        if (mode == "install" && existing is not null) { _log.Info("IIS rule already installed; unchanged."); return; }
        if (_options.DryRun) { _log.Info($"Would {mode} IIS rule: {HostPolicy.RuleName}"); return; }

        Backup();
        if (mode == "remove")
        {
            rules.Remove(existing!);
            manager.CommitChanges();
            _log.Info("Removed IIS rule: " + HostPolicy.RuleName);
            return;
        }

        var newRule = rules.CreateElement("rule");
        newRule.SetAttributeValue("name", HostPolicy.RuleName);
        newRule.SetAttributeValue("enabled", true);
        newRule.SetAttributeValue("patternSyntax", "ECMAScript");
        newRule.SetAttributeValue("stopProcessing", true);
        newRule.GetChildElement("match").SetAttributeValue("url", HostPolicy.UrlPattern);
        var conditions = newRule.GetChildElement("conditions");
        conditions.SetAttributeValue("logicalGrouping", "MatchAll");
        var entries = conditions.GetCollection();
        var hostCondition = entries.CreateElement("add");
        hostCondition.SetAttributeValue("input", "{HTTP_HOST}");
        hostCondition.SetAttributeValue("pattern", HostPolicy.HostPattern);
        entries.Add(hostCondition);
        var queryCondition = entries.CreateElement("add");
        queryCondition.SetAttributeValue("input", "{QUERY_STRING}");
        queryCondition.SetAttributeValue("pattern", HostPolicy.QueryPattern);
        entries.Add(queryCondition);
        newRule.GetChildElement("action").SetAttributeValue("type", "AbortRequest");
        rules.AddAt(0, newRule);
        manager.CommitChanges();
        _log.Info("Installed IIS rule: " + HostPolicy.RuleName);
    }

    private bool HasManagedRewriteRule(ServerManager manager)
    {
        var collection = manager.GetApplicationHostConfiguration()
            .GetSection("system.webServer/rewrite/rules", _options.Site).GetCollection();
        foreach (ConfigurationElement item in collection)
            if (string.Equals(Convert.ToString(item.GetAttributeValue("name")),
                HostPolicy.RuleName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private async Task<bool> DetectRewriteModuleAsync(ServerManager manager)
    {
        // The IIS managed API is the primary source. On some workstations it
        // may disagree with appcmd (the IIS configuration CLI). Never download
        // and install another MSI solely because this first lookup failed.
        if (HasRewriteModule(manager))
        {
            if (_options.Action == "install" && _options.IisMode == "rewrite")
                _log.Info("IIS RewriteModule is already registered; reusing existing module without MSI download.");
            return true;
        }

        var appcmd = Path.Combine(Inetsrv, "appcmd.exe");
        CommandResult probe;
        try
        {
            probe = await WindowsSystem.RunAsync(appcmd,
                ["list", "config", "/section:system.webServer/globalModules"], 20);
        }
        catch (Exception e) when (e is IOException or TimeoutException)
        {
            if (_options.Action == "status")
            {
                _log.Warn("Could not cross-check RewriteModule with appcmd: " + e.Message);
                return false;
            }
            throw new InvalidOperationException(
                "Cannot verify IIS RewriteModule registration with appcmd. " +
                "Refusing to download/reinstall URL Rewrite while the registration state is uncertain.", e);
        }

        if (probe.ExitCode != 0)
        {
            if (_options.Action == "status")
            {
                _log.Warn($"Could not cross-check RewriteModule with appcmd (exit={probe.ExitCode}).");
                return false;
            }
            throw new InvalidOperationException(
                $"IIS appcmd module query failed (exit={probe.ExitCode}). " +
                "Refusing an unnecessary MSI reinstall until module registration can be checked.");
        }

        if (IisModuleRegistration.ContainsRewriteModule(probe.Output))
        {
            _log.Warn("RewriteModule is registered according to appcmd but was not found by the managed IIS API. " +
                      "Skipping MSI download and using the existing module.");
            return true;
        }

        if (_options.Action == "install" && _options.IisMode == "rewrite")
            _log.Info("IIS RewriteModule not registered according to either API; signed MSI installation is required.");
        return false;
    }

    private static bool HasRewriteModule(ServerManager manager)
    {
        var modules = manager.GetApplicationHostConfiguration()
            .GetSection("system.webServer/globalModules").GetCollection();
        foreach (ConfigurationElement module in modules)
        {
            if (string.Equals(Convert.ToString(module.GetAttributeValue("name")), "RewriteModule", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void Backup()
    {
        var appcmd = Path.Combine(Inetsrv, "appcmd.exe");
        if (!File.Exists(appcmd)) throw new FileNotFoundException("appcmd.exe is missing", appcmd);
        var name = "BeforeBabylonRewrite_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var result = WindowsSystem.RunAsync(appcmd, ["add", "backup", name], 30).GetAwaiter().GetResult();
        if (result.ExitCode != 0) throw new InvalidOperationException("IIS backup failed: " + result.Output);
        _log.Info("IIS backup: " + name);
    }

    private async Task EnsureRewriteAsync()
    {
        string? downloadedFile = null;
        var selected = RewriteInstallerPolicy.Select(_options.MsiPath, _options.MsiUrl, AppContext.BaseDirectory);
        string? package = selected.LocalPath;
        try
        {
            if (selected.DownloadUrl is Uri downloadUrl)
            {
                // A missing URL or unreachable Microsoft CDN must not trigger
                // an unsigned/unverified download from a different domain.
                downloadedFile = Path.Combine(Path.GetTempPath(), $"rewrite_{Guid.NewGuid():N}.msi");
                package = downloadedFile;
                _log.Info("Downloading signed IIS URL Rewrite MSI from " + downloadUrl.Host + ".");
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                    using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    const long maxInstallerBytes = 128L * 1024L * 1024L;
                    if (response.Content.Headers.ContentLength is > maxInstallerBytes)
                        throw new InvalidDataException("IIS URL Rewrite MSI exceeds maximum download size.");
                    await using var source = await response.Content.ReadAsStreamAsync();
                    await using var dest = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer)) > 0)
                    {
                        total += read;
                        if (total > maxInstallerBytes)
                            throw new InvalidDataException("IIS URL Rewrite MSI exceeds maximum download size.");
                        await dest.WriteAsync(buffer.AsMemory(0, read));
                    }
                    if (total == 0) throw new InvalidDataException("IIS URL Rewrite download is empty.");
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
                {
                    throw new InvalidOperationException(
                        "IIS URL Rewrite download failed. Copy Microsoft's signed x64 rewrite_amd64_en-US.msi " +
                        "beside BabylonWebServerFix.exe, or use --msi \"\\\\server\\share\\rewrite_amd64_en-US.msi\". " +
                        "For an updated official URL use --msi-url HTTPS_URL. No untrusted fallback was attempted.", e);
                }
            }
            else
            {
                _log.Info("Using offline IIS URL Rewrite MSI: " + package);
            }
            if (package is null || !File.Exists(package))
                throw new FileNotFoundException(
                    "IIS URL Rewrite MSI missing. Provide --msi PATH or copy rewrite_amd64_en-US.msi beside the EXE.", package);
            package = Path.GetFullPath(package);
            // Verify Authenticode trust and the Microsoft signer using WinTrust.
            // No PowerShell process or script execution is required.
            AuthenticodeVerifier.RequireMicrosoftSignature(package);
            _log.Info("Installing signed URL Rewrite MSI: " + package);
            var install = await WindowsSystem.RunAsync(WindowsSystem.SystemExecutable("msiexec.exe"),
                ["/i", package, "/qn", "/norestart"], 240);
            if (install.ExitCode is not (0 or 3010)) throw new InvalidOperationException($"MSI failed: {install.ExitCode}; {install.Output}");
            if (install.ExitCode == 3010) _log.Warn("URL Rewrite MSI installed; restart requested by Windows Installer.");
            _log.Info("URL Rewrite MSI installation complete; exit=" + install.ExitCode);
        }
        finally
        {
            if (downloadedFile is not null)
            {
                try { File.Delete(downloadedFile); }
                catch (IOException e) { _log.Warn("Unable to remove downloaded MSI: " + e.Message); }
                catch (UnauthorizedAccessException e) { _log.Warn("Unable to remove downloaded MSI: " + e.Message); }
            }
        }
    }
}
