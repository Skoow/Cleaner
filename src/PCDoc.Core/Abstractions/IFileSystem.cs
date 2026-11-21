namespace PCDoc.Core.Abstractions;

/// <summary>Un fichier trouvé lors d'un parcours. <paramref name="Created"/> : date de création (une extraction d'archive
/// garde l'ancienne date de modification, pas la date de création).</summary>
public sealed record FileEntry(string Path, long Size, DateTimeOffset LastWrite, DateTimeOffset Created = default)
{
    /// <summary>Date la plus récente entre création et modification.</summary>
    public DateTimeOffset LastTouched => Created > LastWrite ? Created : LastWrite;
}

/// <summary>Dossiers système de confiance (API Windows, jamais les variables d'environnement modifiables par l'utilisateur).</summary>
public interface IKnownFolders
{
    string Windows { get; }

    string ProgramData { get; }
}

/// <summary>Conditions qui interdisent temporairement un nettoyage (mise à jour de Windows en cours…).</summary>
public interface ICleanupGuards
{
    bool IsWindowsUpdateBusy();
}

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

    /// <summary>
    /// Supprime un fichier seulement si son chemin réel (résolu par le système, liens compris) est sous
    /// <paramref name="allowedRoot"/> et qu'il n'a qu'un seul lien physique ; faux sinon (ou s'il est utilisé).
    /// </summary>
    bool TryDeleteFile(string path, string allowedRoot);

    /// <summary>Supprime les sous-dossiers vides sous <paramref name="root"/> (jamais <paramref name="root"/> lui-même),
    /// avec la même vérification du chemin réel.</summary>
    void DeleteEmptySubdirectories(string root);
}

/// <summary>Corbeille de Windows (sans aucune fenêtre).</summary>
public interface IRecycleBin
{
    (long Bytes, long Items) Query();

    void Empty();
}
