using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Autoruns;

/// <summary>Trouve le fichier réellement lancé par une ligne de commande de démarrage.</summary>
public static class CommandLineParser
{
    /// <summary>
    /// Règles, dans l'ordre : variables développées ; chemin entre guillemets ; sinon préfixes coupés aux espaces,
    /// du plus court au plus long, le premier qui existe (avec « .exe » ajouté si besoin) ; nom seul cherché dans
    /// System32 ; rundll32 → la DLL ; rien n'existe → premier jeton (le fichier sera « introuvable »).
    /// </summary>
    public static string? ExtractExecutable(string commandLine, IFileProbe files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var text = files.ExpandEnvironment(commandLine ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var (exe, rest) = SplitExecutable(text, files);
        if (WinPath.FileName(exe).Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase)
            || WinPath.FileName(exe).Equals("rundll32", StringComparison.OrdinalIgnoreCase))
        {
            var dll = RundllTarget(rest);
            if (dll is not null)
            {
                return Resolve(files.ExpandEnvironment(dll), files) ?? dll;
            }
        }

        return exe;
    }

    /// <summary>Chemin d'image d'un pilote ou d'un service (\SystemRoot\…, System32\…, \??\C:\…).</summary>
    public static string NormalizeImagePath(string imagePath, IFileProbe files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var path = (imagePath ?? string.Empty).Trim();
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            path = path[4..];
        }

        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            path = @"%SystemRoot%\" + path[@"\SystemRoot\".Length..];
        }
        else if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith(@"SysWOW64\", StringComparison.OrdinalIgnoreCase))
        {
            path = @"%SystemRoot%\" + path;
        }

        return files.ExpandEnvironment(path);
    }

    private static (string Exe, string Arguments) SplitExecutable(string text, IFileProbe files)
    {
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            var quoted = end < 0 ? text[1..] : text[1..end];
            var rest = end < 0 ? string.Empty : text[(end + 1)..].Trim();
            return (Resolve(quoted, files) ?? quoted, rest);
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i <= tokens.Length; i++)
        {
            var candidate = string.Join(' ', tokens[..i]);
            var found = Resolve(candidate, files);
            if (found is not null)
            {
                return (found, string.Join(' ', tokens[i..]));
            }
        }

        return (tokens[0], string.Join(' ', tokens[1..]));
    }

    private static string? Resolve(string candidate, IFileProbe files)
    {
        foreach (var path in Candidates(candidate, files))
        {
            if (files.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string candidate, IFileProbe files)
    {
        var withExe = WinPath.HasExtension(candidate) ? null : candidate + ".exe";
        yield return candidate;
        if (withExe is not null)
        {
            yield return withExe;
        }

        if (!candidate.Contains('\\', StringComparison.Ordinal))
        {
            var system32 = files.ExpandEnvironment(@"%SystemRoot%\System32\");
            yield return system32 + candidate;
            if (withExe is not null)
            {
                yield return system32 + withExe;
            }
        }
    }

    private static string? RundllTarget(string arguments)
    {
        var args = arguments.Trim();
        if (args.Length == 0)
        {
            return null;
        }

        string dll;
        if (args[0] == '"')
        {
            var end = args.IndexOf('"', 1);
            dll = end < 0 ? args[1..] : args[1..end];
        }
        else
        {
            var comma = args.IndexOf(',', StringComparison.Ordinal);
            var space = args.IndexOf(' ', StringComparison.Ordinal);
            var cut = new[] { comma, space }.Where(i => i >= 0).DefaultIfEmpty(args.Length).Min();
            dll = args[..cut];
        }

        dll = dll.Trim();
        return dll.Length == 0 ? null : dll;
    }
}
