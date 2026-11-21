using System.Runtime.InteropServices;

namespace PCDoc.Infrastructure.Signatures;

#pragma warning disable SYSLIB1054 // DllImport : structures non blittables (chaînes) plus simples à marshaller ainsi.
#pragma warning disable CA1069
/// <summary>P/Invoke wintrust.dll (WinVerifyTrust, catalogues). Aucun accès réseau.</summary>
internal static class NativeMethods
{
    public const uint WtdUiNone = 2;
    public const uint WtdRevokeNone = 0;
    public const uint WtdChoiceFile = 1;
    public const uint WtdChoiceCatalog = 2;
    public const uint WtdStateActionVerify = 1;
    public const uint WtdStateActionClose = 2;
    public const uint WtdRevocationCheckNone = 0x00000010;
    public const uint WtdCacheOnlyUrlRetrieval = 0x00001000;

    public const int TrustENoSignature = unchecked((int)0x800B0100);
    public const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    public const int TrustEProviderUnknown = unchecked((int)0x800B0001);
    public const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);

    public static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>DRIVER_ACTION_VERIFY : politique des pilotes (catalogues WHQL).</summary>
    public static readonly Guid DriverActionVerify = new("F750E6C3-38EE-11d1-85E5-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WintrustFileInfo
    {
        public uint CbStruct;
        public string FilePath;
        public IntPtr HFile;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WintrustCatalogInfo
    {
        public uint CbStruct;
        public uint CatalogVersion;
        public string CatalogFilePath;
        public string MemberTag;
        public string MemberFilePath;
        public IntPtr HMemberFile;
        public IntPtr CalculatedFileHash;
        public uint CbCalculatedFileHash;
        public IntPtr CatalogContext;
        public IntPtr HCatAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WintrustData
    {
        public uint CbStruct;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr Info; // WINTRUST_FILE_INFO* ou WINTRUST_CATALOG_INFO*
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CatalogInfo
    {
        public uint CbStruct;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string CatalogFile;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    public static extern int WinVerifyTrust(IntPtr hwnd, [In] ref Guid action, ref WintrustData data);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CryptCATAdminAcquireContext2(
        out IntPtr catAdmin, IntPtr subsystem, string hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CryptCATAdminReleaseContext(IntPtr catAdmin, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CryptCATAdminCalcHashFromFileHandle2(
        IntPtr catAdmin, IntPtr file, ref uint hashSize, [Out] byte[]? hash, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr CryptCATAdminEnumCatalogFromHash(
        IntPtr catAdmin, byte[] hash, uint hashSize, uint flags, IntPtr previousCatInfo);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CryptCATCatalogInfoFromContext(IntPtr catInfo, ref CatalogInfo info, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CryptCATAdminReleaseCatalogContext(IntPtr catAdmin, IntPtr catInfo, uint flags);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    public static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    public static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr provData, uint signerIndex, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
}
#pragma warning restore CA1069
#pragma warning restore SYSLIB1054
