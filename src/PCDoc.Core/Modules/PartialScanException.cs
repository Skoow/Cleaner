using PCDoc.Core.Findings;

namespace PCDoc.Core.Modules;

/// <summary>
/// Analyse partielle : certaines sources d'un module ont échoué. Les problèmes trouvés par les autres sources sont
/// conservés, et le Bilan affiche « Analyse incomplète » pour ce module.
/// </summary>
public sealed class PartialScanException : Exception
{
    public PartialScanException(IReadOnlyList<Finding> findings, IReadOnlyList<string> failedSources)
        : base("Partial scan: " + string.Join(", ", failedSources))
    {
        Findings = findings;
        FailedSources = failedSources;
    }

    public PartialScanException()
        : this([], [])
    {
    }

    public PartialScanException(string message)
        : base(message)
    {
        Findings = [];
        FailedSources = [];
    }

    public PartialScanException(string message, Exception innerException)
        : base(message, innerException)
    {
        Findings = [];
        FailedSources = [];
    }

    public IReadOnlyList<Finding> Findings { get; }

    public IReadOnlyList<string> FailedSources { get; }
}
