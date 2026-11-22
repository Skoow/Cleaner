using System.Text.RegularExpressions;

namespace PCDoc.Core.Activity;

/// <summary>
/// Rend un rapport publiable sur un forum : nom du compte, nom du PC, dossier du profil, SID, adresses IP publiques et
/// privées, adresses MAC, courriels et numéros de série sont remplacés par des marqueurs neutres.
/// </summary>
public static partial class Anonymizer
{
    public static string Apply(string text, IEnumerable<string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(secrets);
        var result = Profile().Replace(text, @"$1\<utilisateur>"); // d'abord : le nom du dossier du profil
        foreach (var secret in secrets.Where(s => s is { Length: >= 3 }).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(s => s!.Length))
        {
            result = Regex.Replace(result, Regex.Escape(secret!), "<masqué>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }

        result = Sid().Replace(result, "S-1-5-21-<masqué>");
        result = Email().Replace(result, "<courriel>");
        result = Mac().Replace(result, "<MAC>");
        result = IPv4().Replace(result, m => m.Value is "127.0.0.1" or "0.0.0.0" ? m.Value : "<IP>");
        result = Serial().Replace(result, "$1<masqué>");
        return result;
    }

    [GeneratedRegex(@"([A-Za-z]:\\Users)\\[^\\\r\n""<>|]+", RegexOptions.IgnoreCase)]
    private static partial Regex Profile();

    [GeneratedRegex(@"S-1-5-21-[0-9]+-[0-9]+-[0-9]+(-[0-9]+)?")]
    private static partial Regex Sid();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}\b")]
    private static partial Regex Mac();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")]
    private static partial Regex IPv4();

    [GeneratedRegex(@"((?:serial|série|serie|numéro de série|SerialNumber)\s*[:=]\s*)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Serial();
}
