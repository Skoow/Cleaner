using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure;

/// <summary>
/// Existence de fichiers et variables d'environnement. Les variables propres à l'utilisateur
/// (%USERPROFILE%, %APPDATA%, %LOCALAPPDATA%) prennent les valeurs du profil cible.
/// </summary>
public sealed class FileProbe(IUserProfileResolver profiles) : IFileProbe
{
    private readonly Lazy<UserProfile> _profile = new(profiles.Resolve);

    public bool Exists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    public string ExpandEnvironment(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var profile = _profile.Value;
        var withUser = value
            .Replace("%USERPROFILE%", profile.ProfilePath, StringComparison.OrdinalIgnoreCase)
            .Replace("%APPDATA%", profile.RoamingAppData, StringComparison.OrdinalIgnoreCase)
            .Replace("%LOCALAPPDATA%", profile.LocalAppData, StringComparison.OrdinalIgnoreCase);
        return Environment.ExpandEnvironmentVariables(withUser);
    }

    public IReadOnlyList<string> ListFiles(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public byte[]? ReadSmallFile(string path, int maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= maxBytes ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
