using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Tests.Fakes;

/// <summary>Faux journal : renvoie les événements demandés, du plus récent au plus ancien.</summary>
public sealed class FakeEventLogQuery(params EventRecordData[] events) : IEventLogQuery
{
    public string? LastLog { get; private set; }

    /// <summary>Journal attribué à chaque événement (par défaut : tous les journaux).</summary>
    public Dictionary<EventRecordData, string> Logs { get; } = [];

    public IReadOnlyList<EventRecordData> Query(string logName, IReadOnlyCollection<int> eventIds, int maxCount, string? provider = null)
    {
        LastLog = logName;
        return [.. events
            .Where(e => eventIds.Contains(e.EventId))
            .Where(e => provider is null || e.Provider is null || e.Provider == provider)
            .Where(e => !Logs.TryGetValue(e, out var log) || log == logName)
            .OrderByDescending(e => e.TimeCreated).Take(maxCount)];
    }

    public FakeEventLogQuery In(string logName, EventRecordData e)
    {
        Logs[e] = logName;
        return this;
    }

    public static EventRecordData Event(int id, DateTimeOffset at, params (string Key, string Value)[] data) =>
        new(id, at, data.ToDictionary(d => d.Key, d => d.Value));
}
