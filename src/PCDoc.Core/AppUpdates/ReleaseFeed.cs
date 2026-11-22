using System.Globalization;
using System.Text.Json;

namespace PCDoc.Core.AppUpdates;

/// <summary>Nouvelle version de PCDoc publiée sur GitHub : page, installateur MSI pour cette architecture et sommes SHA-256.</summary>
public sealed record AppRelease(Version Version, string Tag, string PageUrl, string? InstallerUrl, string? InstallerName, string? ChecksumsUrl);

/// <summary>Vérification et installation des nouvelles versions de PCDoc.</summary>
public interface IAppUpdater
{
    /// <summary>La nouvelle version, ou null si PCDoc est à jour.</summary>
    Task<AppRelease?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Null si l'installation automatique est possible ; sinon la clé du texte qui explique pourquoi.</summary>
    string? InstallBlockedReason();

    /// <summary>Télécharge l'installateur, vérifie sa somme SHA-256 et sa signature (même éditeur que PCDoc), puis le lance.</summary>
    Task InstallAsync(AppRelease release, Action<string>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Lecture de la réponse de l'API GitHub « dernière version » : seuls les liens du dépôt officiel sont acceptés,
/// et seulement en HTTPS.
/// </summary>
public static class ReleaseFeed
{
    public const string Repository = "Skoow/Cleaner";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    private const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";

    /// <summary>La version publiée si elle est plus récente que <paramref name="current"/>, sinon null.</summary>
    public static AppRelease? Parse(string json, Version current, string architecture)
    {
        ArgumentNullException.ThrowIfNull(current);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || Bool(root, "draft") || Bool(root, "prerelease")
            || Text(root, "tag_name") is not { } tag || ParseVersion(tag) is not { } version
            || Text(root, "html_url") is not { } page || !page.StartsWith(ReleasesPage + "/", StringComparison.Ordinal))
        {
            return null;
        }

        if (version <= current)
        {
            return null;
        }

        string? installerUrl = null, installerName = null, checksums = null;
        var wanted = $"PCDoc-{version.ToString(3)}-{architecture}.msi";
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (Text(asset, "name") is not { } name || Text(asset, "browser_download_url") is not { } url
                    || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !url.EndsWith("/" + name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    (installerUrl, installerName) = (url, name);
                }
                else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksums = url;
                }
            }
        }

        return new AppRelease(version, tag, page, installerUrl, installerName, checksums);
    }

    /// <summary>« v1.2.3 » ou « 1.2.3 » (un suffixe « -beta » est ignoré).</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out var v) && v.Major >= 0 ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;
    }

    /// <summary>Somme SHA-256 (64 caractères hexadécimaux) d'un fichier dans un SHA256SUMS.txt (format sha256sum).</summary>
    public static string? Sha256For(string sums, string fileName)
    {
        ArgumentNullException.ThrowIfNull(sums);
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Length == 64 && parts[0].All(char.IsAsciiHexDigit)
                && parts[1].TrimStart('*').Trim().Equals(fileName, StringComparison.Ordinal))
            {
                return parts[0].ToLower(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>Dernière version trouvée pendant cette session (bandeau au démarrage, puis page Paramètres).</summary>
public sealed class AppUpdateState
{
    public AppRelease? Latest { get; set; }
}
