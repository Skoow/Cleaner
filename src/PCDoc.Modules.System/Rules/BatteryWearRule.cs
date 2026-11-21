using PCDoc.Core.Findings;
using PCDoc.Modules.System.Inventory;
using static PCDoc.Modules.System.Rules.RuleHelpers;

namespace PCDoc.Modules.System.Rules;

/// <summary>Usure de la batterie ≥ 40 % → Attention.</summary>
internal static class BatteryWearRule
{
    public static IEnumerable<Finding> Evaluate(IReadOnlyList<BatteryInfo> batteries)
    {
        for (var i = 0; i < batteries.Count; i++)
        {
            var b = batteries[i];
            if (b.DesignedCapacity > 0 && b.Wear >= 0.40)
            {
                yield return Make($"system.battery-wear.{i}", Severity.Warning,
                    Text("Finding.BatteryWear.Title", b.Wear * 100),
                    Text("Finding.BatteryWear.Explanation", b.Wear * 100, (1 - b.Wear) * 100),
                    $"root\\wmi BatteryStaticData.DesignedCapacity = {b.DesignedCapacity} · BatteryFullChargedCapacity = {b.FullChargedCapacity}");
            }
        }
    }
}
