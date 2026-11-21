using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Tests.Fakes;

/// <summary>Faux registre en mémoire. Les sous-clés sont déduites des chemins ajoutés.</summary>
public sealed class FakeRegistryReader : IRegistryReader
{
    private readonly Dictionary<(RegistryHive, RegistryView, string), Dictionary<string, object?>> _keys = new();

    public FakeRegistryReader Set(RegistryHive hive, string path, string name, object? value,
        RegistryView view = RegistryView.Default)
    {
        var key = (hive, view, path.ToUpperInvariant());
        if (!_keys.TryGetValue(key, out var values))
        {
            _keys[key] = values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        values[name] = value;
        return this;
    }

    public IReadOnlyList<string> GetValueNames(RegistryHive hive, string path, RegistryView view = RegistryView.Default) =>
        _keys.TryGetValue((hive, view, path.ToUpperInvariant()), out var values) ? [.. values.Keys] : [];

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string path, RegistryView view = RegistryView.Default)
    {
        var prefix = path.Length == 0 ? string.Empty : path.ToUpperInvariant() + @"\";
        return _keys.Keys
            .Where(k => k.Item1 == hive && k.Item2 == view && k.Item3.StartsWith(prefix, StringComparison.Ordinal) && k.Item3.Length > prefix.Length)
            .Select(k => k.Item3[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public object? GetValue(RegistryHive hive, string path, string name, RegistryView view = RegistryView.Default) =>
        _keys.TryGetValue((hive, view, path.ToUpperInvariant()), out var values) && values.TryGetValue(name, out var v) ? v : null;
}
