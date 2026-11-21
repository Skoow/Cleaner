namespace PCDoc.Core.Abstractions;

/// <summary>État de la signature numérique d'un fichier.</summary>
#pragma warning disable CA1720 // « Signed » / « Unsigned » sont les termes du domaine.
public enum SignatureStatus
{
    Signed,
    Unsigned,
    Invalid,
    FileNotFound,
    Error,
}
#pragma warning restore CA1720

/// <summary>Résultat de la vérification. <paramref name="IsCatalogSigned"/> : signé via un catalogue de Windows.</summary>
public sealed record SignatureInfo(SignatureStatus Status, string? Publisher, bool IsCatalogSigned)
{
    public static SignatureInfo NotFound { get; } = new(SignatureStatus.FileNotFound, null, false);
}

/// <summary>Vérifie la signature Authenticode (intégrée ou par catalogue), sans accès réseau.</summary>
public interface ISignatureVerifier
{
    SignatureInfo Verify(string filePath);
}
