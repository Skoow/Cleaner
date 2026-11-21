using PCDoc.Core.Abstractions;

namespace PCDoc.Modules.System.Tests.Fakes;

public sealed class FakeSignatureVerifier : ISignatureVerifier
{
    private readonly Dictionary<string, SignatureInfo> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public FakeSignatureVerifier Signed(string path, string publisher)
    {
        _byPath[path] = new SignatureInfo(SignatureStatus.Signed, publisher, false);
        return this;
    }

    public FakeSignatureVerifier Unsigned(string path)
    {
        _byPath[path] = new SignatureInfo(SignatureStatus.Unsigned, null, false);
        return this;
    }

    public SignatureInfo Verify(string filePath) =>
        _byPath.TryGetValue(filePath, out var info) ? info : SignatureInfo.NotFound;
}

public sealed class FakeTaskReader(params ScheduledTaskInfo[] tasks) : IScheduledTaskReader
{
    public IReadOnlyList<ScheduledTaskInfo> ReadAll() => tasks;
}

public sealed class FakeServiceReader(params ServiceInfo[] services) : IServiceReader
{
    public IReadOnlyList<ServiceInfo> ReadAll() => services;
}

public sealed class FakeProfileResolver(string sid = "S-1-5-21-1000", string roaming = @"C:\Users\Lea\AppData\Roaming")
    : IUserProfileResolver
{
    public UserProfile Resolve() =>
        new(sid, @"PC\Lea", @"C:\Users\Lea", roaming, @"C:\Users\Lea\AppData\Local", IsProcessUser: false);
}
