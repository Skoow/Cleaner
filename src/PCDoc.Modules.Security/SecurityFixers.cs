using System.Text.Json;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Security;

/// <summary>Constats sans correction automatique (l'utilisateur doit agir dans Sécurité Windows).</summary>
public static class SecurityFindings
{
    public static Finding NoAntivirus() => new("security.av.none", SecurityModule.Id, Severity.Critical,
        new LocalizedText("Security.av.none.Title"), new LocalizedText("Security.av.none.Explanation"), "AntiVirusProduct: none enabled");

    public static Finding RealTimeOff() => new("security.defender.realtime", SecurityModule.Id, Severity.Critical,
        new LocalizedText("Security.defender.realtime.Title"), new LocalizedText("Security.defender.realtime.Explanation"),
        "MSFT_MpComputerStatus.RealTimeProtectionEnabled = false");

    public static Finding Hosts(Severity severity, string explanationKey, IReadOnlyList<string> lines) => new("security.hosts", SecurityModule.Id, severity,
        new LocalizedText("Security.hosts.Title"), new LocalizedText(explanationKey), string.Join(Environment.NewLine, lines));
}

/// <summary>Base des corrections de ce module : FixData = JSON, identifiant revérifié.</summary>
public abstract class SecurityFixerBase<TData>(string kind) : IFixer
    where TData : class
{
    public string UndoKind => kind;

    public virtual bool SupportsUndo => true;

    public bool CanFix(Finding finding) => Resolve(finding) is not null;

    public abstract Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken);

    public abstract Task ApplyAsync(Finding finding, CancellationToken cancellationToken);

    public abstract Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken);

    public abstract Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken);

    /// <summary>Données revalidées contre l'état actuel du PC (null = refusé).</summary>
    protected abstract TData? Validate(Finding finding, TData data);

    protected TData Guarded(Finding finding) => Resolve(finding) ?? throw new InvalidOperationException("Change refused: no longer needed or invalid");

    protected static string Json(object value) => JsonSerializer.Serialize(value);

    protected static T Payload<T>(UndoEntry entry)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            return JsonSerializer.Deserialize<T>(entry.Payload) ?? throw new InvalidOperationException("Invalid undo entry");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid undo entry", ex);
        }
    }

    private TData? Resolve(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (string.IsNullOrEmpty(finding.FixData) || finding.FixData.Length > 4096)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TData>(finding.FixData) is { } data ? Validate(finding, data) : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException)
        {
            // Problème d'un autre module ou devenu obsolète : jamais d'exception vers le pipeline.
            return null;
        }
    }
}

/// <summary>Met à jour les définitions de Defender (non annulable, sans risque).</summary>
public sealed class DefenderFixer(SecurityEngine engine) : SecurityFixerBase<DefenderFixer.Data>("security.defender")
{
    public override bool SupportsUndo => false;

    public static Finding SignaturesFinding(int ageDays) => new("security.defender.signatures", SecurityModule.Id, Severity.Warning,
        new LocalizedText("Security.defender.signatures.Title", ageDays), new LocalizedText("Security.defender.signatures.Explanation"),
        $"AntivirusSignatureAge = {ageDays}", FixAvailable: true, Risk: RiskLevel.Safe, Reversible: false, FixData: JsonSerializer.Serialize(new Data("update")));

    public override Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        Guarded(finding);
        return Task.FromResult(UndoEntry.Create(finding.Id, UndoKind, "{}", "Security.defender.signatures.Done", []));
    }

    public override async Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        Guarded(finding);
        var exit = await engine.UpdateSignaturesAsync(cancellationToken).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"MpCmdRun -SignatureUpdate exited with code {exit}");
        }
    }

    public override Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken) => Task.FromResult(false);

    public override Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();

    protected override Data? Validate(Finding finding, Data data) => data.Action == "update" && finding.Id == "security.defender.signatures" ? data : null;

    public sealed record Data(string Action);
}

/// <summary>Réactive un profil du pare-feu Windows (réversible : l'état d'origine est remis).</summary>
public sealed class FirewallFixer(SecurityEngine engine) : SecurityFixerBase<FirewallFixer.Data>("security.firewall")
{
    public static Finding FindingFor(string profile) => new($"security.firewall.{profile}", SecurityModule.Id, Severity.Critical,
        new LocalizedText("Security.firewall.Title", new LocalizedText($"Security.firewall.{profile}")), new LocalizedText("Security.firewall.Explanation"),
        $"MSFT_NetFirewallProfile {profile}: Enabled = false", FixAvailable: true, Risk: RiskLevel.Safe, Reversible: true,
        FixData: JsonSerializer.Serialize(new Data(profile)));

    public override Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Guarded(finding);
        return Task.FromResult(UndoEntry.Create(finding.Id, UndoKind, Json(data), "Security.firewall.Undo", [data.Profile]));
    }

    public override Task ApplyAsync(Finding finding, CancellationToken cancellationToken) => engine.SetFirewallAsync(Guarded(finding).Profile, true, cancellationToken);

    public override Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken) =>
        Task.FromResult(engine.TryDisabledFirewallProfiles() is { } disabled && !disabled.Contains(Read(entry).Profile));

    public override Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken) => engine.SetFirewallAsync(Read(entry).Profile, false, cancellationToken);

    protected override Data? Validate(Finding finding, Data data) =>
        data.Profile is not null && SecurityEngine.FirewallProfiles.Contains(data.Profile) && finding.Id == $"security.firewall.{data.Profile}"
        && engine.DisabledFirewallProfiles().Contains(data.Profile) ? data : null;

    private static Data Read(UndoEntry entry) =>
        Payload<Data>(entry) is { } d && SecurityEngine.FirewallProfiles.Contains(d.Profile) ? d : throw new InvalidOperationException("Invalid undo entry");

    public sealed record Data(string Profile);
}

/// <summary>Désactive SMBv1 (ancien partage de fichiers, porte d'entrée de rançongiciels) ; réactivable.</summary>
public sealed class FeatureFixer(SecurityEngine engine) : SecurityFixerBase<FeatureFixer.Data>("security.feature")
{
    public static Finding Smb1Finding() => new("security.smb1", SecurityModule.Id, Severity.Warning,
        new LocalizedText("Security.smb1.Title"), new LocalizedText("Security.smb1.Explanation"), "SMB1Protocol: Enabled",
        FixAvailable: true, Risk: RiskLevel.Conditional, Reversible: true, FixData: JsonSerializer.Serialize(new Data("SMB1Protocol")));

    public override async Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        Guarded(finding);
        var states = await engine.Smb1StatesAsync(cancellationToken).ConfigureAwait(false);
        return UndoEntry.Create(finding.Id, UndoKind, Json(new Saved("SMB1Protocol", states)), "Security.smb1.Title", []);
    }

    public override async Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        Guarded(finding);
        if (!await engine.Smb1EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("SMBv1 is already disabled");
        }

        await engine.SetSmb1Async(false, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        Read(entry);
        return !await engine.Smb1EnabledAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Remet chaque fonction dans son état d'avant (le parent d'abord, puis client, serveur, suppression automatique).</summary>
    public override async Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var saved = Read(entry);
        foreach (var feature in SecurityEngine.Smb1Features)
        {
            if (saved.States.TryGetValue(feature, out var enabled) && enabled)
            {
                await engine.SetFeatureAsync(feature, true, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var feature in SecurityEngine.Smb1Features.Skip(1))
        {
            if (saved.States.TryGetValue(feature, out var enabled) && !enabled && await engine.FeatureEnabledAsync(feature, cancellationToken).ConfigureAwait(false))
            {
                await engine.SetFeatureAsync(feature, false, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Saved Read(UndoEntry entry) =>
        Payload<Saved>(entry) is { Feature: "SMB1Protocol", States: { } s } saved && s.Keys.All(k => SecurityEngine.Smb1Features.Contains(k))
            ? saved
            : throw new InvalidOperationException("Invalid undo entry");

    private sealed record Saved(string Feature, Dictionary<string, bool> States);

    protected override Data? Validate(Finding finding, Data data) =>
        data.Feature == "SMB1Protocol" && finding.Id == "security.smb1" && (int)finding.Risk >= (int)RiskLevel.Conditional ? data : null;

    public sealed record Data(string Feature);
}

/// <summary>
/// Répare deux détournements classiques : « Debugger » dans les options d'exécution d'un programme (supprimé) et
/// Userinit de Winlogon (remis à la valeur de Windows). L'ancienne valeur est gardée pour l'annulation.
/// </summary>
public sealed class RegistryRepairFixer(SecurityEngine engine) : SecurityFixerBase<RegistryRepairFixer.Data>("security.repair")
{
    public static string IfeoId(string root, string exe) =>
        $"security.ifeo.{exe.ToLowerInvariant()}" + (root == SecurityEngine.IfeoPath32 ? ".32" : string.Empty);

    public static Finding IfeoFinding(string root, string exe, string debugger) => new(IfeoId(root, exe), SecurityModule.Id,
        Severity.Warning, new LocalizedText("Security.ifeo.Title", exe), new LocalizedText("Security.ifeo.Explanation"), $"{exe} → {debugger}",
        FixAvailable: true, Risk: RiskLevel.Conditional, Reversible: true, FixData: JsonSerializer.Serialize(new Data("ifeo", root, exe)));

    public static Finding UserinitFinding(string current) => new("security.userinit", SecurityModule.Id, Severity.Critical,
        new LocalizedText("Security.userinit.Title"), new LocalizedText("Security.userinit.Explanation"), $"Userinit = {current}",
        // 🟠 : certains logiciels d'entreprise ajoutent légitimement un programme à Userinit.
        FixAvailable: true, Risk: RiskLevel.Conditional, Reversible: true, FixData: JsonSerializer.Serialize(new Data("userinit", SecurityEngine.WinlogonPath, null)));

    public override Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Guarded(finding);
        var (path, name) = Target(data);
        var payload = new Saved(data, engine.ReadValue(path, name) as string);
        return Task.FromResult(UndoEntry.Create(finding.Id, UndoKind, Json(payload), finding.Title.Key,
            [.. finding.Title.Args.Select(a => a?.ToString() ?? string.Empty)]));
    }

    public override Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Guarded(finding);
        var (path, name) = Target(data);
        engine.WriteValue(path, name, data.Kind == "ifeo" ? null : engine.ExpectedUserinit);
        return Task.CompletedTask;
    }

    public override Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var saved = Read(entry);
        var (path, name) = Target(saved.Target);
        var now = engine.ReadValue(path, name) as string;
        return Task.FromResult(saved.Target.Kind == "ifeo" ? now is null : now is not null && engine.IsExpectedUserinit(now));
    }

    public override Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var saved = Read(entry);
        var (path, name) = Target(saved.Target);
        engine.WriteValue(path, name, saved.Previous);
        return Task.CompletedTask;
    }

    protected override Data? Validate(Finding finding, Data data) => data switch
    {
        { Kind: "ifeo", Exe: { } exe } when IsIfeoRoot(data.Root) && SecurityEngine.IsIfeoName(exe) && finding.Id == IfeoId(data.Root, exe)
                                             && engine.IfeoDebuggers().Any(d => d.Path == data.Root && d.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase))
                                             && (int)finding.Risk >= (int)RiskLevel.Conditional => data,
        { Kind: "userinit" } when data.Root == SecurityEngine.WinlogonPath && finding.Id == "security.userinit" && (int)finding.Risk >= (int)RiskLevel.Conditional
                                  && engine.CurrentUserinit() is { } u && !engine.IsExpectedUserinit(u) => data,
        _ => null,
    };

    private static bool IsIfeoRoot(string root) => root is SecurityEngine.IfeoPath or SecurityEngine.IfeoPath32;

    private static (string Path, string Name) Target(Data data) =>
        data.Kind == "ifeo" ? ($@"{data.Root}\{data.Exe}", "Debugger") : (SecurityEngine.WinlogonPath, "Userinit");

    /// <summary>Le journal est revérifié : seules les deux valeurs prévues peuvent être réécrites.</summary>
    private static Saved Read(UndoEntry entry)
    {
        var saved = Payload<Saved>(entry);
        var ok = saved.Target switch
        {
            { Kind: "ifeo", Exe: { } exe } => IsIfeoRoot(saved.Target.Root) && SecurityEngine.IsIfeoName(exe),
            { Kind: "userinit" } => saved.Target.Root == SecurityEngine.WinlogonPath && saved.Previous is { Length: > 0 },
            _ => false,
        };
        return ok ? saved : throw new InvalidOperationException("Invalid undo entry");
    }

    public sealed record Data(string Kind, string Root, string? Exe);

    private sealed record Saved(Data Target, string? Previous);
}
