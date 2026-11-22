using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Modules.Security;

/// <summary>Analyse antivirus lancée par le pipeline (préparation d'un PC, entretien, bouton) : Defender ou outil d'un autre éditeur.</summary>
public enum ScanKind
{
    DefenderQuick,
    DefenderFull,
    SafetyScanner,
    AdwCleaner,
    AdwCleanerClean,
    KasperskyVrt,
}

/// <summary>
/// Analyses en tant que « corrections » : journalisées, avec point de restauration avant celles qui suppriment
/// (AdwCleaner nettoyage, Kaspersky), qui sont 🟠 et demandent une confirmation renforcée. Non annulables par PCDoc ;
/// les éléments retirés restent dans la quarantaine de l'outil.
/// </summary>
public sealed class ScanFixer(SecurityEngine engine, ISecondOpinion secondOpinion) : SecurityFixerBase<ScanFixer.Data>("security.scan")
{
    public override bool SupportsUndo => false;

    public static RiskLevel RiskOf(ScanKind kind) => kind is ScanKind.AdwCleanerClean or ScanKind.KasperskyVrt ? RiskLevel.Conditional : RiskLevel.Safe;

    public static Finding FindingFor(ScanKind kind) => new($"security.scan.{kind}".ToLowerInvariant(), SecurityModule.Id, Severity.Info,
        new LocalizedText($"Scan.{kind}.Title"), new LocalizedText($"Scan.{kind}.Explanation"), kind.ToString(),
        FixAvailable: true, Risk: RiskOf(kind), Reversible: false, FixData: JsonSerializer.Serialize(new Data(kind.ToString())));

    /// <summary>Lignes du dernier outil lancé (affichées dans la page).</summary>
    public event EventHandler<string>? Output;

    public override Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var kind = Kind(Guarded(finding));
        return Task.FromResult(UndoEntry.Create(finding.Id, UndoKind, kind.ToString(), $"Scan.{kind}.Title", []));
    }

    public override async Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var kind = Kind(Guarded(finding));
        void Line(string l) => Output?.Invoke(this, l);
        switch (kind)
        {
            case ScanKind.DefenderQuick or ScanKind.DefenderFull:
                try
                {
                    await engine.UpdateSignaturesAsync(cancellationToken).ConfigureAwait(false); // définitions à jour avant d'analyser
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
                {
                    Line($"Defender: signatures not updated ({ex.Message})");
                }

                var exit = await engine.ScanAsync(kind == ScanKind.DefenderQuick ? 1 : 2, null, Line, cancellationToken).ConfigureAwait(false);
                if (exit is not (0 or 2))
                {
                    throw new InvalidOperationException($"Microsoft Defender scan exited with code {exit}");
                }

                break;
            default:
                var tool = kind switch
                {
                    ScanKind.SafetyScanner => SecondOpinionTool.SafetyScanner,
                    ScanKind.AdwCleaner => SecondOpinionTool.AdwCleaner,
                    ScanKind.AdwCleanerClean => SecondOpinionTool.AdwCleanerClean,
                    _ => SecondOpinionTool.KasperskyVrt,
                };
                var result = await secondOpinion.RunAsync(tool, Line, cancellationToken).ConfigureAwait(false);
                foreach (var l in result.LogLines)
                {
                    Line(l);
                }

                break;
        }
    }

    public override Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken) => Task.FromResult(false);

    public override Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();

    protected override Data? Validate(Finding finding, Data data) =>
        Enum.TryParse<ScanKind>(data.Scan, out var kind) && Enum.IsDefined(kind) && string.Equals(finding.Id, $"security.scan.{kind}", StringComparison.OrdinalIgnoreCase)
        && (int)finding.Risk >= (int)RiskOf(kind) ? data : null;

    private static ScanKind Kind(Data data) => Enum.Parse<ScanKind>(data.Scan);

    public sealed record Data(string Scan);
}
