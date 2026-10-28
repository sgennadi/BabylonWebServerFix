namespace BabylonWebServerFix;

/// <summary>Read-only diagnostic. The old 2023 LocalUI files are not proof that localhost is unused.</summary>
public static class BabylonUiDetection
{
    public static string Classify(bool localHtmlExists, bool runtimeLocalFileConfirmed) =>
        runtimeLocalFileConfirmed ? "LOCAL_FILE_OBSERVED" :
        localHtmlExists ? "LOCAL_HTML_PRESENT_UNVERIFIED" : "NOT_DETECTED";

    public static void WriteStatus(DeployLog log)
    {
        var localHtml = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Babylon", "LocalUI", "wnd.html");
        var exists = File.Exists(localHtml);
        log.Info($"Babylon UI: {Classify(exists, false)}; LocalUI\\wnd.html={(exists ? "present" : "absent")}.");
        if (exists)
            log.Info("Babylon UI: local HTML is not evidence that HTTP localhost is unused; automatic fix detection remains unverified.");
    }
}
