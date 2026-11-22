namespace PCDoc.Core.Settings;

/// <summary>
/// Dossiers de PCDoc. Mode portable (fichier « portable.txt » à côté de PCDoc.exe, par exemple sur une clé USB) :
/// réglages, journaux techniques et profils de technicien voyagent avec l'outil. Les sauvegardes, le journal
/// d'annulation et les rapports restent toujours dans le dossier protégé du PC (%ProgramData%\PCDoc).
/// </summary>
public static class AppPaths
{
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    public static string DataRoot { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "PCDoc-data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.DataFolderName);

    public static string LogsDir => Path.Combine(DataRoot, "logs");

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    /// <summary>Profils de technicien (choix de la page « Nouveau PC » réutilisables sur d'autres PC).</summary>
    public static string ProfilesDir => Path.Combine(DataRoot, "profiles");
}
