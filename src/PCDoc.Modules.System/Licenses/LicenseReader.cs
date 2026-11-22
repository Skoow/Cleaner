using System.Globalization;
using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Licenses;

/// <summary>Licence d'un produit Microsoft (Windows, Office) : état d'activation et 5 derniers caractères de la clé.</summary>
public sealed record LicenseInfo(string Name, string Description, int Status, string PartialKey)
{
    /// <summary>1 = activé ; 0 non licencié ; 2-6 période de grâce, notification…</summary>
    public bool IsActivated => Status == 1;
}

public sealed record LicenseReport(IReadOnlyList<LicenseInfo> Products, string? OemKey);

/// <summary>
/// Licences installées (SoftwareLicensingProduct) et clé Windows enregistrée dans le micrologiciel du PC
/// (SoftwareLicensingService.OA3xOriginalProductKey, la clé livrée avec le PC). Lecture seule.
/// </summary>
public sealed class LicenseReader(IWmiQuery wmi)
{
    public LicenseReport Read()
    {
        IReadOnlyList<LicenseInfo> products;
        try
        {
            products = [.. wmi.Query(@"root\cimv2",
                    "SELECT Name, Description, LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL")
                .Select(r => new LicenseInfo(r.GetValueOrDefault("Name") as string ?? "?", r.GetValueOrDefault("Description") as string ?? string.Empty,
                    Convert.ToInt32(r.GetValueOrDefault("LicenseStatus") ?? 0, CultureInfo.InvariantCulture), r.GetValueOrDefault("PartialProductKey") as string ?? string.Empty))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (WmiUnavailableException)
        {
            products = [];
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

        return new LicenseReport(products, oem);
    }
}
