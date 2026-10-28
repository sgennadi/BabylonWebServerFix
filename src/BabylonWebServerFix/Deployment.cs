using BabylonWebServerFix.Services;

namespace BabylonWebServerFix;

internal sealed class Deployment(CliOptions options, DeployLog log, ApacheTarget? apacheTarget, bool iisPresent)
{
    private readonly CliOptions _options = options;
    private readonly DeployLog _log = log;
    private readonly ApacheTarget? _apacheTarget = apacheTarget;
    private readonly bool _iisPresent = iisPresent;

    public async Task<int> ExecuteAsync()
    {
        BabylonUiDetection.WriteStatus(_log);
        if (_options.Action == "install" && _options.FixedBabylon)
        {
            _log.Info("Confirmed fixed Babylon (--fixed-babylon): skipping IIS, Apache, cache and restart changes.");
            _log.Info("DONE.");
            return 0;
        }
        var cache = new WinInetCache(_options, _log);
        var iis = new IisService(_options, _log);
        var apache = _apacheTarget is null ? null : new ApacheService(_options, _log, _apacheTarget);
        switch (_options.Action)
        {
            case "status":
                if (_iisPresent) await iis.ApplyAsync("status");
                if (apache is not null) await apache.ApplyAsync("status");
                cache.Status();
                break;
            case "restart-apache":
                if (apache is not null) await apache.RestartAsync();
                else _log.Info("Apache not found; nothing to restart.");
                break;
            case "remove-iis":
                if (_iisPresent) await iis.ApplyAsync("remove");
                else _log.Info("IIS is absent; no managed IIS rules to remove.");
                break;
            case "remove":
                if (_iisPresent) await iis.ApplyAsync("remove");
                if (apache is not null) await apache.ApplyAsync("remove");
                cache.RemoveNextLogonRegistration();
                break;
            case "cache-only":
                await ClearWithBabylonAsync(cache);
                break;
            case "install":
                // Server config changes are applied before clearing cache.
                if (_iisPresent) await iis.ApplyAsync("install");
                if (apache is not null) await apache.ApplyAsync("install");
                if (!_options.SkipCache) await ClearWithBabylonAsync(cache);
                else
                {
                    _log.Info("Cache cleanup skipped by --skip-cache.");
                    await RestartBabylonOnlyAsync();
                }
                break;
        }
        _log.Info("DONE.");
        return 0;
    }

    // Even if cache cleanup was opted out, complete an optional Babylon
    // restart after web-server changes (preserves compatibility while skipping cache).
    private async Task RestartBabylonOnlyAsync()
    {
        var babylon = new BabylonRestart(_options, _log);
        babylon.Prepare();
        try
        {
            if (!_options.DryRun) babylon.StopForCache();
        }
        finally
        {
            await babylon.CompleteAsync();
        }
    }

    private async Task ClearWithBabylonAsync(WinInetCache cache)
    {
        var babylon = new BabylonRestart(_options, _log);
        babylon.Prepare();
        try
        {
            if (!_options.DryRun) babylon.StopForCache();
            await cache.ClearAsync();
        }
        finally
        {
            await babylon.CompleteAsync();
        }
    }
}
