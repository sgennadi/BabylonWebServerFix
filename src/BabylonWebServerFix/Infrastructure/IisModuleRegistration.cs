using System.Text.RegularExpressions;

namespace BabylonWebServerFix;

/// <summary>
/// Parses read-only "appcmd list config /section:system.webServer/globalModules"
/// output to cross-check whether the IIS URL Rewrite module is registered.
/// This is a second observation, not a substitute for testing the module in IIS.
/// </summary>
public static class IisModuleRegistration
{
    private static readonly Regex RewriteModuleEntry = new(
        @"^<add\b[^>]*\bname\s*=\s*[""']RewriteModule[""'](?:\s|/|>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    public static bool ContainsRewriteModule(string? appcmdOutput)
    {
        if (string.IsNullOrWhiteSpace(appcmdOutput)) return false;
        foreach (var line in appcmdOutput.Split('\n'))
        {
            // Only parse an actual <add> entry; not comments, <remove>,
            // similarly named third-party modules or prose containing text.
            if (RewriteModuleEntry.IsMatch(line.Trim()))
                return true;
        }
        return false;
    }
}
