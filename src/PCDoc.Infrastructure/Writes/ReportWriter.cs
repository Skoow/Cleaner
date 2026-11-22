using System.Diagnostics;
using System.IO;
using System.Text;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Activity;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Rapport dans « Documents\PCDoc » de l'utilisateur cible. Ce dossier lui appartient : aucun lien n'est suivi dans le
/// chemin et le fichier est toujours nouveau (jamais d'écrasement), pour qu'un lien ne puisse pas rediriger l'écriture.
/// </summary>
public sealed class ReportWriter(IUserProfileResolver profiles) : IReportWriter
{
    public string Save(string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid report name", nameof(fileName));
        }

        var profile = profiles.Resolve().ProfilePath;
        var documents = Path.Combine(profile, "Documents");
        var folder = Path.Combine(Directory.Exists(documents) ? documents : profile, "PCDoc");
        Directory.CreateDirectory(folder);
        if (!TrustedPaths.HasNoLinks(folder, Path.GetDirectoryName(profile)!))
        {
            throw new InvalidOperationException("The report folder contains a link");
        }

        var path = Path.Combine(folder, fileName);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content.Replace("\n", "\r\n", StringComparison.Ordinal).Replace("\r\r\n", "\r\n", StringComparison.Ordinal))).ToArray();
        stream.Write(bytes);
        return path;
    }

    public void OpenFolder(string filePath)
    {
        if (File.Exists(filePath))
        {
            using var _ = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                $"/select,\"{filePath}\"") { UseShellExecute = false });
        }
    }
}
