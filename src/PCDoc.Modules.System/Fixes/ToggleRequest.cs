using System.Text.Json;
using System.Text.Json.Serialization;
using PCDoc.Core.Abstractions;
using PCDoc.Modules.System.Advisor;
using PCDoc.Modules.System.Autoruns;

namespace PCDoc.Modules.System.Fixes;

/// <summary>Ce qu'il faut activer ou désactiver (stocké dans <c>Finding.FixData</c>).</summary>
public sealed record ToggleRequest(
    AutorunKind Kind,
    ToggleAction Action,
    string Name,
    RegistryHive? Hive = null,
    string? ApprovedSubKey = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ToggleRequest ForStartup(string name, StartupToggleTarget target, ToggleAction action)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new(AutorunKind.Startup, action, target.ValueName, target.Hive, target.ApprovedSubKey);
    }

    public static ToggleRequest ForService(string serviceName, ToggleAction action) => new(AutorunKind.Service, action, serviceName);

    public static ToggleRequest ForTask(string taskPath, ToggleAction action) => new(AutorunKind.ScheduledTask, action, taskPath);

    /// <summary>Requête pour un élément de la page Système, selon le conseil (null si l'élément n'est pas modifiable).</summary>
    public static ToggleRequest? For(AutorunEntry entry, ToggleAction action)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind switch
        {
            AutorunKind.Startup when entry.Startup is { } t => ForStartup(entry.Name, t, action),
            AutorunKind.Service => ForService(entry.ServiceName ?? entry.Name, action),
            AutorunKind.ScheduledTask => ForTask(entry.Name, action),
            _ => null,
        };
    }

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static ToggleRequest? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ToggleRequest>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
