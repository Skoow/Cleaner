using PCDoc.Core.Findings;
using PCDoc.Modules.System.Advisor;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Fixes;
using static PCDoc.Modules.System.Rules.RuleHelpers;

namespace PCDoc.Modules.System.Rules;

/// <summary>Éléments du catalogue encore actifs et désactivables sans risque (🟢) → problème « Info » corrigeable.</summary>
internal static class AdviceRule
{
    public static IEnumerable<Finding> Evaluate(IReadOnlyList<AutorunEntry> entries, AutorunAdvisor advisor)
    {
        foreach (var e in entries.Where(e => e.Enabled))
        {
            var advice = advisor.Advise(e);
            if (advice is not { InCatalog: true, Level: RiskLevel.Safe, Action: ToggleAction.Disable } || ToggleRequest.For(e, advice.Action) is not { } request)
            {
                continue;
            }

            var kind = e.Kind switch
            {
                AutorunKind.Startup => "startup",
                AutorunKind.ScheduledTask => "task",
                _ => "service",
            };
            var display = e.Kind == AutorunKind.ScheduledTask ? WinPath.FileName(e.Name) : e.Name;
            yield return Make($"system.advice.{kind}.{Slug(e.ServiceName ?? e.Name)}", Severity.Info,
                Text("Finding.Advice.Title", display), Text(advice.ReasonKey),
                $"{e.Location}\n{e.CommandLine}\nAdvice: {advice.Level} ({advice.ReasonKey})" +
                (advice.ConditionMet is null ? string.Empty : $"\nCondition verified: {advice.ConditionMet}"))
                with { FixAvailable = true, Risk = RiskLevel.Safe, FixData = request.Serialize() };
        }
    }
}
