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
public sealed record RepairStep(string Tool, string Arguments, TimeSpan Timeout, bool MustSucceed = true, string? RenameFolder = null);

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
        [RepairKind.RenewIp] = [new("ipconfig.exe", "/release", Short, MustSucceed: false), new("ipconfig.exe", "/renew", Short), new("ipconfig.exe", "/flushdns", Short)],
        [RepairKind.RestartAudio] =
        [
            new("net.exe", "stop audiosrv /y", Short, MustSucceed: false), new("net.exe", "stop AudioEndpointBuilder /y", Short, MustSucceed: false),
            new("net.exe", "start AudioEndpointBuilder", Short, MustSucceed: false), new("net.exe", "start audiosrv", Short),
        ],
        [RepairKind.NetworkReset] =
        [
            new("ipconfig.exe", "/flushdns", Short), new("netsh.exe", "winsock reset", Short), new("netsh.exe", "int ip reset", Short),
            new("netsh.exe", "int ipv6 reset", Short, MustSucceed: false),
        ],
        [RepairKind.WindowsUpdateReset] =
        [
            new("net.exe", "stop wuauserv /y", Short, MustSucceed: false), new("net.exe", "stop bits /y", Short, MustSucceed: false),
            new("net.exe", "stop cryptsvc /y", Short, MustSucceed: false),
            new(string.Empty, string.Empty, Short, RenameFolder: "SoftwareDistribution"), new(string.Empty, string.Empty, Short, RenameFolder: @"System32\catroot2"),
            new("net.exe", "start cryptsvc", Short, MustSucceed: false), new("net.exe", "start bits", Short, MustSucceed: false),
            new("net.exe", "start wuauserv", Short, MustSucceed: false),
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
        Exception? renameError = null;
        foreach (var step in Steps[repair])
        {
            if (step.RenameFolder is { } folder)
            {
                try
                {
                    Rename(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    renameError ??= ex; // les services sont tout de même redémarrés par les étapes suivantes
                }

                continue;
            }

            var exit = await runner.RunHiddenAsync(step.Tool, step.Arguments, line => Output?.Invoke(this, line), cancellationToken, step.Timeout)
                .ConfigureAwait(false);
            switch (exit)
            {
                case 0:
                    break;
                case RebootRequiredCode when repair != RepairKind.FlushDns:
                    RebootRequired?.Invoke(this, EventArgs.Empty);
                    break;
                case SourceNotFound:
                    throw new InvalidOperationException($"{step.Tool}: repair source not found (0x800F081F) — Windows Update could not provide healthy files");
                case var _ when !step.MustSucceed:
                    break;
                default:
                    throw new InvalidOperationException($"{step.Tool} {step.Arguments} exited with code {exit} (0x{exit:X8})");
            }
        }

        if (renameError is not null)
        {
            throw new InvalidOperationException($"A Windows Update folder is in use: {renameError.Message}", renameError);
        }

        if (AlwaysNeedsReboot(repair))
        {
            RebootRequired?.Invoke(this, EventArgs.Empty);
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
