using CommunityToolkit.Mvvm.ComponentModel;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;

namespace PCDoc.Presentation.Bilan;

/// <summary>Une ligne du Bilan. Les textes se traduisent à l'affichage (changement de langue sans nouvelle analyse).</summary>
public sealed partial class FindingRowViewModel : ObservableObject
{
    private readonly ILocalizer _localizer;

    [ObservableProperty]
    private bool _isExpanded;

    public FindingRowViewModel(Finding finding, ILocalizer localizer)
    {
        Finding = finding;
        _localizer = localizer;
    }

    public Finding Finding { get; }

    public string Id => Finding.Id;

    public Severity Severity => Finding.Severity;

    public string Title => _localizer.Format(Finding.Title);

    public string Explanation => _localizer.Format(Finding.Explanation);

    public string SeverityLabel => _localizer[$"Severity.{Finding.Severity}"];

    public string ModuleLabel => _localizer[$"Module.{Finding.Module}"];

    public string DetailsHeader => _localizer.Format(new LocalizedText("Bilan.TechDetails", ModuleLabel));

    public string TechnicalDetails => Finding.TechnicalDetails;

    public bool ShowFixButton => Finding.FixAvailable;

    /// <summary>Texte lu par les lecteurs d'écran pour la ligne entière.</summary>
    public string AccessibleName => $"{SeverityLabel} : {Title}";

    internal void RefreshTexts() => OnPropertyChanged(string.Empty);
}
