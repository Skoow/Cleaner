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
    RenewIp,
    RestartAudio,
    NetworkReset,
    WindowsUpdateReset,
}

/// <summary>Une étape : outil de System32 et ses arguments, ou renommage d'un dossier de Windows (<see cref="RenameFolder"/>).</summary>
/// <param name="Always">Étape toujours exécutée, même si une étape précédente a échoué ou a été annulée (redémarrage des services).</param>
public sealed record RepairStep(string Tool, string Arguments, TimeSpan Timeout, bool MustSucceed = true, string? RenameFolder = null, bool Always = false);

/// <summary>
/// Lance un outil officiel de Windows, caché, et relaie sa sortie. Les réparations passent par le pipeline sûr
/// (point de restauration, Historique) ; elles ne sont pas annulables par nature mais sans risque.
/// </summary>
public sealed class RepairFixer(IProcessRunner runner, Action<string>? renameFolder = null, string? windowsDirectory = null) : IFixer
{
    public const string Kind = "diagnostic.repair";

    private const int RebootRequiredCode = 3010; // ERROR_SUCCESS_REBOOT_REQUIRED
    private const int SourceNotFound = unchecked((int)0x800F081F);

    private static readonly TimeSpan Short = TimeSpan.FromMinutes(2);

    /// <summary>Liste fermée des étapes de chaque réparation (outils officiels de Windows uniquement).</summary>
    public static readonly IReadOnlyDictionary<RepairKind, IReadOnlyList<RepairStep>> Steps = new Dictionary<RepairKind, IReadOnlyList<RepairStep>>
    {
        [RepairKind.FlushDns] = [new("ipconfig.exe", "/flushdns", Short)],
        [RepairKind.SystemFiles] = [new("sfc.exe", "/scannow", TimeSpan.FromHours(2))],
        [RepairKind.WindowsImage] = [new("dism.exe", "/Online /Cleanup-Image /RestoreHealth", TimeSpan.FromHours(4))],
        // /renew échoue si une seule carte (débranchée, adresse fixe) ne peut pas renouveler : pas un échec de la réparation.
        [RepairKind.RenewIp] =
        [
            new("ipconfig.exe", "/release", Short, MustSucceed: false), new("ipconfig.exe", "/renew", Short, MustSucceed: false),
            new("ipconfig.exe", "/flushdns", Short, Always: true),
        ],
        // Seul le service Audio est arrêté (sans /y : aucun service dépendant de fabricant n'est coupé) ; « déjà démarré » n'est pas une erreur.
        [RepairKind.RestartAudio] =
        [
            new("net.exe", "stop audiosrv", Short, MustSucceed: false),
            new("net.exe", "start AudioEndpointBuilder", Short, MustSucceed: false, Always: true), new("net.exe", "start audiosrv", Short, MustSucceed: false, Always: true),
        ],
        [RepairKind.NetworkReset] =
        [
            new("ipconfig.exe", "/flushdns", Short), new("netsh.exe", "winsock reset", Short), new("netsh.exe", "int ip reset", Short, MustSucceed: false),
            new("netsh.exe", "int ipv6 reset", Short, MustSucceed: false),
        ],
        // wuauserv et BITS seulement (cryptsvc a des services dépendants, AppLocker par exemple) ; redémarrés quoi qu'il arrive.
        [RepairKind.WindowsUpdateReset] =
        [
            new("net.exe", "stop wuauserv", Short, MustSucceed: false), new("net.exe", "stop bits", Short, MustSucceed: false),
            new(string.Empty, string.Empty, Short, RenameFolder: "SoftwareDistribution"),
            new("net.exe", "start bits", Short, MustSucceed: false, Always: true), new("net.exe", "start wuauserv", Short, MustSucceed: false, Always: true),
        ],
    };

    /// <summary>🟠 : la réinitialisation réseau efface les adresses fixes et les réglages de VPN ; celle de Windows Update son historique.</summary>
    public static RiskLevel RiskOf(RepairKind repair) => repair is RepairKind.NetworkReset or RepairKind.WindowsUpdateReset ? RiskLevel.Conditional : RiskLevel.Safe;

    /// <summary>Redémarrage nécessaire après ces réparations.</summary>
    public static bool AlwaysNeedsReboot(RepairKind repair) => repair is RepairKind.NetworkReset;

    /// <summary>Chaque ligne affichée par l'outil (progression dans la page).</summary>
    public event EventHandler<string>? Output;

    /// <summary>La réparation a réussi mais demande un redémarrage.</summary>
    public event EventHandler? RebootRequired;

    public string UndoKind => Kind;

    public bool SupportsUndo => false;

    public static Finding FindingFor(RepairKind repair) => new(
        $"diagnostic.repair.{repair}".ToLowerInvariant(), DiagnosticModule.Id, Severity.Info,
        new LocalizedText($"Repair.{repair}.Title"), new LocalizedText($"Repair.{repair}.Explanation"),
        string.Join(" ; ", Steps[repair].Select(s => s.RenameFolder is { } f ? $"rename {f}" : $"{s.Tool} {s.Arguments}")),
        FixAvailable: true, Risk: RiskOf(repair), Reversible: false,
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
        Exception? error = null;
        foreach (var step in Steps[repair])
        {
            if (error is not null && !step.Always)
            {
                continue; // après un échec, seules les étapes « toujours » (redémarrage des services) sont faites
            }

            try
            {
                await RunStepAsync(repair, step, error is null ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException
                                           or OperationCanceledException)
            {
                error ??= ex;
            }
        }

        if (error is OperationCanceledException)
        {
            throw error;
        }

        if (error is not null)
        {
            throw new InvalidOperationException(error.Message, error);
        }

        if (AlwaysNeedsReboot(repair))
        {
            RebootRequired?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunStepAsync(RepairKind repair, RepairStep step, CancellationToken cancellationToken)
    {
        if (step.RenameFolder is { } folder)
        {
            Rename(folder);
            return;
        }

        var exit = await runner.RunHiddenAsync(step.Tool, step.Arguments, line => Output?.Invoke(this, line), cancellationToken, step.Timeout)
            .ConfigureAwait(false);
        switch (exit)
        {
            case 0:
                return;
            case RebootRequiredCode when repair != RepairKind.FlushDns:
                RebootRequired?.Invoke(this, EventArgs.Empty);
                return;
            case SourceNotFound:
                throw new InvalidOperationException($"{step.Tool}: repair source not found (0x800F081F) — Windows Update could not provide healthy files");
            case var _ when !step.MustSucceed:
                return;
            default:
                throw new InvalidOperationException($"{step.Tool} {step.Arguments} exited with code {exit} (0x{exit:X8})");
        }
    }

    /// <summary>Renomme un dossier de Windows en « .pcdoc-old » (l'ancien « .pcdoc-old » est supprimé) : Windows le recrée vide.</summary>
    private void Rename(string relative)
    {
        if (renameFolder is not null)
        {
            renameFolder(relative);
            return;
        }

        var windows = windowsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Path.Combine(windows, relative);
        var old = path + ".pcdoc-old";
        if (!Directory.Exists(path) || new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return;
        }

        if (Directory.Exists(old))
        {
            Directory.Delete(old, recursive: true); // Directory.Delete ne suit pas les liens
        }

        Directory.Move(path, old);
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
