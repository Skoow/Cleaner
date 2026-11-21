using System.Text.RegularExpressions;
using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Tests.Fakes;

/// <summary>Faux WMI : des lignes par (espace de noms, classe) ; la clause WHERE est ignorée.</summary>
public sealed partial class FakeWmiQuery : IWmiQuery
{
    private readonly Dictionary<(string Ns, string Class), List<IReadOnlyDictionary<string, object?>>> _rows = new();
    private readonly Dictionary<string, bool> _throwingNamespaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _throwingClasses = new(StringComparer.OrdinalIgnoreCase);

    public FakeWmiQuery Add(string ns, string className, params Dictionary<string, object?>[] rows)
    {
        var key = (ns.ToUpperInvariant(), className.ToUpperInvariant());
        if (!_rows.TryGetValue(key, out var list))
        {
            _rows[key] = list = [];
        }

        list.AddRange(rows);
        return this;
    }

    public FakeWmiQuery Replace(string ns, string className, params Dictionary<string, object?>[] rows)
    {
        _rows[(ns.ToUpperInvariant(), className.ToUpperInvariant())] = [.. rows];
        return this;
    }

    public FakeWmiQuery ThrowOnNamespace(string ns, bool accessDenied = false)
    {
        _throwingNamespaces[ns] = accessDenied;
        return this;
    }

    public FakeWmiQuery ThrowOnClass(string className, bool accessDenied = false)
    {
        _throwingClasses[className] = accessDenied;
        return this;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string ns, string wql)
    {
        var className = wql.TrimStart().StartsWith("ASSOCIATORS", StringComparison.OrdinalIgnoreCase)
            ? ResultClause().Match(wql).Groups[1].Value
            : FromClause().Match(wql).Groups[1].Value;
        if (_throwingNamespaces.TryGetValue(ns, out var denied) || _throwingClasses.TryGetValue(className, out denied))
        {
            throw new WmiUnavailableException(ns, wql, new InvalidOperationException("fake"), denied);
        }

        return _rows.TryGetValue((ns.ToUpperInvariant(), className.ToUpperInvariant()), out var rows) ? rows : [];
    }

    [GeneratedRegex(@"FROM\s+(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex FromClause();

    [GeneratedRegex(@"ResultClass\s*=\s*(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex ResultClause();
}

/// <summary>Raccourci pour écrire une ligne WMI.</summary>
public static class Row
{
    public static Dictionary<string, object?> Of(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
}
