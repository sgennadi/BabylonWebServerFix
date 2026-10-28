using BabylonWebServerFix;

int count = 0;
void Expect(bool condition, string name)
{
    count++;
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("OK: " + name);
}

const string exact = "uid=0&ver=12000&bld=13&iev=11&lngs=14,0,15,3,1,7";
Expect(HostPolicy.Matches("localhost", "/", exact), "actual Babylon request");
Expect(HostPolicy.Matches("LOCALHOST:80", "/", exact), "case insensitive and explicit 80");
Expect(!HostPolicy.Matches("localhost", "/", ""), "ordinary localhost homepage unaffected");
Expect(!HostPolicy.Matches("localhost", "/page", exact), "non-root path unaffected");
Expect(!HostPolicy.Matches("localhost", "/", "uid=0&ver=13000&bld=13&iev=11"), "version scoped to Babylon 12");
Expect(!HostPolicy.Matches("127.0.0.1", "/", exact), "IP-literal host unaffected");
Expect(!HostPolicy.Matches("localhost.evil.com", "/", exact), "host cannot be suffix-matched");
Expect(!HostPolicy.Matches("localhost", "/", "uid=0&ver=12000&bld=13&iev=12"), "IE version scope");
Expect(HostPolicy.ApacheSnippet.Contains("Require all denied", StringComparison.Ordinal), "Apache 403 disclaimer matches rule");
Expect(HostPolicy.ApacheSnippet.Contains(HostPolicy.QueryPattern, StringComparison.Ordinal), "Apache and IIS query patterns same");
Expect(HostPolicy.ApacheIncludeBlock(@"C:\xampp\apache\conf\babylon12-localhost-compat.conf").Contains("C:/xampp/apache/conf/", StringComparison.Ordinal), "Apache Include slash normalization");
Expect(HostPolicy.DistinctWindowsPaths([@"c:\xampp\apache\bin\httpd.exe", @"C:\XAMPP\Apache\Bin\httpd.exe"]).Length == 1,
    "XAMPP process case-insensitive deduplication");

var xamppArgs = HostPolicy.XamppStartArguments(@"C:\xampp\apache", @"C:\xampp\apache\conf\httpd.conf");
Expect(xamppArgs.Length == 4 && xamppArgs[0] == "-d" && xamppArgs[2] == "-f", "XAMPP direct launch includes root and config switches");
Expect(xamppArgs[1] == @"C:\xampp\apache" && xamppArgs[3].EndsWith(@"conf\httpd.conf", StringComparison.OrdinalIgnoreCase),
    "XAMPP restart uses the same explicit configuration that was validated");

Expect(!HostPolicy.ApacheRuleRequiresUpdate(true, true, true),
    "repeated Apache install is idempotent; no restart needed");
Expect(HostPolicy.ApacheRuleRequiresUpdate(false, true, true),
    "Apache include missing; configuration update needed");
Expect(HostPolicy.ApacheRuleRequiresUpdate(true, false, true),
    "Apache snippet unmanaged; configuration update needed");
Expect(HostPolicy.ApacheRuleRequiresUpdate(true, true, false),
    "Apache snippet changed; configuration update needed");

var defaults = CliOptions.Parse(["install"]);
Expect(!defaults.EnableLog && !defaults.CleanupNextLogon, "opt-in switches disabled by default");
Expect(defaults.Site == "Default Web Site", "default IIS site");
Expect(CliOptions.Parse(["install", "--enable-log", "--cleanup-next-logon"]).CleanupNextLogon, "explicit next-logon toggle");
Expect(CliOptions.Parse(["restart-apache", "--skip-apache-restart"]).SkipApacheRestart, "explicit restart toggle");
Expect(CliOptions.Parse(["status", "--dry-run"]).DryRun, "dry run option");

bool badOptionRejected = false;
try { _ = CliOptions.Parse(["install", "--log-path", "C:\\Temp\\x.log"]); }
catch (ArgumentException) { badOptionRejected = true; }
Expect(badOptionRejected, "log path requires opt-in logging");

bool incompatibleRejected = false;
try { _ = CliOptions.Parse(["install", "--skip-cache", "--cleanup-next-logon"]); }
catch (ArgumentException) { incompatibleRejected = true; }
Expect(incompatibleRejected, "conflicting cache flags rejected");

Expect(AuthenticodeVerifier.IsMicrosoftPublisherName(
        "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, C=US"),
    "Microsoft publisher organization accepted");
Expect(AuthenticodeVerifier.IsMicrosoftPublisherName(
        "CN=Code Signing, O=Microsoft Corporation, C=US"),
    "Microsoft publisher organization accepted without matching CN");
Expect(!AuthenticodeVerifier.IsMicrosoftPublisherName(
        "CN=Microsoft Corporation Fake, O=Untrusted Corp"),
    "lookalike publisher rejected");
Expect(!AuthenticodeVerifier.IsMicrosoftPublisherName(
        "CN=Fake, O=Not Microsoft Corporation"),
    "publisher substring not accepted");
Expect(!AuthenticodeVerifier.IsMicrosoftPublisherName(
        "CN=Microsoft Corporation, O=Untrusted Corp"),
    "spoofed CN without Microsoft organization rejected");
if (OperatingSystem.IsWindows())
{
    var unsignedPackage = Path.Combine(Path.GetTempPath(), "BWSF-unsigned-" + Guid.NewGuid().ToString("N") + ".msi");
    try
    {
        File.WriteAllText(unsignedPackage, "unsigned test payload");
        bool rejected = false;
        try { AuthenticodeVerifier.RequireMicrosoftSignature(unsignedPackage); }
        catch (System.Security.SecurityException) { rejected = true; }
        Expect(rejected, "unsigned MSI rejected by native WinTrust (no PowerShell)");
    }
    finally { if (File.Exists(unsignedPackage)) File.Delete(unsignedPackage); }
}

const string msiFile = RewriteInstallerPolicy.LocalFileName;
var offline = RewriteInstallerPolicy.Select(null, null, @"C:\Babylon", p =>
    p.EndsWith(msiFile, StringComparison.OrdinalIgnoreCase));
Expect(offline.LocalPath is not null && offline.DownloadUrl is null,
    "offline signed IIS installer beside EXE takes priority over Internet");
var fromNetwork = RewriteInstallerPolicy.Select(
    @"\\edu-dc02\Installation\Babylon\rewrite_amd64_en-US.msi", null, @"C:\Babylon",
    _ => false);
Expect(fromNetwork.LocalPath is not null && fromNetwork.DownloadUrl is null,
    "explicit UNC MSI source supported without local download");
var fallback = RewriteInstallerPolicy.Select(null, null, @"C:\Babylon", _ => false);
Expect(fallback.LocalPath is null && fallback.DownloadUrl == RewriteInstallerPolicy.DefaultDownloadUrl,
    "official download used only when no offline MSI exists");
var alternative = RewriteInstallerPolicy.ParseMicrosoftDownloadUrl(
    "https://download.microsoft.com/download/new/rewrite_amd64_en-US.msi");
var selectedUrl = RewriteInstallerPolicy.Select(null, alternative, @"C:\Babylon", _ => true);
Expect(selectedUrl.LocalPath is null && selectedUrl.DownloadUrl == alternative,
    "explicit new Microsoft URL overrides an older adjacent MSI");
var cliUrl = CliOptions.Parse(["install", "--msi-url", alternative.AbsoluteUri]);
Expect(cliUrl.MsiUrl == alternative, "new URL override parses successfully");
bool rejectedInsecure = false;
try { CliOptions.Parse(["install", "--msi-url", "http://download.microsoft.com/rewrite.msi"]); }
catch (ArgumentException) { rejectedInsecure = true; }
Expect(rejectedInsecure, "plain HTTP override rejected");
bool rejectedForeign = false;
try { CliOptions.Parse(["install", "--msi-url", "https://download.microsoft.com.evil.test/rewrite.msi"]); }
catch (ArgumentException) { rejectedForeign = true; }
Expect(rejectedForeign, "lookalike non-Microsoft download host rejected");
bool rejectedHtml = false;
try { CliOptions.Parse(["install", "--msi-url", "https://www.microsoft.com/en-us/download/details.aspx?id=1"]); }
catch (ArgumentException) { rejectedHtml = true; }
Expect(rejectedHtml, "download URL must point directly to MSI");
bool rejectedConflict = false;
try { CliOptions.Parse(["install", "--msi", @"C:\rewrite.msi", "--msi-url", alternative.AbsoluteUri]); }
catch (ArgumentException) { rejectedConflict = true; }
Expect(rejectedConflict, "offline MSI and URL source conflict rejected");

Expect(CliOptions.Parse([]).IisMode == "native", "default IIS mode does not require URL Rewrite MSI");
Expect(CliOptions.Parse(["install"]).IisMode == "native", "default install uses native filtering");
Expect(CliOptions.Parse(["status"]).IisMode == "native", "status defaults to native mode");
Expect(CliOptions.Parse(["install", "--iis-mode", "rewrite"]).IisMode == "rewrite",
    "explicit URL Rewrite remains selectable");
Expect(CliOptions.Parse(["install", "--msi", @"C:\Installers\rewrite_amd64_en-US.msi"]).IisMode == "rewrite",
    "explicit offline MSI automatically selects URL Rewrite");
Expect(CliOptions.Parse(["install", "--msi-url",
    "https://download.microsoft.com/download/new/rewrite_amd64_en-US.msi"]).IisMode == "rewrite",
    "explicit Microsoft MSI URL selects URL Rewrite");
bool rejectedNativeMsi = false;
try { CliOptions.Parse(["install", "--iis-mode", "native", "--msi", @"C:\Installers\rewrite.msi"]); }
catch (ArgumentException) { rejectedNativeMsi = true; }
Expect(rejectedNativeMsi, "explicit native and MSI path cannot conflict");
bool rejectedNativeUrl = false;
try { CliOptions.Parse(["install", "--msi-url",
    "https://download.microsoft.com/download/new/rewrite.msi", "--iis-mode", "native"]); }
catch (ArgumentException) { rejectedNativeUrl = true; }
Expect(rejectedNativeUrl, "explicit native mode rejects conflicting URL installer");
Expect(CliOptions.Parse(["install", "--iis-mode", "rewrite", "--msi",
    @"C:\Installers\rewrite.msi"]).IisMode == "rewrite",
    "explicit rewrite and MSI file are compatible");
Expect(CliOptions.Parse(["install", "--iis-mode", "native"]).IisMode == "native",
    "explicit IIS native mode parsed");
Expect(CliOptions.Parse(["install", "--iis-mode", "NATIVE"]).IisMode == "native",
    "IIS native option is case-insensitive");
Expect(CliOptions.Parse(["remove-iis"]).Action == "remove-iis",
    "remove-iis CLI action parsed");
bool rejectedMode = false;
try { CliOptions.Parse(["install", "--iis-mode", "unknown"]); }
catch (ArgumentException) { rejectedMode = true; }
Expect(rejectedMode, "unsupported IIS mode rejected");
Expect(HostPolicy.NativeIisMatches(exact), "native IIS query matches Babylon 12");
Expect(HostPolicy.NativeIisMatches("uid=314&ver=12000&bld=13&iev=11"),
    "native query accepts dynamic UID");
Expect(!HostPolicy.NativeIisMatches(""), "normal homepage unaffected");
Expect(!HostPolicy.NativeIisMatches("uid=0&ver=13000&bld=13&iev=11"),
    "different Babylon version unaffected");
Expect(HostPolicy.NativeIisRuleName != HostPolicy.RuleName,
    "native IIS and URL Rewrite rule identifiers differ");

const string appcmdSample = """
    <system.webServer>
        <globalModules>
            <add name="StaticFileModule" image="%windir%\system32\inetsrv\static.dll" />
            <add name="RewriteModule" image="%SystemRoot%\system32\inetsrv\rewrite.dll" />
        </globalModules>
    </system.webServer>
    """;
Expect(IisModuleRegistration.ContainsRewriteModule(appcmdSample),
    "appcmd globalModules detects an installed RewriteModule");
Expect(IisModuleRegistration.ContainsRewriteModule(
    "    <add image=\"rewrite.dll\" name=\"rewritemodule\" />"),
    "appcmd accepts case-insensitive module names and varying attribute order");
Expect(!IisModuleRegistration.ContainsRewriteModule(
    "    <add name=\"RewriteModuleExtension\" image=\"other.dll\" />"),
    "appcmd rejects similarly named modules");
Expect(!IisModuleRegistration.ContainsRewriteModule(
    "    <remove name=\"RewriteModule\" />"),
    "appcmd does not count module-removal entries");
Expect(!IisModuleRegistration.ContainsRewriteModule(
    "    <!-- <add name=\"RewriteModule\" image=\"rewrite.dll\" /> -->"),
    "appcmd does not accept commented-out module entries");
Expect(!IisModuleRegistration.ContainsRewriteModule(
    "ERROR ( message:Configuration section could not be read. )"),
    "appcmd does not infer module registration from an error");
Expect(!IisModuleRegistration.ContainsRewriteModule(""),
    "empty IIS module output is not evidence of registration");

Console.WriteLine($"Passed {count} smoke tests.");
