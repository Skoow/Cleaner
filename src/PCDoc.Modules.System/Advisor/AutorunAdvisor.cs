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
        var name = entry.Kind is AutorunKind.Service or AutorunKind.Driver ? entry.ServiceName ?? entry.Name : entry.Name;
        var (level, reason, condition, inCatalog) = Classify(entry.Kind, name, entry.IsMicrosoft);
        var (resolved, met) = Resolve(entry.Kind, action, level, condition);

        if (entry.Kind == AutorunKind.Startup && entry.Startup is null)
        {
            resolved = RiskLevel.Locked; // RunOnce : aucun moyen réversible de le désactiver
            reason = "Advice.RunOnce";
        }

        return new Advice(action, resolved, reason, met, inCatalog);
    }

    /// <summary>
    /// Niveau de risque recalculé pour une requête (sans faire confiance au problème qui la porte) : utilisé par le
    /// correcteur avant chaque modification et chaque annulation.
    /// </summary>
    public RiskLevel LevelFor(Fixes.ToggleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (level, _, condition, _) = Classify(request.Kind, request.Name, isMicrosoft: false);
        return Resolve(request.Kind, request.Action, level, condition).Level;
    }

    /// <summary>
    /// Condition vérifiée → 🟢, sinon 🔴. Réactiver : programme au démarrage ou élément « selon l'usage » → 🟢 ;
    /// un service ou une tâche 🔴 le reste (réactiver peut affaiblir la sécurité) ; 🔒 toujours.
    /// </summary>
    private (RiskLevel Level, bool? Met) Resolve(AutorunKind kind, ToggleAction action, RiskLevel level, string? condition)
    {
        if (level == RiskLevel.Locked)
        {
            return (level, null);
        }

        if (action == ToggleAction.Enable)
        {
            return (kind == AutorunKind.Startup || level is RiskLevel.Conditional or RiskLevel.Safe ? RiskLevel.Safe : level, null);
        }

        if (level != RiskLevel.Conditional)
        {
            return (level, null);
        }

        var met = condition is null ? null : conditions.Check(condition);
        return (met == true ? RiskLevel.Safe : RiskLevel.Discouraged, met);
    }

    private (RiskLevel Level, string Reason, string? Condition, bool InCatalog) Classify(AutorunKind kind, string name, bool isMicrosoft)
    {
        switch (kind)
        {
            case AutorunKind.Driver:
                return (RiskLevel.Locked, "Advice.Driver", null, false);
            case AutorunKind.Service:
                if (catalog.LockedServices.Contains(name))
                {
                    return (RiskLevel.Locked, "Advice.Essential", null, true);
                }

                return catalog.Services.TryGetValue(name, out var service)
                    ? (service.Level, service.Reason, service.Condition, true)
                    : (RiskLevel.Discouraged, "Advice.UnknownService", null, false);
            case AutorunKind.ScheduledTask:
                if (catalog.LockedTaskPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    return (RiskLevel.Locked, "Advice.Essential", null, true);
                }

                return catalog.Tasks.TryGetValue(name, out var task)
                    ? (task.Level, task.Reason, task.Condition, true)
                    : (RiskLevel.Discouraged, "Advice.UnknownTask", null, false);
            default:
                var known = catalog.Startup.FirstOrDefault(s => s.Prefix
                    ? name.StartsWith(s.Name, StringComparison.OrdinalIgnoreCase)
                    : name.Equals(s.Name, StringComparison.OrdinalIgnoreCase));
                if (known is not null)
                {
                    return (known.Level, known.Reason, known.Condition, true);
                }

                return isMicrosoft
                    ? (RiskLevel.Discouraged, "Advice.UnknownMicrosoftStartup", null, false)
                    : (RiskLevel.Safe, "Advice.ThirdPartyStartup", null, false);
        }
    }
}
