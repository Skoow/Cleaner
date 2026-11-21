namespace PCDoc.Core.Settings;

/// <summary>Lecture et écriture des paramètres.</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
