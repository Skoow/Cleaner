using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;
using PCDoc.Core.Abstractions;
using static PCDoc.Infrastructure.Signatures.NativeMethods;

namespace PCDoc.Infrastructure.Signatures;

/// <summary>
/// Vérification Authenticode maison (aucune dépendance) :
/// 1. signature intégrée (WinVerifyTrust) ; 2. sinon catalogue de Windows (CryptCATAdmin*) ;
/// 3. éditeur lu dans le certificat du signataire. Aucun accès réseau (révocation non vérifiée).
/// Résultats mis en cache par (chemin, date de modification).
/// </summary>
public sealed class SignatureVerifier : ISignatureVerifier
{
    private readonly ConcurrentDictionary<(string Path, DateTime Modified), SignatureInfo> _cache = new();

    public SignatureInfo Verify(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return SignatureInfo.NotFound;
        }

        try
        {
            var key = (Path.GetFullPath(filePath).ToUpperInvariant(), File.GetLastWriteTimeUtc(filePath));
            return _cache.GetOrAdd(key, _ => VerifyUncached(filePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SignatureInfo(SignatureStatus.Error, null, false);
        }
    }

    private static SignatureInfo VerifyUncached(string path)
    {
        var embedded = VerifyEmbedded(path);
        if (embedded.Code == 0)
        {
            return new SignatureInfo(SignatureStatus.Signed, embedded.Publisher, false);
        }

        if (embedded.Code is not (TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown))
        {
            return new SignatureInfo(SignatureStatus.Invalid, embedded.Publisher, false);
        }

        return VerifyCatalog(path) ?? new SignatureInfo(SignatureStatus.Unsigned, null, false);
    }

    private static (int Code, string? Publisher) VerifyEmbedded(string path)
    {
        var fileInfo = new WintrustFileInfo { CbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(), FilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            return RunWinVerifyTrust(WtdChoiceFile, filePtr);
        }
        finally
        {
            Marshal.DestroyStructure<WintrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    private static SignatureInfo? VerifyCatalog(string path)
    {
        foreach (var algorithm in new[] { "SHA256", "SHA1" })
        {
            var result = VerifyCatalogWith(path, algorithm);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static SignatureInfo? VerifyCatalogWith(string path, string algorithm)
    {
        if (!CryptCATAdminAcquireContext2(out var catAdmin, IntPtr.Zero, algorithm, IntPtr.Zero, 0))
        {
            return null;
        }

        var catInfo = IntPtr.Zero;
        try
        {
            byte[] hash;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                SafeFileHandle handle = stream.SafeFileHandle;
                uint size = 0;
                CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle.DangerousGetHandle(), ref size, null, 0);
                if (size == 0)
                {
                    return null;
                }

                hash = new byte[size];
                if (!CryptCATAdminCalcHashFromFileHandle2(catAdmin, handle.DangerousGetHandle(), ref size, hash, 0))
                {
                    return null;
                }
            }

            catInfo = CryptCATAdminEnumCatalogFromHash(catAdmin, hash, (uint)hash.Length, 0, IntPtr.Zero);
            if (catInfo == IntPtr.Zero)
            {
                return null;
            }

            var info = new CatalogInfo { CbStruct = (uint)Marshal.SizeOf<CatalogInfo>(), CatalogFile = string.Empty };
            if (!CryptCATCatalogInfoFromContext(catInfo, ref info, 0))
            {
                return null;
            }

            var catalog = new WintrustCatalogInfo
            {
                CbStruct = (uint)Marshal.SizeOf<WintrustCatalogInfo>(),
                CatalogFilePath = info.CatalogFile,
                MemberTag = Convert.ToHexString(hash),
                MemberFilePath = path,
                HCatAdmin = catAdmin,
            };
            var hashPtr = Marshal.AllocHGlobal(hash.Length);
            var catalogPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustCatalogInfo>());
            try
            {
                Marshal.Copy(hash, 0, hashPtr, hash.Length);
                catalog.CalculatedFileHash = hashPtr;
                catalog.CbCalculatedFileHash = (uint)hash.Length;
                Marshal.StructureToPtr(catalog, catalogPtr, false);
                var (code, publisher) = RunWinVerifyTrust(WtdChoiceCatalog, catalogPtr);
                return code == 0
                    ? new SignatureInfo(SignatureStatus.Signed, publisher, true)
                    : new SignatureInfo(SignatureStatus.Invalid, publisher, true);
            }
            finally
            {
                Marshal.DestroyStructure<WintrustCatalogInfo>(catalogPtr);
                Marshal.FreeHGlobal(catalogPtr);
                Marshal.FreeHGlobal(hashPtr);
            }
        }
        finally
        {
            if (catInfo != IntPtr.Zero)
            {
                CryptCATAdminReleaseCatalogContext(catAdmin, catInfo, 0);
            }

            CryptCATAdminReleaseContext(catAdmin, 0);
        }
    }

    private static (int Code, string? Publisher) RunWinVerifyTrust(uint unionChoice, IntPtr info)
    {
        var action = GenericVerifyV2;
        var data = new WintrustData
        {
            CbStruct = (uint)Marshal.SizeOf<WintrustData>(),
            UiChoice = WtdUiNone,
            RevocationChecks = WtdRevokeNone,
            UnionChoice = unionChoice,
            Info = info,
            StateAction = WtdStateActionVerify,
            ProvFlags = WtdRevocationCheckNone | WtdCacheOnlyUrlRetrieval,
        };

        var code = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
        try
        {
            return (code, ReadPublisher(data.StateData));
        }
        finally
        {
            data.StateAction = WtdStateActionClose;
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data); // libère l'état ; le code de retour n'a pas d'usage
        }
    }

    /// <summary>CRYPT_PROVIDER_DATA → signataire → premier certificat de la chaîne → nom simple.</summary>
    private static string? ReadPublisher(IntPtr stateData)
    {
        if (stateData == IntPtr.Zero)
        {
            return null;
        }

        var provData = WTHelperProvDataFromStateData(stateData);
        if (provData == IntPtr.Zero)
        {
            return null;
        }

        var signer = WTHelperGetProvSignerFromChain(provData, 0, false, 0);
        if (signer == IntPtr.Zero)
        {
            return null;
        }

        // CRYPT_PROVIDER_SGNR : cbStruct (4) + FILETIME (8) + csCertChain (4), puis pasCertChain à l'offset 16.
        var certCount = Marshal.ReadInt32(signer, 12);
        var certChain = Marshal.ReadIntPtr(signer, 16);
        if (certCount <= 0 || certChain == IntPtr.Zero)
        {
            return null;
        }

        // CRYPT_PROVIDER_CERT : cbStruct (4), puis pCert aligné sur la taille d'un pointeur.
        var certContext = Marshal.ReadIntPtr(certChain, IntPtr.Size);
        if (certContext == IntPtr.Zero)
        {
            return null;
        }

        using var certificate = new X509Certificate2(certContext);
        return certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
    }
}
