namespace PCDoc.Presentation.Settings;

/// <summary>Un composant tiers affiché dans « À propos → Composants ».</summary>
/// <param name="PackageId">Identifiant NuGet (null pour .NET) : un test vérifie que la version affichée
/// est celle de Directory.Packages.props.</param>
public sealed record ComponentInfo(string Name, string Version, string License, string Url, string? PackageId = null);

/// <summary>Composants intégrés à PCDoc v0.1, avec leur licence.</summary>
public static class ThirdPartyComponents
{
    public static IReadOnlyList<ComponentInfo> All { get; } =
    [
        new(".NET", "10.0", "MIT", "https://github.com/dotnet/runtime"),
        new("CommunityToolkit.Mvvm", "8.4.2", "MIT", "https://github.com/CommunityToolkit/dotnet", "CommunityToolkit.Mvvm"),
        new("Microsoft.Extensions.DependencyInjection", "10.0.15", "MIT", "https://github.com/dotnet/runtime", "Microsoft.Extensions.DependencyInjection"),
        new("Serilog", "4.4.0", "Apache-2.0", "https://github.com/serilog/serilog", "Serilog"),
        new("Serilog.Sinks.File", "7.0.0", "Apache-2.0", "https://github.com/serilog/serilog-sinks-file", "Serilog.Sinks.File"),
        new("System.Management", "10.0.15", "MIT", "https://github.com/dotnet/runtime", "System.Management"),
        new("System.Diagnostics.EventLog", "10.0.15", "MIT", "https://github.com/dotnet/runtime", "System.Diagnostics.EventLog"),
        new("TaskScheduler (Microsoft.Win32.TaskScheduler)", "2.12.2", "MIT", "https://github.com/dahall/TaskScheduler", "TaskScheduler"),
    ];
}
