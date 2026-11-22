using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Core.Modules;

namespace PCDoc.Modules.Cleanup;

/// <summary>
/// Effacement définitif de fichiers choisis par l'utilisateur (contenu écrasé, puis supprimé) ou de l'espace libre
/// d'un disque (cipher /w). 🟠 et non annulable : confirmation renforcée. Sur un SSD, l'écrasement n'est pas garanti
/// (le contrôleur déplace les données) : l'explication le dit et conseille BitLocker.
/// </summary>
public sealed class SecureEraseFixer(ISecureEraser eraser, IProcessRunner runner) : IFixer
{
    public const string Kind = "cleanup.erase";
    public const int MaxPaths = 200;

    public string UndoKind => Kind;

    public bool SupportsUndo => false;

    /// <summary>Un point de restauration (cliché VSS) garderait une copie des fichiers effacés.</summary>
    public bool NeedsRestorePoint => false;

    public static Finding ForPaths(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        // Identifiant stable (SHA-256) ; le détail technique ne donne que le nombre d'éléments : le journal d'activité
        // ne doit pas garder les noms des fichiers effacés.
        var hash = Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(
            global::System.Text.Encoding.UTF8.GetBytes(string.Join('|', paths).ToUpperInvariant())))[..16];
        return new Finding($"cleanup.erase.{hash}", CleanupModule.Id, Severity.Info,
            new LocalizedText("Erase.Files.Title", paths.Count), new LocalizedText("Erase.Files.Explanation"), $"{paths.Count} item(s)",
            FixAvailable: true, Risk: RiskLevel.Conditional, Reversible: false, FixData: JsonSerializer.Serialize(new Data("files", [.. paths], null)));
    }

    public static Finding ForFreeSpace(string drive) => new($"cleanup.erase.free.{drive[0]}", CleanupModule.Id, Severity.Info,
        new LocalizedText("Erase.Free.Title", drive), new LocalizedText("Erase.Free.Explanation"), $"cipher.exe /w:{drive}",
        FixAvailable: true, Risk: RiskLevel.Conditional, Reversible: false, FixData: JsonSerializer.Serialize(new Data("free", [], drive)));

    public bool CanFix(Finding finding) => Parse(finding) is not null;

    public Task<UndoEntry> PrepareAsync(Finding finding, CancellationToken cancellationToken)
    {
        _ = Parse(finding) ?? throw Refused();
        return Task.FromResult(UndoEntry.Create(finding.Id, Kind, "{}", finding.Title.Key, [.. finding.Title.Args.Select(a => a?.ToString() ?? string.Empty)]));
    }

    public async Task ApplyAsync(Finding finding, CancellationToken cancellationToken)
    {
        var data = Parse(finding) ?? throw Refused();
        if (data.Mode == "free")
        {
            // cipher /w écrit des zéros, des uns puis des données aléatoires dans tout l'espace libre (long sur un grand disque).
            int exit;
            try
            {
                exit = await runner.RunHiddenAsync("cipher.exe", $"/w:{data.Drive}", null, cancellationToken, TimeSpan.FromHours(12)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                eraser.RemoveCipherLeftovers(data.Drive!); // arrêté en cours : son fichier temporaire remplirait le disque
                throw;
            }

            if (exit != 0)
            {
                eraser.RemoveCipherLeftovers(data.Drive!);
                throw new InvalidOperationException($"cipher.exe exited with code {exit}");
            }

            return;
        }

        var erased = 0;
        var failed = 0;
        await Task.Run(() =>
        {
            foreach (var path in data.Paths)
            {
                foreach (var file in eraser.Expand(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (eraser.Wipe(file))
                    {
                        erased++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                if (Directory.Exists(path))
                {
                    eraser.RemoveEmptyFolders(path);
                }
            }
        }, cancellationToken).ConfigureAwait(false);
        if (failed > 0)
        {
            throw new InvalidOperationException($"{erased} files erased, {failed} could not be erased (in use or protected)");
        }
    }

    public Task<bool> IsUnchangedSinceAsync(UndoEntry entry, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task UndoAsync(UndoEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException("Erased files cannot be restored");

    private static InvalidOperationException Refused() => new("Secure erase refused: invalid or protected path");

    private Data? Parse(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (string.IsNullOrEmpty(finding.FixData) || finding.FixData.Length > 64 * 1024 || (int)finding.Risk < (int)RiskLevel.Conditional)
        {
            return null;
        }

        try
        {
            var data = JsonSerializer.Deserialize<Data>(finding.FixData);
            return data switch
            {
                { Mode: "files", Paths: { Count: > 0 and <= MaxPaths } p } when p.All(eraser.IsAllowed) && finding.Id == ForPaths(p).Id => data,
                { Mode: "free", Drive: { Length: 3 } d } when char.IsAsciiLetter(d[0]) && d[1] == ':' && d[2] == '\\' && finding.Id == ForFreeSpace(d).Id => data,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Data(string Mode, List<string> Paths, string? Drive);
}
