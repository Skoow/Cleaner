using System.Globalization;
using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Licenses;

/// <summary>Licence d'un produit Microsoft (Windows, Office) : état d'activation et 5 derniers caractères de la clé.</summary>
public sealed record LicenseInfo(string Name, string Description, int Status, string PartialKey)
{
    /// <summary>1 = activé ; 0 non licencié ; 2-6 période de grâce, notification…</summary>
    public bool IsActivated => Status == 1;
}

/// <param name="Unavailable">Vrai si la lecture a échoué (service de licences lent ou arrêté) : la liste vide ne veut pas dire « aucune licence ».</param>
public sealed record LicenseReport(IReadOnlyList<LicenseInfo> Products, string? OemKey, bool Unavailable = false);

/// <summary>
/// Licences installées (SoftwareLicensingProduct) et clé Windows enregistrée dans le micrologiciel du PC
/// (SoftwareLicensingService.OA3xOriginalProductKey, la clé livrée avec le PC). Lecture seule.
/// </summary>
public sealed class LicenseReader(IWmiQuery wmi)
{
    public LicenseReport Read()
    {
        IReadOnlyList<LicenseInfo> products;
        var unavailable = false;
        try
        {
            products = [.. wmi.Query(@"root\cimv2",
                    // Windows (55c92734…) et Office (0ff1ce15…) seulement : beaucoup plus rapide que toute la classe.
                    "SELECT Name, Description, LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL"
                    + " AND (ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' OR ApplicationID = '0ff1ce15-a989-479d-af46-f275c6370663')")
                .Select(r => new LicenseInfo(r.GetValueOrDefault("Name") as string ?? "?", r.GetValueOrDefault("Description") as string ?? string.Empty,
                    Convert.ToInt32(r.GetValueOrDefault("LicenseStatus") ?? 0, CultureInfo.InvariantCulture), r.GetValueOrDefault("PartialProductKey") as string ?? string.Empty))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (WmiUnavailableException)
        {
            products = [];
            unavailable = true;
        }

        string? oem = null;
        try
        {
            oem = wmi.Query(@"root\cimv2", "SELECT OA3xOriginalProductKey FROM SoftwareLicensingService")
                .Select(r => r.GetValueOrDefault("OA3xOriginalProductKey") as string).FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));
        }
        catch (WmiUnavailableException)
        {
            // Pas de clé dans le micrologiciel (PC monté, ancienne carte mère).
        }

        return new LicenseReport(products, oem, unavailable);
    }
}
