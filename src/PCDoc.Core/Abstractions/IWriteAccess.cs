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

/// <summary>Tâche planifiée de l'entretien automatique et copie de PCDoc (Program Files, clé USB portable).</summary>
public interface IMaintenanceScheduler
{
    bool IsScheduled();

    /// <summary>Null si l'entretien peut être planifié ; sinon la raison (clé de traduction).</summary>
    string? BlockedReason();

    void Schedule(DayOfWeek day, int hour);

    void Unschedule();

    /// <summary>Copie PCDoc dans Program Files\PCDoc (dossier réservé aux administrateurs) ; renvoie le chemin de l'exe.</summary>
    string InstallToProgramFiles();

    /// <summary>Copie PCDoc en mode portable à la racine d'une clé USB (dossier PCDoc + portable.txt).</summary>
    string CopyPortable(string removableRoot);
}
