using PCDoc.Core.Findings;
using PCDoc.Modules.System.Autoruns;

namespace PCDoc.Modules.System.Advisor;

/// <summary>Action proposée sur un élément : le désactiver s'il est actif, le réactiver sinon.</summary>
public enum ToggleAction
{
    Disable,
    Enable,
}

/// <summary>Conseil pour un élément. <paramref name="ConditionMet"/> : résultat de la condition (null si aucune ou invérifiable).</summary>
public sealed record Advice(ToggleAction Action, RiskLevel Level, string ReasonKey, bool? ConditionMet, bool InCatalog);

/// <summary>
/// Conseiller de désactivation (spec v2 §M2) : catalogue, conditions vérifiées, liste d'exclusion.
/// Élément inconnu : programme au démarrage non Microsoft → 🟢 ; reste → 🔴 ; pilotes → 🔒 en v0.2.
/// Réactiver un élément désactivé est toujours 🟢 (sauf 🔒).
/// </summary>
public sealed class AutorunAdvisor(AdvisorCatalog catalog, IConditionProbe conditions)
{
    public Advice Advise(AutorunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var action = entry.Enabled ? ToggleAction.Disable : ToggleAction.Enable;
        var (level, reason, condition, inCatalog) = Classify(entry);

        bool? met = null;
        if (level == RiskLevel.Conditional)
        {
            met = condition is null ? null : conditions.Check(condition);
            level = met == true ? RiskLevel.Safe : RiskLevel.Discouraged;
        }

        if (action == ToggleAction.Enable && level != RiskLevel.Locked)
        {
            level = RiskLevel.Safe;
        }

        if (entry.Kind == AutorunKind.Startup && entry.Startup is null)
        {
            level = RiskLevel.Locked; // RunOnce : aucun moyen réversible de le désactiver
            reason = "Advice.RunOnce";
        }

        return new Advice(action, level, reason, met, inCatalog);
    }

    private (RiskLevel Level, string Reason, string? Condition, bool InCatalog) Classify(AutorunEntry e)
    {
        switch (e.Kind)
        {
            case AutorunKind.Driver:
                return (RiskLevel.Locked, "Advice.Driver", null, false);
            case AutorunKind.Service:
                var name = e.ServiceName ?? e.Name;
                if (catalog.LockedServices.Contains(name))
                {
                    return (RiskLevel.Locked, "Advice.Essential", null, true);
                }

                return catalog.Services.TryGetValue(name, out var service)
                    ? (service.Level, service.Reason, service.Condition, true)
                    : (RiskLevel.Discouraged, "Advice.UnknownService", null, false);
            case AutorunKind.ScheduledTask:
                if (catalog.LockedTaskPrefixes.Any(p => e.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    return (RiskLevel.Locked, "Advice.Essential", null, true);
                }

                return catalog.Tasks.TryGetValue(e.Name, out var task)
                    ? (task.Level, task.Reason, task.Condition, true)
                    : (RiskLevel.Discouraged, "Advice.UnknownTask", null, false);
            default:
                var known = catalog.Startup.FirstOrDefault(s => s.Prefix
                    ? e.Name.StartsWith(s.Name, StringComparison.OrdinalIgnoreCase)
                    : e.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase));
                if (known is not null)
                {
                    return (known.Level, known.Reason, known.Condition, true);
                }

                return e.IsMicrosoft
                    ? (RiskLevel.Discouraged, "Advice.UnknownMicrosoftStartup", null, false)
                    : (RiskLevel.Safe, "Advice.ThirdPartyStartup", null, false);
        }
    }
}
