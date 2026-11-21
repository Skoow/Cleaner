using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure;

/// <inheritdoc />
public sealed class EventLogQuery : IEventLogQuery
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public IReadOnlyList<EventRecordData> Query(string logName, IReadOnlyCollection<int> eventIds, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        if (eventIds.Count == 0 || maxCount <= 0)
        {
            return [];
        }

        var filter = string.Join(" or ", eventIds.Select(id => "EventID=" + id.ToString(CultureInfo.InvariantCulture)));
        var query = new System.Diagnostics.Eventing.Reader.EventLogQuery(logName, PathType.LogName, $"*[System[({filter})]]")
        {
            ReverseDirection = true, // plus récent d'abord
        };

        var results = new List<EventRecordData>();
        try
        {
            using var reader = new EventLogReader(query);
            while (results.Count < maxCount)
            {
                using var record = reader.ReadEvent();
                if (record is null)
                {
                    break;
                }

                results.Add(new EventRecordData(record.Id, record.TimeCreated ?? DateTimeOffset.MinValue.DateTime,
                    ReadData(record.ToXml())));
            }
        }
        catch (Exception ex) when (ex is EventLogNotFoundException or EventLogException or UnauthorizedAccessException)
        {
            return results;
        }

        return results;
    }

    private static Dictionary<string, string> ReadData(string xml)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit };
        using var text = new StringReader(xml);
        using var xmlReader = System.Xml.XmlReader.Create(text, settings);
        var doc = XDocument.Load(xmlReader);
        foreach (var item in doc.Descendants(Ns + "Data"))
        {
            var name = (string?)item.Attribute("Name");
            if (!string.IsNullOrEmpty(name))
            {
                data[name] = item.Value;
            }
        }

        return data;
    }
}
