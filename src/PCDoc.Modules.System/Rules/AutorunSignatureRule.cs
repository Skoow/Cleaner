using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.System.Autoruns;
using static PCDoc.Modules.System.Rules.RuleHelpers;

namespace PCDoc.Modules.System.Rules;

/// <summary>Élément activé, non Microsoft, non signé ou mal signé → Attention ; fichier introuvable → Info (orphelin).</summary>
internal static class AutorunSignatureRule
{
    public static IEnumerable<Finding> Evaluate(IReadOnlyList<AutorunEntry> entries)
    {
        foreach (var e in entries.Where(e => e.Enabled && !e.IsMicrosoft))
        {
            var kind = Text($"Kind.{e.Kind}");
            var kindSlug = e.Kind switch
            {
                AutorunKind.Startup => "startup",
                AutorunKind.ScheduledTask => "task",
                AutorunKind.Service => "service",
                _ => "driver",
            };
            var details = $"{e.Location}\n{e.CommandLine}\nFile: {e.FilePath ?? "-"}\nSignature: {e.Signature.Status}";

            if (e.Signature.Status is SignatureStatus.Unsigned or SignatureStatus.Invalid)
            {
                yield return Make($"system.unsigned.{kindSlug}.{Slug(e.Name)}", Severity.Warning,
                    Text("Finding.Unsigned.Title", kind, e.Name), Text("Finding.Unsigned.Explanation"), details);
            }
            else if (e.Signature.Status == SignatureStatus.FileNotFound)
            {
                yield return Make($"system.orphan.{kindSlug}.{Slug(e.Name)}", Severity.Info,
                    Text("Finding.Orphan.Title", kind, e.Name), Text("Finding.Orphan.Explanation", kind, e.Name, e.FilePath ?? e.CommandLine),
                    details);
            }
        }
    }
}
