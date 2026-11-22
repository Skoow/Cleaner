using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Activity;
using PCDoc.Core.Findings;
using PCDoc.Core.Localization;
using PCDoc.Modules.Security;
using PCDoc.Presentation.Fixing;

namespace PCDoc.Presentation.Security;

public sealed record ConnectionView(string Process, string Protocol, string Local, string Remote, string State, string Path, bool Suspicious);

public sealed record ExtensionView(BrowserExtension Extension, bool CanBlock, string Status)
{
    public string Browser => Extension.Browser;

    public string Name => Extension.Name;

    public string Version => Extension.Version ?? string.Empty;

    public string Id => Extension.Id;
}

public sealed record VirusTotalView(string Name, string Path, string Result, bool Alert);

#pragma warning disable CA1001 // _tool est libéré par le « using » de chaque outil ; le champ ne sert qu'à l'annulation
/// <summary>Outils de la page Sécurité : activité réseau, extensions, seconde analyse, VirusTotal.</summary>
public sealed partial class SecurityToolsViewModel(
    INetworkConnections network,
    IBrowserExtensions extensions,
    ExtensionBlockFixer blocker,
    ISecondOpinion secondOpinion,
    IVirusTotal virusTotal,
    VirusTotalCheck virusTotalCheck,
    IActivityLog activity,
    ILocalizer localizer,
    FixConfirmationViewModel? fixes) : ObservableObject
{
    private CancellationTokenSource? _tool;

    [ObservableProperty]
    private bool _showAllConnections;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSafetyScannerCommand), nameof(RunAdwCleanerCommand), nameof(RunVirusTotalCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _toolOutput = string.Empty;

    [ObservableProperty]
    private string? _toolStatus;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string? _virusTotalStatus;

    public FixConfirmationViewModel? Fixes => fixes;

    public ObservableCollection<ConnectionView> Connections { get; } = [];

    public ObservableCollection<ExtensionView> Extensions { get; } = [];

    public ObservableCollection<VirusTotalView> VirusTotal { get; } = [];

    public bool HasVirusTotalKey => virusTotal.HasKey;

    partial void OnShowAllConnectionsChanged(bool value) => _ = RefreshConnectionsAsync();

    [RelayCommand]
    private async Task RefreshConnectionsAsync()
    {
        var rows = await Task.Run(() => NetworkActivity.Analyze(network.List())).ConfigureAwait(true);
        Connections.Clear();
        foreach (var r in rows.Where(r => ShowAllConnections || r.Connection.State != "UDP"))
        {
            var c = r.Connection;
            Connections.Add(new ConnectionView(c.ProcessName ?? $"PID {c.ProcessId}", c.Protocol, $"{c.LocalAddress}:{c.LocalPort}",
                c.RemoteAddress is null ? string.Empty : $"{c.RemoteAddress}:{c.RemotePort}",
                localizer[$"Net.State.{c.State}"], c.ProcessPath ?? string.Empty, r.Suspicious));
        }
    }

    [RelayCommand]
    private async Task RefreshExtensionsAsync()
    {
        var list = await Task.Run(extensions.List).ConfigureAwait(true);
        Extensions.Clear();
        foreach (var e in list)
        {
            var blocked = ExtensionBlockFixer.CanBlock(e) && blocker.IsBlocked(e.Browser, e.Id);
            Extensions.Add(new ExtensionView(e, ExtensionBlockFixer.CanBlock(e) && !blocked,
                blocked ? localizer["Ext.Blocked"] : e.Enabled == false ? localizer["Ext.Disabled"] : string.Empty));
        }
    }

    [RelayCommand]
    private void BlockExtension(ExtensionView? row)
    {
        if (row is { CanBlock: true })
        {
            fixes?.Request([ExtensionBlockFixer.FindingFor(row.Extension)]);
        }
    }

    private bool CanRunTool() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunTool))]
    private Task RunSafetyScannerAsync() => RunToolAsync(SecondOpinionTool.SafetyScanner);

    [RelayCommand(CanExecute = nameof(CanRunTool))]
    private Task RunAdwCleanerAsync() => RunToolAsync(SecondOpinionTool.AdwCleaner);

    [RelayCommand]
    private void CancelTool() => _tool?.Cancel();

    private async Task RunToolAsync(SecondOpinionTool tool)
    {
        IsBusy = true;
        ToolOutput = string.Empty;
        ToolStatus = localizer[$"SecondOpinion.Running.{tool}"];
        using var cts = _tool = new CancellationTokenSource();
        var gate = new object();
        try
        {
            var result = await Task.Run(() => secondOpinion.RunAsync(tool, line =>
            {
                lock (gate)
                {
                    ToolOutput = (ToolOutput.Length > 6000 ? ToolOutput[^4000..] : ToolOutput) + line + "\n";
                }
            }, cts.Token)).ConfigureAwait(true);
            foreach (var line in result.LogLines)
            {
                ToolOutput += line + "\n";
            }

            ToolStatus = localizer.Format(new LocalizedText("SecondOpinion.Done", localizer[$"SecondOpinion.{tool}"], result.ExitCode));
            activity.Write("security", ToolStatus);
        }
        catch (OperationCanceledException)
        {
            ToolStatus = localizer["SecondOpinion.Cancelled"];
        }
#pragma warning disable CA1031 // téléchargement, signature ou outil en échec : message, jamais de plantage
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ToolStatus = localizer.Format(new LocalizedText("SecondOpinion.Failed", localizer[$"SecondOpinion.{tool}"], ex.Message));
            activity.Write("security", ToolStatus);
        }
        finally
        {
            _tool = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        try
        {
            virusTotal.SetKey(ApiKey);
            ApiKey = string.Empty;
            VirusTotalStatus = virusTotal.HasKey ? localizer["VT.KeySaved"] : localizer["VT.KeyRemoved"];
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            VirusTotalStatus = localizer["VT.KeyInvalid"];
        }

        OnPropertyChanged(nameof(HasVirusTotalKey));
    }

    private bool CanRunVirusTotal() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunVirusTotal))]
    private async Task RunVirusTotalAsync()
    {
        if (!virusTotal.HasKey)
        {
            VirusTotalStatus = localizer["VT.NeedKey"];
            return;
        }

        IsBusy = true;
        VirusTotal.Clear();
        VirusTotalStatus = localizer["VT.Running"];
        using var cts = _tool = new CancellationTokenSource();
        try
        {
            var context = SynchronizationContext.Current;
            var rows = await Task.Run(() => virusTotalCheck.RunAsync(row => context?.Post(_ => VirusTotal.Add(View(row)), null), cts.Token))
                .ConfigureAwait(true);
            var alerts = rows.Count(r => r.Stats is { Malicious: > 0 });
            VirusTotalStatus = localizer.Format(new LocalizedText("VT.Done", rows.Count, alerts));
            activity.Write("security", VirusTotalStatus);
        }
        catch (OperationCanceledException)
        {
            VirusTotalStatus = localizer["SecondOpinion.Cancelled"];
        }
        finally
        {
            _tool = null;
            IsBusy = false;
        }
    }

    private VirusTotalView View(VirusTotalRow row) => new(row.Name, row.Path,
        row.Error is not null ? localizer.Format(new LocalizedText("VT.Error", row.Error))
        : row.Stats is null ? localizer["VT.Unknown"]
        : localizer.Format(new LocalizedText("VT.Stats", row.Stats.Malicious, row.Stats.Malicious + row.Stats.Suspicious + row.Stats.Harmless + row.Stats.Undetected)),
        row.Stats is { Malicious: > 0 });
}
#pragma warning restore CA1001
