namespace PCDoc.Core.Settings;

/// <summary>
/// Dossiers de PCDoc. Mode portable (fichier « portable.txt » à côté de PCDoc.exe, par exemple sur une clé USB) :
/// réglages, journaux techniques et profils de technicien voyagent avec l'outil. Les sauvegardes, le journal
/// d'annulation et les rapports restent toujours dans le dossier protégé du PC (%ProgramData%\PCDoc).
/// </summary>
public static class AppPaths
{
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    private static string LocalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.DataFolderName);

    /// <summary>Clé protégée en écriture, ou dossier devenu un lien : retour au dossier local (jamais d'écriture à travers un lien).</summary>
    public static string DataRoot { get; } = IsPortable && IsUsable(Path.Combine(AppContext.BaseDirectory, "PCDoc-data"))
        ? Path.Combine(AppContext.BaseDirectory, "PCDoc-data")
        : LocalRoot;

    private static bool IsUsable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string LogsDir => Path.Combine(DataRoot, "logs");

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    /// <summary>Profils de technicien (choix de la page « Nouveau PC » réutilisables sur d'autres PC).</summary>
    public static string ProfilesDir => Path.Combine(DataRoot, "profiles");
}
