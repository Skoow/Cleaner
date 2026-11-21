using System.Text;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.System.Rules;

internal static class RuleHelpers
{
    public static Finding Make(string id, Severity severity, LocalizedText title, LocalizedText explanation, string details) =>
        new(id, SystemModule.Id, severity, title, explanation, details);

    public static LocalizedText Text(string key, params object[] args) => new(key, args);

    /// <summary>Fragment d'identifiant stable : minuscules, espaces et caractères spéciaux remplacés par « - ».</summary>
    public static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '-');
        }

        return builder.ToString().Trim('-');
    }
}
