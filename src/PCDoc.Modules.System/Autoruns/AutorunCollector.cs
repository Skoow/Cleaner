using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Autoruns;

/// <summary>Éléments lus, et sources qui n'ont pas pu être lues (« startup », « tasks », « services »).</summary>
public sealed record AutorunScanResult(IReadOnlyList<AutorunEntry> Entries, IReadOnlyList<string> FailedSources);

/// <summary>Source des éléments lancés automatiquement (simulable dans les tests de la page Système).</summary>
public interface IAutorunSource
{
    AutorunScanResult CollectAll();
}

/// <summary>Démarrage + tâches planifiées + services + pilotes, chacun avec la signature de son fichier.</summary>
public sealed class AutorunCollector(
    IRegistryReader registry,
    IFileProbe files,
    IUserProfileResolver profiles,
    ISignatureVerifier signatures,
    IScheduledTaskReader tasks,
    IServiceReader services) : IAutorunSource
{
    /// <summary>Chaque source est lue séparément : une source en panne n'efface pas les autres.</summary>
    public AutorunScanResult CollectAll()
    {
        var entries = new List<Pending>();
        var failed = new List<string>();

        Read("startup", failed, () =>
        {
            foreach (var item in new StartupCollector(registry, files, profiles).Collect())
            {
                entries.Add(Entry(AutorunKind.Startup, item.Name, item.Location, item.CommandLine,
                    CommandLineParser.ExtractExecutable(item.CommandLine, files), item.Enabled) with { Startup = item.Toggle });
            }
        });

        Read("tasks", failed, () =>
        {
            foreach (var task in tasks.ReadAll())
            {
                foreach (var command in task.ExecCommands)
                {
                    var isCom = command.StartsWith("COM ", StringComparison.OrdinalIgnoreCase);
                    entries.Add(Entry(AutorunKind.ScheduledTask, task.Path, task.Path, command,
                        isCom ? null : CommandLineParser.ExtractExecutable(command, files), task.Enabled));
                }
            }
        });

        Read("services", failed, () =>
        {
            foreach (var service in services.ReadAll())
            {
                var image = service.ImagePath ?? string.Empty;
                var path = string.IsNullOrWhiteSpace(image) ? null
                    : service.IsDriver ? CommandLineParser.NormalizeImagePath(image, files)
                    : CommandLineParser.ExtractExecutable(image, files);
                entries.Add(Entry(service.IsDriver ? AutorunKind.Driver : AutorunKind.Service, service.DisplayName,
                    $@"HKLM\SYSTEM\CurrentControlSet\Services\{service.Name}", image, path,
                    service.StartMode != ServiceStartMode.Disabled) with { ServiceName = service.Name });
            }
        });

        // Vérification des signatures en parallèle (plusieurs centaines de fichiers) ; l'ordre est conservé.
        var results = new AutorunEntry[entries.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) };
        Parallel.For(0, entries.Count, options, i => results[i] = Complete(entries[i]));
        return new AutorunScanResult(results, failed);
    }

    private sealed record Pending(AutorunKind Kind, string Name, string Location, string Command, string? Path, bool Enabled)
    {
        public string? ServiceName { get; init; }

        public StartupToggleTarget? Startup { get; init; }
    }

    private static Pending Entry(AutorunKind kind, string name, string location, string command, string? path, bool enabled) =>
        new(kind, name, location, command, path, enabled);

    private static void Read(string source, List<string> failed, Action read)
    {
        try
        {
            read();
        }
#pragma warning disable CA1031 // Une source en panne est signalée, jamais fatale.
        catch (Exception)
#pragma warning restore CA1031
        {
            failed.Add(source);
        }
    }

    private AutorunEntry Complete(Pending p)
    {
        var (kind, name, location, command, path, enabled) = (p.Kind, p.Name, p.Location, p.Command, p.Path, p.Enabled);
        SignatureInfo signature;
        try
        {
            signature = path is null ? SignatureInfo.NotApplicable : signatures.Verify(path);
        }
#pragma warning disable CA1031 // Un fichier illisible ne doit pas interrompre la vérification des autres.
        catch (Exception)
#pragma warning restore CA1031
        {
            signature = new SignatureInfo(SignatureStatus.Error, null, false);
        }

        var isMicrosoft = signature.Status == SignatureStatus.Signed
                          && signature.Publisher?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) == true;
        return new AutorunEntry(kind, name, location, command, path, enabled, signature, isMicrosoft)
        {
            ServiceName = p.ServiceName,
            Startup = p.Startup,
        };
    }
}
