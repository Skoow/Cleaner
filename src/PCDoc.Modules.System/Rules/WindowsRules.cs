using PCDoc.Core.Findings;
using PCDoc.Modules.System.Inventory;
using static PCDoc.Modules.System.Rules.RuleHelpers;

namespace PCDoc.Modules.System.Rules;

/// <summary>Activation, fin de support de Windows 10, compatibilité Windows 11.</summary>
internal static class WindowsRules
{
    public static readonly DateTimeOffset Windows10EndOfSupport = new(2025, 10, 14, 0, 0, 0, TimeSpan.Zero);

    public static IEnumerable<Finding> Evaluate(WindowsInfo windows, Win11Readiness? readiness, DateTimeOffset now)
    {
        if (windows.Activation == ActivationState.NotActivated)
        {
            yield return Make("system.not-activated", Severity.Warning,
                Text("Finding.NotActivated.Title"), Text("Finding.NotActivated.Explanation"),
                "SoftwareLicensingProduct (ApplicationID 55c92734-…) · LicenseStatus ≠ 1");
        }

        if (!windows.IsWindows10)
        {
            yield break;
        }

        if (now >= Windows10EndOfSupport)
        {
            yield return Make("system.win10-eos", Severity.Warning,
                Text("Finding.Win10Eos.Title"), Text("Finding.Win10Eos.Explanation"),
                $"{windows.Edition} · build {windows.Build}.{windows.Ubr} · end of support 2025-10-14");
        }

        if (readiness is null)
        {
            yield break;
        }

        var details = string.Join("\n", readiness.Checks.Select(c => $"{c.Check}: {c.State}"));
        if (readiness.AllPass)
        {
            yield return Make("system.win11-ready", Severity.Info,
                Text("Finding.Win11Ready.Title"), Text("Finding.Win11Ready.Explanation"), details);
        }
        else if (readiness.Failed.Count > 0)
        {
            LocalizedText[] failed = [.. readiness.Failed.Select(c => Text($"Check.{c}"))];
            yield return Make("system.win11-blocked", Severity.Warning,
                Text("Finding.Win11Blocked.Title"), new LocalizedText("Finding.Win11Blocked.Explanation", (object)failed), details);
        }
    }
}
