using System.Diagnostics;

namespace BabylonWebServerFix.Services;

internal sealed record BabylonSnapshot(int Id, int Session, string UserSid, string Exe);
internal sealed record BabylonPlan(BabylonSnapshot[] Processes, string TaskName, object Folder, object RegisteredTask);

/// <summary>
/// Recreate Babylon in the original interactive user token, never as elevated
/// administrator or SYSTEM. Tasks are temporary and removed after launch.
/// </summary>
internal sealed class BabylonRestart(CliOptions options, DeployLog log)
{
    private readonly CliOptions _options = options;
    private readonly DeployLog _log = log;
    private readonly List<BabylonPlan> _plans = [];

    public void Prepare()
    {
        if (_options.SkipBabylonRestart) return;
        var started = GetSnapshots();
        if (started.Length == 0) return;
        if (_options.DryRun)
        {
            _log.Info($"Would restart Babylon for {started.Length} running instance(s).");
            return;
        }
        object scheduler;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service") ??
                       throw new InvalidOperationException("Task Scheduler COM unavailable.");
            scheduler = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Task Scheduler cannot be created.");
            ((dynamic)scheduler).Connect();
        }
        catch (Exception e)
        {
            _log.Warn("Cannot prepare Babylon restart; leaving running instances untouched: " + e.Message);
            return;
        }
        dynamic folder = ((dynamic)scheduler).GetFolder("\\");
        foreach (var group in started.GroupBy(s => s.UserSid, StringComparer.OrdinalIgnoreCase))
        {
            var paths = HostPolicy.DistinctWindowsPaths(group.Select(p => p.Exe));
            var sessions = group.Select(p => p.Session).Distinct().ToArray();
            if (paths.Length != 1 || sessions.Length != 1)
            {
                _log.Warn("Babylon user has multiple paths/sessions; restart skipped to avoid wrong desktop.");
                continue;
            }
            var taskName = "BabylonWebServerFix-" + Guid.NewGuid().ToString("N");
            try
            {
                dynamic definition = ((dynamic)scheduler).NewTask(0);
                definition.RegistrationInfo.Description = "Temporary non-elevated Babylon restart";
                definition.Settings.Enabled = true;
                definition.Settings.AllowDemandStart = true;
                definition.Settings.Hidden = true;
                definition.Settings.DisallowStartIfOnBatteries = false;
                definition.Settings.StopIfGoingOnBatteries = false;
                definition.Settings.ExecutionTimeLimit = "PT0S";
                definition.Principal.UserId = group.Key;
                definition.Principal.LogonType = 3; // Interactive token only
                definition.Principal.RunLevel = 0; // LUA, not elevated
                dynamic action = definition.Actions.Create(0); // Exec
                action.Path = paths[0];
                action.WorkingDirectory = Path.GetDirectoryName(paths[0]);
                object registered = folder.RegisterTaskDefinition(taskName, definition, 6, group.Key, null, 3, null);
                _plans.Add(new BabylonPlan(group.ToArray(), taskName, folder, registered));
                _log.Info($"Prepared Babylon restart for existing user session {sessions[0]}.");
            }
            catch (Exception e)
            {
                _log.Warn("Cannot register Babylon restart task; leaving process running: " + e.Message);
                try { folder.DeleteTask(taskName, 0); } catch { }
            }
        }
    }

    public void StopForCache()
    {
        foreach (var plan in _plans)
        {
            foreach (var snapshot in plan.Processes)
            {
                try
                {
                    using var process = Process.GetProcessById(snapshot.Id);
                    if (process.SessionId != snapshot.Session ||
                        !WindowsSystem.SamePath(process.MainModule?.FileName, snapshot.Exe) ||
                        WindowsSystem.OwnerSid(process.Id) != snapshot.UserSid) continue;
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                        process.WaitForExit(2500);
                    }
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: false);
                        process.WaitForExit(2500);
                    }
                    _log.Info("Stopped Babylon PID=" + snapshot.Id + " before cache cleanup.");
                }
                catch (ArgumentException) { /* Process already exited. */ }
                catch (Exception e) { _log.Warn("Could not stop Babylon safely: " + e.Message); }
            }
        }
    }

    public async Task CompleteAsync()
    {
        foreach (var plan in _plans)
        {
            try
            {
                var representative = plan.Processes[0];
                if (IsRunning(representative))
                {
                    _log.Info("Babylon already running in original session; avoiding duplicate.");
                    continue;
                }
                ((dynamic)plan.RegisteredTask).Run(null);
                bool found = false;
                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(500);
                    if (IsRunning(representative)) { found = true; break; }
                }
                if (found) _log.Info("Babylon restarted in original interactive session.");
                else _log.Warn("Babylon restart requested, but a new process was not confirmed.");
            }
            catch (Exception e) { _log.Warn("Babylon restart error: " + e.Message); }
            finally
            {
                try { ((dynamic)plan.Folder).DeleteTask(plan.TaskName, 0); }
                catch (Exception e) { _log.Warn("Cannot delete temporary Babylon task: " + e.Message); }
            }
        }
        _plans.Clear();
    }

    private static BabylonSnapshot[] GetSnapshots()
    {
        var found = new List<BabylonSnapshot>();
        foreach (var process in Process.GetProcessesByName("Babylon"))
        {
            using (process)
            {
                try
                {
                    var session = process.SessionId;
                    var path = process.MainModule?.FileName;
                    var sid = WindowsSystem.OwnerSid(process.Id);
                    if (session <= 0 || sid is null || path is null || !File.Exists(path)) continue;
                    found.Add(new BabylonSnapshot(process.Id, session, sid, path));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return found.ToArray();
    }

    private static bool IsRunning(BabylonSnapshot original) => GetSnapshots().Any(p =>
        p.Session == original.Session &&
        string.Equals(p.UserSid, original.UserSid, StringComparison.OrdinalIgnoreCase) &&
        WindowsSystem.SamePath(p.Exe, original.Exe));
}
