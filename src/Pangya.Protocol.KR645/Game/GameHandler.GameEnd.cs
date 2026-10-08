using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Game;
using Pangya.Domain.Players;
using Pangya.Domain.Shop;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Fim de partida de um jogador (docs/protocolo/SPEC-resultado-fim-de-jogo.md), em duas fases:
/// 1) BeginGameEnd, antes do placar: calcula a recompensa (sem gravar) e devolve o EXP que vai no registro do 0x64/0x8F
///    ou no 0x77 (a tela de resultado anima a barra a partir dele);
/// 2) FinishGameEnd, quando o cliente manda o 0x06 (estatística final, logo depois do placar; no torneio, quando a tela
///    abre) ou depois de EndWait: grava recompensa, estatística e presentes de nível e só então manda 0x10D/0x43/0xC6.
///    Antes disso a tela somaria o EXP duas vezes (ela parte do EXP que o cliente já tem).
/// </summary>
public sealed partial class GameHandler
{
    const ushort SLevelUp = 0x10D, SItemsWon = 0xF8, SMyItemsWon = 0xCC, SMassResult = 0x77, CGiftList = 0x93;
    static readonly TimeSpan EndWait = TimeSpan.FromSeconds(20);

    sealed record PendingEnd(uint Pang, uint Bonus, int Holes, bool Finished, (int Course, int Score)? Course, int PangRate, int ExpRate,
        Rewards.ExpInput ExpIn, (int RoomTid, int Kind)? Trophy, IReadOnlyList<int> AwardItems,
        IReadOnlyList<(int TypeId, int Count)> Treasure);
    PendingEnd? pendingEnd;

    /// <summary>
    /// Fase 1: deixa a recompensa pendente e devolve o EXP a mostrar (0 para quem saiu ou está no nível máximo).
    /// players/position/positionPenalty/coursePlayed alimentam a fórmula de EXP do GB (Rewards.Exp). trophy/awardItems:
    /// troféu do torneio que entra na contagem do perfil e o item do prêmio (vai por carta).
    /// </summary>
    public int BeginGameEnd(uint reportedPang, uint reportedBonus, int holes, bool finished, (int Course, int Score)? course = null,
        int players = 1, int position = 0, bool positionPenalty = true, int coursePlayed = 0, (int RoomTid, int Kind)? trophy = null,
        IReadOnlyList<int>? awardItems = null, IReadOnlyList<(int TypeId, int Count)>? treasure = null)
    {
        var p = player!;
        var now = DateTime.UtcNow;
        MessengerPlaying(false);
        int pangRate = CardService.ActiveRate(p, ctx.Data.Cards, CardInfo.AbilityPangRate, now);
        int expRate = CardService.ActiveRate(p, ctx.Data.Cards, CardInfo.AbilityExpRate, now);
        var expIn = new Rewards.ExpInput(players, ctx.Data.CourseStars(coursePlayed), position, positionPenalty, p.Level);
        var end = new PendingEnd(reportedPang, reportedBonus, holes, finished, course, pangRate, expRate, expIn,
            finished ? trophy : null, finished ? awardItems ?? [] : [], finished ? treasure ?? [] : []);
        if (Interlocked.Exchange(ref pendingEnd, end) is { } stale) _ = FinishAsync(stale);     // partida anterior ainda aberta
        _ = Task.Delay(EndWait).ContinueWith(_ => FinishGameEnd(end), TaskScheduler.Default);
        var (_, exp) = Rewards.Compute(reportedPang, reportedBonus, holes, finished, ctx.World.Config.Rewards, pangRate, expRate, expIn);
        return exp;
    }

    /// <summary>Fase 2 (uma vez): pelo 0x06, pelo prazo ou ao desconectar. only = só esta partida (o prazo de uma antiga não fecha a nova).</summary>
    void FinishGameEnd(PendingEnd? only = null)
    {
        var end = only == null ? Interlocked.Exchange(ref pendingEnd, null)
            : Interlocked.CompareExchange(ref pendingEnd, null, only) == only ? only : null;
        if (end != null) _ = FinishAsync(end);
    }

    async Task FinishAsync(PendingEnd e)
    {
        var p = player!;
        try
        {
            var stats = lastGameStats;
            lastGameStats = null;
            int levelBefore = p.Level;
            var r = await Rewards.ApplyAsync(ctx.Players.Store, p, e.Pang, e.Bonus, e.Holes, e.Finished, ctx.World.Config.Rewards,
                e.Course, e.PangRate, e.ExpRate, stats, e.ExpIn, e.Trophy);
            if (r.Pang != 0 || r.Exp != 0)
                Log.Info($"{conn} recompensa: +{r.Pang} pang, +{r.Exp} EXP{(r.LevelsUp > 0 ? $", subiu {r.LevelsUp} nível(is) -> {p.Level}" : "")}");
            if (e.Trophy is { Kind: > Trophy.None } t) Log.Info($"{conn} troféu {t.Kind} (1 ouro, 2 prata, 3 bronze) da sala {t.RoomTid:X8}");
            if (r.LevelsUp > 0) await LevelUpGiftsAsync(levelBefore, p.Level);
            foreach (var tid in e.AwardItems) await AwardItemAsync(tid);
            if (e.Finished && e.Holes > 0)                                  // 0x43: totais, EXP/nível novos, troféus e o registro do curso
            {
                var w = new PacketWriter(SStatsUpdate, 0x160).Struct(PlayerStructs.Statistics(p)).Struct(PlayerStructs.Trophies(p));
                if (e.Course is { } c) w.U8((byte)c.Course).Struct(PlayerStructs.MapStat(p, c.Course)); else w.U8(0xFF);
                conn.Send(w.U8(0xFF));
            }
            bool treasurePang = e.Treasure.Count > 0 && await DeliverTreasureAsync(e.Treasure);
            if (r.Pang != 0 || r.LevelsUp > 0 || treasurePang) conn.Send(new PacketWriter(SPang).U64((ulong)p.Pang).U64(0));
        }
        catch (Exception ex) { Log.Error($"{conn} falha ao gravar a recompensa", ex); }
    }

    /// <summary>
    /// Presentes da tabela do cliente para cada nível alcançado (o 0x10D abre a janela com o do nível novo; o cliente diz
    /// que o presente vai para o correio). Uma carta do sistema por nível; sem correio, direto no inventário.
    /// </summary>
    async Task LevelUpGiftsAsync(int from, int to)
    {
        var p = Player;
        for (int lv = from + 1; lv <= to; lv++)
        {
            var gifts = Levels.Gifts(lv);
            if (gifts.Length == 0) continue;
            if (ctx.Mail != null)
            {
                await ctx.Mail.SendSystemAsync(p.AccountId, "@Pangya", $"Presente do nivel {lv}", gifts);
                continue;
            }
            foreach (var (tid, qty) in gifts)
            {
                if (tid == Levels.PangPouch)
                {
                    p.Pang += qty;
                    await ctx.Players.Store.ApplyAsync(p.AccountId, new PlayerChanges { Pang = p.Pang });
                    continue;
                }
                bool had = p.FindType(tid) != null;
                var (code, granted) = await ctx.Shop.GiveAsync(p, tid, qty);
                if (code != ShopCode.Ok || granted.Count == 0 || p.Find(granted[0].Id) is not { } it)
                {
                    Log.Info($"{conn} presente do nível {lv} ({tid:X8}) não entregue: {code}");
                    continue;
                }
                conn.Send(had && it.IsConsumable
                    ? new PacketWriter(SItemCounts).U8(1).U32((uint)it.TypeId).U32((uint)it.Id).U16((ushort)it.Quantity)
                    : new PacketWriter(SItems).U16(1).U16(1).Struct(PlayerStructs.ItemInfo(it)));
            }
        }
        conn.Send(new PacketWriter(SLevelUp).U8(1).U8((byte)to).U8(0));       // sLevelUpDone {feito, nível, tipo}
        if (ctx.Mail != null) conn.Send(new PacketWriter(SNewMail));           // carta nova: o cliente pede a lista
        Log.Info($"{conn} presentes de nível {from + 1}..{to} enviados{(ctx.Mail != null ? " pelo correio" : "")}");
    }

    /// <summary>
    /// Entrega do Treasure Hunter: grava os prêmios e manda 0x12C (u8 n, n × {u32 uid, u32 tid, u32 id do item, u16 qtd
    /// ganha, u8 0, i32 0, u16 0}); o cliente soma no inventário dele (pang no saldo). true = ganhou pang.
    /// </summary>
    async Task<bool> DeliverTreasureAsync(IReadOnlyList<(int TypeId, int Count)> prizes)
    {
        var p = Player;
        var w = new PacketWriter(STreasureGifts);
        var lines = new List<(int Tid, int Id, int Count)>(prizes.Count);
        long pang = 0;
        foreach (var (tid, count) in prizes)
        {
            if (tid == TreasureHunter.PangTid) { pang += count; lines.Add((tid, 0, count)); continue; }
            var (code, granted) = await ctx.Shop.GiveAsync(p, tid, count);
            if (code == ShopCode.Ok && granted.Count > 0) lines.Add((tid, granted[0].Id, count));
            else Log.Info($"{conn} treasure hunter: 0x{tid:X8} x{count} não entregue ({code})");
        }
        if (pang > 0)
        {
            p.Pang += pang;
            await ctx.Players.Store.ApplyAsync(p.AccountId, new PlayerChanges { Pang = p.Pang });
        }
        w.U8((byte)lines.Count);
        foreach (var (tid, id, count) in lines)
            w.U32((uint)p.AccountId).U32((uint)tid).U32((uint)id).U16((ushort)count).U8(0).I32(0).U16(0);
        conn.Send(w);
        Log.Info($"{conn} treasure hunter: {lines.Count} prêmio(s), +{pang} pang");
        return pang > 0;
    }

    /// <summary>
    /// 0x129 sub 1: u8 n, n × {u8 curso, u32 gauge} — barra de cada mapa na escolha de mapa (o cliente só mostra 700..1000).
    /// O gauge por curso ainda não é simulado: todos cheios, como o GB.
    /// </summary>
    static PacketWriter TreasureGauges()
    {
        const int Courses = 20, Full = 1000;
        var w = new PacketWriter(STreasureGaugeList).U8(1).U8(Courses);
        for (int i = 0; i < Courses; i++) w.U8((byte)i).U32(Full);
        return w;
    }

    /// <summary>Item do troféu do torneio: carta do sistema (sem correio, direto no inventário).</summary>
    async Task AwardItemAsync(int tid)
    {
        if (!ctx.Data.Exists(tid)) { Log.Info($"{conn} item do troféu {tid:X8} não existe no pangya.iff"); return; }
        if (ctx.Mail != null)
        {
            await ctx.Mail.SendSystemAsync(Player.AccountId, "@Pangya", "Premio do torneio", [(tid, 1)]);
            conn.Send(new PacketWriter(SNewMail));
            return;
        }
        var (code, granted) = await ctx.Shop.GiveAsync(Player, tid, 1);
        if (code == ShopCode.Ok && granted.Count > 0 && Player.Find(granted[0].Id) is { } it)
            conn.Send(new PacketWriter(SItems).U16(1).U16(1).Struct(PlayerStructs.ItemInfo(it)));
    }

    /// <summary>0xF8 (não-mass, antes do 0x64): u16 n, n × {u32 guid, u8 dobro 0, u16 k, k × u32 tid} — itens ganhos na partida.</summary>
    public static PacketWriter ItemsWon(IReadOnlyList<(uint Guid, IReadOnlyList<int> Items)> players)
    {
        var w = new PacketWriter(SItemsWon).U16((ushort)players.Count);
        foreach (var (guid, items) in players)
        {
            w.U32(guid).U8(0).U16((ushort)items.Count);
            foreach (var tid in items) w.U32((uint)tid);
        }
        return w;
    }

    /// <summary>
    /// Torneio/approach: 0xCC (meus itens: u8 dobro 0, u16 n, n × tid) e 0x77 (u32 EXP, u32 troféu da sala, u8 troféu
    /// ganho, u8 equipe vencedora, [guild: 4 × u32], 12 × {u32 guid 0xFFFFFFFF = vazio, u32 tid}: [0..5] medalhas (sorte,
    /// mais rápido, drive, chip-in, long putt, recuperação; ainda nenhuma), [6..11] premiados 1º..6º com o item).
    /// Nunca nos outros modos: no campo o 0x77 manda o cliente de volta à sala.
    /// </summary>
    public void SendMassResult(int exp, IReadOnlyList<int> items, bool guild, int matchTid = 0, int myTrophy = 0,
        IReadOnlyList<Domain.Rooms.TourneyAward>? awards = null, IReadOnlyList<Domain.Rooms.TourneyMedal>? medals = null,
        (int Winner, uint PangWin, uint Points, uint PangRed, uint PangBlue)? guildResult = null)
    {
        var cc = new PacketWriter(SMyItemsWon).U8(0).U16((ushort)items.Count);
        foreach (var tid in items) cc.U32((uint)tid);
        conn.Send(cc);
        var w = new PacketWriter(SMassResult, 0x80).U32((uint)Math.Max(exp, 0)).U32((uint)matchTid).U8((byte)myTrophy)
            .U8((byte)(guildResult?.Winner ?? 2));                           // equipe vencedora (2 = nenhuma/empate)
        if (guild)                                                           // tipo 6: meu pang de guilda, meus pontos, pang de cada lado
        {
            var gr = guildResult ?? default;
            w.U32(gr.PangWin).U32(gr.Points).U32(gr.PangRed).U32(gr.PangBlue);
        }
        var slots = new (uint Guid, uint Tid)[12];
        for (int i = 0; i < slots.Length; i++) slots[i] = (0xFFFFFFFF, 0);
        if (awards != null)
            foreach (var a in awards)
                if (a.Position < 6) slots[6 + a.Position] = (a.Player.Guid, (uint)a.ItemTid);
        if (medals != null)
            foreach (var m in medals)
                if (m.Slot is >= 0 and < Domain.Rooms.Medal.Count) slots[m.Slot] = (m.Player.Guid, (uint)m.ItemTid);
        foreach (var (guid, tid) in slots) w.U32(guid).U32(tid);
        conn.Send(w);
    }
}
