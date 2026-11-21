using System.Collections.Concurrent;
using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Advisor;

/// <summary>Vérifie une condition du catalogue : vrai / faux, ou null si PCDoc ne peut pas le savoir.</summary>
public interface IConditionProbe
{
    bool? Check(string condition);
}

/// <summary>Conditions vérifiées par WMI et le registre (résultats mis en cache).</summary>
public sealed class ConditionProbe(IWmiQuery wmi, IRegistryReader registry) : IConditionProbe
{
    private const string Cimv2 = @"root\cimv2";
    private static readonly string[] VirtualPrinters =
        ["Microsoft Print to PDF", "Microsoft XPS Document Writer", "OneNote", "Fax", "Send To OneNote", "Adobe PDF", "PDF"];

    private readonly ConcurrentDictionary<string, bool?> _cache = new(StringComparer.Ordinal);

    public bool? Check(string condition) => _cache.GetOrAdd(condition, Evaluate);

    private bool? Evaluate(string condition)
    {
        try
        {
            return condition switch
            {
                // WMI tourne sous le compte qui a lancé PCDoc : on ajoute les imprimantes réseau du profil cible.
                "no-printer" => registry.GetSubKeyNames(RegistryHive.TargetUser, @"Printers\Connections").Count == 0
                                && !wmi.Query(Cimv2, "SELECT Name FROM Win32_Printer").Any(r =>
                    r.GetValueOrDefault("Name") is string name && !VirtualPrinters.Any(v => name.Contains(v, StringComparison.OrdinalIgnoreCase))),
                "no-bluetooth" => NoDevice("Bluetooth"),
                "no-biometric" => NoDevice("Biometric"),
                "no-smartcard" => NoDevice("SmartCardReader"),
                "desktop" => wmi.Query(Cimv2, "SELECT Name FROM Win32_Battery").Count == 0,
                "not-insider" => registry.GetValue(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\WindowsSelfHost\Applicability", "EnablePreviewBuilds") is not (int and > 0),
                _ => null, // condition inconnue ou invérifiable (ex. « no-xbox »)
            };
        }
        catch (WmiUnavailableException)
        {
            return null;
        }
    }

    private bool NoDevice(string pnpClass) =>
        wmi.Query(Cimv2, $"SELECT Name FROM Win32_PnPEntity WHERE PNPClass='{pnpClass}'").Count == 0;
}
