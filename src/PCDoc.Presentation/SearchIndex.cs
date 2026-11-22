using PCDoc.Modules.Cleanup;
using PCDoc.Modules.Diagnostic.Repairs;
using PCDoc.Modules.Optimize;
using PCDoc.Modules.Security;

namespace PCDoc.Presentation;

/// <summary>Élément trouvable par la recherche : clé de traduction du titre et page où il se trouve.</summary>
public sealed record SearchEntry(string TitleKey, PageId Page);

/// <summary>Résultat affiché sous la zone de recherche.</summary>
public sealed record SearchResult(string Title, string PageLabel, PageId Page);

/// <summary>Tout ce que l'on peut chercher dans PCDoc : pages, réglages, réparations, nettoyages, outils.</summary>
public static class SearchIndex
{
    public static IReadOnlyList<SearchEntry> Build()
    {
        var entries = new List<SearchEntry>
        {
            new("Nav.Bilan", PageId.Bilan), new("Nav.Setup", PageId.Setup), new("Nav.System", PageId.System), new("Nav.Diagnostic", PageId.Diagnostic),
            new("Nav.Cleanup", PageId.Cleanup), new("Nav.Apps", PageId.Apps), new("Nav.Updates", PageId.Updates), new("Nav.Optimize", PageId.Optimize),
            new("Nav.Security", PageId.Security), new("Nav.Privacy", PageId.Privacy), new("Nav.History", PageId.History), new("Nav.Settings", PageId.Settings),
            new("Assistant.NoInternet", PageId.Diagnostic), new("Assistant.NoSound", PageId.Diagnostic),
            new("Updates.DriverBackup", PageId.Updates), new("Updates.Store", PageId.Updates), new("Updates.Tab.Install", PageId.Updates),
            new("Apps.Tab.Preinstalled", PageId.Apps), new("Apps.Uninstall", PageId.Apps),
            new("Security.QuickScan", PageId.Security), new("Security.FullScan", PageId.Security), new("Security.Tab.Usb", PageId.Security),
            new("Security.Tab.Network", PageId.Security), new("Security.Tab.Extensions", PageId.Security), new("SecondOpinion.SafetyScanner", PageId.Security),
            new("SecondOpinion.AdwCleaner", PageId.Security), new("VT.Run", PageId.Security),
            new("Maintenance.Title", PageId.Settings), new("Portable.Title", PageId.Settings), new("Setup.TechProfile", PageId.Setup),
            new("History.ExportReport", PageId.History),
        };
        entries.AddRange(TweakCatalog.Default.Tweaks.Select(t => new SearchEntry($"Optimize.{t.Id}.Title", PageId.Optimize)));
        entries.AddRange(OptimizeModule.Privacy.Tweaks.Select(t => new SearchEntry($"Privacy.{t.Id}.Title", PageId.Privacy)));
        entries.AddRange(SecurityModule.Settings.Tweaks.Select(t => new SearchEntry($"Security.{t.Id}.Title", PageId.Security)));
        entries.AddRange(Enum.GetValues<RepairKind>().Select(r => new SearchEntry($"Repair.{r}.Title", PageId.Diagnostic)));
        entries.AddRange(CleanupCatalog.Default.Categories.Select(c => new SearchEntry($"Cleanup.{c.Id}.Title", PageId.Cleanup)));
        return entries;
    }

    /// <summary>Recherche sans tenir compte des accents ni de la casse ; tous les mots doivent apparaître.</summary>
    public static IReadOnlyList<SearchResult> Find(IReadOnlyList<SearchEntry> index, string query, Func<string, string> translate, int max = 12)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(translate);
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words.All(w => w.Length < 2))
        {
            return [];
        }

        return [.. index
            .Select(e => (Entry: e, Title: translate(e.TitleKey)))
            .Where(x => words.All(w => Normalize(x.Title).Contains(w, StringComparison.Ordinal)))
            .Select(x => new SearchResult(x.Title, translate($"Nav.{x.Entry.Page}"), x.Entry.Page))
            .DistinctBy(r => (r.Title, r.Page))
            .Take(max)];
    }

    private static string Normalize(string? text)
    {
        var decomposed = (text ?? string.Empty).ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string([.. decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)]);
    }
}
