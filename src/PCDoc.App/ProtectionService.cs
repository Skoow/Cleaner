using PCDoc.Infrastructure.Writes;
using PCDoc.Presentation;

namespace PCDoc.App;

/// <summary>Protection du système (points de restauration) via WMI.</summary>
public sealed class ProtectionService : IProtectionService
{
    public bool IsEnabled() => RestorePointService.IsProtectionEnabled();

    public bool Enable() => RestorePointService.EnableProtection();
}
