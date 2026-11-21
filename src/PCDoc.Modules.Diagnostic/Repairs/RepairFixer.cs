using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Diagnostic.Repairs;

/// <summary>Réparations en un clic (liste fermée : aucune commande libre).</summary>
public enum RepairKind
{
    FlushDns,
    SystemFiles,
    WindowsImage,
}

/// <summary>
/// Lance un outil officiel de Windows, caché, et relaie sa sortie. Les réparations passent par le pipeline sûr
/// (point de restauration, Historique) ; elles ne sont pas annulables par nature mais sans risque.
/// </summary>
public sealed class RepairFixer(IProcessRunner runner) : IFixer
{
    public const string Kind = "diagnostic.repair";

    private static readonly Dictionary<RepairKind, (string Tool, string Arguments)> Commands = new()
    {
        [RepairKind.FlushDns] = ("ipconfig.exe", "/flushdns"),
        [RepairKind.SystemFiles] = ("sfc.exe", "/scannow"),
        [RepairKind.WindowsImage] = ("dism.exe", "/Online /Cleanup-Image /RestoreHealth"),
    };

    /// <summary>Chaque ligne affichée par l'outil (progression dans la page).</summary>
    public event EventHandler<string>? Output;

    public string UndoKind => Kind;

    public bool SupportsUndo => false;

    public static Finding FindingFor(RepairKind repair) => new(
        $"diagnostic.repair.{repair}".ToLowerInvariant(), DiagnosticModule.Id, Severity.Info,
        new LocalizedText($"Repair.{repair}.Title"), new LocalizedText($"Repair.{repair}.Explanation"),
        $"{Commands[repair].Tool} {Commands[repair].Arguments}",
        FixAvailable: true, Risk: RiskLevel.Safe, Reversible: false,
        FixData: JsonSerializer.Serialize(new Data(repair.ToString())));

    public bool CanFix(Finding finding) => Parse(finding) is not null;

    public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var repair = Parse(finding) ?? throw new InvalidOperationException("Not a repair");
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, repair.ToString(), $"Repair.{repair}.Title", []));
    }

    public async Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var repair = Parse(finding) ?? throw new InvalidOperationException("Not a repair");
        var (tool, arguments) = Commands[repair];
        var exit = await runner.RunHiddenAsync(tool, arguments, line => Output?.Invoke(this, line), cancellationToken).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"{tool} exited with code {exit}");
        }
    }

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Repairs cannot be undone");

    private static RepairKind? Parse(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (string.IsNullOrEmpty(finding.FixData) || finding.FixData.Length > 256)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Data>(finding.FixData) is { } d && Enum.TryParse<RepairKind>(d.Repair, out var kind)
                   && Enum.IsDefined(kind) && finding.Id.Equals($"diagnostic.repair.{kind}", StringComparison.OrdinalIgnoreCase)
                ? kind
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Data(string Repair);
}
