using System.Buffers.Binary;
using System.Text;

namespace PCDoc.Modules.System.Autoruns;

/// <summary>
/// Lecture minimale d'un raccourci Windows (.lnk, format MS-SHLLINK) : cible locale et arguments.
/// Le fichier est une donnée non fiable : toute incohérence donne <c>null</c>.
/// </summary>
public static class LnkParser
{
    private const int HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 0x1;
    private const uint HasLinkInfo = 0x2;
    private const uint HasName = 0x4;
    private const uint HasRelativePath = 0x8;
    private const uint HasWorkingDir = 0x10;
    private const uint HasArguments = 0x20;
    private const uint IsUnicode = 0x80;

    public static (string Target, string? Arguments)? TryRead(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != HeaderSize)
        {
            return null;
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[0x14..]);
        var offset = HeaderSize;
        if ((flags & HasLinkTargetIdList) != 0)
        {
            if (!Fits(data, offset, 2))
            {
                return null;
            }

            offset += 2 + BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        }

        if ((flags & HasLinkInfo) == 0 || !Fits(data, offset, 0x1C))
        {
            return null;
        }

        var info = data[offset..];
        var infoSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(info);
        var infoHeaderSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[4..]);
        var infoFlags = BinaryPrimitives.ReadUInt32LittleEndian(info[8..]);
        if (infoSize < 0x1C || !Fits(data, offset, infoSize) || (infoFlags & 1) == 0)
        {
            return null;
        }

        info = info[..infoSize];
        string? target;
        if (infoHeaderSize >= 0x24)
        {
            var unicodeOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[0x1C..]);
            var unicodeSuffix = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[0x20..]);
            target = UnicodeZ(info, unicodeOffset) + UnicodeZ(info, unicodeSuffix);
        }
        else
        {
            var baseOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[0x10..]);
            var suffixOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[0x18..]);
            target = AnsiZ(info, baseOffset) + AnsiZ(info, suffixOffset);
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        offset += infoSize;
        var unicode = (flags & IsUnicode) != 0;
        string? arguments = null;
        foreach (var flag in new[] { HasName, HasRelativePath, HasWorkingDir, HasArguments })
        {
            if ((flags & flag) == 0)
            {
                continue;
            }

            if (!Fits(data, offset, 2))
            {
                return (target, null);
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            var bytes = unicode ? count * 2 : count;
            if (!Fits(data, offset + 2, bytes))
            {
                return (target, null);
            }

            var span = data.Slice(offset + 2, bytes);
            if (flag == HasArguments)
            {
                arguments = unicode ? Encoding.Unicode.GetString(span) : Encoding.Latin1.GetString(span);
            }

            offset += 2 + bytes;
        }

        return (target, string.IsNullOrWhiteSpace(arguments) ? null : arguments);
    }

    private static bool Fits(ReadOnlySpan<byte> data, int offset, int length) =>
        offset >= 0 && length >= 0 && offset <= data.Length - length;

    private static string AnsiZ(ReadOnlySpan<byte> block, int offset)
    {
        if (offset <= 0 || offset >= block.Length)
        {
            return string.Empty;
        }

        var span = block[offset..];
        var end = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? span : span[..end]);
    }

    private static string UnicodeZ(ReadOnlySpan<byte> block, int offset)
    {
        if (offset <= 0 || offset >= block.Length - 1)
        {
            return string.Empty;
        }

        var span = block[offset..];
        for (var i = 0; i + 1 < span.Length; i += 2)
        {
            if (span[i] == 0 && span[i + 1] == 0)
            {
                return Encoding.Unicode.GetString(span[..i]);
            }
        }

        return string.Empty;
    }
}
