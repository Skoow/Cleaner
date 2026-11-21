using System.IO;
using PCDoc.Core.Settings;
using PCDoc.Presentation;

namespace PCDoc.App;

/// <summary>
/// Supprime le fichier de paramètres et les journaux de PCDoc. Le journal du jour, ouvert par l'application,
/// est vidé à la prochaine ouverture : on ignore les fichiers verrouillés plutôt que d'échouer.
/// </summary>
public sealed class AppDataCleaner : IAppDataCleaner
{
    public void Clear()
    {
        TryDelete(AppPaths.SettingsFile);
        if (Directory.Exists(AppPaths.LogsDir))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDir, "*.log"))
            {
                TryDelete(file);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier en cours d'utilisation : ignoré.
        }
    }
}
