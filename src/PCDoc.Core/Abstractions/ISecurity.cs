namespace PCDoc.Core.Abstractions;

/// <summary>MpCmdRun.exe de Microsoft Defender (Program Files\Windows Defender, dossier réservé aux administrateurs).</summary>
public interface IDefenderCommand
{
    /// <summary>Lance MpCmdRun caché. Defender absent → <see cref="FileNotFoundException"/>.</summary>
    Task<int> RunAsync(string arguments, Action<string>? onOutput, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed record RemovableDrive(string Root, string? Label);

public sealed record RootItem(string Path, bool IsDirectory, FileAttributes Attributes);

/// <summary>Clés USB et cartes mémoire : racine seulement, sans jamais suivre de lien.</summary>
public interface IRemovableDrives
{
    IReadOnlyList<RemovableDrive> List();

    /// <summary>Vrai si <paramref name="root"/> (« E:\ ») est la racine d'un lecteur amovible branché.</summary>
    bool IsRemovableRoot(string root);

    IReadOnlyList<RootItem> RootItems(string root);

    void SetAttributes(string path, FileAttributes attributes);

    void Rename(string path, string newPath);
}
