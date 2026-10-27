using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BabylonWebServerFix.Services;

internal sealed class WinInetCache(CliOptions options, DeployLog log)
{
    private readonly CliOptions _options = options;
    private readonly DeployLog _log = log;
    private const string ActiveSetupId = "{DBFA967A-B72E-416D-A674-1E16C25B4D32}";
    private const string ActiveSetupPath = @"SOFTWARE\Microsoft\Active Setup\Installed Components\" + ActiveSetupId;
    private const string ProfileList = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    private static readonly string[] Paths =
    [
        @"AppData\Local\Microsoft\Windows\INetCache\IE",
        @"AppData\Local\Microsoft\Windows\INetCache\Low\IE",
        @"AppData\Local\Microsoft\Windows\INetCache\Content.IE5",
        @"AppData\Local\Microsoft\Windows\INetCache\Low\Content.IE5",
        @"AppData\Local\Microsoft\Windows\Temporary Internet Files\Content.IE5"
    ];

    public void Status()
    {
        using var active = Registry.LocalMachine.OpenSubKey(ActiveSetupPath);
        _log.Info(active is null ? "Next-logon cleanup: absent (default)." :
            "Next-logon cleanup: registered; version=" + Convert.ToString(active.GetValue("Version")));
    }

    public async Task ClearAsync()
    {
        if (_options.DryRun)
        {
            var userCount = EnumerateProfiles().Length;
            _log.Info($"Would clear WinINet temporary cache in up to {userCount} profiles (no cookies/passwords). ");
            _log.Info(_options.CleanupNextLogon ? "Would enable next-logon cleanup." : "Would remove old next-logon registration, if any.");
            return;
        }
        UpdateNextLogonRegistration();
        await ClearCurrentUserIndexAsync();
        var profiles = EnumerateProfiles();
        int removed = 0, failures = 0, visited = 0;
        foreach (var profile in profiles)
        {
            if (!Directory.Exists(profile)) continue;
            visited++;
            int profileRemoved = 0, profileFailed = 0;
            foreach (var relative in Paths)
            {
                var dir = Path.Combine(profile, relative);
                if (!Directory.Exists(dir)) continue;
                var stats = CleanDirectory(dir);
                profileRemoved += stats.Removed;
                profileFailed += stats.Failed;
            }
            removed += profileRemoved;
            failures += profileFailed;
            _log.Info($"Profile {profile}; removed={profileRemoved}; failures={profileFailed}");
        }
        _log.Info($"CACHE SUMMARY: profiles={visited}; files_removed={removed}; failures={failures}");
        if (failures > 0) _log.Warn("Some files were locked. Opt in to --cleanup-next-logon if necessary.");
    }

    private string[] EnumerateProfiles()
    {
        using var profiles = Registry.LocalMachine.OpenSubKey(ProfileList);
        if (profiles is null) return [];
        var root = WindowsSystem.WindowsDirectory.TrimEnd('\\') + "\\ServiceProfiles\\";
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in profiles.GetSubKeyNames())
        {
            // Domain and local interactive users. Excludes SYSTEM, LocalService and
            // other built-in/service SIDs. Additional filesystem path checks follow.
            if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) continue;
            using var profile = profiles.OpenSubKey(sid);
            var raw = profile?.GetValue("ProfileImagePath") as string;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var path = Environment.ExpandEnvironmentVariables(raw);
            if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)) continue;
            var full = Path.GetFullPath(path);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (full.Equals(Path.Combine(WindowsSystem.WindowsDirectory, "ServiceProfiles"), StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileName(full).Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(full).Equals("Public", StringComparison.OrdinalIgnoreCase)) continue;
            if (new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            paths.Add(full);
        }
        return paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private (int Removed, int Failed) CleanDirectory(string directory)
    {
        int removed = 0, failed = 0, warned = 0;
        var stack = new Stack<string>();
        stack.Push(directory);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            try
            {
                var directoryInfo = new DirectoryInfo(current);
                if (directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach (var child in directoryInfo.EnumerateFileSystemInfos())
                {
                    try
                    {
                        if (child.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (child is DirectoryInfo childDirectory) stack.Push(childDirectory.FullName);
                        else { File.Delete(child.FullName); removed++; }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        failed++;
                        if (++warned <= 2) _log.Warn("Cache file locked/denied: " + child.FullName);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed++;
                if (++warned <= 2) _log.Warn("Cache directory inaccessible: " + current);
            }
        }
        return (removed, failed);
    }

    private async Task ClearCurrentUserIndexAsync()
    {
        try
        {
            var result = await WindowsSystem.RunAsync(WindowsSystem.SystemExecutable("rundll32.exe"),
                ["InetCpl.cpl,ClearMyTracksByProcess", "8"], 50);
            _log.Info("Native WinINet cleanup (current admin token) completed; exit=" + result.ExitCode);
        }
        catch (Exception e) { _log.Warn("Native cleanup not completed: " + e.Message); }
    }

    private void UpdateNextLogonRegistration()
    {
        if (_options.CleanupNextLogon)
        {
            using var key = Registry.LocalMachine.CreateSubKey(ActiveSetupPath, writable: true)
                ?? throw new IOException("Active Setup registry write failed.");
            key.SetValue("Version", _options.CacheVersion, RegistryValueKind.String);
            key.SetValue("StubPath", "rundll32.exe InetCpl.cpl,ClearMyTracksByProcess 8", RegistryValueKind.String);
            key.SetValue("IsInstalled", 1, RegistryValueKind.DWord);
            _log.Info("Registered optional one-time per-user cache cleanup at next logon.");
        }
        else
        {
            using var prior = Registry.LocalMachine.OpenSubKey(ActiveSetupPath);
            if (prior is null) { _log.Info("Next-logon cleanup disabled (default)."); return; }
            // Remove registration created by earlier PowerShell versions too.
            Registry.LocalMachine.DeleteSubKeyTree(ActiveSetupPath, throwOnMissingSubKey: false);
            _log.Info("Removed previous next-logon registration.");
        }
    }

    public void RemoveNextLogonRegistration()
    {
        if (_options.DryRun) { _log.Info("Would remove managed next-logon registration."); return; }
        Registry.LocalMachine.DeleteSubKeyTree(ActiveSetupPath, throwOnMissingSubKey: false);
        _log.Info("Managed next-logon registration removed.");
    }
}
