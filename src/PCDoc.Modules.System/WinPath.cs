namespace PCDoc.Modules.System;

/// <summary>
/// Opérations sur des chemins Windows (séparateur « \ »), identiques quel que soit le système qui exécute le code
/// (les tests tournent aussi sous Linux, où System.IO.Path ne reconnaît pas « \ »).
/// </summary>
internal static class WinPath
{
    public static string FileName(string path)
    {
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut < 0 ? path : path[(cut + 1)..];
    }

    public static bool HasExtension(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1;
    }

    public static string Combine(string directory, string relative) =>
        directory.TrimEnd('\\', '/') + @"\" + relative.TrimStart('\\', '/');
}
