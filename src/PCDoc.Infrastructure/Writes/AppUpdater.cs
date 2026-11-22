using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PCDoc.Core;
using PCDoc.Core.Abstractions;
using PCDoc.Core.AppUpdates;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Mise à jour de PCDoc depuis les versions publiées sur GitHub. Aucune information n'est envoyée (simple lecture de
/// la dernière version). L'installation automatique n'est faite que pour PCDoc installé dans Program Files et signé :
/// le MSI téléchargé (dossier protégé) doit avoir la somme SHA-256 publiée ET la même signature que PCDoc lui-même.
/// Sinon la page de téléchargement est proposée.
/// </summary>
public sealed class AppUpdater(ISignatureVerifier signatures) : IAppUpdater
{
    private const long MaxInstaller = 400L * 1024 * 1024;
    private static readonly string[] AllowedHosts = ["api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    private static readonly HttpClient Http = CreateClient();

    public async Task<AppRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        var current = ReleaseFeed.ParseVersion(AppInfo.Version) ?? new Version(0, 0, 0);
        var json = await GetTextAsync(ReleaseFeed.LatestUrl, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        return ReleaseFeed.Parse(json, current, Architecture());
    }

    public string? InstallBlockedReason()
    {
        var exe = Environment.ProcessPath;
        var folder = Path.Combine(TrustedPaths.ProgramFiles, "PCDoc");
        if (exe is null || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(exe))?.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase)
            || !TrustedPaths.HasNoLinks(exe, TrustedPaths.ProgramFiles) || !TrustedPaths.OnlyAdminsCanWrite(exe, TrustedPaths.ProgramFiles))
        {
            return "AppUpdate.NotInstalled";
        }

        return signatures.Verify(exe) is { Status: SignatureStatus.Signed, Publisher: not null } ? null : "AppUpdate.Unsigned";
    }

    public async Task InstallAsync(AppRelease release, Action<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (InstallBlockedReason() is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        if (release.InstallerUrl is null || release.InstallerName is null || release.ChecksumsUrl is null)
        {
            throw new InvalidOperationException("The release has no installer for this PC");
        }

        var temp = SecureFolders.EnsureChild("temp");
        foreach (var old in Directory.EnumerateDirectories(temp, "update-*"))
        {
            TryDelete(old); // installateurs des mises à jour précédentes (msiexec en a gardé sa propre copie)
        }

        var folder = Path.Combine(temp, "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var msi = Path.Combine(folder, release.InstallerName);
        progress?.Invoke(release.InstallerName);
        var sums = await GetTextAsync(release.ChecksumsUrl, 64 * 1024, cancellationToken).ConfigureAwait(false);
        var expected = ReleaseFeed.Sha256For(sums, release.InstallerName) ?? throw new InvalidOperationException("No checksum for the installer");
        await DownloadAsync(release.InstallerUrl, msi, cancellationToken).ConfigureAwait(false);

        string actual;
        await using (var stream = File.OpenRead(msi))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        }

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            TryDelete(folder);
            throw new InvalidOperationException("The installer checksum does not match");
        }

        var mine = signatures.Verify(Environment.ProcessPath!);
        var theirs = signatures.Verify(msi);
        if (theirs.Status != SignatureStatus.Signed || !string.Equals(theirs.Publisher, mine.Publisher, StringComparison.Ordinal))
        {
            TryDelete(folder);
            throw new InvalidOperationException($"Installer signature refused ({theirs.Status}, {theirs.Publisher})");
        }

        // msiexec (élevé comme PCDoc) remplace PCDoc une fois celui-ci fermé ; l'appelant ferme l'application.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), $"/i \"{msi}\" /passive /norestart")
        {
            UseShellExecute = false,
            WorkingDirectory = folder,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("msiexec did not start");
        progress?.Invoke("msiexec");
    }

    private static string Architecture() => RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PCDoc-update-check"); // requis par l'API GitHub ; aucune donnée sur le PC
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static void CheckFinalHost(HttpResponseMessage response)
    {
        var final = response.RequestMessage?.RequestUri;
        if (final?.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(final.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Download was redirected to an unexpected site ({final?.Host})");
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetTextAsync(string url, int maxBytes, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        CheckFinalHost(response);
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidOperationException("Response too large");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return bytes.Length > maxBytes ? throw new InvalidOperationException("Response too large") : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static async Task DownloadAsync(string url, string path, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        CheckFinalHost(response);
        if (response.Content.Headers.ContentLength > MaxInstaller)
        {
            throw new InvalidOperationException("Download too large");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxInstaller)
            {
                throw new InvalidOperationException("Download too large");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            CleanupFileSystem.DeleteTree(folder, SecureFolders.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Encore utilisé par msiexec : supprimé la fois suivante.
        }
    }
}
