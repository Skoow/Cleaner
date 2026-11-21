using System.Diagnostics;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Localization;
using PCDoc.Modules.System;
using PCDoc.Modules.System.Autoruns;
using PCDoc.Modules.System.Boot;
using PCDoc.Modules.System.Inventory;
using PCDoc.Presentation.SystemPage;

namespace PCDoc.Presentation.Tests;

public class SystemPageViewModelTests
{
    private const long GB = 1_000_000_000;

    internal static SystemInventory Pc() => new(
        Section<WindowsInfo>.Of(new WindowsInfo("Windows 11", "Windows 11 Pro", "24H2", 26100, 2314,
            new DateTimeOffset(2025, 3, 12, 0, 0, 0, TimeSpan.Zero), ActivationState.Activated)),
        Section<CpuInfo>.Of(new CpuInfo("AMD Ryzen 5 5600X", 6, 12, 3700)),
        Section<MemoryInfo>.Of(new MemoryInfo(16 * GB, 10 * GB, [new MemoryModule(8 * GB, 3200, "Kingston", "DIMM_A2")])),
        Section<IReadOnlyList<GpuInfo>>.Of([new GpuInfo("RTX 3060", "32.0", 12 * GB)]),
        Section<BoardInfo>.Of(new BoardInfo("ASUS", "B550")),
        Section<BiosInfo>.Of(new BiosInfo("AMI", "3607", new DateOnly(2024, 8, 15))),
        Section<IReadOnlyList<DiskInfo>>.Of([new DiskInfo("Samsung 980", 500 * GB, DiskMediaType.Ssd, true)]),
        Section<IReadOnlyList<VolumeInfo>>.Of([new VolumeInfo("C:", 500 * GB, 50 * GB, "NTFS")]),
        Section<IReadOnlyList<DisplayInfo>>.Of([new DisplayInfo("DELL", 27.2)]),
        Section<IReadOnlyList<BatteryInfo>>.Of([]),
        Section<Win11Readiness>.Of(new Win11Readiness(CheckState.Pass, CheckState.Pass, CheckState.Pass, CheckState.Pass,
            CheckState.Unknown, CheckState.Pass, CheckState.Pass)));

    private static AutorunEntry Entry(string name, AutorunKind kind, SignatureStatus status, string? publisher = null, bool enabled = true) =>
        new(kind, name, @"HKCU\Run", $"\"C:\\x\\{name}.exe\"", $@"C:\x\{name}.exe", enabled, new SignatureInfo(status, publisher, false),
            publisher?.StartsWith("Microsoft", StringComparison.Ordinal) == true);

    private sealed class Sources(Func<SystemInventory> inv, Func<BootHistory> boot, Func<IReadOnlyList<AutorunEntry>> autoruns)
        : IInventorySource, IBootSource, IAutorunSource
    {
        public int Calls;

        public SystemInventory Collect() { Interlocked.Increment(ref Calls); return inv(); }

        public BootHistory Read(int maxBoots = 30) => boot();

        IReadOnlyList<AutorunEntry> IAutorunSource.Collect() => autoruns();
    }

    private static readonly AutorunEntry[] SampleAutoruns =
    [
        Entry("OneDrive", AutorunKind.Startup, SignatureStatus.Signed, "Microsoft Corporation"),
        Entry("UpdaterHelper", AutorunKind.Startup, SignatureStatus.Unsigned),
        Entry("OldSync", AutorunKind.Startup, SignatureStatus.FileNotFound),
        Entry("GoogleUpdate", AutorunKind.ScheduledTask, SignatureStatus.Signed, "Google LLC"),
        Entry("Spooler", AutorunKind.Service, SignatureStatus.Signed, "Microsoft Windows"),
        Entry("rgbsvc", AutorunKind.Service, SignatureStatus.Unsigned),
        Entry("disk", AutorunKind.Driver, SignatureStatus.Signed, "Microsoft Windows"),
    ];

    internal sealed class FakeClipboard : IClipboardService
    {
        public string? Text;

        public void SetText(string text) => Text = text;
    }

    [Theory]
    [InlineData(4, AutorunKind.Startup)]
    [InlineData(5, AutorunKind.ScheduledTask)]
    [InlineData(6, AutorunKind.Service)]
    public void SelectedTab_SetsKindFilter(int tab, AutorunKind kind)
    {
        var (vm, _) = Make();

        vm.SelectedTab = tab;

        vm.KindFilter.Should().Be(kind);
    }

    [Fact]
    public async Task CopyDetails_PutsLocationAndCommandOnClipboard()
    {
        var clipboard = new FakeClipboard();
        var (vm, _) = Make(clipboard: clipboard);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.CopyDetailsCommand.Execute(vm.Autoruns[1]);

        clipboard.Text.Should().Contain(@"HKCU\Run").And.Contain("UpdaterHelper.exe");
    }

    private static (SystemPageViewModel Vm, Sources Src) Make(
        Func<SystemInventory>? inv = null, Func<BootHistory>? boot = null, Func<IReadOnlyList<AutorunEntry>>? autoruns = null,
        IClipboardService? clipboard = null)
    {
        var src = new Sources(inv ?? Pc, boot ?? (() => new BootHistory([])), autoruns ?? (() => SampleAutoruns));
        var localizer = new Localizer();
        localizer.Register(CoreResources.Manager);
        SystemModule.RegisterResources(localizer);
        return (new SystemPageViewModel(src, src, src, localizer, clipboard), src);
    }

    [Fact]
    public async Task Refresh_LoadsAllSourcesInParallel()
    {
        var (vm, _) = Make(
            () => { Thread.Sleep(300); return Pc(); },
            () => { Thread.Sleep(300); return new BootHistory([]); },
            () => { Thread.Sleep(300); return SampleAutoruns; });

        var watch = Stopwatch.StartNew();
        await vm.RefreshCommand.ExecuteAsync(null);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(800));
        vm.Inventory.Should().NotBeNull();
        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_SourceFailure_ShowsSectionUnavailable()
    {
        var (vm, _) = Make(autoruns: () => throw new InvalidOperationException("denied"));

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.AutorunsUnavailable.Should().BeTrue();
        vm.InventoryUnavailable.Should().BeFalse();
        vm.OverviewCards.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Overview_ShowsWindowsCardInFrench()
    {
        var (vm, _) = Make();

        await vm.RefreshCommand.ExecuteAsync(null);

        var windows = vm.OverviewCards[0];
        windows.Title.Should().Be("Windows");
        windows.Lines.Should().Contain(l => l.Label == "Édition" && l.Value == "Windows 11 Pro");
        windows.Lines.Should().Contain(l => l.Label == "Activation" && l.Value == "Activé");
    }

    [Fact]
    public async Task Overview_NoBattery_SaysDesktop()
    {
        var (vm, _) = Make();

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.OverviewCards.Single(c => c.Title == "Batterie").Note.Should().Contain("Aucune batterie");
    }

    [Fact]
    public async Task Overview_Win11Card_ListsChecks()
    {
        var (vm, _) = Make();

        await vm.RefreshCommand.ExecuteAsync(null);

        var card = vm.OverviewCards.Single(c => c.Title == "Compatibilité Windows 11");
        card.Lines.Should().HaveCount(7);
        card.Lines.Should().Contain(l => l.Label == "Processeur pris en charge" && l.State == CheckState.Unknown);
    }

    [Fact]
    public async Task LanguageSwitch_RebuildsCards()
    {
        var (vm, _) = Make();
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Localizer.Language = AppLanguage.English;

        vm.OverviewCards.Should().Contain(c => c.Title == "Battery");
    }

    [Fact]
    public async Task Filter_ByKind_StartupTab()
    {
        var (vm, _) = Make();
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.KindFilter = AutorunKind.Startup;

        vm.Autoruns.Select(a => a.Name).Should().Equal("OneDrive", "UpdaterHelper", "OldSync");
    }

    [Fact]
    public async Task Filter_ServicesTab_IncludesDrivers()
    {
        var (vm, _) = Make();
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.KindFilter = AutorunKind.Service;

        vm.Autoruns.Select(a => a.Name).Should().BeEquivalentTo("Spooler", "rgbsvc", "disk");
    }

    [Fact]
    public async Task Filter_UnsignedOnly()
    {
        var (vm, _) = Make();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.KindFilter = AutorunKind.Startup;

        vm.UnsignedOnly = true;

        vm.Autoruns.Select(a => a.Name).Should().Equal("UpdaterHelper", "OldSync");
    }

    [Fact]
    public async Task Search_MatchesNameAndPublisher_CaseInsensitive()
    {
        var (vm, _) = Make();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.KindFilter = null;

        vm.SearchText = "google";
        vm.Autoruns.Select(a => a.Name).Should().Equal("GoogleUpdate");

        vm.SearchText = "MICROSOFT";
        vm.Autoruns.Select(a => a.Name).Should().BeEquivalentTo("OneDrive", "Spooler", "disk");
    }

    [Fact]
    public async Task Rows_HaveSignatureLabelsAndCount()
    {
        var (vm, _) = Make();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.KindFilter = AutorunKind.Startup;

        vm.Autoruns.Select(a => a.SignatureLabel).Should().Equal("Signé", "Non signé", "Introuvable");
        vm.Autoruns[1].Publisher.Should().Be("—");
        vm.CountText.Should().Be("3 élément(s) · lecture seule");
    }

    [Fact]
    public async Task Refresh_WhileLoading_IsIgnored()
    {
        var gate = new ManualResetEventSlim();
        var (vm, src) = Make(() => { gate.Wait(TimeSpan.FromSeconds(5)); return Pc(); });

        var first = vm.RefreshCommand.ExecuteAsync(null);
        var second = vm.RefreshCommand.ExecuteAsync(null);
        gate.Set();
        await Task.WhenAll(first, second);

        src.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Boot_SummaryAndBars()
    {
        var now = DateTimeOffset.Now;
        var boots = Enumerable.Range(0, 10).Select(i =>
            new BootRecord(now.AddDays(-i), TimeSpan.FromSeconds(i < 5 ? 75 : 50), TimeSpan.Zero, TimeSpan.Zero,
                i == 0 ? [new BootDegradation(DegradationKind.Driver, "nv.sys", "NVIDIA", TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(6.2))] : [])).ToList();
        var (vm, _) = Make(boot: () => new BootHistory(boots));

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.HasBootData.Should().BeTrue();
        vm.BootBars.Should().HaveCount(10);
        vm.BootBars[^1].IsLast.Should().BeTrue(); // le plus récent à droite
        vm.BootBars[^1].Ratio.Should().BeApproximately(1.0, 0.01);
        vm.MedianText.Should().Be("75 s");
        vm.TrendText.Should().Be("En hausse");
        vm.LastDegradations.Single().Delay.Should().Be("6,2 s");
        vm.BootChartDescription.Should().Contain("10");
    }

    [Fact]
    public async Task Boot_NoData()
    {
        var (vm, _) = Make();

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.HasBootData.Should().BeFalse();
        vm.BootBars.Should().BeEmpty();
    }
}
