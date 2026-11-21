using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.Apps.Tests;

internal sealed class FakeRunner : IProcessRunner
{
    public List<(string Tool, string Args)> Calls { get; } = [];

    public Func<string, string, (int Exit, string[] Lines)> Respond { get; set; } = (_, _) => (0, []);

    public Task<int> RunHiddenAsync(string system32Tool, string arguments, Action<string>? onOutput, CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        Calls.Add((system32Tool, arguments));
        var (exit, lines) = Respond(system32Tool, arguments);
        foreach (var line in lines)
        {
            onOutput?.Invoke(line);
        }

        return Task.FromResult(exit);
    }

    /// <summary>Script PowerShell décodé depuis -EncodedCommand.</summary>
    public static string Script(string args) =>
        global::System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(args[(args.LastIndexOf(' ') + 1)..]));
}

internal sealed class FakeLauncher : IUninstallLauncher
{
    public List<(string Exe, string Args, bool Hidden)> Calls { get; } = [];

    public Action? OnRun { get; set; }

    public Task<int> RunAsync(string executable, string arguments, bool hidden, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add((executable, arguments, hidden));
        OnRun?.Invoke();
        return Task.FromResult(0);
    }

    public bool AreArgumentsTrusted(string arguments) => !arguments.Contains(@"C:\ProgramData", StringComparison.OrdinalIgnoreCase);

    public bool IsTrusted(string executable) =>
        executable.StartsWith(@"C:\Program Files", StringComparison.OrdinalIgnoreCase) && executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}

internal sealed class FakeQuarantine : IQuarantine
{
    public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string PlanFor(string originalPath) => @"C:\ProgramData\PCDoc\quarantine\20261121-" + originalPath.Split('\\')[^1];

    public void Move(string originalPath, string quarantinePath)
    {
        Folders.Remove(originalPath).Should().BeTrue();
        Folders.Add(quarantinePath);
    }

    public void Restore(string quarantinePath, string originalPath)
    {
        Folders.Remove(quarantinePath).Should().BeTrue();
        Folders.Add(originalPath);
    }

    public bool DirectoryExists(string path) => Folders.Contains(path);

    public bool IsLeftoverCandidate(string path) =>
        path.StartsWith(@"C:\Program Files", StringComparison.OrdinalIgnoreCase) && path.Count(c => c == '\\') >= 2;

    public void PurgeOlderThan(TimeSpan age)
    {
    }
}

internal sealed class FakeProfiles(bool isProcessUser = true) : IUserProfileResolver
{
    public const string Sid = "S-1-5-21-1-2-3-1001";

    public UserProfile Resolve() => new(Sid, "Lea", @"C:\Users\Lea", @"C:\Users\Lea\AppData\Roaming", @"C:\Users\Lea\AppData\Local", isProcessUser);
}

internal sealed class FakeNames : IAppDisplayNames
{
    public string? Resolve(StorePackage package) => package.Name == "Microsoft.BingWeather" ? "Météo" : null;
}
