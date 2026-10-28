using System.Text.RegularExpressions;

namespace BabylonWebServerFix;

/// <summary>Exact compatibility policy migrated from tested PowerShell v3/v10.</summary>
public static class HostPolicy
{
    public const string RuleName = "Babylon12-Localhost-Compatibility";
    public const string HostPattern = @"^localhost(:80)?$";
    public const string QueryPattern = @"^uid=[0-9]+&ver=12000&bld=13&iev=11(&|$)";
    public const string UrlPattern = "^$";
    public const string NativeIisRuleName = "Babylon12-Compatibility-Native";
    // Native Request Filtering cannot apply host/path conditions. Query substring is site-wide.
    public const string NativeIisQuerySequence = "&ver=12000&bld=13&iev=11";
    public static bool NativeIisMatches(string query) =>
        query.Contains(NativeIisQuerySequence, StringComparison.OrdinalIgnoreCase);
    public const string ApacheFileName = "babylon12-localhost-compat.conf";
    public const string BeginMarker = "# BEGIN Babylon12-Localhost-Compatibility";
    public const string EndMarker = "# END Babylon12-Localhost-Compatibility";

    public static bool Matches(string host, string path, string query) =>
        path == "/" &&
        Regex.IsMatch(host, HostPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        Regex.IsMatch(query.TrimStart('?'), QueryPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ApacheSnippet => string.Join("\r\n", new[]
    {
        "# Managed by Babylon-IIS-Apache; do not edit manually",
        "<LocationMatch \"^/$\">",
        "    <If \"req('Host') =~ m#^localhost(:80)?$# && %{QUERY_STRING} =~ m#^uid=[0-9]+&ver=12000&bld=13&iev=11(&|$)#\">",
        "        Require all denied",
        "    </If>",
        "</LocationMatch>",
        ""
    });

    public static string ApacheIncludeBlock(string absoluteSnippetPath) =>
        BeginMarker + "\r\nInclude \"" + absoluteSnippetPath.Replace('\\', '/') + "\"\r\n" + EndMarker + "\r\n";

    // An idempotent install changes nothing when our include and exact snippet
    // already exist. File timestamps do not indicate whether an Apache reload
    // is necessary: do not use them to trigger disruptive restarts.
    public static bool ApacheRuleRequiresUpdate(bool includePresent, bool snippetManaged, bool snippetExact) =>
        !includePresent || !snippetManaged || !snippetExact;

    // Start the selected XAMPP binary directly instead of using unreliable BATs.
    public static string[] XamppStartArguments(string root, string config) =>
        ["-d", root, "-f", config];

    public static string[] DistinctWindowsPaths(IEnumerable<string> paths) =>
        paths.Where(p => !string.IsNullOrWhiteSpace(p))
             .Select(p => p.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\'))
             .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
