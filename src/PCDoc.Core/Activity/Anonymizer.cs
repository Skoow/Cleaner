using System.Net;
using System.Text.RegularExpressions;

namespace PCDoc.Core.Activity;

/// <summary>
/// Rend un rapport publiable sur un forum : nom du compte, du PC et du domaine, dossiers de profil, SID, courriels,
/// adresses IP (v4 et v6), adresses MAC, serveurs réseau (\\serveur), clés de produit et numéros de série sont remplacés
/// par des marqueurs neutres. Les noms connus ne sont masqués qu'en mot entier (« Max » ne touche pas « Maximum »).
/// </summary>
public static partial class Anonymizer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static string Apply(string text, IEnumerable<string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(secrets);
        var result = Email().Replace(text, "<courriel>"); // avant les noms : sinon « <masqué>@domaine » ne serait plus reconnu
        result = Unc().Replace(result, @"\\<serveur>");
        result = Profile().Replace(result, @"$1\<utilisateur>");
        result = OneDrive().Replace(result, "OneDrive - <masqué>");
        foreach (var secret in secrets.Where(s => s is { Length: >= 3 }).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(s => s!.Length))
        {
            var pattern = @"(?<![\p{L}\p{N}])" + Regex.Escape(secret!.Trim()) + @"(?![\p{L}\p{N}])";
            result = Regex.Replace(result, pattern, "<masqué>", RegexOptions.IgnoreCase, Timeout);
        }

        result = Sid().Replace(result, "S-1-<masqué>");
        result = ProductKey().Replace(result, "<clé>");
        result = Mac().Replace(result, "<MAC>");
        result = MacLabel().Replace(result, "$1<MAC>");
        result = IPv4().Replace(result, m => m.Value is "127.0.0.1" or "0.0.0.0" ? m.Value : "<IP>");
        result = IPv6Candidate().Replace(result, m => IsIPv6(m.Value) ? "<IPv6>" : m.Value);
        result = Serial().Replace(result, "$1<masqué>");
        return result;
    }

    /// <summary>Vraie adresse IPv6 (pas une heure « 10:20:30 ») ; « ::1 » et « :: » sont gardés.</summary>
    private static bool IsIPv6(string value)
    {
        var colons = value.Count(c => c == ':');
        return colons >= 2 && (value.Contains("::", StringComparison.Ordinal) || colons >= 5) && value is not ("::1" or "::")
               && IPAddress.TryParse(value.Split('%')[0], out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
    }

    /// <summary>Profil sous Users suivi d'un sous-dossier (le chemin complet du profil est aussi dans les noms connus).</summary>
    [GeneratedRegex(@"([A-Za-z]:\\Users)\\(?!Public\\|Default\\|All Users\\)[^\\\r\n""<>|:*?]+(?=\\)", RegexOptions.IgnoreCase)]
    private static partial Regex Profile();

    [GeneratedRegex(@"OneDrive - [^\\\r\n""<>|:*?]+")]
    private static partial Regex OneDrive();

    [GeneratedRegex(@"(?<![\\\w])\\\\[A-Za-z0-9._$\-]+(?=\\)")]
    private static partial Regex Unc();

    [GeneratedRegex(@"S-1-(?:5-21|12-1)(?:-[0-9]+){2,}")]
    private static partial Regex Sid();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(?:[A-Z0-9]{5}-){4}[A-Z0-9]{5}\b")]
    private static partial Regex ProductKey();

    [GeneratedRegex(@"\b([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex Mac();

    [GeneratedRegex(@"((?:MAC|MACAddress|Physical Address|Adresse physique)\s*[:=]?\s*)[0-9A-Fa-f]{12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MacLabel();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")]
    private static partial Regex IPv4();

    [GeneratedRegex(@"(?<![0-9A-Za-z:])[0-9A-Fa-f:]{2,39}(?:%[0-9]+)?(?![0-9A-Za-z:])")]
    private static partial Regex IPv6Candidate();

    /// <summary>Numéro de série : masqué jusqu'à la fin de la ligne (il peut contenir des espaces).</summary>
    [GeneratedRegex(@"((?:serial|série|serie|numéro de série|SerialNumber|Volume Serial Number is|Numéro de série du volume est)\s*[:=]?\s*)[^\r\n,;]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex Serial();
}
