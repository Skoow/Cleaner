namespace PCDoc.Core.Abstractions;

/// <summary>Tests d'existence de fichiers et développement des variables d'environnement.</summary>
public interface IFileProbe
{
    bool Exists(string path);

    /// <summary>Développe %VARIABLE% avec les valeurs du profil cible quand elles le concernent.</summary>
    string ExpandEnvironment(string value);
}
