using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PCDoc.Core;
using PCDoc.Core.Abstractions;
using PCDoc.Core.AppUpdates;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Mise à jour de PCDoc depuis les versions publiées sur GitHub. Aucune information n'est envoyée (simple lecture de
/// la dernière version). L'installation automatique n'est faite que pour PCDoc installé dans Program Files et signé :
/// le MSI téléchargé (dossier protégé) doit avoir la somme SHA-256 publiée, une signature valide et le MÊME
/// signataire que PCDoc : nom complet identique, identité Trusted Signing (EKU 1.3.6.1.4.1.311.97.*) identique, chaîne
/// vers une racine de la machine (pas celles ajoutées par un compte) et certificats non révoqués. msiexec n'est lancé
/// qu'à la fermeture de PCDoc. Sinon la page de téléchargement est proposée.
/// </summary>
public sealed class AppUpdater(ISignatureVerifier signatures) : IAppUpdater
{
    private const long MaxInstaller = 400L * 1024 * 1024;
    private static readonly string[] AllowedHosts = ["api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    private static readonly HttpClient Http = CreateClient();

    public async Task<AppRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        PurgeOldDownloads(TimeSpan.FromHours(1));
        var current = ReleaseFeed.ParseVersion(AppInfo.Version) ?? new Version(0, 0, 0);
        var json = await GetTextAsync(ReleaseFeed.LatestUrl, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        return ReleaseFeed.Parse(json, current, Architecture(), AppInfo.Version.Contains('-', StringComparison.Ordinal));
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

    public async Task<string> DownloadAsync(AppRelease release, Action<string>? progress, CancellationToken cancellationToken)
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

        PurgeOldDownloads(TimeSpan.Zero);
        var folder = Path.Combine(SecureFolders.EnsureChild("temp"), "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var msi = Path.Combine(folder, release.InstallerName);
            progress?.Invoke(release.InstallerName);
            var sums = await GetTextAsync(release.ChecksumsUrl, 64 * 1024, cancellationToken).ConfigureAwait(false);
            var expected = ReleaseFeed.Sha256For(sums, release.InstallerName) ?? throw new InvalidOperationException("No checksum for the installer");
            await DownloadFileAsync(release.InstallerUrl, msi, cancellationToken).ConfigureAwait(false);

            string actual;
            await using (var stream = File.OpenRead(msi))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            }

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The installer checksum does not match");
            }

            var mine = signatures.Verify(Environment.ProcessPath!);
            var theirs = signatures.Verify(msi);
            var signer = theirs.Status == SignatureStatus.Signed ? SameSigner(Environment.ProcessPath!, msi) : "not signed";
            if (theirs.Status != SignatureStatus.Signed || !string.Equals(theirs.Publisher, mine.Publisher, StringComparison.Ordinal) || signer is not null)
            {
                throw new InvalidOperationException($"Installer signature refused ({theirs.Status}, {theirs.Publisher}, {signer})");
            }

            return msi;
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    public void LaunchInstaller(string installer)
    {
        ArgumentNullException.ThrowIfNull(installer);
        var full = Path.GetFullPath(installer);
        var folder = Path.GetDirectoryName(full)!;
        if (!TrustedPaths.IsUnder(full, SecureFolders.Root) || !Path.GetFileName(folder).StartsWith("update-", StringComparison.Ordinal)
            || !full.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new InvalidOperationException("Unexpected installer path");
        }

        // msiexec (élevé comme PCDoc) ; journal détaillé dans le dossier protégé ; pas de redémarrage automatique.
        var log = Path.Combine(folder, "install.log");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), $"/i \"{full}\" /passive /norestart /l*v \"{log}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = folder,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("msiexec did not start");
    }

    /// <summary>
    /// Null si le MSI a le même signataire que PCDoc ; sinon la raison. Le nom seul ne suffit pas : une autre personne
    /// ou une racine ajoutée au magasin de l'utilisateur pourrait produire un certificat au même nom.
    /// </summary>
    internal static string? SameSigner(string exe, string msi)
    {
        try
        {
#pragma warning disable SYSLIB0057 // Lecture du certificat du signataire d'un fichier signé (Authenticode), pas d'un fichier de certificat.
            using var mine = new X509Certificate2(X509Certificate.CreateFromSignedFile(exe));
            using var theirs = new X509Certificate2(X509Certificate.CreateFromSignedFile(msi));
#pragma warning restore SYSLIB0057
            if (!string.Equals(mine.Subject, theirs.Subject, StringComparison.Ordinal))
            {
                return "different subject";
            }

            var myIdentity = TrustedSigningIdentity(mine);
            if (myIdentity.Count > 0 && !myIdentity.SetEquals(TrustedSigningIdentity(theirs)))
            {
                return "different signing identity";
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            using (var roots = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                roots.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                chain.ChainPolicy.CustomTrustStore.AddRange(roots.Certificates);
            }

            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.3")); // signature de code
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            // Validité dans le temps déjà vérifiée par WinVerifyTrust avec l'horodatage (certificats Trusted Signing de 3 jours).
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
            return chain.Build(theirs) ? null : "chain: " + string.Join(", ", chain.ChainStatus.Select(c => c.Status));
        }
        catch (CryptographicException ex)
        {
            return ex.Message;
        }
    }

    private static HashSet<string> TrustedSigningIdentity(X509Certificate2 certificate) =>
        [.. certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(e => e.EnhancedKeyUsages.Cast<Oid>())
            .Select(o => o.Value ?? string.Empty)
            .Where(v => v.StartsWith("1.3.6.1.4.1.311.97.", StringComparison.Ordinal))];

    private static void PurgeOldDownloads(TimeSpan olderThan)
    {
        try
        {
            foreach (var old in Directory.EnumerateDirectories(SecureFolders.EnsureChild("temp"), "update-*"))
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(old) >= olderThan)
                {
                    TryDelete(old);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Dossier protégé indisponible : rien à purger.
        }
    }

    private static string Architecture() => RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = Timeout.InfiniteTimeSpan };
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        cancellationToken = timeout.Token;
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        CheckFinalHost(response);
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidOperationException("Response too large");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return bytes.Length > maxBytes ? throw new InvalidOperationException("Response too large") : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        cancellationToken = timeout.Token;
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
