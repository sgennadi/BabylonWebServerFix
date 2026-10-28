using BabylonWebServerFix.Services;

namespace BabylonWebServerFix;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CliOptions options;
        try { options = CliOptions.Parse(args); }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine("Argument error: " + e.Message);
            Console.Error.WriteLine(CliOptions.Usage);
            return 2;
        }
        if (options.Help)
        {
            Console.WriteLine(CliOptions.Usage);
            return 0;
        }
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            Console.Error.WriteLine("Windows x64 is required.");
            return 2;
        }
        if (options.Action != "status" && !WindowsSystem.IsAdministrator())
        {
            Console.Error.WriteLine("Run from an elevated Command Prompt / PowerShell (Administrator).");
            return 2;
        }
        try
        {
            // Explicit fixed-Babylon confirmation must be a no-op even without a web server.
            if (options.Action == "install" && options.FixedBabylon)
            {
                using var fixedLog = new DeployLog(options);
                BabylonUiDetection.WriteStatus(fixedLog);
                fixedLog.Info("Confirmed fixed Babylon (--fixed-babylon): no changes made.");
                return 0;
            }
            // Do not create a log file if neither web server is installed.
            // IIS-only cleanup must not inspect or depend on Apache installations.
            var apache = options.Action == "remove-iis" ? null : ApacheService.Detect(options);
            var iisFound = IisService.IsInstalled;
            if (!iisFound && apache is null)
            {
                Console.WriteLine("Neither IIS nor Apache was found. No changes made, including cache.");
                return 0;
            }
            using var log = new DeployLog(options);
            log.Info($"BEGIN: Action={options.Action}; Computer={Environment.MachineName}; DryRun={options.DryRun}");
            var application = new Deployment(options, log, apache, iisFound);
            return await application.ExecuteAsync();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("ERROR: " + e.Message);
            return 1;
        }
    }
}
