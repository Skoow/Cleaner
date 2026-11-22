using System.Text;
using PCDoc.Core.Abstractions;

namespace PCDoc.Core.Activity;

/// <summary>Journal lisible de tout ce que fait PCDoc (date, heure, catégorie, message), exportable en .txt.</summary>
public interface IActivityLog
{
    void Write(string category, string message);

    /// <summary>Toutes les lignes, de la plus ancienne à la plus récente.</summary>
    IReadOnlyList<string> ReadAll();
}

/// <summary>Journal désactivé (dossier protégé indisponible).</summary>
public sealed class NullActivityLog : IActivityLog
{
    public void Write(string category, string message)
    {
    }

    public IReadOnlyList<string> ReadAll() => [];
}

/// <summary>
/// Fichier texte UTF-8 (une ligne par action) dans le dossier protégé de PCDoc. Les sauts de ligne d'un message sont
/// remplacés pour qu'une ligne corresponde toujours à une action.
/// </summary>
public sealed class TextActivityLog(string path, IClock clock, long maxBytes = 4 * 1024 * 1024) : IActivityLog
{
    private readonly Lock _gate = new();

    public string Path => path;

    public void Write(string category, string message)
    {
        var line = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{clock.Now.ToLocalTime():yyyy-MM-dd HH:mm:ss}  [{Clean(category)}]  {Clean(message)}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                // Taille limitée : l'ancien journal devient « .old.txt » (le précédent est remplacé).
                if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
                {
                    File.Move(path, System.IO.Path.ChangeExtension(path, ".old.txt"), overwrite: true);
                }

                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                var bytes = Encoding.UTF8.GetBytes(line);
                stream.Write(bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Le journal ne doit jamais empêcher une action ; l'échec est visible dans les journaux techniques.
            }
        }
    }

    public IReadOnlyList<string> ReadAll()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    private static string Clean(string? text) =>
        (text ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " | ", StringComparison.Ordinal).Trim();
}

/// <summary>Rapport texte : en-tête (logiciel, PC, date) puis les actions du journal.</summary>
public static class ActivityReport
{
    public static string Build(IReadOnlyList<string> lines, string product, string version, string machine, string windows, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var text = new StringBuilder()
            .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{product} {version} — rapport d'activité / activity report")
            .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"PC : {machine}")
            .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Windows : {windows}")
            .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Date : {now.ToLocalTime():yyyy-MM-dd HH:mm:ss}")
            .AppendLine(new string('=', 72));
        if (lines.Count == 0)
        {
            text.AppendLine("(aucune action / no action)");
        }

        foreach (var line in lines)
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }
}

/// <summary>Enregistre un rapport dans les Documents de l'utilisateur cible (jamais en suivant un lien) et ouvre son dossier.</summary>
public interface IReportWriter
{
    /// <summary>Chemin du fichier créé.</summary>
    string Save(string fileName, string content);

    void OpenFolder(string filePath);
}
