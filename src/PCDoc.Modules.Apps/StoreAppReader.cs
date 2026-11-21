using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.Apps;

/// <summary>
/// Applications du Store du profil cible et paquets provisionnés, via PowerShell caché. La sortie est du JSON encodé
/// en base64 (insensible à la page de code de la console).
/// </summary>
public sealed partial class StoreAppReader(IProcessRunner runner, IAppDisplayNames names, IUserProfileResolver profiles)
{
    public const string PowerShell = @"WindowsPowerShell\v1.0\powershell.exe";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public async Task<IReadOnlyList<InstalledApp>> ReadAsync(CancellationToken cancellationToken)
    {
        var profile = profiles.Resolve();
        var user = profile.IsProcessUser || !Sid().IsMatch(profile.Sid) ? string.Empty : $" -User '{profile.Sid}'";
        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $p = @(Get-AppxPackage{{user}} | ForEach-Object { [pscustomobject]@{ N = $_.Name; F = $_.PackageFullName; Fa = $_.PackageFamilyName; P = $_.Publisher; V = [string]$_.Version; L = $_.InstallLocation; Fw = [bool]$_.IsFramework; Nr = [bool]$_.NonRemovable; S = [string]$_.SignatureKind } })
            $v = @(Get-AppxProvisionedPackage -Online | ForEach-Object { [pscustomobject]@{ D = $_.DisplayName; N = $_.PackageName } })
            $j = [pscustomobject]@{ Packages = $p; Provisioned = $v } | ConvertTo-Json -Compress -Depth 3
            'PCDOC:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($j))
            """;
        var lines = new List<string>();
        var exit = await runner.RunHiddenAsync(PowerShell, Arguments(script), lines.Add, cancellationToken, Timeout).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException($"powershell.exe exited with code {exit}");
        }

        return Parse(lines.LastOrDefault(l => l.StartsWith("PCDOC:", StringComparison.Ordinal)));
    }

    /// <summary>Script passé en -EncodedCommand (UTF-16 en base64) : aucun problème de guillemets.</summary>
    public static string Arguments(string script) =>
        "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "
        + Convert.ToBase64String(Encoding.Unicode.GetBytes(script ?? string.Empty));

    internal IReadOnlyList<InstalledApp> Parse(string? line)
    {
        if (line is null)
        {
            return [];
        }

        Output? output;
        try
        {
            output = JsonSerializer.Deserialize<Output>(Encoding.UTF8.GetString(Convert.FromBase64String(line[6..])));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return [];
        }

        var provisioned = (output?.Provisioned ?? [])
            .Where(v => v.N is not null && SafeName().IsMatch(v.N) && v.D is not null)
            .GroupBy(v => v.D!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().N!, StringComparer.OrdinalIgnoreCase);
        var apps = new List<InstalledApp>();
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in output?.Packages ?? [])
        {
            if (p.N is null || p.F is null || p.Fa is null || !SafeName().IsMatch(p.F) || !SafeName().IsMatch(p.Fa) || !families.Add(p.Fa))
            {
                continue;
            }

            var package = new StorePackage(p.N, p.F, p.Fa, p.P, p.V, p.L, p.Fw, p.Nr, string.Equals(p.S, "System", StringComparison.OrdinalIgnoreCase));
            apps.Add(new InstalledApp(
                $"store:{p.Fa}", AppKind.Store, AppScope.User, names.Resolve(package) ?? p.N, Publisher(p.P), p.V,
                InstallLocation: p.L, PackageFullName: p.F, PackageFamilyName: p.Fa,
                ProvisionedName: provisioned.GetValueOrDefault(p.N), IsFramework: p.Fw, NonRemovable: p.Nr || package.IsSystemSigned));
        }

        // Paquets provisionnés absents du profil : ils seraient installés pour chaque nouveau compte.
        foreach (var (name, full) in provisioned)
        {
            var family = FamilyOf(full);
            if (family is not null && families.Add(family))
            {
                apps.Add(new InstalledApp($"store:{family}", AppKind.Store, AppScope.Machine, name, PackageFamilyName: family, ProvisionedName: full));
            }
        }

        return apps;
    }

    /// <summary>« Nom_Version_Archi_Ressource_Éditeur » → « Nom_Éditeur ».</summary>
    internal static string? FamilyOf(string fullName)
    {
        var parts = fullName.Split('_');
        return parts.Length == 5 ? $"{parts[0]}_{parts[4]}" : null;
    }

    internal static bool IsSafeName(string? value) => value is not null && SafeName().IsMatch(value);

    internal static bool IsSid(string? value) => value is not null && Sid().IsMatch(value);

    private static string? Publisher(string? subject) =>
        subject is null ? null : Regex.Match(subject, "(?:^|, )O=\"?([^\",]+)") is { Success: true } m ? m.Groups[1].Value : subject;

    [GeneratedRegex(@"^[A-Za-z0-9._~\-]{1,255}$")]
    private static partial Regex SafeName();

    [GeneratedRegex(@"^S-1-[0-9]+(-[0-9]+){1,14}$")]
    private static partial Regex Sid();

#pragma warning disable CA1812
    private sealed record Output(Package[]? Packages, Provisioned[]? Provisioned);

    private sealed record Package(string? N, string? F, string? Fa, string? P, string? V, string? L, bool Fw, bool Nr, string? S);

    private sealed record Provisioned(string? D, string? N);
#pragma warning restore CA1812
}
