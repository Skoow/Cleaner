using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;

namespace PCDoc.Core.Fixing;

/// <summary>
/// Journal d'annulation transactionnel : fichier JSON Lines en ajout seul. Une entrée est écrite « en attente »
/// avant la modification, puis son état est mis à jour. Une ligne illisible est ignorée.
/// </summary>
public sealed class UndoJournal(string filePath, IClock clock)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly Lock _gate = new();

    public string FilePath => filePath;

    public UndoEntry Begin(UndoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var stamped = entry with { Timestamp = clock.Now, Status = UndoStatus.Pending };
        Append(new Line("begin", stamped, null, null));
        return stamped;
    }

    public void Mark(string id, UndoStatus status) => Append(new Line("mark", null, id, status));

    /// <summary>Toutes les entrées avec leur dernier état, la plus récente d'abord.</summary>
    public IReadOnlyList<UndoEntry> Entries()
    {
        lock (_gate)
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var entries = new Dictionary<string, UndoEntry>(StringComparer.Ordinal);
            foreach (var raw in File.ReadLines(filePath))
            {
                Line? line;
                try
                {
                    line = JsonSerializer.Deserialize<Line>(raw, Options);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (line?.Op == "begin" && line.Entry is { } e)
                {
                    entries[e.Id] = e;
                }
                else if (line?.Op == "mark" && line.Id is { } id && line.Status is { } status && entries.TryGetValue(id, out var existing))
                {
                    entries[id] = existing with { Status = status };
                }
            }

            return [.. entries.Values.OrderByDescending(e => e.Timestamp)];
        }
    }

    /// <summary>Corrections interrompues (coupure de courant, plantage) : à signaler au démarrage.</summary>
    public IReadOnlyList<UndoEntry> Interrupted() => [.. Entries().Where(e => e.Status == UndoStatus.Pending)];

    private void Append(Line line)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(JsonSerializer.Serialize(line, Options));
            writer.Flush();
            stream.Flush(flushToDisk: true); // écrit vraiment sur le disque avant de modifier le système
        }
    }

    private sealed record Line(string Op, UndoEntry? Entry, string? Id, UndoStatus? Status);
}
