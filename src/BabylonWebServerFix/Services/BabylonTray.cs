using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BabylonWebServerFix.Services;

/// <summary>
/// Opt-in initialization of Babylon's remembered tray behavior.
/// Only addresses a visible Babylon main window belonging to the caller's
/// interactive user SID and session; never changes Babylon config or launches it.
/// </summary>
internal static class BabylonTray
{
    private const uint WmSysCommand = 0x0112;
    private const uint ScMinimize = 0xF020;
    private const uint SmtoAbortIfHung = 0x0002;

    public static async Task<int> InitializeAsync(CliOptions options, DeployLog log)
    {
        using var caller = Process.GetCurrentProcess();
        if (!Environment.UserInteractive || caller.SessionId <= 0)
        {
            log.Warn("tray-init requires an interactive user session (not SYSTEM or Session 0).");
            return 3;
        }

        using var identity = WindowsIdentity.GetCurrent();
        string? sid = identity.User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
        {
            log.Warn("Cannot determine the interactive user's SID; no action taken.");
            return 3;
        }

        log.Info($"Looking for Babylon.exe in current user session {caller.SessionId}; wait={options.TrayWaitSeconds}s.");
        var elapsed = Stopwatch.StartNew();
        bool sawBabylon = false;

        while (true)
        {
            foreach (var process in Process.GetProcessesByName("Babylon"))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != caller.SessionId ||
                            !string.Equals(WindowsSystem.OwnerSid(process.Id), sid, StringComparison.OrdinalIgnoreCase))
                            continue;

                        sawBabylon = true;
                        process.Refresh();
                        IntPtr hwnd = process.MainWindowHandle;
                        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
                            continue;

                        _ = GetWindowThreadProcessId(hwnd, out uint windowPid);
                        if (windowPid != (uint)process.Id)
                            continue;

                        if (options.DryRun)
                        {
                            log.Info($"Would send SC_MINIMIZE to Babylon PID={process.Id}; dry-run, no changes.");
                            return 0;
                        }

                        // The tested PowerShell workaround sends the same native
                        // WM_SYSCOMMAND/SC_MINIMIZE message, not ShowWindow(SW_MINIMIZE).
                        IntPtr sent = SendMessageTimeoutW(hwnd, WmSysCommand,
                            new UIntPtr(ScMinimize), IntPtr.Zero, SmtoAbortIfHung, 2500, out _);
                        if (sent == IntPtr.Zero)
                        {
                            log.Warn($"Babylon minimize message failed for PID={process.Id} (Win32 {Marshal.GetLastWin32Error()}).");
                            return 1;
                        }
                        log.Info($"Sent SC_MINIMIZE to Babylon PID={process.Id} in user's interactive desktop.");
                        log.Info("Verify the tray icon once; Babylon may remember this for subsequent -AutoStart launches.");
                        return 0;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or
                        System.ComponentModel.Win32Exception or ArgumentException)
                    {
                        log.Warn("Skipped an inaccessible or exiting Babylon process: " + ex.Message);
                    }
                }
            }


            if (elapsed.Elapsed >= TimeSpan.FromSeconds(options.TrayWaitSeconds))
                break;

            await Task.Delay(500);
        }

        if (sawBabylon)
        {
            log.Info("Babylon is running without an accessible visible window. No action taken.");
            return 0;
        }
        log.Warn("Babylon.exe was not running in the current user's session. Start Babylon through its existing startup entry and retry.");
        return 3;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr window, uint message, UIntPtr wParam, IntPtr lParam,
        uint flags, uint timeoutMilliseconds, out IntPtr result);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
