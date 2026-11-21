using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Tests.Fakes;

/// <summary>Faux journal : renvoie les événements demandés, du plus récent au plus ancien.</summary>
public sealed class FakeEventLogQuery(params EventRecordData[] events) : IEventLogQuery
{
    public string? LastLog { get; private set; }

    public IReadOnlyList<EventRecordData> Query(string logName, IReadOnlyCollection<int> eventIds, int maxCount)
    {
        LastLog = logName;
        return [.. events.Where(e => eventIds.Contains(e.EventId)).OrderByDescending(e => e.TimeCreated).Take(maxCount)];
    }

    public static EventRecordData Event(int id, DateTimeOffset at, params (string Key, string Value)[] data) =>
        new(id, at, data.ToDictionary(d => d.Key, d => d.Value));
}
