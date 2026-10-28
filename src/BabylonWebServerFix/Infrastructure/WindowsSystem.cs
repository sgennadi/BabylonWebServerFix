using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace BabylonWebServerFix;

internal static class WindowsSystem
{
    public static string WindowsDirectory => Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
    public static string SystemExecutable(string name) => Path.Combine(WindowsDirectory, "System32", name);

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value == "S-1-5-18" ||
            new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool ServiceInstalled(string name) =>
        Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name) is { } key && Close(key);

    private static bool Close(RegistryKey key) { key.Dispose(); return true; }

    public static bool SamePath(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public static async Task<CommandResult> RunAsync(string file, IEnumerable<string> arguments, int timeoutSeconds = 60, string? workingDirectory = null)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments) p.StartInfo.ArgumentList.Add(argument);
        if (!p.Start()) throw new IOException("Cannot launch " + file);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Command timed out after {timeoutSeconds}s: {file}");
        }
        return new CommandResult(p.ExitCode, (await stdout + "\n" + await stderr).Trim());
    }

    public static int[] ListeningPids(int port)
    {
        var args = new[] { "-ano", "-p", "TCP" };
        var result = RunAsync(SystemExecutable("netstat.exe"), args, 15).GetAwaiter().GetResult();
        if (result.ExitCode != 0) return [];
        var ids = new HashSet<int>();
        foreach (var line in result.Output.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || !fields[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)) continue;
            if (!fields[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
            if (!fields[1].EndsWith(":" + port, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(fields[^1], out var id)) ids.Add(id);
        }
        return ids.ToArray();
    }

    public static string? OwnerSid(int processId)
    {
        var processHandle = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (processHandle == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(processHandle, 0x0008, out var token)) return null; // TOKEN_QUERY
            try
            {
                using var identity = new WindowsIdentity(token);
                return identity.User?.Value;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(processHandle); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr handle, uint access, out IntPtr token);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed record CommandResult(int ExitCode, string Output);
