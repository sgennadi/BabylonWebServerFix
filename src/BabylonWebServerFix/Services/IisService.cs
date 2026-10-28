using System.Security;
using Microsoft.Web.Administration;

namespace BabylonWebServerFix.Services;

internal sealed class IisService(CliOptions options, DeployLog log)
{
    private static readonly Uri OfficialMsi = new("https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi");
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

            var moduleFound = HasRewriteModule(manager);
            if (moduleFound || mode != "install")
            {
                if (!moduleFound) _log.Warn("IIS RewriteModule missing; no rule to inspect or remove.");
                else ApplyRule(manager, mode);
                return;
            }
            if (_options.DryRun)
            {
                _log.Info("Would install Microsoft IIS URL Rewrite 2.1 x64 and configure compatibility rule.");
                return;
            }
        }

        // Release IIS configuration handles before running an MSI, which may
        // register a new schema and module. Reopen after successful installation.
        await EnsureRewriteAsync();
        using var updated = new ServerManager();
        if (!HasRewriteModule(updated))
            throw new InvalidOperationException("RewriteModule not registered after MSI; restart Windows and retry.");
        ApplyRule(updated, mode);
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

    private static bool HasRewriteModule(ServerManager manager)
    {
        var modules = manager.GetApplicationHostConfiguration()
            .GetSection("system.webServer/globalModules").GetCollection();
        foreach (ConfigurationElement module in modules)
        {
            if (string.Equals(Convert.ToString(module.GetAttributeValue("name")), "RewriteModule", StringComparison.Ordinal))
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
        var package = _options.MsiPath;
        try
        {
            if (string.IsNullOrWhiteSpace(package))
            {
                var beside = Path.Combine(AppContext.BaseDirectory, "rewrite_amd64_en-US.msi");
                if (File.Exists(beside)) package = beside;
                else
                {
                    downloadedFile = Path.Combine(Path.GetTempPath(), $"rewrite_{Guid.NewGuid():N}.msi");
                    package = downloadedFile;
                    _log.Info("Downloading official IIS URL Rewrite 2.1 MSI.");
                    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                    await using var source = await client.GetStreamAsync(OfficialMsi);
                    await using var dest = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(dest);
                }
            }
            package = Path.GetFullPath(package);
            if (!File.Exists(package)) throw new FileNotFoundException("URL Rewrite MSI missing", package);
            var quoted = package.Replace("'", "''", StringComparison.Ordinal);
            var signatureCommand = "$s=Get-AuthenticodeSignature -LiteralPath '" + quoted +
                "';if($s.Status -eq 'Valid' -and $s.SignerCertificate -and $s.SignerCertificate.Subject -match 'Microsoft Corporation'){exit 0}else{exit 1}";
            var signature = await WindowsSystem.RunAsync(WindowsSystem.SystemExecutable("WindowsPowerShell\\v1.0\\powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-Command", signatureCommand], 45);
            if (signature.ExitCode != 0) throw new SecurityException("MSI signature is not valid Microsoft Authenticode.");
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
