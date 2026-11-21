using System.Reflection;

namespace PCDoc.Core;

/// <summary>
/// Identité du produit. Le nom vient de &lt;Product&gt; dans Directory.Build.props :
/// c'est le seul endroit où il est écrit.
/// </summary>
public static class AppInfo
{
    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    public static string ProductName { get; } =
        Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "PCDoc";

    public static string Version { get; } = StripCommitSuffix(
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    /// <summary>Nom du dossier de données (%LOCALAPPDATA%, %ProgramData%).</summary>
    public static string DataFolderName => ProductName;

    private static string StripCommitSuffix(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
