using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace BabylonWebServerFix;

/// <summary>
/// Verifies an embedded Authenticode signature and the primary signer's identity.
/// Uses the native Windows Software Publisher Trust Provider; does not invoke
/// PowerShell or require scripts to be enabled.
/// </summary>
public static class AuthenticodeVerifier
{
    private static readonly Guid AuthenticodeAction =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static void RequireMicrosoftSignature(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Authenticode requires Windows.");

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Signed package not found.", fullPath);

        IntPtr pathPtr = IntPtr.Zero;
        IntPtr filePtr = IntPtr.Zero;
        bool trustInvoked = false;
        var action = AuthenticodeAction;
        var trust = new WinTrustData
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
            dwUIChoice = 2,                   // WTD_UI_NONE
            fdwRevocationChecks = 1,          // WTD_REVOKE_WHOLECHAIN
            dwUnionChoice = 1,                // WTD_CHOICE_FILE
            dwStateAction = 1,                // WTD_STATEACTION_VERIFY
            dwProvFlags = 0x00000080         // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
        };

        try
        {
            pathPtr = Marshal.StringToHGlobalUni(fullPath);
            var file = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = pathPtr
            };
            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(file, filePtr, fDeleteOld: false);
            trust.pFile = filePtr;

            trustInvoked = true;
            int result = WinVerifyTrust(IntPtr.Zero, ref action, ref trust);
            if (result != 0)
                throw new SecurityException(
                    $"MSI Authenticode trust verification failed (0x{unchecked((uint)result):X8}).");
            if (trust.hWVTStateData == IntPtr.Zero)
                throw new SecurityException("WinTrust returned no signer state.");

            IntPtr provider = WTHelperProvDataFromStateData(trust.hWVTStateData);
            IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero :
                WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            IntPtr providerCert = signer == IntPtr.Zero ? IntPtr.Zero :
                WTHelperGetProvCertFromChain(signer, 0);
            if (providerCert == IntPtr.Zero)
                throw new SecurityException("WinTrust did not return a publisher certificate.");

            // The first fields of CRYPT_PROVIDER_CERT are cbStruct and pCert.
            IntPtr certContextPtr = Marshal.PtrToStructure<CryptProviderCertPrefix>(providerCert).pCert;
            if (certContextPtr == IntPtr.Zero)
                throw new SecurityException("WinTrust publisher certificate context is missing.");

            var context = Marshal.PtrToStructure<CertContext>(certContextPtr);
            if (context.pbCertEncoded == IntPtr.Zero || context.cbCertEncoded == 0 ||
                context.cbCertEncoded > 1024 * 1024)
                throw new SecurityException("WinTrust publisher certificate is invalid.");

            var der = new byte[checked((int)context.cbCertEncoded)];
            Marshal.Copy(context.pbCertEncoded, der, 0, der.Length);
            using var cert = X509CertificateLoader.LoadCertificate(der);
            if (!IsMicrosoftPublisherName(cert.Subject))
                throw new SecurityException("MSI signature is trusted but the signer is not Microsoft Corporation.");
        }
        finally
        {
            try
            {
                if (trustInvoked)
                {
                    trust.dwStateAction = 2; // WTD_STATEACTION_CLOSE: free provider state
                    _ = WinVerifyTrust(IntPtr.Zero, ref action, ref trust);
                }
            }
            finally
            {
                if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
                if (pathPtr != IntPtr.Zero) Marshal.FreeHGlobal(pathPtr);
            }
        }
    }

    // Require the certificate's Organization RDN to match exactly.
    // A look-alike Common Name alone is insufficient to identify Microsoft.
    // WinVerifyTrust has already validated the file's Authenticode trust.
    public static bool IsMicrosoftPublisherName(string subject) =>
        Regex.IsMatch(subject,
            """(?:^|,\s*)O\s*=\s*"?Microsoft Corporation"?\s*(?:,|$)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderCertPrefix
    {
        public uint cbStruct;
        public IntPtr pCert;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CertContext
    {
        public uint dwCertEncodingType;
        public IntPtr pbCertEncoded;
        public uint cbCertEncoded;
        public IntPtr pCertInfo;
        public IntPtr hCertStore;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr hWnd, ref Guid pgActionID, ref WinTrustData pWVTData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr pProvData, uint idxSigner,
        [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner,
        uint idxCounterSigner);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(
        IntPtr pSgnr, uint idxCert);
}
