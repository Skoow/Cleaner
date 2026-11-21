using System.Globalization;
using System.Text;

namespace PCDoc.Modules.System.Fixes;

/// <summary>Contenu d'un fichier .reg (format de l'Éditeur du Registre) pour sauvegarder des valeurs d'origine.</summary>
public static class RegFile
{
    public static string Format(string fullKeyPath, IEnumerable<(string Name, object? Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var text = new StringBuilder()
            .Append("Windows Registry Editor Version 5.00\r\n\r\n")
            .Append('[').Append(fullKeyPath).Append("]\r\n");
        foreach (var (name, value) in values)
        {
            text.Append('"').Append(Escape(name)).Append("\"=").Append(value switch
            {
                null => "-", // la valeur n'existait pas : la réimporter la supprime
                byte[] bytes => "hex:" + string.Join(',', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture))),
                int dword => "dword:" + dword.ToString("x8", CultureInfo.InvariantCulture),
                string s => "\"" + Escape(s) + "\"",
                _ => "\"" + Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty) + "\"",
            }).Append("\r\n");
        }

        return text.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
