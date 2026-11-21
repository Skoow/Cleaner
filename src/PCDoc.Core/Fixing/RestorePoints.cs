namespace PCDoc.Core.Fixing;

/// <summary>Résultat de la demande de point de restauration.</summary>
public enum RestorePointOutcome
{
    Created,

    /// <summary>Un point de moins de 24 h existe : Windows refuserait d'en créer un autre, on le réutilise.</summary>
    ReusedRecent,

    /// <summary>La protection du système est désactivée sur le disque de Windows.</summary>
    ProtectionDisabled,

    Failed,
}

/// <summary>Points de restauration de Windows.</summary>
public interface IRestorePointService
{
    Task<RestorePointOutcome> EnsureAsync(string description, CancellationToken cancellationToken);
}
