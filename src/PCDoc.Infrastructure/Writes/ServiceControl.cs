using System.ComponentModel;
using System.Runtime.InteropServices;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>Type de démarrage des services via l'API officielle (ChangeServiceConfig / ChangeServiceConfig2).</summary>
public sealed partial class ServiceControl(IRegistryReader registry) : IServiceControl
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ServiceConfigDelayedAutoStartInfo = 3;

    public ServiceStartMode GetStartMode(string serviceName)
    {
        var key = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
        var start = registry.GetValue(RegistryHive.LocalMachine, key, "Start") as int?;
        var delayed = registry.GetValue(RegistryHive.LocalMachine, key, "DelayedAutostart") is int d && d == 1;
        return start switch
        {
            0 => ServiceStartMode.Boot,
            1 => ServiceStartMode.System,
            2 => delayed ? ServiceStartMode.AutomaticDelayed : ServiceStartMode.Automatic,
            3 => ServiceStartMode.Manual,
            4 => ServiceStartMode.Disabled,
            _ => throw new InvalidOperationException($"Service {serviceName} not found"),
        };
    }

    public void SetStartMode(string serviceName, ServiceStartMode mode)
    {
        var startType = mode switch
        {
            ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed => 2u,
            ServiceStartMode.Manual => 3u,
            ServiceStartMode.Disabled => 4u,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), "Boot/System start types are never changed by PCDoc"),
        };

        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            var service = OpenServiceW(manager, serviceName, ServiceChangeConfig | ServiceQueryConfig);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                if (!ChangeServiceConfigW(service, ServiceNoChange, startType, ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                if (startType == 2)
                {
                    var delayed = mode == ServiceStartMode.AutomaticDelayed ? 1 : 0;
                    if (!ChangeServiceConfig2W(service, ServiceConfigDelayedAutoStartInfo, ref delayed))
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    }
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr OpenServiceW(IntPtr manager, string name, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfigW(IntPtr service, uint serviceType, uint startType, uint errorControl,
        string? binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? startName, string? password, string? displayName);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig2W(IntPtr service, uint infoLevel, ref int info);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(IntPtr handle);
}
