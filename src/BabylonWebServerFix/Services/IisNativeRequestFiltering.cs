using Microsoft.Web.Administration;

namespace BabylonWebServerFix.Services;

/// <summary>
/// Default MSI-free IIS Request Filtering rule. Unlike URL Rewrite this
/// rejects all paths/hosts on the selected site with a matching query string,
/// returning HTTP 404.19 rather than aborting the connection.
/// </summary>
internal sealed class IisNativeRequestFiltering(CliOptions options, DeployLog log)
{
    private readonly CliOptions _options = options;
    private readonly DeployLog _log = log;

    public void Apply(ServerManager manager, string mode)
    {
        if (!Available(manager))
        {
            if (mode == "install")
                throw new InvalidOperationException(
                    "IIS RequestFilteringModule is not registered. Enable the Windows IIS Request Filtering feature (no URL Rewrite MSI needed).");
            _log.Info("IIS native Request Filtering module is absent.");
            return;
        }

        // A location in applicationHost.config scoped to the selected site.
        var section = manager.GetApplicationHostConfiguration().GetSection(
            "system.webServer/security/requestFiltering", _options.Site);
        var rules = section.GetCollection("filteringRules");
        ConfigurationElement? existing = null;
        foreach (ConfigurationElement rule in rules)
            if (string.Equals(Convert.ToString(rule.GetAttributeValue("name")),
                HostPolicy.NativeIisRuleName, StringComparison.OrdinalIgnoreCase))
            {
                existing = rule;
                break;
            }

        if (mode == "status")
        {
            if (existing is null) _log.Info("IIS native Babylon rule absent.");
            else if (IsManaged(existing)) _log.Info("IIS native Babylon rule installed (HTTP 404.19; site-wide query match).");
            else _log.Warn("IIS native rule name exists, but its contents differ; no changes made.");
            return;
        }

        // Never adopt, overwrite or delete a rule whose contents differ from
        // ours, even if an administrator reused the same name.
        if (existing is not null && !IsManaged(existing))
            throw new InvalidOperationException(
                "Native IIS rule name collision: an existing rule has unexpected contents. Manual review required.");

        if (mode == "remove")
        {
            if (existing is null) { _log.Info("IIS native Babylon rule already absent."); return; }
            if (_options.DryRun) { _log.Info("Would remove IIS native Babylon rule."); return; }
            Backup();
            rules.Remove(existing);
            manager.CommitChanges();
            _log.Info("Removed IIS native Babylon rule.");
            return;
        }

        if (mode != "install") throw new ArgumentException("Unsupported native IIS operation: " + mode);
        if (existing is not null) { _log.Info("IIS native Babylon rule already installed; unchanged."); return; }
        if (_options.DryRun)
        {
            _log.Info("Would install built-in IIS Request Filtering rule (HTTP 404.19; no MSI).");
            return;
        }

        Backup();
        var ruleNew = rules.CreateElement("filteringRule");
        ruleNew.SetAttributeValue("name", HostPolicy.NativeIisRuleName);
        ruleNew.SetAttributeValue("scanUrl", false);
        ruleNew.SetAttributeValue("scanQueryString", true);
        ruleNew.SetAttributeValue("scanAllRaw", false);
        ruleNew.SetAttributeValue("denyUnescapedPercent", false);
        var deny = ruleNew.GetCollection("denyStrings");
        var entry = deny.CreateElement("add");
        entry.SetAttributeValue("string", HostPolicy.NativeIisQuerySequence);
        deny.Add(entry);
        rules.Add(ruleNew);
        manager.CommitChanges();
        _log.Info("Installed IIS native Request Filtering rule (HTTP 404.19; no URL Rewrite MSI required).");
        _log.Info("Native rule matches the Babylon query substring across all paths and hostnames of the selected IIS site.");
    }

    private static bool IsManaged(ConfigurationElement e)
    {
        if (!Convert.ToBoolean(e.GetAttributeValue("scanQueryString")) ||
            Convert.ToBoolean(e.GetAttributeValue("scanUrl")) ||
            Convert.ToBoolean(e.GetAttributeValue("scanAllRaw")) ||
            Convert.ToBoolean(e.GetAttributeValue("denyUnescapedPercent")) ||
            e.GetCollection("scanHeaders").Count != 0 ||
            e.GetCollection("appliesTo").Count != 0) return false;
        var list = e.GetCollection("denyStrings");
        if (list.Count != 1) return false;
        foreach (ConfigurationElement item in list)
            return string.Equals(Convert.ToString(item.GetAttributeValue("string")),
                HostPolicy.NativeIisQuerySequence, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static bool Available(ServerManager manager)
    {
        var modules = manager.GetApplicationHostConfiguration()
            .GetSection("system.webServer/globalModules").GetCollection();
        foreach (ConfigurationElement module in modules)
            if (string.Equals(Convert.ToString(module.GetAttributeValue("name")),
                "RequestFilteringModule", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void Backup()
    {
        var executable = Path.Combine(WindowsSystem.WindowsDirectory, "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("IIS appcmd.exe not found.", executable);
        var name = "BeforeBabylonNativeFilter_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var r = WindowsSystem.RunAsync(executable, ["add", "backup", name], 30).GetAwaiter().GetResult();
        if (r.ExitCode != 0) throw new InvalidOperationException("IIS backup failed: " + r.Output);
        _log.Info("IIS backup: " + name);
    }
}
