namespace PCDoc.Core.Abstractions;

/// <summary>Un fichier trouvé lors d'un parcours.</summary>
public sealed record FileEntry(string Path, long Size, DateTimeOffset LastWrite);

/// <summary>
/// Parcours et suppression de fichiers pour le nettoyage. Les liens (jonctions, liens symboliques) ne sont jamais suivis.
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);

    /// <summary>Sous-dossiers directs (hors liens).</summary>
    IReadOnlyList<string> ListDirectories(string path);

    /// <summary>Fichiers sous <paramref name="root"/>, récursivement si demandé, sans traverser de lien. Erreurs d'accès ignorées.</summary>
    IEnumerable<FileEntry> EnumerateFiles(string root, bool recursive);

    /// <summary>Supprime un fichier ; faux s'il est utilisé ou protégé.</summary>
    bool TryDeleteFile(string path);

    /// <summary>Supprime les sous-dossiers vides sous <paramref name="root"/> (jamais <paramref name="root"/> lui-même).</summary>
    void DeleteEmptySubdirectories(string root);
}

/// <summary>Corbeille de Windows (sans aucune fenêtre).</summary>
public interface IRecycleBin
{
    (long Bytes, long Items) Query();

    void Empty();
}
