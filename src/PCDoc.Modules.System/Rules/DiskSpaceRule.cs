using PCDoc.Core.Findings;
using PCDoc.Modules.System.Inventory;
using static PCDoc.Modules.System.Rules.RuleHelpers;

namespace PCDoc.Modules.System.Rules;

/// <summary>Volume fixe : libre &lt; 10 % → Attention ; &lt; 5 % ou &lt; 2 Gio → Critique.</summary>
internal static class DiskSpaceRule
{
    private const long TwoGiB = 2L * 1024 * 1024 * 1024;

    public static IEnumerable<Finding> Evaluate(IReadOnlyList<VolumeInfo> volumes)
    {
        foreach (var v in volumes.Where(v => v.SizeBytes > 0))
        {
            var ratio = v.FreeRatio;
            Severity? severity = ratio < 0.05 || v.FreeBytes < TwoGiB ? Severity.Critical
                : ratio < 0.10 ? Severity.Warning
                : null;
            if (severity is null)
            {
                continue;
            }

            yield return Make($"system.disk-low.{Slug(v.Letter)}", severity.Value,
                Text("Finding.DiskLow.Title", v.Letter),
                Text("Finding.DiskLow.Explanation", v.Letter, v.FreeBytes / 1e9, v.SizeBytes / 1e9, ratio * 100),
                $"Win32_LogicalDisk DeviceID={v.Letter}\nFreeSpace = {v.FreeBytes:N0} B  Size = {v.SizeBytes:N0} B\n" +
                "Thresholds: warning < 10 %, critical < 5 % or < 2 GiB");
        }
    }
}
