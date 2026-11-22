using System.IO;
using System.Runtime.InteropServices;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Parcours et suppression pour le nettoyage. Les liens ne sont jamais suivis lors du parcours, et chaque suppression se
/// fait **par handle** : le fichier est ouvert sans suivre de lien, son chemin réel est demandé au système
/// (GetFinalPathNameByHandle) et comparé à la racine autorisée, il doit n'avoir qu'un lien physique, puis il est supprimé
/// par ce même handle. Échanger un dossier contre un lien pendant l'analyse ne peut donc rien faire supprimer ailleurs.
/// </summary>
public sealed partial class CleanupFileSystem : IFileSystem
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Vrai seulement si aucun dossier du chemin (y compris les parents) n'est un lien.</summary>
    public bool DirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        for (var dir = new DirectoryInfo(path); dir is not null && dir.Parent is not null; dir = dir.Parent)
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<string> ListDirectories(string path) =>
        DirectoryExists(path) ? [.. new DirectoryInfo(path).EnumerateDirectories("*", Options).Select(d => d.FullName)] : [];

    public IEnumerable<FileEntry> EnumerateFiles(string root, bool recursive)
    {
        if (!DirectoryExists(root))
        {
            yield break;
        }

        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> items;
            try
            {
                items = [.. dir.EnumerateFileSystemInfos("*", Options)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var item in items)
            {
                if (item is FileInfo file)
                {
                    yield return new FileEntry(file.FullName, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
                }
                else if (recursive && item is DirectoryInfo sub)
                {
                    pending.Push(sub);
                }
            }
        }
    }

    public bool TryDeleteFile(string path, string allowedRoot) => DeleteByHandle(path, allowedRoot, directory: false);

    public void DeleteEmptySubdirectories(string root)
    {
        foreach (var sub in ListDirectories(root))
        {
            DeleteEmptySubdirectories(sub);
            DeleteByHandle(sub, root, directory: true); // échoue sans rien faire si le dossier n'est pas vide
        }
    }

    /// <summary>
    /// Supprime une arborescence sans jamais suivre de lien : les liens sont supprimés eux-mêmes, et chaque élément est
    /// supprimé par un handle dont le chemin réel est vérifié sous <paramref name="allowedRoot"/>.
    /// </summary>
    internal static void DeleteTree(string directory, string allowedRoot)
    {
        IEnumerable<FileSystemInfo> items;
        try
        {
            items = [.. new DirectoryInfo(directory).EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false,
            })];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var item in items)
        {
            var isDirectory = item.Attributes.HasFlag(FileAttributes.Directory);
            if (item.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                DeleteByHandle(item.FullName, allowedRoot, isDirectory, allowLink: true);
            }
            else if (isDirectory)
            {
                DeleteTree(item.FullName, allowedRoot);
            }
            else
            {
                DeleteByHandle(item.FullName, allowedRoot, directory: false, allowLink: false, allowHardLinks: true);
            }
        }

        DeleteByHandle(directory, allowedRoot, directory: true);
    }

    private static bool DeleteByHandle(string path, string allowedRoot, bool directory, bool allowLink = false, bool allowHardLinks = false)
    {
        var canonicalRoot = FinalPathOf(allowedRoot, directory: true);
        if (canonicalRoot is null)
        {
            return false;
        }

        using var handle = CreateFileW(@"\\?\" + Path.GetFullPath(path), Delete | FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero, OpenExisting,
            FlagOpenReparsePoint | (directory ? FlagBackupSemantics : 0), IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        var isLink = GetFileInformationByHandle(handle, out var info) && (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0;
        if (info.Attributes == 0
            || (isLink && !allowLink)
            || (!isLink && ((info.Attributes & (uint)FileAttributes.Directory) != 0) != directory)
            || (!directory && !isLink && !allowHardLinks && info.NumberOfLinks != 1))
        {
            return false; // lien, mauvais type ou lien physique vers un autre fichier : refusé
        }

        var real = FinalPath(handle);
        if (real is null || !real.StartsWith(canonicalRoot.TrimEnd('\\') + @"\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Suppression par le handle vérifié (sémantique POSIX, attribut lecture seule ignoré sans le modifier).
        var ex = new FileDispositionInfoEx { Flags = DispositionDelete | DispositionPosixSemantics | DispositionIgnoreReadonly };
        if (SetFileInformationByHandle(handle, FileDispositionInfoExClass, ref ex, (uint)Marshal.SizeOf<FileDispositionInfoEx>()))
        {
            return true;
        }

        var legacy = new FileDispositionInfo { DeleteFile = 1 };
        return SetFileInformationByHandle(handle, FileDispositionInfoClass, ref legacy, (uint)Marshal.SizeOf<FileDispositionInfo>());
    }

    /// <summary>
    /// Effacement sûr d'un fichier : ouvert sans suivre de lien et sans partage, refusé s'il est un lien ou a plusieurs
    /// liens physiques, contenu écrasé par des données aléatoires (écriture directe sur le disque), puis supprimé par le
    /// même handle.
    /// </summary>
    internal static bool WipeByHandle(string path)
    {
        using var handle = CreateFileW(@"\\?\" + Path.GetFullPath(path), GenericWrite | Delete | FileReadAttributes, 0, IntPtr.Zero, OpenExisting,
            FlagOpenReparsePoint | FlagWriteThrough, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        if (!GetFileInformationByHandle(handle, out var info)
            || (info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || info.NumberOfLinks != 1)
        {
            return false;
        }

        var length = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
        using (var stream = new FileStream(handle, FileAccess.Write, 1, isAsync: false))
        {
            var buffer = new byte[1 << 20];
            for (long written = 0; written < length; written += buffer.Length)
            {
                System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);
                stream.Write(buffer, 0, (int)Math.Min(buffer.Length, length - written));
            }

            stream.Flush(flushToDisk: true);
            stream.SetLength(0);
            stream.Flush(flushToDisk: true);
            var ex = new FileDispositionInfoEx { Flags = DispositionDelete | DispositionPosixSemantics | DispositionIgnoreReadonly };
            if (SetFileInformationByHandle(handle, FileDispositionInfoExClass, ref ex, (uint)Marshal.SizeOf<FileDispositionInfoEx>()))
            {
                return true;
            }

            var legacy = new FileDispositionInfo { DeleteFile = 1 };
            return SetFileInformationByHandle(handle, FileDispositionInfoClass, ref legacy, (uint)Marshal.SizeOf<FileDispositionInfo>());
        }
    }

    private const uint GenericWrite = 0x40000000;
    private const uint FlagWriteThrough = 0x80000000;

    private static string? FinalPathOf(string path, bool directory)
    {
        using var handle = CreateFileW(@"\\?\" + Path.GetFullPath(path), FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero, OpenExisting,
            FlagOpenReparsePoint | (directory ? FlagBackupSemantics : 0), IntPtr.Zero);
        return handle.IsInvalid ? null : FinalPath(handle);
    }

    private static unsafe string? FinalPath(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var buffer = new char[1024];
        uint length;
        fixed (char* p = buffer)
        {
            length = GetFinalPathNameByHandleW(handle, p, (uint)buffer.Length, 0);
        }

        if (length == 0 || length >= buffer.Length)
        {
            return null;
        }

        var text = new string(buffer, 0, (int)length);
        return text.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? null
            : text.StartsWith(@"\\?\", StringComparison.Ordinal) ? text[4..] : text;
    }

    private const uint Delete = 0x00010000;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint FileShareDelete = 4;
    private const uint OpenExisting = 3;
    private const uint FlagOpenReparsePoint = 0x00200000;
    private const uint FlagBackupSemantics = 0x02000000;
    private const int FileDispositionInfoClass = 4;
    private const int FileDispositionInfoExClass = 21;
    private const uint DispositionDelete = 0x1;
    private const uint DispositionPosixSemantics = 0x2;
    private const uint DispositionIgnoreReadonly = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        public byte DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfoEx
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out ByHandleFileInformation info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle handle, char* buffer, uint size, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int infoClass,
        ref FileDispositionInfoEx info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int infoClass,
        ref FileDispositionInfo info, uint size);
}

/// <summary>Dossiers système via les dossiers connus de Windows (SHGetKnownFolderPath), pas les variables d'environnement.</summary>
public sealed class KnownFolders : IKnownFolders
{
    public string Windows { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Windows, Environment.SpecialFolderOption.DoNotVerify);

    public string ProgramData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify);
}

/// <summary>Une mise à jour est en cours si Windows Update ou TrustedInstaller tourne, ou si un redémarrage est attendu.</summary>
public sealed class CleanupGuards(IWmiQuery wmi, IRegistryReader registry) : ICleanupGuards
{
    public bool IsRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName) || processName.IndexOfAny(['\\', '/', '.']) >= 0)
        {
            return true; // nom invalide : prudence
        }

        var running = System.Diagnostics.Process.GetProcessesByName(processName);
        foreach (var p in running)
        {
            p.Dispose();
        }

        return running.Length > 0;
    }

    public bool IsWindowsUpdateBusy()
    {
        try
        {
            var rows = wmi.Query(@"root\cimv2",
                "SELECT State FROM Win32_Service WHERE Name = 'wuauserv' OR Name = 'TrustedInstaller'");
            if (rows.Any(r => (r.GetValueOrDefault("State") as string) is { } state && !state.Equals("Stopped", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        catch (WmiUnavailableException)
        {
            return true; // état inconnu : prudence
        }

        return registry.GetValueNames(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired").Count > 0
               || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS", "pending.xml"));
    }
}

/// <summary>Corbeille via l'API du Shell, sans confirmation ni barre de progression (aucune fenêtre).</summary>
public sealed partial class RecycleBin : IRecycleBin
{
    private const uint NoConfirmation = 0x1;
    private const uint NoProgressUi = 0x2;
    private const uint NoSound = 0x4;

    public (long Bytes, long Items) Query()
    {
        var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
        var hr = SHQueryRecycleBinW(null, ref info);
        return hr == 0 ? (info.Bytes, info.Items) : throw new InvalidOperationException($"SHQueryRecycleBin failed 0x{hr:X8}");
    }

    public void Empty()
    {
        var hr = SHEmptyRecycleBinW(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        if (hr != 0 && hr != unchecked((int)0x8000FFFF)) // E_UNEXPECTED = corbeille déjà vide
        {
            throw new InvalidOperationException($"SHEmptyRecycleBin failed 0x{hr:X8}");
        }
    }

    // SHQUERYRBINFO : alignement naturel (8) en 64 bits ; PCDoc n'existe qu'en x64 et ARM64.
    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string? rootPath, ref ShQueryRbInfo info);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(IntPtr hwnd, string? rootPath, uint flags);
}

/// <summary>Effacement définitif de fichiers choisis par l'utilisateur, en refusant les dossiers de Windows et des programmes.</summary>
public sealed class SecureEraser(IUserProfileResolver profiles) : ISecureEraser
{
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public bool IsAllowed(string path)
    {
        if (TrustedPaths.Normalize(path) is not { } full || full.Length <= 3 || (!File.Exists(full) && !Directory.Exists(full)))
        {
            return false;
        }

        var profile = profiles.Resolve().ProfilePath.TrimEnd('\\');
        var users = Path.GetDirectoryName(profile)?.TrimEnd('\\') ?? string.Empty;
        string[] forbidden = [TrustedPaths.Windows, TrustedPaths.ProgramFiles, TrustedPaths.ProgramFilesX86, SecureFolders.Root,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\Microsoft"];
        return !forbidden.Any(f => f.Length > 0 && (TrustedPaths.IsUnder(full, f) || full.Equals(f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
               && !full.Equals(profile, StringComparison.OrdinalIgnoreCase) && !full.Equals(users, StringComparison.OrdinalIgnoreCase)
               && !File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint);
    }

    public IReadOnlyList<string> Expand(string path)
    {
        if (!IsAllowed(path))
        {
            return [];
        }

        return File.Exists(path) ? [path] : [.. new DirectoryInfo(path).EnumerateFiles("*", Recursive).Select(f => f.FullName)];
    }

    public bool Wipe(string file) => IsAllowed(file) && CleanupFileSystem.WipeByHandle(file);

    public void RemoveEmptyFolders(string folder)
    {
        if (!IsAllowed(folder) || !Directory.Exists(folder))
        {
            return;
        }

        foreach (var sub in new DirectoryInfo(folder).EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            RemoveEmptyFolders(sub.FullName);
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder, recursive: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier en cours d'utilisation : laissé.
        }
    }
}
