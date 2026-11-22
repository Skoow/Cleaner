using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.System.Autoruns;

namespace PCDoc.Modules.Security;

/// <summary>Connexion affichée, avec un signal si le programme tourne depuis un dossier inhabituel (Temp, Téléchargements…).</summary>
public sealed record ConnectionRow(NetConnection Connection, bool Suspicious);

/// <summary>Activité réseau : connexions établies et ports en écoute, triés (suspects en premier).</summary>
public static class NetworkActivity
{
    private static readonly string[] UnusualFolders = [@"\AppData\Local\Temp\", @"\Downloads\", @"\Users\Public\", @"\Windows\Temp\", @"\$Recycle.Bin\"];

    public static IReadOnlyList<ConnectionRow> Analyze(IEnumerable<NetConnection> connections) =>
        [.. connections
            .Where(c => c.State is "ESTABLISHED" or "LISTEN" or "UDP")
            .Select(c => new ConnectionRow(c, c.ProcessPath is { } p && UnusualFolders.Any(f => p.Contains(f, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(r => r.Suspicious)
            .ThenBy(r => r.Connection.State == "ESTABLISHED" ? 0 : 1)
            .ThenBy(r => r.Connection.ProcessName, StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// Bloque une extension de Chrome, Edge ou Brave par la stratégie officielle ExtensionInstallBlocklist (HKLM) : le
/// navigateur la désactive et la retire. Réversible (la valeur ajoutée est supprimée à l'annulation).
/// </summary>
public sealed partial class ExtensionBlockFixer(IRegistryReader reader, IRegistryValueWriter writer) : SecurityFixerBase<ExtensionBlockFixer.Data>("security.extension")
{
    public static readonly IReadOnlyDictionary<string, string> PolicyKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Chrome"] = @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallBlocklist",
        ["Edge"] = @"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallBlocklist",
        ["Brave"] = @"SOFTWARE\Policies\BraveSoftware\Brave\ExtensionInstallBlocklist",
    };

    public static bool CanBlock(BrowserExtension extension) =>
        extension is not null && PolicyKeys.ContainsKey(extension.Browser) && ChromiumId().IsMatch(extension.Id);

    public static Finding FindingFor(BrowserExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return new Finding($"security.extension.{extension.Browser}.{extension.Id}", SecurityModule.Id, Severity.Info,
            new LocalizedText("Security.extension.Title", extension.Name, extension.Browser), new LocalizedText("Security.extension.Explanation"),
            $"{extension.Browser} {extension.Id} {extension.Version}", FixAvailable: CanBlock(extension), Risk: RiskLevel.Safe, Reversible: true,
            FixData: JsonSerializer.Serialize(new Data(extension.Browser, extension.Id)));
    }

    public bool IsBlocked(string browser, string id) =>
        PolicyKeys.TryGetValue(browser, out var key)
        && reader.GetValueNames(RegistryHive.LocalMachine, key, RegistryView.Registry64)
            .Any(n => string.Equals(reader.GetValue(RegistryHive.LocalMachine, key, n, RegistryView.Registry64) as string, id, StringComparison.Ordinal));

    public override Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Guarded(finding);
        var key = PolicyKeys[data.Browser];
        var used = reader.GetValueNames(RegistryHive.LocalMachine, key, RegistryView.Registry64).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = Enumerable.Range(1, 10_000).First(i => !used.Contains(i.ToString(global::System.Globalization.CultureInfo.InvariantCulture)));
        var saved = new Saved(data.Browser, data.Id, index.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
        _planned[data.Browser + data.Id] = saved;
        return Task.FromResult(UndoEntry.Create(finding.Id, UndoKind, Json(saved), "Security.extension.Title", [data.Id, data.Browser]));
    }

    public override Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Guarded(finding);
        if (!_planned.TryRemove(data.Browser + data.Id, out var saved))
        {
            throw new InvalidOperationException("Extension block not prepared");
        }

        writer.SetValue(RegistryHive.LocalMachine, PolicyKeys[saved.Browser], saved.ValueName, saved.Id);
        return Task.CompletedTask;
    }

    public override Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var s = Read(entry);
        return Task.FromResult(string.Equals(reader.GetValue(RegistryHive.LocalMachine, PolicyKeys[s.Browser], s.ValueName, RegistryView.Registry64) as string,
            s.Id, StringComparison.Ordinal));
    }

    public override Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken)
    {
        var s = Read(entry);
        writer.SetValue(RegistryHive.LocalMachine, PolicyKeys[s.Browser], s.ValueName, null);
        return Task.CompletedTask;
    }

    protected override Data? Validate(Finding finding, Data data) =>
        data.Browser is not null && data.Id is not null && PolicyKeys.ContainsKey(data.Browser) && ChromiumId().IsMatch(data.Id)
        && finding.Id == $"security.extension.{data.Browser}.{data.Id}" && !IsBlocked(data.Browser, data.Id)
            ? data
            : null;

    private static Saved Read(UndoEntry entry) =>
        Payload<Saved>(entry) is { } s && PolicyKeys.ContainsKey(s.Browser) && ChromiumId().IsMatch(s.Id) && ValueName().IsMatch(s.ValueName)
            ? s
            : throw new InvalidOperationException("Invalid undo entry");

    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, Saved> _planned = new(StringComparer.Ordinal);

    [GeneratedRegex("^[a-p]{32}$")]
    private static partial Regex ChromiumId();

    [GeneratedRegex("^[0-9]{1,5}$")]
    private static partial Regex ValueName();

    public sealed record Data(string Browser, string Id);

    private sealed record Saved(string Browser, string Id, string ValueName);
}

/// <summary>Résultat VirusTotal d'un programme lancé au démarrage.</summary>
public sealed record VirusTotalRow(string Name, string Path, string Sha256, VirusTotalStats? Stats, string? Error);

/// <summary>
/// Vérifie sur VirusTotal les programmes lancés au démarrage, par leur empreinte SHA-256 uniquement (aucun fichier
/// envoyé). Une requête toutes les 16 s : la clé gratuite permet 4 requêtes par minute.
/// </summary>
public sealed class VirusTotalCheck(IAutorunSource autoruns, IVirusTotal client, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const long MaxFileSize = 256L * 1024 * 1024;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<IReadOnlyList<VirusTotalRow>> RunAsync(Action<VirusTotalRow>? progress, CancellationToken cancellationToken)
    {
        var entries = await Task.Run(autoruns.CollectAll, cancellationToken).ConfigureAwait(false);
        var files = entries.Entries
            .Where(e => e.FilePath is not null && e.Kind is AutorunKind.Startup or AutorunKind.ScheduledTask && !e.IsMicrosoft)
            .GroupBy(e => e.FilePath!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var rows = new List<VirusTotalRow>();
        var first = true;
        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = await Task.Run(() => Hash(entry.FilePath!), cancellationToken).ConfigureAwait(false);
            if (hash is null)
            {
                continue;
            }

            if (!first)
            {
                await _delay(TimeSpan.FromSeconds(16), cancellationToken).ConfigureAwait(false);
            }

            first = false;
            VirusTotalRow row;
            try
            {
                row = new VirusTotalRow(entry.Name, entry.FilePath!, hash, await client.LookupAsync(hash, cancellationToken).ConfigureAwait(false), null);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or KeyNotFoundException
                                           or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                row = new VirusTotalRow(entry.Name, entry.FilePath!, hash, null, ex.Message);
            }

            rows.Add(row);
            progress?.Invoke(row);
            if (row.Error?.Contains("refused the API key", StringComparison.Ordinal) == true)
            {
                break; // clé refusée : inutile d'attendre 16 s par fichier restant
            }
        }

        return rows;
    }

    internal static string? Hash(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileSize)
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
