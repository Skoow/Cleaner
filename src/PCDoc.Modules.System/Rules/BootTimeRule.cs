using PCDoc.Core.Findings;
using PCDoc.Modules.System.Boot;
using static PCDoc.Modules.System.Rules.RuleHelpers;

namespace PCDoc.Modules.System.Rules;

/// <summary>Médiane des 5 derniers démarrages &gt; 60 s → Attention, &gt; 120 s → Critique ; retards &gt; 5 s du dernier → Info.</summary>
internal static class BootTimeRule
{
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan VerySlow = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan NotableDelay = TimeSpan.FromSeconds(5);

    public static IEnumerable<Finding> Evaluate(BootHistory history)
    {
        var median = history.Median(5);
        if (median is { } m && m > Slow)
        {
            var threshold = m > VerySlow ? VerySlow : Slow;
            yield return Make("system.boot-slow", m > VerySlow ? Severity.Critical : Severity.Warning,
                Text("Finding.BootSlow.Title", m.TotalSeconds),
                Text("Finding.BootSlow.Explanation", m.TotalSeconds, (int)threshold.TotalSeconds),
                $"{BootPerformanceReader.LogName}\nEventID 100 · BootTime (ms) : " +
                string.Join(", ", history.Boots.Take(5).Select(b => ((long)b.Total.TotalMilliseconds).ToString("N0", global::System.Globalization.CultureInfo.InvariantCulture))) +
                $"\nMedian = {(long)m.TotalMilliseconds:N0} ms");
        }

        if (history.Boots.Count == 0)
        {
            yield break;
        }

        foreach (var d in history.Boots[0].Degradations.Where(d => d.DegradationTime > NotableDelay))
        {
            yield return Make($"system.boot-degradation.{Slug(d.Name)}", Severity.Info,
                Text("Finding.BootDegradation.Title", d.FriendlyName, d.DegradationTime.TotalSeconds),
                Text($"Finding.BootDegradation.Explanation.{d.Kind}"),
                $"{BootPerformanceReader.LogName}\nKind = {d.Kind} · Name = {d.Name} · DegradationTime = {(long)d.DegradationTime.TotalMilliseconds} ms");
        }
    }
}
