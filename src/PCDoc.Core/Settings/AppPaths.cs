namespace PCDoc.Core.Settings;

/// <summary>Emplacements des données de PCDoc pour le compte qui l'a lancé (spec v2 §3.4).</summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.DataFolderName);

    public static string LogsDir => Path.Combine(DataRoot, "logs");

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
}
