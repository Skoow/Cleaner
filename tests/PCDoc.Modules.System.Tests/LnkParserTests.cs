using System.Text;
using PCDoc.Modules.System.Autoruns;

namespace PCDoc.Modules.System.Tests;

public class LnkParserTests
{
    /// <summary>Raccourci minimal (MS-SHLLINK) : en-tête + LinkInfo avec LocalBasePath + arguments Unicode.</summary>
    internal static byte[] BuildLnk(string target, string? arguments = null)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        const uint HasLinkInfo = 0x2, HasArguments = 0x20, IsUnicode = 0x80;
        w.Write(0x4Cu);
        w.Write(new Guid("00021401-0000-0000-C000-000000000046").ToByteArray());
        w.Write(HasLinkInfo | IsUnicode | (arguments is null ? 0 : HasArguments));
        w.Write(new byte[0x4C - 0x18]); // reste de l'en-tête
        var path = Encoding.Default.GetBytes(target + "\0");
        const int HeaderSize = 0x1C;
        var size = HeaderSize + path.Length + 1;
        w.Write((uint)size);
        w.Write((uint)HeaderSize);
        w.Write(1u);              // VolumeIDAndLocalBasePath
        w.Write(0u);              // VolumeIDOffset (non utilisé ici)
        w.Write((uint)HeaderSize); // LocalBasePathOffset
        w.Write(0u);              // CommonNetworkRelativeLinkOffset
        w.Write((uint)(HeaderSize + path.Length)); // CommonPathSuffixOffset → chaîne vide
        w.Write(path);
        w.Write((byte)0);
        if (arguments is not null)
        {
            w.Write((ushort)arguments.Length);
            w.Write(Encoding.Unicode.GetBytes(arguments));
        }

        return ms.ToArray();
    }

    [Fact]
    public void TryRead_LocalBasePathAndArguments()
    {
        var bytes = BuildLnk(@"C:\Users\Lea\AppData\Roaming\Spotify\Spotify.exe", "--minimized");

        LnkParser.TryRead(bytes).Should().Be((@"C:\Users\Lea\AppData\Roaming\Spotify\Spotify.exe", "--minimized"));
    }

    [Fact]
    public void TryRead_Garbage_ReturnsNull()
    {
        LnkParser.TryRead([1, 2, 3]).Should().BeNull();
        LnkParser.TryRead(new byte[200]).Should().BeNull();
    }

    [Fact]
    public void TryRead_UnicodeHeaderButShortLinkInfo_ReturnsNullInsteadOfThrowing()
    {
        var bytes = BuildLnk(@"C:\a.exe");
        // LinkInfoHeaderSize ≥ 0x24 (champs Unicode annoncés) mais LinkInfoSize = 0x1C : bloc trop court.
        BitConverter.GetBytes(0x1Cu).CopyTo(bytes, 0x4C);
        BitConverter.GetBytes(0x24u).CopyTo(bytes, 0x50);

        var act = () => LnkParser.TryRead(bytes);

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void TryRead_RandomCorruption_NeverThrows()
    {
        var original = BuildLnk(@"C:\Users\Lea\app.exe", "--x");
        var random = new Random(42);
        for (var i = 0; i < 2000; i++)
        {
            var copy = (byte[])original.Clone();
            copy[0x4C + random.Next(copy.Length - 0x4C)] = (byte)random.Next(256);
            copy[0x4C + random.Next(copy.Length - 0x4C)] = (byte)random.Next(256);

            var act = () => LnkParser.TryRead(copy);

            act.Should().NotThrow();
        }
    }

    [Fact]
    public void TryRead_TruncatedLinkInfo_ReturnsNull()
    {
        var bytes = BuildLnk(@"C:\a.exe");

        LnkParser.TryRead(bytes[..0x50]).Should().BeNull();
    }
}
