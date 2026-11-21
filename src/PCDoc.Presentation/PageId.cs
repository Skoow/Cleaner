namespace PCDoc.Presentation;

/// <summary>Pages de la fenêtre (v0.1).</summary>
public enum PageId
{
    Bilan,
    System,
    Diagnostic,
    Cleanup,
    History,
    Settings,
}

/// <summary>Entrée du menu latéral. <paramref name="Glyph"/> : icône Segoe Fluent Icons.</summary>
public sealed record NavItem(PageId Id, string LabelKey, string Glyph);
