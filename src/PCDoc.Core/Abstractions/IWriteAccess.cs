namespace PCDoc.Core.Abstractions;

/// <summary>Écritures dans le registre (utilisées uniquement par les correcteurs, après confirmation).</summary>
public interface IRegistryWriter
{
    void SetBinary(RegistryHive hive, string path, string name, byte[] value);

    void DeleteValue(RegistryHive hive, string path, string name);

    /// <summary>Chemin complet pour un fichier .reg (ex. HKEY_USERS\S-1-5-21-…\Software\…).</summary>
    string FullPath(RegistryHive hive, string path);
}

/// <summary>Type de démarrage des services (API officielle du gestionnaire de services).</summary>
public interface IServiceControl
{
    ServiceStartMode GetStartMode(string serviceName);

    void SetStartMode(string serviceName, ServiceStartMode mode);
}

/// <summary>Activation des tâches planifiées.</summary>
public interface ITaskControl
{
    /// <summary>Null si la tâche n'existe pas.</summary>
    bool? IsEnabled(string taskPath);

    void SetEnabled(string taskPath, bool enabled);
}

/// <summary>Dossier de sauvegarde réservé aux administrateurs (%ProgramData%\PCDoc\backup).</summary>
public interface IBackupStore
{
    /// <summary>Enregistre un fichier .reg d'origine ; renvoie son chemin.</summary>
    string SaveRegistryBackup(string name, string regFileContent);
}
