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
    /// Effacement sûr d'un fichier : ouvert sans suivre de lien final, sans partage en lecture ni en écriture, refusé s'il est un lien, a
    /// plusieurs liens physiques, est compressé, chiffré, fragmenté (sparse), seulement dans le cloud ou sur un système
    /// de fichiers à copie sur écriture (ReFS). Le vrai chemin, lu sur le handle (dossiers liés, noms courts résolus),
    /// doit être accepté par <paramref name="realPathAllowed"/>. Les flux de données secondaires puis le contenu sont
    /// écrasés par des données aléatoires (écriture directe sur le disque), puis le fichier est supprimé par le même handle.
    /// </summary>
    internal static bool WipeByHandle(string path, Func<string, bool> realPathAllowed, CancellationToken cancellationToken = default) =>
        WipeByHandle(path, realPathAllowed, out _, cancellationToken);

    internal static bool WipeByHandle(string path, Func<string, bool> realPathAllowed, out string? reason, CancellationToken cancellationToken = default)
    {
        reason = null;
        if (path.IndexOf(':', 2) >= 0)
        {
            reason = "stream syntax";
            return false; // syntaxe de flux (« fichier:flux », « dossier::$INDEX_ALLOCATION ») refusée
        }

        ClearReadOnly(path, realPathAllowed); // un fichier en lecture seule ne s'ouvre pas en écriture
        using var handle = CreateFileW(@"\\?\" + Path.GetFullPath(path), GenericWrite | Delete | FileReadAttributes, FileShareDelete, IntPtr.Zero, OpenExisting,
            FlagOpenReparsePoint | FlagWriteThrough, IntPtr.Zero); // partage « suppression » seul : requis par NTFS pour ouvrir les flux secondaires
        if (handle.IsInvalid)
        {
            reason = $"open failed ({Marshal.GetLastPInvokeError()})";
            return false;
        }

        const uint unsafeAttributes = (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Compressed
            | FileAttributes.Encrypted | FileAttributes.SparseFile | FileAttributes.Offline) | RecallOnOpen | RecallOnDataAccess;
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & unsafeAttributes) != 0 || info.NumberOfLinks != 1)
        {
            reason = $"attributes 0x{info.Attributes:X}, links {info.NumberOfLinks}";
            return false;
        }

        if (FinalPath(handle) is not { } real || !realPathAllowed(real))
        {
            reason = "real path refused";
            return false;
        }

        if (!OverwritesInPlace(handle))
        {
            reason = "file system does not overwrite in place";
            return false;
        }

        if (!WipeStreams(real, cancellationToken))
        {
            reason = $"alternate stream ({Marshal.GetLastPInvokeError()})";
            return false;
        }

        var length = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
        using (var stream = new FileStream(handle, FileAccess.Write, 1, isAsync: false))
        {
            Overwrite(stream, length, cancellationToken);
            stream.SetLength(0);
            stream.Flush(flushToDisk: true);
            var ex = new FileDispositionInfoEx { Flags = DispositionDelete | DispositionPosixSemantics | DispositionIgnoreReadonly };
            if (SetFileInformationByHandle(handle, FileDispositionInfoExClass, ref ex, (uint)Marshal.SizeOf<FileDispositionInfoEx>()))
            {
                return true;
            }

            var legacy = new FileDispositionInfo { DeleteFile = 1 };
            if (SetFileInformationByHandle(handle, FileDispositionInfoClass, ref legacy, (uint)Marshal.SizeOf<FileDispositionInfo>()))
            {
                return true;
            }

            reason = $"delete failed ({Marshal.GetLastPInvokeError()})";
            return false;
        }
    }

    /// <summary>Retire l'attribut « lecture seule » par un handle ouvert sans suivre de lien (rien n'est fait sur un lien).</summary>
    private static void ClearReadOnly(string path, Func<string, bool> realPathAllowed)
    {
        using var handle = CreateFileW(@"\\?\" + Path.GetFullPath(path), FileReadAttributes | FileWriteAttributes, FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero, OpenExisting, FlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)
            || (info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || (info.Attributes & (uint)FileAttributes.ReadOnly) == 0
            || FinalPath(handle) is not { } real || !realPathAllowed(real))
        {
            return; // rien n'est modifié sur un fichier refusé
        }

        var basic = new FileBasicInfo { FileAttributes = info.Attributes & ~(uint)FileAttributes.ReadOnly };
        SetFileInformationByHandle(handle, FileBasicInfoClass, ref basic, (uint)Marshal.SizeOf<FileBasicInfo>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime; // 0 = inchangé
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    private const uint FileWriteAttributes = 0x0100;
    private const int FileBasicInfoClass = 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, int infoClass, ref FileBasicInfo info, uint size);

    private static void Overwrite(FileStream stream, long length, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 << 20];
        for (long written = 0; written < length; written += buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            System.Security.Cryptography.RandomNumberGenerator.Fill(buffer);
            stream.Write(buffer, 0, (int)Math.Min(buffer.Length, length - written));
        }

        stream.Flush(flushToDisk: true);
    }

    /// <summary>NTFS et FAT écrasent les données en place ; ReFS (et Dev Drive) écrit ailleurs : effacement non garanti.</summary>
    private static unsafe bool OverwritesInPlace(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var name = stackalloc char[64];
        if (!GetVolumeInformationByHandleW(handle, null, 0, out _, out _, out _, name, 64))
        {
            return false;
        }

        var fs = new string(name);
        return fs is "NTFS" or "FAT32" or "FAT" or "exFAT";
    }

    /// <summary>Écrase les flux secondaires (ex. « Zone.Identifier ») : la suppression seule les laisserait lisibles sur le disque.</summary>
    private static unsafe bool WipeStreams(string realPath, CancellationToken cancellationToken)
    {
        var streams = new List<(string Name, long Size)>();
        var find = FindFirstStreamW(@"\\?\" + realPath, 0, out var data, 0);
        if (find == new IntPtr(-1))
        {
            return Marshal.GetLastPInvokeError() == 38; // ERROR_HANDLE_EOF : aucun flux (fichier vide)
        }

        try
        {
            do
            {
                var name = new string(data.StreamName);
                if (!name.Equals("::$DATA", StringComparison.OrdinalIgnoreCase))
                {
                    streams.Add((name, data.StreamSize));
                }
            }
            while (FindNextStreamW(find, out data));
        }
        finally
        {
            FindClose(find);
        }

        foreach (var (name, size) in streams)
        {
            using var stream = CreateFileW(@"\\?\" + realPath + name, GenericWrite, FileShareDelete, IntPtr.Zero, OpenExisting, FlagWriteThrough, IntPtr.Zero);
            if (stream.IsInvalid)
            {
                return false;
            }

            using var fs = new FileStream(stream, FileAccess.Write, 1, isAsync: false);
            Overwrite(fs, size, cancellationToken);
            fs.SetLength(0);
        }

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct FindStreamData
    {
        public long StreamSize;
        public fixed char StreamName[296];
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindFirstStreamW(string fileName, int infoLevel, out FindStreamData data, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNextStreamW(IntPtr find, out FindStreamData data);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(IntPtr find);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetVolumeInformationByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle file, char* volumeName, uint volumeNameSize,
        out uint serial, out uint maxComponent, out uint flags, char* fileSystemName, uint fileSystemNameSize);

    private const uint RecallOnOpen = 0x40000;
    private const uint RecallOnDataAccess = 0x400000;

    internal static string? RealPath(string path, bool directory) => FinalPathOf(path, directory);

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

/// <summary>
/// Effacement définitif de fichiers choisis par l'utilisateur. Refusés : Windows, les programmes, ProgramData et ses
/// dossiers Microsoft, la racine de chaque profil et du dossier Users, les dossiers système à la racine d'un disque,
/// les dossiers de PCDoc et son propre dossier. La vérification porte sur le chemin saisi ET sur le vrai chemin lu sur
/// le handle (liens, jonctions comme « All Users » ou « Documents and Settings », noms courts « PROGRA~1 »).
/// </summary>
public sealed class SecureEraser(IUserProfileResolver profiles) : ISecureEraser
{
    private static readonly string[] RootSystemFolders = ["$Recycle.Bin", "System Volume Information", "Recovery", "Boot", "EFI", "Config.Msi"];

    public bool IsAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf(':', 2) >= 0 || TrustedPaths.Normalize(path) is not { } normalized)
        {
            return false;
        }

        var full = TrustedPaths.LongPath(normalized).TrimEnd('\\');
        var directory = Directory.Exists(full);
        if (!directory && !File.Exists(full))
        {
            return false;
        }

        return !Denied(full) && !File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)
               && CleanupFileSystem.RealPath(full, directory) is { } real && !Denied(real.TrimEnd('\\'));
    }

    /// <summary>
    /// Fichiers à effacer. Un lien, un dossier illisible ou un élément refusé y figure tel quel : son effacement échoue
    /// et il est compté comme non effacé (jamais ignoré en silence).
    /// </summary>
    public IReadOnlyList<string> Expand(string path)
    {
        if (!IsAllowed(path))
        {
            return [];
        }

        if (File.Exists(path))
        {
            return [path];
        }

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(path);
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 };
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
                {
                    if (entry is DirectoryInfo && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        pending.Push(entry.FullName);
                    }
                    else
                    {
                        files.Add(entry.FullName); // fichier, ou lien : refusé ensuite par Wipe
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                files.Add(dir); // dossier illisible : compté comme non effacé
            }
        }

        return files;
    }

    /// <summary>Raison du dernier refus d'effacement (pour le journal et le diagnostic).</summary>
    public string? LastError { get; private set; }

    public bool Wipe(string file)
    {
        if (!IsAllowed(file) || !File.Exists(file))
        {
            LastError = "path refused";
            return false;
        }

        var ok = CleanupFileSystem.WipeByHandle(file, real => !Denied(real.TrimEnd('\\')), out var reason);
        LastError = ok ? null : reason;
        return ok;
    }

    public void RemoveCipherLeftovers(string drive)
    {
        if (drive is not { Length: 3 } || !char.IsAsciiLetter(drive[0]) || drive[1] != ':' || drive[2] != '\\')
        {
            return;
        }

        var leftover = Path.Combine(drive, "EFSTMPWP");
        try
        {
            if (Directory.Exists(leftover) && !File.GetAttributes(leftover).HasFlag(FileAttributes.ReparsePoint))
            {
                CleanupFileSystem.DeleteTree(leftover, drive);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Encore utilisé : Windows le supprimera au prochain cipher, ou à la main.
        }
    }

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

    private bool Denied(string full)
    {
        if (full.Length <= 3 || full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).TrimEnd('\\');
        var app = AppContext.BaseDirectory.TrimEnd('\\');
        string[] trees = [TrustedPaths.Windows, TrustedPaths.ProgramFiles, TrustedPaths.ProgramFilesX86, SecureFolders.Root, programData + @"\Microsoft", app];
        if (trees.Any(f => f.Length > 0 && (TrustedPaths.IsUnder(full, f) || full.Equals(f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        var drive = Path.GetPathRoot(full) ?? string.Empty;
        if (RootSystemFolders.Any(f => TrustedPaths.IsUnder(full + @"\", Path.Combine(drive, f))))
        {
            return true;
        }

        var users = Path.GetDirectoryName(profiles.Resolve().ProfilePath.TrimEnd('\\'))?.TrimEnd('\\') ?? string.Empty;
        string[] exact = [programData, users, .. ProfileRoots()];
        return exact.Any(e => e.Length > 0 && full.Equals(e, StringComparison.OrdinalIgnoreCase))
               || (users.Length > 0 && string.Equals(Path.GetDirectoryName(full), users, StringComparison.OrdinalIgnoreCase)) // racine d'un profil
               || (users.Length > 0 && TrustedPaths.IsUnder(full, Path.Combine(users, "Default")));
    }

    /// <summary>Racines de tous les profils déclarés (y compris hors de C:\Users).</summary>
    private static IEnumerable<string> ProfileRoots()
    {
        using var list = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        foreach (var sid in list?.GetSubKeyNames() ?? [])
        {
            using var key = list!.OpenSubKey(sid);
            if (key?.GetValue("ProfileImagePath") is string p && p.Length > 3)
            {
                yield return Environment.ExpandEnvironmentVariables(p).TrimEnd('\\');
            }
        }
    }
}
