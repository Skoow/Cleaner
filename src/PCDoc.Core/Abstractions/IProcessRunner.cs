namespace PCDoc.Core.Abstractions;

/// <summary>
/// Lance un outil de Windows **caché** (aucune fenêtre, règle de la fenêtre unique) et transmet sa sortie ligne par ligne.
/// </summary>
public interface IProcessRunner
{
    /// <param name="system32Tool">Nom d'un exécutable de System32 (jamais un chemin libre).</param>
    /// <param name="timeout">Durée maximale (1 h par défaut) ; dépassée → <see cref="TimeoutException"/>.</param>
    Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken,
        TimeSpan? timeout = null);
}
