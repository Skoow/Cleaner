namespace PCDoc.Core.Abstractions;

/// <summary>Tests d'existence de fichiers et développement des variables d'environnement.</summary>
public interface IFileProbe
{
    bool Exists(string path);

    /// <summary>Développe %VARIABLE% avec les valeurs du profil cible quand elles le concernent.</summary>
    string ExpandEnvironment(string value);

    /// <summary>Fichiers d'un dossier (non récursif). Dossier absent ou refusé → liste vide.</summary>
    IReadOnlyList<string> ListFiles(string directory);

    /// <summary>Contenu d'un petit fichier (raccourci .lnk…), ou null s'il est absent, refusé ou plus gros que la limite.</summary>
    byte[]? ReadSmallFile(string path, int maxBytes);
}
