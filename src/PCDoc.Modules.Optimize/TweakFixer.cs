using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;
using PCDoc.Modules.System.Fixes;

namespace PCDoc.Modules.Optimize;

/// <summary>
/// Applique un réglage du catalogue ; toujours réversible (valeurs d'origine dans le journal, .reg sauvegardé).
/// L'annulation relit le catalogue : le journal ne peut faire écrire que les valeurs prévues par ce réglage.
/// </summary>
public sealed class TweakFixer(OptimizeEngine engine, IRegistryWriter paths, IBackupStore backups, Func<DateTimeOffset> now) : IFixer
{
    public const string Kind = "optimize.tweak";

    public string UndoKind => Kind;

    public static Finding FindingFor(TweakState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new Finding(
            $"optimize.{state.Tweak.Id}", OptimizeModule.Id, Severity.Info,
            new LocalizedText($"Optimize.{state.Tweak.Id}.Title"),
            new LocalizedText($"Optimize.{state.Tweak.Id}.Explanation"),
            state.Tweak.Id,
            FixAvailable: state.Available && !state.Applied, Risk: state.Risk, Reversible: true,
            FixData: JsonSerializer.Serialize(new Data(state.Tweak.Id)));
    }

    public bool CanFix(Finding finding) => Resolve(finding) is not null;

    public async Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var tweak = Guarded(finding);
        var before = new Payload(tweak.Id);
        switch (tweak.Kind)
        {
            case TweakKind.PowerPlan:
                before = before with { Plan = await engine.ActivePlanAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("No active power plan") };
                break;
            case TweakKind.Hibernate:
                before = before with { Hibernate = engine.HibernateEnabled() };
                break;
            default:
                var values = tweak.Values.Select(v => Saved.From(engine.Read(v))).ToList();
                before = before with { Values = values };
                var index = 0;
                foreach (var group in tweak.Values.Select((v, i) => (v, i)).GroupBy(x => (x.v.Hive, x.v.Path)))
                {
                    backups.SaveRegistryBackup($"optimize-{tweak.Id}-{now():yyyyMMdd-HHmmss}-{index++}.reg",
                        RegFile.Format(paths.FullPath(group.Key.Hive, group.Key.Path), group.Select(x => (x.v.Name, values[x.i].Value))));
                }

                break;
        }

        return UndoEntry.Create(finding.Id, Kind, JsonSerializer.Serialize(before), $"Optimize.{tweak.Id}.Title", []);
    }

    public async Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var tweak = Guarded(finding);
        switch (tweak.Kind)
        {
            case TweakKind.PowerPlan:
                await engine.SetActivePlanAsync(tweak.Plan!, cancellationToken).ConfigureAwait(false);
                break;
            case TweakKind.Hibernate:
                await engine.SetHibernateAsync(false, cancellationToken).ConfigureAwait(false);
                break;
            default:
                foreach (var v in tweak.Values)
                {
                    engine.Write(v, v.Target);
                }

                break;
        }
    }

    public async Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var (tweak, _) = GuardedPayload(entry);
        return tweak.Kind switch
        {
            TweakKind.PowerPlan => await engine.ActivePlanAsync(cancellationToken).ConfigureAwait(false) == tweak.Plan,
            TweakKind.Hibernate => !engine.HibernateEnabled(),
            _ => tweak.Values.All(v => Equals(engine.Read(v), v.Target)),
        };
    }

    public async Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var (tweak, payload) = GuardedPayload(entry);
        switch (tweak.Kind)
        {
            case TweakKind.PowerPlan:
                await engine.SetActivePlanAsync(payload.Plan!, cancellationToken).ConfigureAwait(false);
                break;
            case TweakKind.Hibernate:
                await engine.SetHibernateAsync(payload.Hibernate == true, cancellationToken).ConfigureAwait(false);
                break;
            default:
                for (var i = 0; i < tweak.Values.Count; i++)
                {
                    engine.Write(tweak.Values[i], payload.Values![i].Value);
                }

                break;
        }
    }

    private Tweak Guarded(Finding finding) =>
        Resolve(finding) ?? throw new InvalidOperationException("Change refused: unknown setting or understated risk");

    private Tweak? Resolve(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (string.IsNullOrEmpty(finding.FixData) || finding.FixData.Length > 256)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Data>(finding.FixData)?.Tweak is { } id && finding.Id == $"optimize.{id}"
                   && engine.Catalog.Find(id) is { } tweak && (int)finding.Risk >= (int)engine.RiskOf(tweak)
                   && (tweak.Kind != TweakKind.Registry || engine.IsCompatible(tweak))
                ? tweak
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Le journal est une donnée lue sur le disque : réglage connu, valeurs du bon nombre et du bon type.</summary>
    private (Tweak Tweak, Payload Payload) GuardedPayload(UndoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(entry.Payload);
        }
        catch (JsonException)
        {
            payload = null;
        }

        var tweak = payload is null ? null : engine.Catalog.Find(payload.Tweak);
        var ok = tweak?.Kind switch
        {
            TweakKind.PowerPlan => OptimizeEngine.IsGuid(payload!.Plan),
            TweakKind.Hibernate => payload!.Hibernate is not null,
            TweakKind.Registry => payload!.Values is { } v && v.Count == tweak.Values.Count
                                  && v.Select((s, i) => s.Value is null || s.Value.GetType() == tweak.Values[i].Target.GetType()).All(x => x),
            _ => false,
        };
        return ok && entry.FindingId == $"optimize.{tweak!.Id}" ? (tweak, payload!) : throw new InvalidOperationException("Undo refused: invalid entry");
    }

    private sealed record Data(string Tweak);

    /// <summary>Valeur d'origine : DWORD, texte, ou absente.</summary>
    public sealed record Saved(int? Dword, string? Text)
    {
        public object? Value => Dword is { } d ? d : Text;

        public static Saved From(object? value) => value switch
        {
            int d => new Saved(d, null),
            string s => new Saved(null, s),
            _ => new Saved(null, null),
        };
    }

    private sealed record Payload(string Tweak, string? Plan = null, bool? Hibernate = null, List<Saved>? Values = null);
}
