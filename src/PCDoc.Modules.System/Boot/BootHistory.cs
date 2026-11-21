namespace PCDoc.Modules.System.Boot;

/// <summary>Type d'élément qui a ralenti un démarrage (événements 101 à 110).</summary>
public enum DegradationKind
{
    Application,
    Driver,
    Service,
    Other,
}

/// <summary>Tendance des temps de démarrage.</summary>
public enum BootTrend
{
    Unknown,
    Up,
    Down,
    Stable,
}

public sealed record BootDegradation(
    DegradationKind Kind, string Name, string FriendlyName, TimeSpan TotalTime, TimeSpan DegradationTime);

public sealed record BootRecord(
    DateTimeOffset Date, TimeSpan Total, TimeSpan MainPath, TimeSpan PostBoot, IReadOnlyList<BootDegradation> Degradations);

/// <summary>Historique des démarrages, du plus récent au plus ancien.</summary>
public sealed record BootHistory(IReadOnlyList<BootRecord> Boots)
{
    private const int TrendWindow = 5;
    private const double TrendThreshold = 0.10;

    /// <summary>Médiane des <paramref name="lastN"/> derniers démarrages (null s'il n'y en a aucun).</summary>
    public TimeSpan? Median(int lastN) => MedianOf(Boots.Take(lastN));

    /// <summary>Compare la médiane des 5 derniers à celle des 5 précédents (± 10 % = stable).</summary>
    public BootTrend Trend
    {
        get
        {
            if (Boots.Count < TrendWindow * 2)
            {
                return BootTrend.Unknown;
            }

            var recent = MedianOf(Boots.Take(TrendWindow))!.Value.TotalMilliseconds;
            var previous = MedianOf(Boots.Skip(TrendWindow).Take(TrendWindow))!.Value.TotalMilliseconds;
            if (previous <= 0)
            {
                return BootTrend.Unknown;
            }

            var change = (recent - previous) / previous;
            return change > TrendThreshold ? BootTrend.Up : change < -TrendThreshold ? BootTrend.Down : BootTrend.Stable;
        }
    }

    private static TimeSpan? MedianOf(IEnumerable<BootRecord> boots)
    {
        var sorted = boots.Select(b => b.Total).Order().ToList();
        if (sorted.Count == 0)
        {
            return null;
        }

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
