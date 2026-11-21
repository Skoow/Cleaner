using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Optimize;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Optimize;

public sealed partial class TweakRow(TweakState state, string title, string explanation, string status, string riskLabel, Finding finding, bool canSelect)
    : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public TweakState State { get; } = state;

    public string Title { get; } = title;

    public string Explanation { get; } = explanation;

    /// <summary>« Déjà appliqué », « Indisponible » ou vide.</summary>
    public string Status { get; } = status;

    public string RiskLabel { get; } = riskLabel;

    public Finding Finding { get; } = finding;

    public bool CanSelect { get; } = canSelect;
}

public sealed record ProfileChoice(string Id, string Label);

/// <summary>Page Optimisation : réglages réversibles à cocher, profils Jeu / Bureautique / Batterie.</summary>
public sealed partial class OptimizePageViewModel : ObservableObject
{
    private readonly IOptimizeSource _source;
    private readonly ILocalizer _localizer;
    private readonly Func<Core.Settings.AppMode> _mode;
    private IReadOnlyList<TweakState>? _states;
    private HashSet<string>? _selected;

    [ObservableProperty]
    private bool _isLoading;

    public OptimizePageViewModel(IOptimizeSource source, ILocalizer localizer, FixConfirmationViewModel? fixes, Func<Core.Settings.AppMode>? mode = null)
    {
        _source = source;
        _localizer = localizer;
        _mode = mode ?? (() => Core.Settings.AppMode.Novice);
        Fixes = fixes;
        if (fixes is not null)
        {
            fixes.Completed += (_, _) => _ = AnalyzeAsync();
        }

        localizer.PropertyChanged += (_, _) => Build();
    }

    public FixConfirmationViewModel? Fixes { get; }

    public ObservableCollection<TweakRow> Tweaks { get; } = [];

    public IReadOnlyList<ProfileChoice> Profiles => [.. TweakCatalog.ProfileIds.Select(p => new ProfileChoice(p, _localizer[$"Optimize.Profile.{p}"]))];

    public bool HasLoaded => _states is not null;

    public Task EnsureLoadedAsync() => HasLoaded ? Task.CompletedTask : AnalyzeAsync();

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        IsLoading = true;
        try
        {
            _states = await _source.AnalyzeAsync(CancellationToken.None).ConfigureAwait(true);
            _selected = null; // après une correction, l'état a changé : sélection recalculée
            Build();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Coche exactement les réglages du profil (ceux qui sont proposés dans le mode actuel).</summary>
    [RelayCommand]
    private void ApplyProfile(string? profile)
    {
        foreach (var row in Tweaks)
        {
            row.IsSelected = row.CanSelect && profile is not null && row.State.Tweak.Profiles.Contains(profile);
        }
    }

    [RelayCommand]
    private void ApplySelected() => Fixes?.Request(Tweaks.Where(r => r.IsSelected && r.CanSelect).Select(r => r.Finding));

    private void Build()
    {
        if (_states is not { } states)
        {
            return;
        }

        _selected ??= [.. states.Where(s => s.Tweak.InBilan && s.Risk == RiskLevel.Safe && s.Available && !s.Applied).Select(s => s.Tweak.Id)];
        var selected = _selected;
        Tweaks.Clear();
        foreach (var state in states)
        {
            var finding = TweakFixer.FindingFor(state);
            var canSelect = FixPolicy.IsOffered(finding, _mode());
            var status = !state.Available ? _localizer["Optimize.Unavailable"] : state.Applied ? _localizer["Optimize.Applied"] : string.Empty;
            var row = new TweakRow(state, _localizer[$"Optimize.{state.Tweak.Id}.Title"], _localizer[$"Optimize.{state.Tweak.Id}.Explanation"], status,
                _localizer[$"Risk.{state.Risk}"], finding, canSelect)
            {
                IsSelected = canSelect && selected.Contains(state.Tweak.Id),
            };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TweakRow.IsSelected))
                {
                    if (row.IsSelected)
                    {
                        selected.Add(row.State.Tweak.Id);
                    }
                    else
                    {
                        selected.Remove(row.State.Tweak.Id);
                    }
                }
            };
            Tweaks.Add(row);
        }

        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(HasLoaded));
    }
}
