namespace BabylonWebServerFix;

/// <summary>
/// Chooses a trusted source for the optional IIS URL Rewrite installer.
/// Offline packages and explicit paths are preferred; a missing/renamed
/// Microsoft download is not an excuse to fetch an untrusted third-party MSI.
/// </summary>
public static class RewriteInstallerPolicy
{
    public const string LocalFileName = "rewrite_amd64_en-US.msi";
    public static readonly Uri DefaultDownloadUrl = new(
        "https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi");

    public static RewriteInstallerSource Select(
        string? requestedFile,
        Uri? requestedUrl,
        string executableDirectory,
        Func<string, bool>? fileExists = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedFile) && requestedUrl is not null)
            throw new ArgumentException("--msi and --msi-url cannot be combined.");

        if (!string.IsNullOrWhiteSpace(requestedFile))
            return new RewriteInstallerSource(Path.GetFullPath(requestedFile), null);

        // A newer official download address explicitly supplied by an
        // administrator wins over an older local installer beside the EXE.
        if (requestedUrl is not null)
            return new RewriteInstallerSource(null, ValidateMicrosoftDownloadUrl(requestedUrl));

        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        var local = Path.GetFullPath(Path.Combine(executableDirectory, LocalFileName));
        if ((fileExists ?? File.Exists)(local))
            return new RewriteInstallerSource(local, null);

        return new RewriteInstallerSource(null, DefaultDownloadUrl);
    }

    public static Uri ParseMicrosoftDownloadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new ArgumentException("--msi-url must be a valid absolute HTTPS URL to a Microsoft MSI.");
        return ValidateMicrosoftDownloadUrl(uri);
    }

    public static Uri ValidateMicrosoftDownloadUrl(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = uri.DnsSafeHost;
        bool microsoftHost =
            host.Equals("download.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("aka.ms", StringComparison.OrdinalIgnoreCase);

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            !microsoftHost || !uri.AbsolutePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--msi-url requires a direct HTTPS .msi link on an official Microsoft download host.");
        return uri;
    }
}

public sealed record RewriteInstallerSource(string? LocalPath, Uri? DownloadUrl);
