namespace PCDoc.Core.Findings;

/// <summary>
/// Texte à traduire au moment de l'affichage : une clé de ressource et ses arguments.
/// Changer de langue met donc à jour les textes sans relancer l'analyse.
/// </summary>
public sealed class LocalizedText : IEquatable<LocalizedText>
{
    public LocalizedText(string key, params object[] args)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Key = key;
        Args = args ?? [];
    }

    public string Key { get; }

    public IReadOnlyList<object> Args { get; }

    public bool Equals(LocalizedText? other) =>
        other is not null && Key == other.Key && Args.SequenceEqual(other.Args);

    public override bool Equals(object? obj) => Equals(obj as LocalizedText);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        foreach (var arg in Args)
        {
            hash.Add(arg);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Args.Count == 0 ? Key : $"{Key}({string.Join(", ", Args)})";
}
