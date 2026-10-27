using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BabylonWebServerFix.Services;

internal sealed record ApacheTarget(string Exe, string Root, string Config, string? ServiceName, bool ServiceRunning)
{
    public string Snippet => Path.Combine(Path.GetDirectoryName(Config)!, HostPolicy.ApacheFileName);
    public bool IsXampp => string.Equals(
        Path.Combine(Path.GetDirectoryName(Root)!, "apache", "bin", "httpd.exe"),
        Exe, StringComparison.OrdinalIgnoreCase);
}

internal sealed class ApacheService(CliOptions options, DeployLog log, ApacheTarget target)
{
    private readonly CliOptions _options = options;
    private readonly DeployLog _log = log;
    private readonly ApacheTarget _target = target;

    public static ApacheTarget? Detect(CliOptions options)
    {
        var known = new List<string>
        {
            @"C:\xampp\apache\bin\httpd.exe",
            @"C:\Apache24\bin\httpd.exe",
            @"C:\Apache2\bin\httpd.exe",
            @"C:\Program Files\Apache Software Foundation\Apache2.4\bin\httpd.exe",
            @"C:\Program Files\Apache Software Foundation\Apache24\bin\httpd.exe"
        };
        foreach (var stack in new[] { @"C:\wamp64\bin\apache", @"C:\wamp\bin\apache", @"C:\laragon\bin\apache" })
        {
            if (!Directory.Exists(stack)) continue;
            try { known.AddRange(Directory.EnumerateFiles(stack, "httpd.exe", SearchOption.AllDirectories)); }
            catch (UnauthorizedAccessException) { }
        }
        var running = GetRunningApache();
        var services = DiscoverApacheServices();
        known.AddRange(services.Select(s => s.Exe));
        known.AddRange(running.Select(p => p.Exe));
        if (!string.IsNullOrWhiteSpace(options.ApacheHttpdPath)) known.Add(options.ApacheHttpdPath);
        var installed = HostPolicy.DistinctWindowsPaths(known).Where(File.Exists).ToArray();
        var active = HostPolicy.DistinctWindowsPaths(running.Select(p => p.Exe));

        string? exe;
        if (options.ApacheHttpdPath is not null)
        {
            if (!File.Exists(options.ApacheHttpdPath)) throw new FileNotFoundException("Specified Apache not found", options.ApacheHttpdPath);
            exe = Path.GetFullPath(options.ApacheHttpdPath);
        }
        else if (active.Length == 1) exe = active[0];
        else if (active.Length > 1)
        {
            var listenerPids = WindowsSystem.ListeningPids(80);
            var listeners = HostPolicy.DistinctWindowsPaths(running.Where(p => listenerPids.Contains(p.Id)).Select(p => p.Exe));
            if (listeners.Length != 1)
                throw new InvalidOperationException("More than one different Apache executable is running; pass --apache-httpd PATH.");
            exe = listeners[0];
        }
        else if (installed.Length == 1) exe = installed[0];
        else if (installed.Length == 0) return null;
        else throw new InvalidOperationException("Multiple Apache installations found; pass --apache-httpd PATH.");

        var matching = services.Where(s => WindowsSystem.SamePath(s.Exe, exe)).ToArray();
        if (matching.Length > 1) throw new InvalidOperationException("Multiple Apache services share one executable; select a configuration explicitly.");
        var service = matching.FirstOrDefault();
        var root = Directory.GetParent(Path.GetDirectoryName(exe)!)!.FullName;
        string conf;
        if (options.ApacheConfigPath is not null) conf = options.ApacheConfigPath;
        else if (service?.ConfigFromCommandLine is { Length: > 0 } confArgument)
            conf = Path.IsPathRooted(confArgument) ? confArgument : Path.Combine(root, confArgument);
        else conf = Path.Combine(root, "conf", "httpd.conf");
        if (!File.Exists(conf)) throw new FileNotFoundException("Apache configuration not found; use --apache-config", conf);
        return new ApacheTarget(Path.GetFullPath(exe), root, Path.GetFullPath(conf),
            options.ApacheConfigPath is null ? service?.Name : null,
            service is not null && running.Any(p => WindowsSystem.SamePath(p.Exe, exe)));
    }

    private sealed record RunningApache(int Id, string Exe);
    private sealed record ApacheServiceData(string Name, string Exe, string? ConfigFromCommandLine);

    private static RunningApache[] GetRunningApache()
    {
        var result = new List<RunningApache>();
        foreach (var p in Process.GetProcessesByName("httpd"))
        {
            using (p)
            {
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (path != null && File.Exists(path)) result.Add(new RunningApache(p.Id, path));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return result.ToArray();
    }

    private static ApacheServiceData[] DiscoverApacheServices()
    {
        var result = new List<ApacheServiceData>();
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null) return [];
        foreach (var name in services.GetSubKeyNames())
        {
            using var key = services.OpenSubKey(name);
            string command = key?.GetValue("ImagePath") as string ?? "";
            if (!command.Contains("httpd.exe", StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(command, "(?i)(?:\"(?<quoted>[^\"]*httpd\\.exe)\"|(?<plain>[A-Z]:\\\\.*?httpd\\.exe))");
            var exe = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value;
            if (!File.Exists(exe)) continue;
            var cf = Regex.Match(command, "(?i)(?:^|\\s)-f\\s+(?:\"(?<q>[^\"]+)\"|(?<p>\\S+))");
            string? config = cf.Success ? (cf.Groups["q"].Success ? cf.Groups["q"].Value : cf.Groups["p"].Value) : null;
            result.Add(new ApacheServiceData(name, exe, config));
        }
        return result.ToArray();
    }

    private Task<CommandResult> HttpdAsync(params string[] args)
    {
        var baseArgs = new List<string>();
        if (_target.ServiceName is not null) { baseArgs.Add("-n"); baseArgs.Add(_target.ServiceName); }
        else { baseArgs.Add("-d"); baseArgs.Add(_target.Root); baseArgs.Add("-f"); baseArgs.Add(_target.Config); }
        baseArgs.AddRange(args);
        return WindowsSystem.RunAsync(_target.Exe, baseArgs, 30, _target.Root);
    }

    private async Task TestSyntaxAsync()
    {
        var test = await HttpdAsync("-t");
        if (test.ExitCode != 0) throw new InvalidOperationException("Apache syntax error: " + test.Output);
        _log.Info("Apache syntax OK.");
    }

    public async Task ApplyAsync(string mode)
    {
        _log.Info($"Apache: {_target.Exe}; config={_target.Config}; service={_target.ServiceName ?? "(XAMPP/console)"}");
        var confBytes = await File.ReadAllBytesAsync(_target.Config);
        // Latin-1 preserves every pre-existing configuration byte during edits.
        var conf = Encoding.Latin1.GetString(confBytes);
        bool includePresent = conf.Contains(HostPolicy.BeginMarker, StringComparison.Ordinal) &&
                              conf.Contains(HostPolicy.EndMarker, StringComparison.Ordinal);
        bool snippetExists = File.Exists(_target.Snippet);
        var snippet = snippetExists ? await File.ReadAllTextAsync(_target.Snippet, Encoding.ASCII) : "";
        bool ownSnippet = snippetExists && snippet.StartsWith("# Managed by Babylon-", StringComparison.Ordinal) &&
                          StripFirstLine(snippet) == StripFirstLine(HostPolicy.ApacheSnippet);
        _log.Info($"Apache managed include={includePresent}; managed snippet={ownSnippet}");

        if (mode == "status") return;
        if (mode == "remove" && !includePresent) { _log.Info("Apache rule already absent."); return; }
        if (mode == "install" && includePresent && ownSnippet && snippet == HostPolicy.ApacheSnippet)
        {
            _log.Info("Apache rule already installed; unchanged.");
            if (NeedsRestart() && !_options.SkipApacheRestart)
                await RestartAsync();
            return;
        }
        if (snippetExists && !ownSnippet) throw new InvalidOperationException("Apache snippet exists but is not managed by us: " + _target.Snippet);
        if (mode == "install")
        {
            var version = await WindowsSystem.RunAsync(_target.Exe, ["-v"], 15);
            if (version.ExitCode != 0 || !version.Output.Contains("Apache/2.4.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only Apache 2.4 is supported.");
        }
        await TestSyntaxAsync();
        if (_options.DryRun) { _log.Info($"Would {mode} Apache compatibility rule."); return; }
        var backup = _target.Config + ".BabylonBackup." + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        File.Copy(_target.Config, backup, overwrite: false);
        _log.Info("Apache config backup: " + backup);
        var originalSnippet = snippetExists ? await File.ReadAllBytesAsync(_target.Snippet) : null;
        try
        {
            if (mode == "install")
            {
                await File.WriteAllTextAsync(_target.Snippet, HostPolicy.ApacheSnippet, Encoding.ASCII);
                if (!includePresent)
                {
                    var prefix = conf.EndsWith('\n') ? "" : "\r\n";
                    var updated = conf + prefix + HostPolicy.ApacheIncludeBlock(_target.Snippet);
                    await File.WriteAllBytesAsync(_target.Config, Encoding.Latin1.GetBytes(updated));
                }
            }
            else
            {
                string pattern = @"(?m)^# BEGIN Babylon12-Localhost-Compatibility\r?\n.*?\r?\n# END Babylon12-Localhost-Compatibility\r?\n?";
                var updated = Regex.Replace(conf, pattern, "", RegexOptions.Singleline);
                await File.WriteAllBytesAsync(_target.Config, Encoding.Latin1.GetBytes(updated));
                if (ownSnippet) File.Delete(_target.Snippet);
            }
            await TestSyntaxAsync();
            if (!_options.SkipApacheRestart) await RestartAsync();
            else _log.Info("Apache restart skipped by --skip-apache-restart.");
            _log.Info($"Apache rule {mode} completed.");
        }
        catch
        {
            await File.WriteAllBytesAsync(_target.Config, confBytes);
            if (originalSnippet is null) { if (File.Exists(_target.Snippet)) File.Delete(_target.Snippet); }
            else await File.WriteAllBytesAsync(_target.Snippet, originalSnippet);
            _log.Warn("Apache config restored after failure.");
            try { if (!_options.SkipApacheRestart) await RestartAsync(); } catch { }
            throw;
        }
    }

    private static string StripFirstLine(string input)
    {
        var index = input.IndexOf('\n');
        return index < 0 ? "" : input[(index + 1)..];
    }

    private bool NeedsRestart()
    {
        var active = GetRunningApache().Where(p => WindowsSystem.SamePath(p.Exe, _target.Exe)).ToArray();
        if (active.Length == 0) return false;
        var newestChange = new[] { _target.Config, _target.Snippet }.Where(File.Exists)
            .Max(path => File.GetLastWriteTimeUtc(path));
        foreach (var entry in active)
        {
            try
            {
                using var p = Process.GetProcessById(entry.Id);
                if (p.StartTime.ToUniversalTime() > newestChange) return false;
            }
            catch (Exception) { }
        }
        return true;
    }

    public async Task RestartAsync()
    {
        if (_options.SkipApacheRestart) { _log.Info("Apache restart skipped."); return; }
        var running = GetRunningApache().Where(p => WindowsSystem.SamePath(p.Exe, _target.Exe)).ToArray();
        if (running.Length == 0) { _log.Info("Apache stopped; leaving it stopped."); return; }
        if (_options.DryRun) { _log.Info("Would restart running Apache: " + _target.Exe); return; }
        if (_target.ServiceName is not null)
        {
            var restart = await WindowsSystem.RunAsync(_target.Exe,
                ["-k", "restart", "-n", _target.ServiceName], 45, _target.Root);
            if (restart.ExitCode != 0) throw new InvalidOperationException("Apache service restart failed: " + restart.Output);
            _log.Info("Apache service restarted.");
            return;
        }
        if (!_target.IsXampp) { _log.Warn("Console Apache is not XAMPP; restart it manually."); return; }
        if (Process.GetCurrentProcess().SessionId == 0 ||
            System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value == "S-1-5-18")
        {
            _log.Warn("XAMPP console restart skipped under SYSTEM/Session 0.");
            return;
        }
        var other = GetRunningApache().Where(p => !WindowsSystem.SamePath(p.Exe, _target.Exe)).ToArray();
        if (other.Length > 0) { _log.Warn("Other Apache installations running; XAMPP batch restart skipped."); return; }
        var xampp = Directory.GetParent(_target.Root)!.FullName;
        var stop = Path.Combine(xampp, "apache_stop.bat");
        var start = Path.Combine(xampp, "apache_start.bat");
        if (!File.Exists(stop) || !File.Exists(start)) { _log.Warn("XAMPP batch files missing; restart manually."); return; }
        _log.Info("Restarting XAMPP Apache via apache_stop.bat / apache_start.bat.");
        await RunBatchAsync(stop, wait: true);
        bool stopped = false;
        for (int i = 0; i < 30; i++)
        {
            if (GetRunningApache().All(p => !WindowsSystem.SamePath(p.Exe, _target.Exe))) { stopped = true; break; }
            await Task.Delay(500);
        }
        if (!stopped) { _log.Warn("XAMPP did not stop; not starting a duplicate process."); return; }
        await RunBatchAsync(start, wait: false);
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(500);
            var now = GetRunningApache().Where(p => WindowsSystem.SamePath(p.Exe, _target.Exe)).ToArray();
            if (now.Length > 0 && WindowsSystem.ListeningPids(80).Intersect(now.Select(p => p.Id)).Any())
            { _log.Info("XAMPP Apache restarted and listening on port 80."); return; }
        }
        _log.Warn("Apache restart launched but port 80 listener was not confirmed.");
    }

    private static async Task RunBatchAsync(string file, bool wait)
    {
        var psi = new ProcessStartInfo(WindowsSystem.SystemExecutable("cmd.exe"))
        {
            Arguments = $"/D /S /C \"\"{file}\"\"",
            WorkingDirectory = Path.GetDirectoryName(file)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        using var p = Process.Start(psi) ?? throw new IOException("Cannot run XAMPP batch: " + file);
        if (wait)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { /* Batch files may contain PAUSE; Apache stop is verified separately. */ }
        }
    }
}
