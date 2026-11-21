using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCDoc.Core.Settings;

/// <summary>
/// Paramètres dans un fichier JSON. Le fichier est une donnée non fiable (modifiable sans droits admin) :
/// toute valeur invalide donne les paramètres par défaut, jamais une erreur.
/// </summary>
public sealed class JsonSettingsStore(string filePath, CultureInfo uiCulture) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        MaxDepth = 8,
    };

    private const long MaxFileSize = 64 * 1024;

    public AppSettings Load()
    {
        var defaults = AppSettings.DefaultsFor(uiCulture);
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length > MaxFileSize)
            {
                return defaults;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), Options);
            return loaded is not null && Enum.IsDefined(loaded.Language) && Enum.IsDefined(loaded.Theme)
                ? loaded
                : defaults;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        Directory.CreateDirectory(directory);
        var temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, filePath, overwrite: true);
    }
}
