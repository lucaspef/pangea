using System.IO.Compression;
using Pangya.Domain.Players;

namespace Pangya.Tests;

/// <summary>Self Design (SPEC-self-design.md): peças do IFF, nome do arquivo, chaves de upload e o zip.</summary>
public class SelfDesignTests
{
    [Fact]
    public void UccPartsComeFromPartCategory()
    {
        var data = Pangya.Protocol.KR645.Kr645GameData.Load(TestEnv.Config.Data.IffPath);
        var draw = data.UccPart(0x0800602F);                                // 그리기용상의1(누리)
        var copy = data.UccPart(0x08006033);                                // 복제용 (mesma roupa)
        Assert.NotNull(draw);
        Assert.NotNull(copy);
        Assert.True(draw!.CanDraw && !draw.CanCopy);
        Assert.True(copy!.CanCopy && !copy.CanDraw);
        Assert.Equal(draw.Clothes, copy.Clothes);
        Assert.NotEqual(draw.Clothes, data.UccPart(0x0800A020)!.Clothes);   // calça: outra roupa
        Assert.Equal("m_ts_u01f01_0000f424.jpg", Ucc.FileName(draw.Texture, "0000f424"));
        Assert.Null(data.UccPart(0x08000800));                              // peça comum
    }

    [Fact]
    public void FileNameFiltersLikeTheClient()
    {
        Assert.Equal("2c_pv_u01f01_ab12.jpg", Ucc.FileName("2#C_PV_U01f-01.jpg", "ab12"));
        Assert.True(Ucc.ValidFileName("2c_pv_u01f01_ab12.jpg"));
        Assert.False(Ucc.ValidFileName("../x.jpg"));
        Assert.False(Ucc.ValidFileName("a.png"));
        Assert.True(Ucc.ValidIndex("0000f424"));
        Assert.False(Ucc.ValidIndex("123456789"));
        Assert.False(Ucc.ValidIndex("ABC"));
    }

    [Fact]
    public void ItemIndexDefaultsToTheItemId()
    {
        var it = new Item { Id = 1000000, TypeId = 0x0800602F };
        Assert.Equal("000f4240", Ucc.Index(it));
        it.Attrs["ucc_idx"] = "abc";
        Assert.Equal("abc", Ucc.Index(it));
    }

    [Fact]
    public void UploadKeyIsSingleUseAndExpires()
    {
        var u = new UccUploads();
        var now = DateTime.UtcNow;
        var key = u.Issue(1, 50, "a_1.jpg", now);
        Assert.Null(u.Consume(1, 50, "errada", now));
        Assert.Null(u.Consume(2, 50, key, now));                            // outra conta
        Assert.Equal("a_1.jpg", u.Consume(1, 50, key, now));
        Assert.Null(u.Consume(1, 50, key, now));                            // uso único
        var old = u.Issue(1, 51, "b.jpg", now);
        Assert.Null(u.Consume(1, 51, old, now + UccUploads.KeyLifetime + TimeSpan.FromSeconds(1)));
        Assert.False(u.TakeUploaded(1, 50, now));
        u.MarkUploaded(1, 50, now);
        Assert.True(u.TakeUploaded(1, 50, now));
        Assert.False(u.TakeUploaded(1, 50, now));
    }

    static byte[] Zip(params string[] names)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var n in names)
            {
                using var s = z.CreateEntry(n).Open();
                s.Write(new byte[300]);
            }
        return ms.ToArray();
    }

    [Fact]
    public void UploadMustBeTheClientZip()
    {
        Assert.True(Pangya.Web.WebServer.ValidUccZip(Zip("front", "back", "icon")));
        Assert.True(Pangya.Web.WebServer.ValidUccZip(Zip("front", "back")));
        Assert.False(Pangya.Web.WebServer.ValidUccZip(Zip("front")));
        Assert.False(Pangya.Web.WebServer.ValidUccZip(Zip("front", "back", "x.exe")));
        Assert.False(Pangya.Web.WebServer.ValidUccZip(new byte[100]));
    }
}
