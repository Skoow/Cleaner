using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Updates;

/// <summary>Installe une mise à jour Windows, met à jour ou installe un logiciel (non annulable ; point de restauration avant).</summary>
public sealed class UpdateFixer(UpdatesEngine engine) : IFixer
{
    public const string Kind = "update";

    public string UndoKind => Kind;

    public bool SupportsUndo => false;

    public static Finding ForWindows(WindowsUpdateItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Make($"updates.wu.{item.Id}", new LocalizedText("Finding.Updates.Windows.Title", item.Title),
            item.IsDriver ? "Updates.Explanation.Driver" : item.IsOptional ? "Updates.Explanation.Optional" : "Updates.Explanation.Windows",
            UpdatesEngine.RiskOf(item), new Data("wu", item.Id), item.KbArticle ?? item.Id);
    }

    public static Finding ForUpgrade(WingetUpgrade upgrade)
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        return Make($"updates.upgrade.{upgrade.Id}", new LocalizedText("Finding.Updates.Upgrade.Title", upgrade.Name, upgrade.Version, upgrade.Available),
            "Updates.Explanation.Upgrade", RiskLevel.Safe, new Data("upgrade", upgrade.Id), upgrade.Id);
    }

    public static Finding ForInstall(WingetPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return Make($"updates.install.{package.Id}", new LocalizedText("Finding.Updates.Install.Title", package.Name),
            "Updates.Explanation.Install", RiskLevel.Safe, new Data("install", package.Id), package.Id);
    }

    private static Finding Make(string id, LocalizedText title, string explanation, RiskLevel risk, Data data, string details) => new(
        id, UpdatesModule.Id, Severity.Info, title, new LocalizedText(explanation), details,
        FixAvailable: true, Risk: risk, Reversible: false, FixData: JsonSerializer.Serialize(data));

    public bool CanFix(Finding finding) => Resolve(finding) is not null;

    public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        _ = Resolve(finding) ?? throw Refused();
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, finding.Id, finding.Title.Key, [.. finding.Title.Args.Select(a => a?.ToString() ?? string.Empty)]));
    }

    public Task ApplyAsync(Finding finding, CancellationToken cancellationToken) => (Resolve(finding) ?? throw Refused()) switch
    {
        WindowsUpdateItem w => engine.InstallWindowsAsync(w, cancellationToken),
        WingetUpgrade u => engine.UpgradeAsync(u, cancellationToken),
        WingetPackage p => engine.InstallAsync(p, cancellationToken),
        _ => throw Refused(),
    };

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException("Updates cannot be undone here");

    private static InvalidOperationException Refused() => new("Update refused: not found by the last search or riskier than announced");

    private object? Resolve(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (string.IsNullOrEmpty(finding.FixData) || finding.FixData.Length > 512)
        {
            return null;
        }

        Data? data;
        try
        {
            data = JsonSerializer.Deserialize<Data>(finding.FixData);
        }
        catch (JsonException)
        {
            return null;
        }

        return data switch
        {
            { Kind: "wu" } when finding.Id == $"updates.wu.{data.Id}" && engine.FindWindows(data.Id) is { } w
                                && (int)finding.Risk >= (int)UpdatesEngine.RiskOf(w) => w,
            { Kind: "upgrade" } when finding.Id == $"updates.upgrade.{data.Id}" && Winget.IsValidId(data.Id) => engine.FindUpgrade(data.Id),
            { Kind: "install" } when finding.Id == $"updates.install.{data.Id}" && Winget.IsValidId(data.Id) => engine.FindPackage(data.Id),
            _ => null,
        };
    }

    private sealed record Data(string Kind, string Id);
}
