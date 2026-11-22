using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PCDoc.Core.Abstractions;
using PCDoc.Core.Activity;

namespace PCDoc.Infrastructure.Writes;

/// <summary>Tables TCP (IPv4/IPv6) et UDP de Windows avec le programme propriétaire (GetExtendedTcpTable).</summary>
public sealed partial class NetworkConnections : INetworkConnections
{
    private static readonly string[] States = ["", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB"];

    public IReadOnlyList<NetConnection> List()
    {
        var names = new Dictionary<int, (string? Name, string? Path)>();
        (string?, string?) Process(int pid)
        {
            if (!names.TryGetValue(pid, out var info))
            {
                names[pid] = info = (NameOf(pid), PathOf(pid));
            }

            return info;
        }

        var result = new List<NetConnection>();
        foreach (var (row, v6) in TcpRows(2).Select(r => (r, false)).Concat(TcpRows(23).Select(r => (r, true))))
        {
            var (name, path) = Process(row.Pid);
            result.Add(new NetConnection(v6 ? "TCPv6" : "TCP", row.Local, row.LocalPort, row.State == 2 ? null : row.Remote, row.State == 2 ? null : row.RemotePort,
                row.State is >= 0 and < 13 ? States[row.State] : "?", row.Pid, name, path));
        }

        foreach (var (address, port, pid) in UdpRows())
        {
            var (name, path) = Process(pid);
            result.Add(new NetConnection("UDP", address, port, null, null, "UDP", pid, name, path));
        }

        return result;
    }

    private sealed record TcpRow(string Local, int LocalPort, string Remote, int RemotePort, int State, int Pid);

    private static List<TcpRow> TcpRows(int family)
    {
        var rows = new List<TcpRow>();
        var buffer = Query((p, ref s) => GetExtendedTcpTable(p, ref s, false, family, 5, 0));
        if (buffer is null)
        {
            return rows;
        }

        var count = BitConverter.ToInt32(buffer, 0);
        var size = family == 2 ? 24 : 56;
        for (var i = 0; i < count && 4 + ((i + 1) * size) <= buffer.Length; i++)
        {
            var o = 4 + (i * size);
            if (family == 2)
            {
                rows.Add(new TcpRow(new IPAddress(BitConverter.ToUInt32(buffer, o + 4)).ToString(), Port(buffer, o + 8),
                    new IPAddress(BitConverter.ToUInt32(buffer, o + 12)).ToString(), Port(buffer, o + 16), BitConverter.ToInt32(buffer, o), BitConverter.ToInt32(buffer, o + 20)));
            }
            else
            {
                rows.Add(new TcpRow(new IPAddress(buffer[o..(o + 16)]).ToString(), Port(buffer, o + 20),
                    new IPAddress(buffer[(o + 24)..(o + 40)]).ToString(), Port(buffer, o + 44), BitConverter.ToInt32(buffer, o + 48), BitConverter.ToInt32(buffer, o + 52)));
            }
        }

        return rows;
    }

    private static List<(string, int, int)> UdpRows()
    {
        var rows = new List<(string, int, int)>();
        var buffer = Query((p, ref s) => GetExtendedUdpTable(p, ref s, false, 2, 1, 0));
        if (buffer is null)
        {
            return rows;
        }

        var count = BitConverter.ToInt32(buffer, 0);
        for (var i = 0; i < count && 4 + ((i + 1) * 12) <= buffer.Length; i++)
        {
            var o = 4 + (i * 12);
            rows.Add((new IPAddress(BitConverter.ToUInt32(buffer, o)).ToString(), Port(buffer, o + 4), BitConverter.ToInt32(buffer, o + 8)));
        }

        return rows;
    }

    private static int Port(byte[] buffer, int offset) => (buffer[offset] << 8) | buffer[offset + 1];

    private delegate uint TableCall(IntPtr buffer, ref int size);

    private static byte[]? Query(TableCall call)
    {
        var size = 0;
        call(IntPtr.Zero, ref size);
        for (var attempt = 0; attempt < 3 && size > 0; attempt++)
        {
            var memory = Marshal.AllocHGlobal(size);
            try
            {
                if (call(memory, ref size) == 0)
                {
                    var bytes = new byte[size];
                    Marshal.Copy(memory, bytes, 0, size);
                    return bytes;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        return null;
    }

    private static string? NameOf(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return pid == 4 ? "System" : p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? PathOf(int pid)
    {
        var handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, int reserved);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, int reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, [Out] char[] name, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>
/// Extensions de Chrome, Edge, Brave (manifest.json, nom traduit via _locales) et Firefox (extensions.json) du profil
/// cible. Lecture seule ; les liens du profil ne sont pas suivis ; fichiers de plus de 1 Mo ignorés.
/// </summary>
public sealed class BrowserExtensions(IUserProfileResolver profiles) : IBrowserExtensions
{
    private const int MaxJson = 1 << 20;

    private static readonly (string Browser, string Folder)[] Chromium =
    [
        ("Chrome", @"Google\Chrome\User Data"),
        ("Edge", @"Microsoft\Edge\User Data"),
        ("Brave", @"BraveSoftware\Brave-Browser\User Data"),
    ];

    public IReadOnlyList<BrowserExtension> List()
    {
        var profile = profiles.Resolve();
        var result = new List<BrowserExtension>();
        foreach (var (browser, folder) in Chromium)
        {
            var root = Path.Combine(profile.LocalAppData, folder);
            foreach (var userProfile in Dirs(root).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)))
            {
                foreach (var extension in Dirs(Path.Combine(userProfile, "Extensions")))
                {
                    var version = Dirs(extension).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault();
                    if (version is not null && ReadChromium(browser, Path.GetFileName(extension), version) is { } item)
                    {
                        result.Add(item);
                    }
                }
            }
        }

        foreach (var firefoxProfile in Dirs(Path.Combine(profile.RoamingAppData, @"Mozilla\Firefox\Profiles")))
        {
            result.AddRange(ReadFirefox(firefoxProfile));
        }

        return [.. result.GroupBy(e => (e.Browser, e.Id)).Select(g => g.First()).OrderBy(e => e.Browser).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static BrowserExtension? ReadChromium(string browser, string id, string folder)
    {
        using var manifest = Json(Path.Combine(folder, "manifest.json"));
        if (manifest is null)
        {
            return null;
        }

        var root = manifest.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
        if (name.StartsWith("__MSG_", StringComparison.Ordinal) && name.EndsWith("__", StringComparison.Ordinal))
        {
            var key = name[6..^2];
            var locale = root.TryGetProperty("default_locale", out var l) ? l.GetString() : "en";
            using var messages = Json(Path.Combine(folder, "_locales", locale ?? "en", "messages.json"));
            name = messages?.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) is { Value: var v }
                   && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("message", out var m) ? m.GetString() ?? id : id;
        }

        return new BrowserExtension(browser, id, name, root.TryGetProperty("version", out var ver) ? ver.GetString() : null, null, folder);
    }

    private static IEnumerable<BrowserExtension> ReadFirefox(string profile)
    {
        using var doc = Json(Path.Combine(profile, "extensions.json"));
        if (doc is null || !doc.RootElement.TryGetProperty("addons", out var addons) || addons.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var addon in addons.EnumerateArray())
        {
            var type = addon.TryGetProperty("type", out var t) ? t.GetString() : null;
            var location = addon.TryGetProperty("location", out var loc) ? loc.GetString() : null;
            if (type != "extension" || location is "app-builtin" or "app-system-defaults" or "app-system-addons")
            {
                continue;
            }

            var id = addon.TryGetProperty("id", out var i) ? i.GetString() ?? "?" : "?";
            var name = addon.TryGetProperty("defaultLocale", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("name", out var dn)
                ? dn.GetString() ?? id
                : id;
            yield return new BrowserExtension("Firefox", id, name, addon.TryGetProperty("version", out var v) ? v.GetString() : null,
                addon.TryGetProperty("active", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False ? a.GetBoolean() : null, profile);
        }
    }

    private static List<string> Dirs(string path)
    {
        try
        {
            return Directory.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                ? new DirectoryInfo(path).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                    .Select(d => d.FullName).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static JsonDocument? Json(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxJson || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            return JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Seconde analyse par un outil d'un autre éditeur : téléchargé en HTTPS depuis son site officiel dans un dossier
/// temporaire protégé, lancé seulement si sa signature est valide et du bon éditeur, puis supprimé. Son journal est
/// copié dans les rapports.
/// </summary>
public sealed class SecondOpinion(ISignatureVerifier signatures, IReportWriter? reports) : ISecondOpinion
{
    private const long MaxDownload = 600L * 1024 * 1024;

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 })
    {
        Timeout = TimeSpan.FromMinutes(20),
    };

    public async Task<SecondOpinionResult> RunAsync(SecondOpinionTool tool, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        var (url, file, publisher, arguments, timeout) = tool switch
        {
            SecondOpinionTool.AdwCleaner => ("https://downloads.malwarebytes.com/file/adwcleaner", "adwcleaner.exe", "Malwarebytes", "/eula /clean /noreboot /path \"{0}\"",
                TimeSpan.FromHours(1)),
            _ => (RuntimeInformation.OSArchitecture == Architecture.X86 ? "https://go.microsoft.com/fwlink/?LinkId=212733" : "https://go.microsoft.com/fwlink/?LinkId=212732",
                "msert.exe", "Microsoft Corporation", "/Q", TimeSpan.FromHours(3)),
        };
        if (tool == SecondOpinionTool.SafetyScanner && RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            throw new PlatformNotSupportedException("Microsoft Safety Scanner has no ARM64 version");
        }

        var folder = Path.Combine(SecureFolders.EnsureChild("temp"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var exe = Path.Combine(folder, file);
            onOutput?.Invoke($"Download {url}");
            await DownloadAsync(url, exe, cancellationToken).ConfigureAwait(false);
            var signature = signatures.Verify(exe);
            if (signature.Status != SignatureStatus.Signed || signature.Publisher?.Contains(publisher, StringComparison.OrdinalIgnoreCase) != true)
            {
                throw new InvalidOperationException($"{file}: signature {signature.Status} ({signature.Publisher}) refused");
            }

            onOutput?.Invoke($"Signature OK: {signature.Publisher}");
            var exit = await RunHiddenAsync(exe, string.Format(System.Globalization.CultureInfo.InvariantCulture, arguments, folder), onOutput, timeout, cancellationToken)
                .ConfigureAwait(false);
            var log = ReadLog(tool, folder);
            if (log.Count > 0)
            {
                try
                {
                    reports?.Save($"{tool}-{DateTime.Now:yyyyMMdd-HHmmss}.txt", string.Join(Environment.NewLine, log));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Le résultat reste affiché dans la page.
                }
            }

            return new SecondOpinionResult(exit, [.. log.TakeLast(40)]);
        }
        finally
        {
            try
            {
                CleanupFileSystem.DeleteTree(folder, SecureFolders.Root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Supprimé à la prochaine purge.
            }
        }
    }

    private static async Task DownloadAsync(string url, string path, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Download was redirected outside HTTPS");
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxDownload)
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
            if (total > MaxDownload)
            {
                throw new InvalidOperationException("Download too large");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> RunHiddenAsync(string exe, string arguments, Action<string>? onOutput, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        var gate = new object();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { lock (gate) { onOutput?.Invoke(e.Data.Trim()); } } };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { lock (gate) { onOutput?.Invoke(e.Data.Trim()); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Déjà terminé.
            }

            throw;
        }

        return process.ExitCode;
    }

    /// <summary>Journal : AdwCleaner dans &lt;dossier&gt;\AdwCleaner\Logs, Safety Scanner dans %SystemRoot%\debug\msert.log.</summary>
    private static List<string> ReadLog(SecondOpinionTool tool, string folder)
    {
        try
        {
            var file = tool == SecondOpinionTool.AdwCleaner
                ? new DirectoryInfo(Path.Combine(folder, "AdwCleaner", "Logs")) is { Exists: true } logs
                    ? logs.EnumerateFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
                    : null
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "debug", "msert.log");
            if (file is null || !File.Exists(file) || new FileInfo(file).Length > 8 * 1024 * 1024)
            {
                return [];
            }

            var bytes = File.ReadAllBytes(file);
            var text = bytes is [0xFF, 0xFE, ..] ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2) : Encoding.UTF8.GetString(bytes);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            return tool == SecondOpinionTool.SafetyScanner ? lines.TakeLast(200).ToList() : lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>
/// Client VirusTotal (API v3, recherche par empreinte). La clé de l'utilisateur est chiffrée avec DPAPI (compte
/// actuel) dans le dossier de données ; elle n'est jamais journalisée.
/// </summary>
public sealed partial class VirusTotalClient(string keyFile) : IVirusTotal
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30), BaseAddress = new Uri("https://www.virustotal.com/api/v3/") };

    private string? _key = Load(keyFile);

    public bool HasKey => !string.IsNullOrEmpty(_key);

    public void SetKey(string? apiKey)
    {
        var key = apiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _key = null;
            if (File.Exists(keyFile))
            {
                File.Delete(keyFile);
            }

            return;
        }

        if (key.Length != 64 || !key.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("A VirusTotal API key has 64 hexadecimal characters", nameof(apiKey));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
        File.WriteAllBytes(keyFile, Protect(Encoding.UTF8.GetBytes(key)));
        _key = key;
    }

    public async Task<VirusTotalStats?> LookupAsync(string sha256, CancellationToken cancellationToken)
    {
        if (sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Invalid SHA-256", nameof(sha256));
        }

        var key = _key ?? throw new InvalidOperationException("No VirusTotal key");
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"files/{sha256}");
            request.Headers.Add("x-apikey", key);
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return null;
                case HttpStatusCode.TooManyRequests when attempt == 0:
                    await Task.Delay(TimeSpan.FromSeconds(61), cancellationToken).ConfigureAwait(false);
                    continue;
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new InvalidOperationException("VirusTotal refused the API key");
            }

            response.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var stats = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("last_analysis_stats");
            int Get(string name) => stats.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;
            return new VirusTotalStats(Get("malicious"), Get("suspicious"), Get("harmless"), Get("undetected"));
        }
    }

    private static string? Load(string path)
    {
        try
        {
            return File.Exists(path) ? Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(path))) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null; // clé d'un autre compte ou d'un autre PC (mode portable) : à ressaisir
        }
    }

    private static byte[] Protect(byte[] data) => Crypt(data, protect: true);

    private static byte[] Unprotect(byte[] data) => Crypt(data, protect: false);

    private static byte[] Crypt(byte[] data, bool protect)
    {
        var input = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, input, data.Length);
            var blobIn = new DataBlob { Size = data.Length, Data = input };
            var ok = protect
                ? CryptProtectData(ref blobIn, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, out var blobOut)
                : CryptUnprotectData(ref blobIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, out blobOut);
            if (!ok)
            {
                throw new InvalidOperationException("DPAPI failed");
            }

            try
            {
                var result = new byte[blobOut.Size];
                Marshal.Copy(blobOut.Data, result, 0, blobOut.Size);
                return result;
            }
            finally
            {
                LocalFree(blobOut.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
