using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Autoruns;

/// <summary>Source des éléments lancés automatiquement (simulable dans les tests de la page Système).</summary>
public interface IAutorunSource
{
    IReadOnlyList<AutorunEntry> Collect();
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
    public IReadOnlyList<AutorunEntry> Collect()
    {
        var entries = new List<AutorunEntry>();

        foreach (var item in new StartupCollector(registry, files, profiles).Collect())
        {
            entries.Add(Entry(AutorunKind.Startup, item.Name, item.Location, item.CommandLine,
                CommandLineParser.ExtractExecutable(item.CommandLine, files), item.Enabled));
        }

        foreach (var task in tasks.ReadAll())
        {
            foreach (var command in task.ExecCommands)
            {
                var isCom = command.StartsWith("COM ", StringComparison.OrdinalIgnoreCase);
                entries.Add(Entry(AutorunKind.ScheduledTask, task.Path, task.Path, command,
                    isCom ? null : CommandLineParser.ExtractExecutable(command, files), task.Enabled));
            }
        }

        foreach (var service in services.ReadAll())
        {
            var image = service.ImagePath ?? string.Empty;
            var path = service.IsDriver
                ? CommandLineParser.NormalizeImagePath(image, files)
                : CommandLineParser.ExtractExecutable(image, files);
            entries.Add(Entry(service.IsDriver ? AutorunKind.Driver : AutorunKind.Service, service.DisplayName,
                $@"HKLM\SYSTEM\CurrentControlSet\Services\{service.Name}", image, path,
                service.StartMode != ServiceStartMode.Disabled));
        }

        return entries;
    }

    private AutorunEntry Entry(AutorunKind kind, string name, string location, string command, string? path, bool enabled)
    {
        var signature = path is null ? SignatureInfo.NotApplicable : signatures.Verify(path);
        var isMicrosoft = signature.Status == SignatureStatus.Signed
                          && signature.Publisher?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) == true;
        return new AutorunEntry(kind, name, location, command, path, enabled, signature, isMicrosoft);
    }
}
