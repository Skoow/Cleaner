namespace PCDoc.Core.Abstractions;

/// <summary>Un événement : identifiant, date et champs nommés de EventData.</summary>
public sealed record EventRecordData(int EventId, DateTimeOffset TimeCreated, IReadOnlyDictionary<string, string> Data);

/// <summary>Lecture des journaux d'événements.</summary>
public interface IEventLogQuery
{
    /// <summary>Événements les plus récents d'abord. Journal absent ou refusé → liste vide.</summary>
    IReadOnlyList<EventRecordData> Query(string logName, IReadOnlyCollection<int> eventIds, int maxCount);
}
