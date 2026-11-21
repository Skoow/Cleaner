namespace PCDoc.Modules.Diagnostic;

#pragma warning disable CA1000 // Fabriques statiques lisibles : Section<T>.Of(...).
/// <summary>Une partie du diagnostic : la valeur, ou « non disponible » avec la raison.</summary>
public sealed record Section<T>(T? Value, bool Available, string? Reason)
{
    public static Section<T> Of(T value) => new(value, true, null);

    public static Section<T> Unavailable(string reason) => new(default, false, reason);
}
#pragma warning restore CA1000

/// <summary>Famille d'un code d'arrêt (écran bleu), pour une explication simple.</summary>
public enum BugCheckCategory
{
    Unknown,
    Driver,
    Memory,
    Disk,
    Hardware,
    System,
}

public sealed record BlueScreen(DateTimeOffset Date, uint Code, string CodeName, BugCheckCategory Category, string Parameters, string? DumpFile);

public sealed record AppCrashGroup(string App, int Crashes, int Hangs, string? TopModule, DateTimeOffset Last);

public sealed record DeviceProblem(string Name, uint Code, string? DeviceId);

public enum DiskHealthStatus
{
    Healthy,
    Warning,
    Unhealthy,
    Unknown,
}

public sealed record DiskHealth(string Name, DiskHealthStatus Health, int? WearPercent, int? TemperatureC, long? UncorrectedReadErrors);

public sealed record OldDriver(string Device, string Provider, string? Version, DateOnly Date);

/// <summary>Résultat complet du diagnostic (lecture seule).</summary>
public sealed record DiagnosticReport(
    Section<IReadOnlyList<BlueScreen>> BlueScreens,
    Section<int> UnexpectedShutdowns,
    Section<IReadOnlyList<AppCrashGroup>> AppCrashes,
    Section<IReadOnlyList<DeviceProblem>> Devices,
    Section<IReadOnlyList<DiskHealth>> Disks,
    Section<bool> RebootPending,
    Section<IReadOnlyList<OldDriver>> OldDrivers);
