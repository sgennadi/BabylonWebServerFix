namespace BabylonWebServerFix;

public sealed class DeployLog : IDisposable
{
    private readonly StreamWriter? _file;
    public DeployLog(CliOptions o)
    {
        // Dry-run never creates files, even when logging is requested.
        if (!o.EnableLog || o.DryRun) return;
        var path = Path.GetFullPath(o.LogPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Babylon-IIS-Apache", "Deploy.log"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        Info("Log enabled: " + path);
    }
    private void Write(string level, string message)
    {
        Console.WriteLine(level == "INFO" ? message : level + ": " + message);
        try { _file?.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] [{level}] {message}"); }
        catch (IOException e) { Console.Error.WriteLine("Optional log write failed: " + e.Message); }
    }
    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Dispose() => _file?.Dispose();
}
