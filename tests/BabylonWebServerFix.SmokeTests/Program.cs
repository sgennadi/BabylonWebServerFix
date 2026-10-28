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

Console.WriteLine($"Passed {count} smoke tests.");
