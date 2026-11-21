namespace PCDoc.Core.Abstractions;

/// <summary>Horloge injectable (testable).</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

/// <summary>Horloge réelle.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
