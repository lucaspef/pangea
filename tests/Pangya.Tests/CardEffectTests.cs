using Pangya.Core.Config;
using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

public class GbExpFormulaTests
{
    static readonly RewardConfig Cfg = new();

    [Fact]
    public void PlayersHolesStarsAndPosition()
    {
        var e = new Rewards.ExpInput(Players: 4, Stars: 4, Position: 0, PositionPenalty: true, Level: 10);
        Assert.Equal(48, Rewards.Exp(e, 3, true, 0, Cfg));                         // 4 estrelas × 3 buracos × 4 jogadores
        Assert.Equal(43, Rewards.Exp(e with { Position = 1 }, 3, true, 0, Cfg));    // 2º lugar: × 0,9 (43,2)
        Assert.Equal(72, Rewards.Exp(e, 3, true, 50, Cfg));                        // geleia branca +50%
        Assert.Equal(96, Rewards.Exp(e, 3, true, 0, new RewardConfig { ExpRate = 200 }));   // servidor 2×
        Assert.Equal(48, Rewards.Exp(e with { Position = 3, PositionPenalty = false }, 3, true, 0, Cfg));   // torneio
        Assert.Equal(48, Rewards.Exp(e with { Level = 70 }, 3, true, 0, Cfg));      // nível 70 também ganha (barra de 1,6 M)
        Assert.Equal(0, Rewards.Exp(e, 9, false, 0, Cfg));                         // saiu
    }

    [Fact]
    public void CourseStarsComeFromCourseDifficulty()
    {
        var d = Pangya.Protocol.KR645.Kr645GameData.Load(TestEnv.Config.Data.IffPath);
        float s = d.CourseStars(0);
        Assert.Equal(1f, s);                                                      // Blue Lagoon: 1 estrela
        Assert.Equal(3f, d.CourseStars(3) - 2);                                   // Wind Hill: 5
        Assert.Equal(1f, d.CourseStars(0x7E));                                     // curso inexistente
    }
}

public class CardRewardTests
{
    [Fact]
    public void RatesApplyAfterThePerHoleCap()
    {
        var cfg = new RewardConfig { MaxPangPerHole = 100 };
        Assert.Equal((300L, 3), Rewards.Compute(500, 0, 3, true, cfg));            // teto 3 × 100; EXP 1 jogador × 3 buracos
        Assert.Equal((360L, 4), Rewards.Compute(500, 0, 3, true, cfg, 20, 50));    // depois × 1,2 e EXP × 1,5 (truncado)
        Assert.Equal((0L, 0), Rewards.Compute(500, 0, 3, false, cfg, 20, 50));
    }

    [Fact]
    public void NewestUnexpiredSpecialCardWins()
    {
        var cards = new Dictionary<int, CardInfo>
        {
            [0x7C800001] = new(0x7C800001, true, 0, CardInfo.AbilityPangRate, 10, 60, 1),
            [0x7C800002] = new(0x7C800002, true, 0, CardInfo.AbilityPangRate, 50, 60, 1),
            [0x7C800003] = new(0x7C800003, true, 0, CardInfo.AbilityExpRate, 20, 60, 1),
        };
        var now = DateTime.UtcNow;
        var p = new Player();
        p.Add(new Item { Id = 10, TypeId = 0x7C800001, Location = ItemLocation.ActiveCard, ExpiresAt = now.AddHours(1) });
        p.Add(new Item { Id = 11, TypeId = 0x7C800002, Location = ItemLocation.ActiveCard, ExpiresAt = now.AddHours(-1) });   // vencido
        p.Add(new Item { Id = 12, TypeId = 0x7C800003, Location = ItemLocation.ActiveCard, ExpiresAt = now.AddHours(1) });
        p.Add(new Item { Id = 13, TypeId = 0x7C800002, Location = ItemLocation.Inventory, Quantity = 3 });                 // só na pilha
        Assert.Equal(10, CardService.ActiveRate(p, cards, CardInfo.AbilityPangRate, now));
        Assert.Equal(20, CardService.ActiveRate(p, cards, CardInfo.AbilityExpRate, now));
        p.Add(new Item { Id = 14, TypeId = 0x7C800002, Location = ItemLocation.ActiveCard, ExpiresAt = now.AddHours(1) });
        Assert.Equal(50, CardService.ActiveRate(p, cards, CardInfo.AbilityPangRate, now));
    }
}

/// <summary>O 0x74 leva os cards em vigor de cada jogador (docs/protocolo/SPEC-cards-efeitos.md §4).</summary>
[Collection("db")]
public class CardsInGameTests(DbFixture fx)
{
    [Fact]
    public async Task GameStartCarriesEachPlayersActiveCards()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        int special = 0;
        foreach (var (tid, ci) in env.Data.Cards)
            if (ci.Ability == CardInfo.AbilityPangRate && ((tid >> 22) & 0xF) == 2) { special = tid; break; }
        Assert.NotEqual(0, special);
        var (acc, key) = await env.NewPlayerAsync();
        await using (var db = await env.S.Db.OpenAsync())
            await Dapper.SqlMapper.ExecuteAsync(db,
                "insert into items(account_id, type_id, quantity, location, expires_at) values (@acc, @tid, 1, 2, now() + interval '1 hour')",
                new { acc = acc.Id, tid = special });

        await using var c = await env.ConnectAsync();
        await GameEnv.SendLoginAsync(c, acc, key);
        await c.ExpectAsync(0x94);
        await c.SendAsync(new PacketWriter(0x08).U8(0).U32(60000).U32(0).U8(4).U8(0).U8(3).U8(0).U8(0).Str("t").Str(""));
        await c.ExpectAsync(0x46);
        await c.SendAsync(new PacketWriter(0x0E).U32((uint)acc.Id));
        var r = await c.ExpectAsync(0x74);
        Assert.Equal((0, 1), (r.U8(), r.U8()));
        r.Struct<sUserInfo>();
        r.Struct<SYSTEMTIME>();
        Assert.Equal(1, r.U8());
        var card = r.Struct<sSCardAvilityPeriodInfo>();
        Assert.Equal(((uint)special, 2, CardInfo.AbilityPangRate, (byte)1), (card.tid, card.cardType, card.Avility, card.valid));
        Assert.Equal((uint)env.Data.Cards[special].AbilityValue, card.AvilityValue);
    }
}
