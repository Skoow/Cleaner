using System.Management;
using System.Runtime.InteropServices;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure;

/// <inheritdoc />
public sealed class WmiQuery : IWmiQuery
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string ns, string wql)
    {
        try
        {
            var scope = new ManagementScope(ns);
            scope.Connect();
            var options = new System.Management.EnumerationOptions { Timeout = Timeout, ReturnImmediately = true, Rewindable = false };
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), options);
            using var results = searcher.Get();
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            foreach (var item in results)
            {
                using (item)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in item.Properties)
                    {
                        row[property.Name] = property.Value;
                    }

                    rows.Add(row);
                }
            }

            return rows;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException
                                       or ArgumentException or TimeoutException)
        {
            throw new WmiUnavailableException(ns, wql, ex);
        }
    }
}
