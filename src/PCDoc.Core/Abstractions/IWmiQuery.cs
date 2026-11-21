namespace PCDoc.Core.Abstractions;

/// <summary>Requêtes WMI en lecture seule.</summary>
public interface IWmiQuery
{
    /// <summary>Exécute une requête WQL. Lève <see cref="WmiUnavailableException"/> si la classe ou l'espace de noms
    /// est absent ou refusé.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string ns, string wql);
}

/// <summary>Classe ou espace de noms WMI indisponible (absent, accès refusé, service arrêté).</summary>
public sealed class WmiUnavailableException : Exception
{
    public WmiUnavailableException(string ns, string query, Exception inner)
        : base($"WMI unavailable: {ns} / {query}: {inner?.Message}", inner)
    {
        Namespace = ns;
        QueryText = query;
    }

    public WmiUnavailableException()
    {
        Namespace = QueryText = string.Empty;
    }

    public WmiUnavailableException(string message)
        : base(message)
    {
        Namespace = QueryText = string.Empty;
    }

    public WmiUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
        Namespace = QueryText = string.Empty;
    }

    public string Namespace { get; }

    public string QueryText { get; }
}
