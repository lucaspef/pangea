using System.Net;
using System.Runtime.InteropServices;
using Pangya.Core.Crypto;
using Pangya.Core.Limits;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

public class CipherTests
{
    static IEnumerable<string[]> Vectors() =>
        File.ReadLines(Path.Combine(TestEnv.Root, "tests/Pangya.Tests/Data/crypto_vectors.txt")).Select(l => l.Split(' '));

    static byte[] Hex(string s) => s == "-" ? [] : Convert.FromHexString(s);

    [Fact]
    public void MatchesClientCodeVectors()
    {
        int n = 0;
        foreach (var v in Vectors())
        {
            int k = int.Parse(v[1]);
            byte seed = byte.Parse(v[2]);
            var body = Hex(v[3]);
            var wire = Hex(v[4]);
            if (v[0] == "c2s")
            {
                // o servidor decifra o que o código do cliente cifrou...
                var payload = wire.AsSpan(4).ToArray();
                Assert.True(PacketCipher.OpenClient(payload, k, seed));
                Assert.Equal(body, payload[1..]);
                // ...e o nosso "cliente de teste" produz exatamente os mesmos bytes
                Assert.Equal(wire, PacketCipher.SealClient(body, k, seed));
            }
            else
            {
                var buf = new byte[body.Length + PacketCipher.ServerOverhead];
                body.CopyTo(buf, PacketCipher.ServerOverhead);
                int len = PacketCipher.SealServer(buf, body.Length, k, seed);
                Assert.Equal(wire, buf[..len]);
            }
            n++;
        }
        Assert.Equal(400, n);
    }

    [Fact]
    public void TamperedPacketIsRejected()
    {
        var payload = PacketCipher.SealClient([0x02, 0x00, 1, 2, 3, 4, 5], 7, 99).AsSpan(4).ToArray();
        payload[0] ^= 1;                       // corrompe o byte de conferência
        Assert.False(PacketCipher.OpenClient(payload, 7, 99));
        Assert.False(PacketCipher.OpenClient(PacketCipher.SealClient([1, 0], 7, 99).AsSpan(4).ToArray(), 8, 99));
    }
}

public class PacketTests
{
    static PacketReader Read(PacketWriter w)
    {
        var body = w.Body.ToArray();
        var r = new PacketReader();
        r.Reset(body, 0, body.Length);
        return r;
    }

    [Fact]
    public void WriterAndReaderRoundTrip()
    {
        using var w = new PacketWriter(0x1234).U8(7).U16(65000).U32(0xDEADBEEF).I32(-5).U64(ulong.MaxValue).F32(1.5f).Str("한글abc").Fixed("x", 4);
        var r = Read(w);
        Assert.Equal(0x1234, r.Id);
        Assert.Equal(7, r.U8());
        Assert.Equal(65000, r.U16());
        Assert.Equal(0xDEADBEEF, r.U32());
        Assert.Equal(-5, r.I32());
        Assert.Equal(ulong.MaxValue, r.U64());
        Assert.Equal(1.5f, r.F32());
        Assert.Equal("한글abc", r.Str());
        Assert.Equal([(byte)'x', 0, 0, 0], r.Bytes(4).ToArray());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void WriterGrowsBeyondInitialCapacity()
    {
        using var w = new PacketWriter(1, 4).Zeros(10_000).U8(9);
        Assert.Equal(10_003, w.BodyLength);
        Assert.Equal(9, w.Body[^1]);
    }

    [Fact]
    public void ShortPacketThrows() => Assert.Throws<PacketException>(() => Read(new PacketWriter(1).U8(1)).U32());

    [Fact]
    public void OversizedStringThrows() => Assert.Throws<PacketException>(() => Read(new PacketWriter(1).Str(new string('a', 100))).Str(64));

    [Fact]
    public void StructsAreCopiedByteForByte()
    {
        var e = new sUserEquip { guidChar = 0x11223344, tidBall = 0x14000000 };
        e.tidItemSlot[9] = 7;
        var r = Read(new PacketWriter(0x70).Struct(e));
        var back = r.Struct<sUserEquip>();
        Assert.Equal(0x11223344u, back.guidChar);
        Assert.Equal(7u, back.tidItemSlot[9]);
    }

    [Fact]
    public void BitfieldsLandOnClientOffsets()
    {
        var u = new sUserInfo();
        u.info.DoTutorial = 1;                 // emulador Python: b[0x073] = 1
        u.info.RookieFEvent = 3;
        var bytes = MemoryMarshal.AsBytes(new Span<sUserInfo>(ref u));
        Assert.Equal(0xC1, bytes[0x73]);
        Assert.Equal(1u, u.info.DoTutorial);
    }
}

public class TextTests
{
    [Fact]
    public void FixedStringIsTerminatedAndTruncated()
    {
        var f = new byte[6];
        Cp949.Write(f, "abcdefgh");
        Assert.Equal("abcde", Cp949.Read(f));
        Assert.Equal(0, f[5]);
    }

    [Fact]
    public void KoreanCharIsNotSplit()
    {
        var f = new byte[4];
        Cp949.Write(f, "a한글");                // a(1) + 한(2) + 글(2): cabe "a한" (3 bytes) + zero
        Assert.Equal("a한", Cp949.Read(f));
        var g = new byte[3];
        Cp949.Write(g, "a한");                  // só cabem 2 bytes: não pode sobrar meio "한"
        Assert.Equal("a", Cp949.Read(g));
    }
}

public class LimitTests
{
    [Fact]
    public void TokenBucketLimitsBurst()
    {
        var b = new TokenBucket(5);
        Assert.Equal(5, Enumerable.Range(0, 20).Count(_ => b.TryTake()));
    }

    [Fact]
    public void IpLimiterCountsAndReleases()
    {
        var l = new IpConnectionLimiter(2);
        var ip = IPAddress.Loopback;
        Assert.True(l.TryAcquire(ip));
        Assert.True(l.TryAcquire(ip));
        Assert.False(l.TryAcquire(ip));
        l.Release(ip);
        Assert.True(l.TryAcquire(ip));
        l.Release(ip);
        l.Release(ip);
        Assert.Equal(0, l.Count(ip));
    }

    [Fact]
    public void AttemptLimiterBlocksWithinWindow()
    {
        var l = new AttemptLimiter(3, TimeSpan.FromMinutes(1));
        Assert.True(l.TryAttempt("a") && l.TryAttempt("a") && l.TryAttempt("a"));
        Assert.False(l.TryAttempt("a"));
        Assert.True(l.TryAttempt("b"));
    }
}

public class IffTests
{
    [Fact]
    public void LoadsEveryKnownTableWithExactRecordSize()
    {
        var iff = Kr645Iff.Load(Path.Combine(TestEnv.Root, "data/pangya.iff"));
        Assert.Equal(10, iff.Characters.Length);
        Assert.Equal(0x04000000u, iff.Characters[0].c.TypeId);
        Assert.Contains(iff.Balls, b => b.c.TypeId == 0x14000000);
        Assert.Contains(iff.ClubSets, c => c.c.TypeId == 0x10000000);
        Assert.True(iff.Items.Length > 0 && iff.Parts.Length > 0);
        Assert.All(iff.Characters, c => Assert.NotEmpty(Cp949.Read(c.c.Name)));
    }

    [Fact]
    public void WrongRecordTypeIsRejected()
    {
        var a = Pangya.Core.Iff.IffArchive.Load(Path.Combine(TestEnv.Root, "data/pangya.iff"));
        Assert.Throws<InvalidDataException>(() => a.Table<Iff.sItem>("Item.iff"));   // header 645 (200 bytes) != dados 642 (196)
    }
}
