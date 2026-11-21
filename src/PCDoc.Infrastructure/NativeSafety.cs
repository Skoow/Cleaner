using System.Runtime.InteropServices;

namespace PCDoc.Infrastructure;

/// <summary>Protections du processus appliquées au démarrage (spec v2 §10).</summary>
public static partial class NativeSafety
{
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    /// <summary>
    /// Les DLL chargées dynamiquement ne sont plus cherchées dans le dossier courant ni dans le PATH
    /// (anti-détournement de DLL) : seulement dossier de l'application, System32 et dossiers ajoutés explicitement.
    /// System32 seul casserait le chargement des DLL natives de WPF extraites par la version « fichier unique ».
    /// </summary>
    public static bool RestrictDllSearch() => SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDefaultDllDirectories(uint directoryFlags);
}
