using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Findings;
using PCDoc.Modules.Optimize;
using PCDoc.Modules.System.Autoruns;

namespace PCDoc.Modules.Security;

public sealed record AntivirusInfo(string Name, bool Enabled, bool UpToDate, bool IsDefender);

public sealed record DefenderInfo(bool Available, bool Active, bool RealTime, int? SignatureAgeDays, int? QuickScanAgeDays, bool TamperProtected);

public sealed record Detection(string Name, int Severity, string? When, bool Removed, string? Resource);

/// <summary>Lecteur amovible : dossiers cachés à la racine et raccourcis qui lancent un script (« virus raccourci »).</summary>
public sealed record UsbDrive(RemovableDrive Drive, IReadOnlyList<RootItem> HiddenFolders, IReadOnlyList<string> SuspiciousShortcuts)
{
    public bool IsInfected => SuspiciousShortcuts.Count > 0;
}

public sealed record SecurityReport(
    IReadOnlyList<AntivirusInfo> Antivirus,
    DefenderInfo Defender,
    IReadOnlyList<Detection> Detections,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<UsbDrive> Usb);

public interface ISecuritySource
{
    Task<SecurityReport> AnalyzeAsync(CancellationToken cancellationToken);

    /// <summary>Analyse Defender : 1 = rapide, 2 = complète, 3 = un lecteur amovible (<paramref name="root"/>).</summary>
    Task<int> ScanAsync(int scanType, string? root, Action<string>? onOutput, CancellationToken cancellationToken);
}

/// <summary>
/// Antivirus (Centre de sécurité, Defender), pare-feu, SMBv1, cachettes de logiciels malveillants (IFEO, Userinit,
/// fichier hosts), réglages de sécurité réversibles, et lecteurs amovibles.
/// </summary>
public sealed partial class SecurityEngine(
    IWmiQuery wmi,
    IRegistryReader reader,
    IRegistryValueWriter writer,
    IProcessRunner runner,
    IDefenderCommand defender,
    IFileProbe files,
    IRemovableDrives drives,
    OptimizeEngine settings,
    string windowsDirectory) : ISecuritySource
{
    public const string IfeoPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    public const string IfeoPath32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    public const string WinlogonPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    public static readonly IReadOnlyList<string> FirewallProfiles = ["Domain", "Private", "Public"];

    private const string DefenderNamespace = @"root\Microsoft\Windows\Defender";
    private static readonly TimeSpan DismTimeout = TimeSpan.FromMinutes(10);
    private static readonly string[] ScriptHosts = ["cmd.exe", "wscript.exe", "cscript.exe", "rundll32.exe", "powershell.exe", "pwsh.exe", "mshta.exe", "regsvr32.exe"];
    private static readonly string[] SystemRootFolders = ["System Volume Information", "$RECYCLE.BIN", "RECYCLER"];

    private static readonly string[] SensitiveDomains =
    [
        "microsoft.com", "windowsupdate.com", "windows.com", "update.microsoft.com", "live.com", "virustotal.com", "malwarebytes.com",
        "kaspersky.com", "eset.com", "avast.com", "avg.com", "bitdefender.com", "norton.com", "mcafee.com", "emsisoft.com", "sophos.com",
    ];

    public OptimizeEngine Settings => settings;

    /// <summary>Appelé à la fin de chaque analyse Defender (type, code de résultat) pour le journal d'activité.</summary>
    public Action<int, int>? ScanCompleted { get; set; }

    public string ExpectedUserinit => windowsDirectory.TrimEnd('\\') + @"\system32\userinit.exe,";

    public async Task<SecurityReport> AnalyzeAsync(CancellationToken cancellationToken)
    {
        var antivirus = ReadAntivirus();
        var defenderInfo = ReadDefender(antivirus);
        var findings = new List<Finding>();

        if (!antivirus.Any(a => a.Enabled) && !defenderInfo.Active)
        {
            findings.Add(SecurityFindings.NoAntivirus());
        }

        if (defenderInfo.Active && !defenderInfo.RealTime)
        {
            findings.Add(SecurityFindings.RealTimeOff());
        }

        if (defenderInfo.Active && defenderInfo.SignatureAgeDays is > 3)
        {
            findings.Add(DefenderFixer.SignaturesFinding(defenderInfo.SignatureAgeDays.Value));
        }

        if (!ThirdPartyFirewall())
        {
            findings.AddRange(DisabledFirewallProfiles().Select(FirewallFixer.FindingFor));
        }

        if (await Smb1EnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            findings.Add(FeatureFixer.Smb1Finding());
        }

        findings.AddRange(IfeoDebuggers().Select(d => RegistryRepairFixer.IfeoFinding(d.Path, d.Exe, d.Debugger)));
        if (CurrentUserinit() is { } userinit && !IsExpectedUserinit(userinit))
        {
            findings.Add(RegistryRepairFixer.UserinitFinding(userinit));
        }

        if (HostsFinding() is { } hosts)
        {
            findings.Add(hosts);
        }

        var states = await settings.AnalyzeAsync(cancellationToken).ConfigureAwait(false);
        findings.AddRange(states.Where(s => s.Available && !s.Applied).Select(TweakFixer.FindingFor));

        var usb = ScanUsb();
        findings.AddRange(usb.Where(u => u.IsInfected).Select(UsbFixer.FindingFor));
        return new SecurityReport(antivirus, defenderInfo, ReadDetections(), findings, usb);
    }

    public async Task<int> ScanAsync(int scanType, string? root, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        var arguments = scanType switch
        {
            1 or 2 => $"-Scan -ScanType {scanType}",
            // Racine vérifiée (« E:\ », sans espace) : non guillemetée, sinon « \" » serait lu comme un guillemet échappé.
            3 when root is not null && DriveRoot().IsMatch(root) && drives.IsRemovableRoot(root) => $"-Scan -ScanType 3 -File {root}",
            _ => throw new ArgumentException("Invalid scan request", nameof(scanType)),
        };
        var exit = await defender.RunAsync(arguments, onOutput, scanType == 2 ? TimeSpan.FromHours(8) : TimeSpan.FromHours(2), cancellationToken)
            .ConfigureAwait(false);
        ScanCompleted?.Invoke(scanType, exit);
        return exit;
    }

    public Task<int> UpdateSignaturesAsync(CancellationToken cancellationToken) =>
        defender.RunAsync("-SignatureUpdate", null, TimeSpan.FromMinutes(30), cancellationToken);

    // ---------- Antivirus ----------

    internal IReadOnlyList<AntivirusInfo> ReadAntivirus()
    {
        try
        {
            return [.. wmi.Query(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct").Select(row =>
            {
                var state = Convert.ToInt64(row.GetValueOrDefault("productState") ?? 0L, CultureInfo.InvariantCulture);
                var name = row.GetValueOrDefault("displayName") as string ?? "?";
                // productState : octet du milieu = activé (0x10), octet bas = signatures à jour (0x00).
                return new AntivirusInfo(name, ((state >> 12) & 0xF) == 1, ((state >> 4) & 0xF) == 0,
                    name is "Windows Defender" or "Microsoft Defender Antivirus" or "Microsoft Defender");
            })];
        }
        catch (WmiUnavailableException)
        {
            return [];
        }
    }

    internal DefenderInfo ReadDefender(IReadOnlyList<AntivirusInfo> antivirus)
    {
        try
        {
            var rows = wmi.Query(DefenderNamespace,
                "SELECT AntivirusEnabled, AMRunningMode, RealTimeProtectionEnabled, AntivirusSignatureAge, QuickScanAge, IsTamperProtected FROM MSFT_MpComputerStatus");
            var row = rows.Count > 0 ? rows[0] : null;
            if (row is null)
            {
                return new DefenderInfo(false, false, false, null, null, false);
            }

            static bool Flag(object? v) => v is bool b && b;
            // Valeurs non signées ; 4294967295 (ou 65535) = jamais fait.
            static int? Days(object? v) => v is null ? null : Convert.ToInt64(v, CultureInfo.InvariantCulture) is var d && d is >= 0 and < 65535 ? (int)d : null;
            // Un autre antivirus actif met Defender en mode passif : ce n'est pas un problème.
            var otherActive = antivirus.Any(a => a.Enabled && !a.IsDefender);
            // AMRunningMode : « Normal » = actif ; « Passive Mode », « SxS Passive Mode », « EDR Block Mode » = passif.
            var mode = row.GetValueOrDefault("AMRunningMode") as string;
            var active = Flag(row.GetValueOrDefault("AntivirusEnabled")) && !otherActive
                         && (mode is null || mode.Equals("Normal", StringComparison.OrdinalIgnoreCase));
            return new DefenderInfo(true, active, Flag(row.GetValueOrDefault("RealTimeProtectionEnabled")),
                Days(row.GetValueOrDefault("AntivirusSignatureAge")), Days(row.GetValueOrDefault("QuickScanAge")),
                Flag(row.GetValueOrDefault("IsTamperProtected")));
        }
        catch (WmiUnavailableException)
        {
            return new DefenderInfo(false, false, false, null, null, false);
        }
    }

    internal IReadOnlyList<Detection> ReadDetections()
    {
        try
        {
            var names = wmi.Query(DefenderNamespace, "SELECT ThreatID, ThreatName, SeverityID FROM MSFT_MpThreat")
                .Where(r => r.GetValueOrDefault("ThreatID") is not null)
                .GroupBy(r => Convert.ToString(r["ThreatID"], CultureInfo.InvariantCulture)!)
                .ToDictionary(g => g.Key, g => g.First());
            return [.. wmi.Query(DefenderNamespace, "SELECT ThreatID, InitialDetectionTime, ActionSuccess, Resources FROM MSFT_MpThreatDetection")
                .Select(r =>
                {
                    var id = Convert.ToString(r.GetValueOrDefault("ThreatID"), CultureInfo.InvariantCulture) ?? string.Empty;
                    var threat = names.GetValueOrDefault(id);
                    var when = r.GetValueOrDefault("InitialDetectionTime") as string;
                    var resource = r.GetValueOrDefault("Resources") is string[] { Length: > 0 } res ? res[0] : null;
                    return new Detection(threat?.GetValueOrDefault("ThreatName") as string ?? id,
                        Convert.ToInt32(threat?.GetValueOrDefault("SeverityID") ?? 0, CultureInfo.InvariantCulture),
                        CimToLocal(when),
                        r.GetValueOrDefault("ActionSuccess") is true, resource);
                })];
        }
        catch (WmiUnavailableException)
        {
            return [];
        }
    }

    /// <summary>Date CIM « aaaaMMjjHHmmss.ffffff±ooo » (décalage en minutes) → heure locale lisible.</summary>
    internal static string? CimToLocal(string? cim)
    {
        if (cim is not { Length: >= 14 } || !DateTime.TryParseExact(cim[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            return null;
        }

        var offset = cim.Length >= 25 && int.TryParse(cim[21..25], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var m) ? m : 0;
        var utc = new DateTimeOffset(t, TimeSpan.FromMinutes(offset)).UtcDateTime;
        return utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    // ---------- Pare-feu ----------

    private bool ThirdPartyFirewall()
    {
        try
        {
            return wmi.Query(@"root\SecurityCenter2", "SELECT displayName, productState FROM FirewallProduct")
                .Any(r => ((Convert.ToInt64(r.GetValueOrDefault("productState") ?? 0L, CultureInfo.InvariantCulture) >> 12) & 0xF) == 1);
        }
        catch (WmiUnavailableException)
        {
            return false;
        }
    }

    /// <summary>Profils désactivés ; WMI indisponible → <see cref="WmiUnavailableException"/> via <see cref="TryDisabledFirewallProfiles"/>.</summary>
    public IReadOnlyList<string> DisabledFirewallProfiles() => TryDisabledFirewallProfiles() ?? [];

    /// <summary>Null si l'état du pare-feu n'a pas pu être lu.</summary>
    public IReadOnlyList<string>? TryDisabledFirewallProfiles()
    {
        try
        {
            return [.. wmi.Query(@"root\StandardCimv2", "SELECT Name, Enabled FROM MSFT_NetFirewallProfile")
                .Where(r => r.GetValueOrDefault("Name") is string n && FirewallProfiles.Contains(n)
                            && r.GetValueOrDefault("Enabled") is { } e && Convert.ToInt32(e, CultureInfo.InvariantCulture) == 0)
                .Select(r => (string)r["Name"]!)];
        }
        catch (WmiUnavailableException)
        {
            return null;
        }
    }

    public async Task SetFirewallAsync(string profile, bool enabled, CancellationToken cancellationToken)
    {
        if (!FirewallProfiles.Contains(profile))
        {
            throw new ArgumentException("Unknown firewall profile", nameof(profile));
        }

        var exit = await runner.RunHiddenAsync("netsh.exe", $"advfirewall set {profile.ToLowerInvariant()}profile state {(enabled ? "on" : "off")}", null,
            cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"netsh.exe exited with code {exit}");
        }
    }

    // ---------- SMBv1 ----------

    /// <summary>SMB1Protocol et ses sous-fonctions (client, serveur, suppression automatique).</summary>
    public static readonly IReadOnlyList<string> Smb1Features = ["SMB1Protocol", "SMB1Protocol-Client", "SMB1Protocol-Server", "SMB1Protocol-Deprecation"];

    public Task<bool> Smb1EnabledAsync(CancellationToken cancellationToken) => FeatureEnabledAsync("SMB1Protocol", cancellationToken);

    public async Task<bool> FeatureEnabledAsync(string feature, CancellationToken cancellationToken)
    {
        if (!Smb1Features.Contains(feature))
        {
            throw new ArgumentException("Unknown feature", nameof(feature));
        }

        var lines = new List<string>();
        var exit = await runner.RunHiddenAsync("dism.exe", $"/English /Online /Get-FeatureInfo /FeatureName:{feature}", lines.Add, cancellationToken,
            DismTimeout).ConfigureAwait(false);
        return exit == 0 && lines.Any(l => StateLine().Match(l) is { Success: true } m && m.Groups[1].Value.StartsWith("Enable", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>État de chaque fonction SMBv1 avant la correction (pour une annulation exacte).</summary>
    public async Task<Dictionary<string, bool>> Smb1StatesAsync(CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var f in Smb1Features)
        {
            states[f] = await FeatureEnabledAsync(f, cancellationToken).ConfigureAwait(false);
        }

        return states;
    }

    public async Task SetSmb1Async(bool enabled, CancellationToken cancellationToken) =>
        await SetFeatureAsync("SMB1Protocol", enabled, cancellationToken).ConfigureAwait(false);

    public async Task SetFeatureAsync(string feature, bool enabled, CancellationToken cancellationToken)
    {
        if (!Smb1Features.Contains(feature))
        {
            throw new ArgumentException("Unknown feature", nameof(feature));
        }

        // Sans /All : seule la fonction demandée est activée (les sous-fonctions sont remises une par une).
        var arguments = enabled
            ? $"/Online /Enable-Feature /FeatureName:{feature} /NoRestart"
            : $"/Online /Disable-Feature /FeatureName:{feature} /NoRestart";
        var exit = await runner.RunHiddenAsync("dism.exe", arguments, null, cancellationToken, DismTimeout).ConfigureAwait(false);
        if (exit is not (0 or 3010))
        {
            throw new InvalidOperationException($"dism.exe exited with code {exit}");
        }
    }

    // ---------- Cachettes ----------

    /// <summary>Valeurs « Debugger » des options d'exécution : un programme lancé est remplacé par un autre.</summary>
    public IReadOnlyList<(string Path, string Exe, string Debugger)> IfeoDebuggers()
    {
        var result = new List<(string, string, string)>();
        foreach (var root in new[] { IfeoPath, IfeoPath32 })
        {
            foreach (var exe in reader.GetSubKeyNames(RegistryHive.LocalMachine, root, RegistryView.Registry64))
            {
                if (IsIfeoName(exe) && reader.GetValue(RegistryHive.LocalMachine, $@"{root}\{exe}", "Debugger", RegistryView.Registry64) is string d
                    && d.Trim().Length > 0)
                {
                    result.Add((root, exe, d));
                }
            }
        }

        return result;
    }

    internal static bool IsIfeoName(string? name) => name is not null && IfeoName().IsMatch(name);

    public string? CurrentUserinit() => reader.GetValue(RegistryHive.LocalMachine, WinlogonPath, "Userinit", RegistryView.Registry64) as string;

    public bool IsExpectedUserinit(string value) =>
        value.Trim().TrimEnd(',').Trim().Equals(ExpectedUserinit.TrimEnd(','), StringComparison.OrdinalIgnoreCase);

    public object? ReadValue(string path, string name) => reader.GetValue(RegistryHive.LocalMachine, path, name, RegistryView.Registry64);

    public void WriteValue(string path, string name, string? value) => writer.SetValue(RegistryHive.LocalMachine, path, name, value);

    /// <summary>Domaines de Microsoft ou d'antivirus détournés dans le fichier hosts (constat, pas de correction automatique).</summary>
    internal Finding? HostsFinding()
    {
        var bytes = files.ReadSmallFile(windowsDirectory.TrimEnd('\\') + @"\System32\drivers\etc\hosts", 1 << 20);
        if (bytes is null)
        {
            return null;
        }

        var redirected = new List<string>();
        var blocked = new List<string>();
        var text = bytes is [0xFF, 0xFE, ..] ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
            : bytes is [0xFE, 0xFF, ..] ? Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
            : Encoding.UTF8.GetString(bytes);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var loopback = parts[0] is "0.0.0.0" or "::1" or "::" || parts[0].StartsWith("127.", StringComparison.Ordinal);
            foreach (var host in parts[1..].Where(h => SensitiveDomains.Any(d => h.Equals(d, StringComparison.OrdinalIgnoreCase)
                                                                                  || h.EndsWith("." + d, StringComparison.OrdinalIgnoreCase))))
            {
                (loopback ? blocked : redirected).Add($"{parts[0]} {host}");
            }
        }

        return redirected.Count > 0 ? SecurityFindings.Hosts(Severity.Warning, "Security.hosts.Redirected", redirected)
            : blocked.Count > 0 ? SecurityFindings.Hosts(Severity.Info, "Security.hosts.Blocked", blocked)
            : null;
    }

    // ---------- Lecteurs amovibles ----------

    public IReadOnlyList<UsbDrive> ScanUsb() => [.. drives.List().Select(d => ScanDrive(d.Root)).OfType<UsbDrive>()];

    public UsbDrive? ScanDrive(string root)
    {
        var drive = drives.List().FirstOrDefault(d => d.Root.Equals(root, StringComparison.OrdinalIgnoreCase));
        if (drive is null || root is null || !DriveRoot().IsMatch(root))
        {
            return null;
        }

        var items = drives.RootItems(root);
        var hidden = items.Where(i => i.IsDirectory && i.Attributes.HasFlag(FileAttributes.Hidden)
                                      && !SystemRootFolders.Contains(i.Path.TrimEnd('\\').Split('\\')[^1], StringComparer.OrdinalIgnoreCase)).ToList();
        var suspicious = items.Where(i => !i.IsDirectory && i.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && LaunchesScript(i.Path))
            .Select(i => i.Path).ToList();
        return new UsbDrive(drive, hidden, suspicious);
    }

    public bool IsRemovableRoot(string? root) => root is { Length: 3 } && DriveRoot().IsMatch(root) && drives.IsRemovableRoot(root);

    public IRemovableDrives Drives => drives;

    private bool LaunchesScript(string lnk)
    {
        if (files.ReadSmallFile(lnk, 1 << 16) is not { } data || LnkParser.TryRead(data) is not { } link)
        {
            return false;
        }

        var target = link.Target.Replace('/', '\\');
        var name = target[(target.LastIndexOf('\\') + 1)..];
        var args = link.Arguments ?? string.Empty;
        return ScriptHosts.Contains(name, StringComparer.OrdinalIgnoreCase)
               || Regex.IsMatch(args, @"\.(vbs|vbe|js|jse|wsf|bat|cmd|ps1|hta)\b", RegexOptions.IgnoreCase);
    }

    [GeneratedRegex(@"^[A-Za-z]:\\$")]
    internal static partial Regex DriveRoot();

    [GeneratedRegex(@"^[A-Za-z0-9_.\- ]{1,100}$")]
    private static partial Regex IfeoName();

    [GeneratedRegex(@"^\s*State\s*:\s*(\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex StateLine();
}
