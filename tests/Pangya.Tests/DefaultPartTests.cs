namespace Pangya.Tests;

/// <summary>Peças padrão que o cliente veste sem o item (visto no tutorial do My Room: 0x08000600 na parte 1).</summary>
public class DefaultPartTests
{
    [Fact]
    public void DefaultPartsComeFromTheIffPattern()
    {
        var data = Pangya.Protocol.KR645.Kr645GameData.Load(TestEnv.Config.Data.IffPath);
        const int Nuri = 0x04000000, Hana = 0x04000001;
        Assert.True(data.IsDefaultPart(Nuri, 0x08000400));                  // 기본머리
        Assert.True(data.IsDefaultPart(Nuri, 0x08000600));                  // 기본보조머리
        Assert.True(data.IsDefaultPart(Nuri, 0x0800A600));                  // 허벅지
        Assert.True(data.IsDefaultPart(Nuri, 0x08024400));                  // 스케이트보드 (entrada)
        Assert.False(data.IsDefaultPart(Nuri, 0x08000800));                 // 카우보이모자: da loja
        Assert.False(data.IsDefaultPart(Nuri, 0x08024800));                 // versão "등장용" vendida
        Assert.False(data.IsDefaultPart(Nuri, 0x08040400));                 // cabelo da Hana
        Assert.True(data.IsDefaultPart(Hana, 0x08040400));
        Assert.False(data.IsDefaultPart(Nuri, 0x08000A00));                 // não existe
    }
}
