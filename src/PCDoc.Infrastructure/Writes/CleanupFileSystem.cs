using System.IO;
using System.Runtime.InteropServices;
using PCDoc.Core.Abstractions;

namespace PCDoc.Infrastructure.Writes;

/// <summary>
/// Parcours et suppression pour le nettoyage. Les liens (jonctions, liens symboliques, points de montage) ne sont jamais
/// suivis : un lien placé dans un dossier temporaire ne peut pas faire supprimer des fichiers ailleurs.
/// </summary>
public sealed class CleanupFileSystem : IFileSystem
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public bool DirectoryExists(string path) =>
        Directory.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

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
                    yield return new FileEntry(file.FullName, file.Length, file.LastWriteTimeUtc);
                }
                else if (recursive && item is DirectoryInfo sub)
                {
                    pending.Push(sub);
                }
            }
        }
    }

    public bool TryDeleteFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            if (info.IsReadOnly)
            {
                info.IsReadOnly = false;
            }

            info.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void DeleteEmptySubdirectories(string root)
    {
        foreach (var sub in ListDirectories(root))
        {
            DeleteEmptySubdirectories(sub);
            try
            {
                if (!Directory.EnumerateFileSystemEntries(sub).Any())
                {
                    Directory.Delete(sub, recursive: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dossier utilisé ou protégé : laissé en place.
            }
        }
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
