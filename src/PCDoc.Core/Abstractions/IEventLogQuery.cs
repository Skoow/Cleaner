namespace PCDoc.Core.Abstractions;

/// <summary>
/// Un événement : identifiant, date, champs de EventData (champs sans nom : « #0 », « #1 »…) et source.
/// </summary>
public sealed record EventRecordData(
    int EventId, DateTimeOffset TimeCreated, IReadOnlyDictionary<string, string> Data, string? Provider = null);

/// <summary>Lecture des journaux d'événements.</summary>
public interface IEventLogQuery
{
    /// <summary>Événements les plus récents d'abord. Journal absent ou refusé → liste vide.</summary>
    IReadOnlyList<EventRecordData> Query(string logName, IReadOnlyCollection<int> eventIds, int maxCount, string? provider = null);
}
