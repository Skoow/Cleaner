using System.Text.RegularExpressions;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Optimize;

/// <summary>État d'un réglage : niveau de risque sur ce PC, déjà appliqué ou non, disponible ou non.</summary>
public sealed record TweakState(Tweak Tweak, RiskLevel Risk, bool Applied, bool Available);

public interface IOptimizeSource
{
    Task<IReadOnlyList<TweakState>> AnalyzeAsync(CancellationToken cancellationToken);
}

/// <summary>Lit et applique les réglages (registre, powercfg). Les écritures ne visent que les valeurs du catalogue.</summary>
public sealed partial class OptimizeEngine(
    TweakCatalog catalog,
    IRegistryReader reader,
    IRegistryValueWriter writer,
    IProcessRunner runner,
    IWmiQuery wmi) : IOptimizeSource
{
    internal const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";
    private static readonly TimeSpan PowercfgTimeout = TimeSpan.FromMinutes(2);

    public TweakCatalog Catalog => catalog;

    public async Task<IReadOnlyList<TweakState>> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var desktop = IsDesktop();
        var plans = catalog.Tweaks.Any(t => t.Kind == TweakKind.PowerPlan) ? await ListPlansAsync(cancellationToken).ConfigureAwait(false) : [];
        var active = plans.Count > 0 ? await ActivePlanAsync(cancellationToken).ConfigureAwait(false) : null;
        return [.. catalog.Tweaks.Select(t => new TweakState(t, RiskOn(t, desktop), t.Kind switch
        {
            TweakKind.PowerPlan => t.Plan == active,
            TweakKind.Hibernate => !HibernateEnabled(),
            _ => IsApplied(t),
        }, t.Kind == TweakKind.PowerPlan ? plans.Contains(t.Plan!) : t.Kind != TweakKind.Registry || IsCompatible(t)))];
    }

    public RiskLevel RiskOf(Tweak tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        return RiskOn(tweak, IsDesktop());
    }

    private static RiskLevel RiskOn(Tweak t, bool desktop) => desktop && t.DesktopRisk is { } d ? d : t.Risk;

    /// <summary>Pas de batterie → PC fixe ; WMI indisponible → prudence (considéré portable).</summary>
    public bool IsDesktop()
    {
        try
        {
            return wmi.Query(@"root\cimv2", "SELECT Name FROM Win32_Battery").Count == 0;
        }
        catch (WmiUnavailableException)
        {
            return false;
        }
    }

    public object? Read(TweakValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return reader.GetValue(value.Hive, value.Path, value.Name, RegistryView.Registry64);
    }

    public void Write(TweakValue value, object? data)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.SetValue(value.Hive, value.Path, value.Name, data);
    }

    public bool IsApplied(Tweak tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        return tweak.Values.All(v => Read(v) is { } current ? v.IsSatisfiedBy(current) : tweak.AbsentIsApplied);
    }

    /// <summary>
    /// Chaque valeur existante a le type de la valeur cible (DWORD ou texte) : sinon le réglage n'est pas proposé, car
    /// l'annulation ne saurait pas remettre exactement l'ancien type.
    /// </summary>
    public bool IsCompatible(Tweak tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        return tweak.Values.All(v => Read(v) is not { } current || current.GetType() == v.Target.GetType());
    }

    /// <summary>HibernateEnabled, à défaut HibernateEnabledDefault ; inconnu → considérée active.</summary>
    public bool HibernateEnabled()
    {
        var value = reader.GetValue(RegistryHive.LocalMachine, PowerKey, "HibernateEnabled", RegistryView.Registry64)
                    ?? reader.GetValue(RegistryHive.LocalMachine, PowerKey, "HibernateEnabledDefault", RegistryView.Registry64);
        return value is not int v || v != 0;
    }

    public Task SetHibernateAsync(bool enabled, CancellationToken cancellationToken) =>
        Powercfg(enabled ? "/hibernate on" : "/hibernate off", cancellationToken);

    public async Task<string?> ActivePlanAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await runner.RunHiddenAsync("powercfg.exe", "/getactivescheme", lines.Add, cancellationToken, PowercfgTimeout).ConfigureAwait(false);
        return lines.Select(l => Guid().Match(l)).FirstOrDefault(m => m.Success)?.Value.ToLowerInvariant();
    }

    public async Task<IReadOnlyList<string>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await runner.RunHiddenAsync("powercfg.exe", "/list", lines.Add, cancellationToken, PowercfgTimeout).ConfigureAwait(false);
        return [.. lines.Select(l => Guid().Match(l)).Where(m => m.Success).Select(m => m.Value.ToLowerInvariant())];
    }

    public Task SetActivePlanAsync(string plan, CancellationToken cancellationToken) =>
        IsGuid(plan) ? Powercfg($"/setactive {plan}", cancellationToken) : throw new ArgumentException("Invalid power plan", nameof(plan));

    internal static bool IsGuid(string? text) => text is not null && Guid().Match(text) is { Success: true } m && m.Length == text.Length;

    private async Task Powercfg(string arguments, CancellationToken cancellationToken)
    {
        var exit = await runner.RunHiddenAsync("powercfg.exe", arguments, null, cancellationToken, PowercfgTimeout).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"powercfg.exe {arguments} exited with code {exit}");
        }
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();
}
