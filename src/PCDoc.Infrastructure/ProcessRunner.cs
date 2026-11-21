using System.Diagnostics;
using System.Text;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure;

/// <summary>
/// Lance un outil de System32 sans fenêtre (CreateNoWindow, aucune console visible), avec un chemin complet
/// (jamais le PATH) et lit sa sortie ligne par ligne.
/// </summary>
public sealed partial class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(1);

    static ProcessRunner() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(system32Tool) || system32Tool.IndexOfAny(['\\', '/', ':', '"']) >= 0
            || !system32Tool.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only a System32 executable name is allowed", nameof(system32Tool));
        }

        var path = Path.Combine(Environment.SystemDirectory, system32Tool);
        var start = new ProcessStartInfo(path, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory,
            // SFC écrit en UTF-16 quand sa sortie est redirigée ; les autres outils dans la page de code OEM.
            StandardOutputEncoding = system32Tool.Equals("sfc.exe", StringComparison.OrdinalIgnoreCase)
                ? Encoding.Unicode
                : Encoding.GetEncoding((int)GetOEMCP()),
        };

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Forward(e.Data, onOutput);
        process.ErrorDataReceived += (_, e) => Forward(e.Data, onOutput);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout ?? DefaultTimeout);
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
                // Le processus s'est terminé entre-temps.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"{system32Tool} did not finish within {(timeout ?? DefaultTimeout).TotalMinutes:0} minutes");
        }

        return process.ExitCode;
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();

    private static void Forward(string? line, Action<string>? onOutput)
    {
        var text = line?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        if (!string.IsNullOrEmpty(text))
        {
            onOutput?.Invoke(text);
        }
    }
}
