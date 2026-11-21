using System.Runtime.InteropServices;

namespace PCDoc.Infrastructure;

/// <summary>Protections du processus appliquées au démarrage (spec v2 §10).</summary>
public static partial class NativeSafety
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    /// <summary>Les DLL chargées dynamiquement ne sont plus cherchées que dans System32 (anti-détournement de DLL).</summary>
    public static bool RestrictDllSearchToSystem32() => SetDefaultDllDirectories(LoadLibrarySearchSystem32);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDefaultDllDirectories(uint directoryFlags);
}
