using System.Text.Json;

namespace PCDoc.Presentation.Setup;

/// <summary>
/// Profil de technicien : les choix de la page « Nouveau PC » (étapes, usage, logiciels). Il ne contient que des
/// identifiants : à l'application, seuls ceux qui existent dans les catalogues de PCDoc sont retenus.
/// </summary>
public sealed record TechProfile(string Name, string Usage, IReadOnlyList<string> Steps, IReadOnlyList<string> Software);

/// <summary>Profils au format JSON dans un dossier (un fichier par profil, nom nettoyé).</summary>
public sealed class TechProfileStore(string folder)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public IReadOnlyList<TechProfile> List()
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var result = new List<TechProfile>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Take(200))
        {
            try
            {
                if (new FileInfo(file).Length < 64 * 1024 && JsonSerializer.Deserialize<TechProfile>(File.ReadAllText(file), Options) is { Name.Length: > 0 } p)
                {
                    result.Add(p with { Steps = p.Steps ?? [], Software = p.Software ?? [] });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Fichier abîmé ou illisible : ignoré.
            }
        }

        return [.. result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public string Save(TechProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var name = SafeName(profile.Name);
        if (name.Length == 0)
        {
            throw new ArgumentException("Empty profile name", nameof(profile));
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(profile with { Name = profile.Name.Trim() }, Options));
        return path;
    }

    internal static string SafeName(string? name) =>
        new string([.. (name ?? string.Empty).Trim().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')]).Trim() is var n && n.Length > 60 ? n[..60] : n;
}
