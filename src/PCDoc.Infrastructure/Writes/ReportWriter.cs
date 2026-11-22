using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Activity;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Rapports dans %ProgramData%\PCDoc\reports : dossier réservé aux administrateurs en écriture, lisible par
/// l'utilisateur cible. Écrire dans un dossier de l'utilisateur avec les droits d'administrateur permettrait de
/// rediriger l'écriture par un lien ; ici personne d'autre ne peut rien y placer.
/// </summary>
public sealed class ReportWriter(IUserProfileResolver profiles) : IReportWriter
{
    public string Save(string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid report name", nameof(fileName));
        }

        ArgumentNullException.ThrowIfNull(content);
        var folder = SecureFolders.EnsureReadableChild("reports", new SecurityIdentifier(profiles.Resolve().Sid));
        var path = Path.Combine(folder, fileName);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        stream.Write(Encoding.UTF8.GetPreamble());
        stream.Write(Encoding.UTF8.GetBytes(text));
        return path;
    }

    /// <summary>
    /// Ouvre l'Explorateur seulement si l'utilisateur cible est celui du processus (l'Explorateur déjà ouvert, sans
    /// élévation, prend alors la demande). Sinon un Explorateur administrateur serait donné à un utilisateur standard.
    /// </summary>
    public void OpenFolder(string filePath)
    {
        if (File.Exists(filePath) && profiles.Resolve().IsProcessUser)
        {
            using var _ = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                $"/select,\"{filePath}\"") { UseShellExecute = false });
        }
    }
}
