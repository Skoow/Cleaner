using System.Globalization;
using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Boot;

/// <summary>Source de l'historique de démarrage (simulable dans les tests de la page Système).</summary>
public interface IBootSource
{
    BootHistory Read(int maxBoots = 30);
}

/// <summary>
/// Lit le journal Microsoft-Windows-Diagnostics-Performance/Operational : événement 100 = un démarrage
/// (BootTime, MainPathBootTime, BootPostBootTime en ms) ; 101 à 110 = éléments qui l'ont ralenti,
/// rattachés au dernier démarrage qui les précède.
/// </summary>
public sealed class BootPerformanceReader(IEventLogQuery log) : IBootSource
{
    public const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";
    private const int BootEvent = 100;
    private static readonly int[] EventIds = [100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110];

    public BootHistory Read(int maxBoots = 30)
    {
        // Les dégradations sont plus nombreuses que les démarrages : on lit large.
        var events = log.Query(LogName, EventIds, maxBoots * 12).OrderBy(e => e.TimeCreated).ToList();

        var boots = new List<(DateTimeOffset Date, TimeSpan Total, TimeSpan Main, TimeSpan Post, List<BootDegradation> Items)>();
        foreach (var e in events)
        {
            if (e.EventId == BootEvent)
            {
                var total = Ms(e, "BootTime");
                if (total is null)
                {
                    continue;
                }

                boots.Add((e.TimeCreated, total.Value, Ms(e, "MainPathBootTime") ?? TimeSpan.Zero,
                    Ms(e, "BootPostBootTime") ?? TimeSpan.Zero, []));
            }
            else if (boots.Count > 0)
            {
                boots[^1].Items.Add(new BootDegradation(
                    KindOf(e.EventId),
                    e.Data.GetValueOrDefault("Name") ?? "?",
                    e.Data.GetValueOrDefault("FriendlyName") ?? e.Data.GetValueOrDefault("Name") ?? "?",
                    Ms(e, "TotalTime") ?? TimeSpan.Zero,
                    Ms(e, "DegradationTime") ?? TimeSpan.Zero));
            }
        }

        return new BootHistory([.. boots
            .OrderByDescending(b => b.Date)
            .Take(maxBoots)
            .Select(b => new BootRecord(b.Date, b.Total, b.Main, b.Post, b.Items))]);
    }

    private static DegradationKind KindOf(int eventId) => eventId switch
    {
        101 => DegradationKind.Application,
        102 => DegradationKind.Driver,
        103 => DegradationKind.Service,
        _ => DegradationKind.Other,
    };

    private static TimeSpan? Ms(EventRecordData e, string field) =>
        e.Data.TryGetValue(field, out var raw)
        && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms >= 0
            ? TimeSpan.FromMilliseconds(ms)
            : null;
}
