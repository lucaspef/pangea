using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Players;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Itens de My Room que se "abrem" e pacotes pequenos de aviso (SPEC-coverage.md do emulador; layouts confirmados no
/// cliente, prêmios em pang inventados como no emulador): bolsa da sorte, envelope de ano novo, caixas de evento,
/// aluguel, fita de replay, missões do tutorial, escola e lista de servidores.
/// </summary>
public sealed partial class GameHandler
{
    const ushort CLuckyPouch = 0x59, CNewYearMoney = 0x90, CEventBox = 0x91, CS4Box = 0xAA, CRentalExtend = 0xE8,
        CRentalDelete = 0xE9, CReplayTape = 0x4A, CTutorialMission = 0xA6, CChangeSchool = 0x3B, CServerList = 0x43;
    // avisos sem resposta: fim de treino, Go/Stop, denúncia, replay offline, treino, penalidade de chat, estado, debug
    const ushort CTrainingEnd = 0x2E, CGoStop = 0x36, CReport = 0x3A, CReplayOffline = 0x49, CTraining = 0x5A, CChatPenalty = 0x4F,
        CStateReply = 0x40, CTaskManager = 0x85, CConsole = 0x86;
    const ushort SLuckyPouch = 0x121, SNewYearMoney = 0xDA, SEventBox = 0xCB, SRentalExtend = 0x194, SRentalDelete = 0x195,
        STutorial = 0x11B, SSchool = 0x4F, SServerList = 0x9D;
    public const int LuckyPouch = 0x1A000004, NewYearMoney = 0x1A00003B, PangPouchTid = 0x1A000010;
    static readonly int[] EventBoxes = [0x1A00003E, 0x1A000048], S4Boxes = [0x1A000054, 0x1A0000BC], ReplayTapes = [0x1A000000, 0x1A00004F];
    /// <summary>Pang de cada caixa [inventado, igual ao emulador].</summary>
    const int PouchPang = 1000, NewYearPang = 10000, S4BoxPang = 3000;
    /// <summary>Extensão de um item alugado: 7 dias (o cliente mostra 0xA8 = 168 horas).</summary>
    static readonly TimeSpan RentalExtension = TimeSpan.FromHours(168);

    async ValueTask<bool> HandleBoxesAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CLuckyPouch: p.Skip(p.Remaining); await LuckyPouchAsync(); return true;
            case CNewYearMoney: await NewYearMoneyAsync(p.U32()); return true;
            case CEventBox: await EventBoxAsync(p.U32()); return true;
            case CS4Box: await S4BoxAsync(p.U32()); return true;
            case CRentalExtend: await RentalAsync(p.U32(), extend: true); return true;
            case CRentalDelete: await RentalAsync(p.U32(), extend: false); return true;
            case CReplayTape: await ReplayTapeAsync(p.Remaining >= 4 ? (int)p.U32() : 0); return true;
            case CTutorialMission: await TutorialMissionAsync(p); return true;
            case CChangeSchool: await ChangeSchoolAsync(p.U32()); return true;
            case CServerList: await ServerListAsync(); return true;
            case CTrainingEnd or CGoStop or CReport or CReplayOffline or CTraining or CChatPenalty or CStateReply or CTaskManager or CConsole:
                Log.Debug($"{conn} aviso 0x{p.Id:X2} ({p.Remaining} bytes)");
                p.Skip(p.Remaining);
                return true;
            default: return false;
        }
    }

    /// <summary>Gasta 1 unidade (pilha; 0 apaga) e soma pang, numa gravação. null = não tem.</summary>
    async Task<Item?> UseOneAsync(Item? it, long pang = 0)
    {
        if (it is not { Location: ItemLocation.Inventory, Quantity: > 0 } || PlayerActions.Free(Player, it) == 0) return null;   // à venda
        var p = Player;
        var left = it.Clone();
        left.Quantity--;
        var ch = new PlayerChanges();
        if (left.Quantity == 0) ch.Removed.Add(it.Id); else ch.Updated.Add(left);
        if (pang != 0) ch.Pang = p.Pang + pang;
        await ctx.Players.Store.ApplyAsync(p.AccountId, ch);
        if (left.Quantity == 0) p.Items.Remove(it.Id); else p.Items[it.Id] = left;
        p.Pang += pang;
        return left;
    }

    Item? FindOf(uint guidOrTid, int[] tids)
    {
        if (Player.Find((int)guidOrTid) is { } byId && Array.IndexOf(tids, byId.TypeId) >= 0) return byId;
        foreach (var t in tids)
            if ((guidOrTid == 0 || guidOrTid == (uint)t) && Player.FindType(t) is { } byType) return byType;
        return null;
    }

    PacketWriter Count(Item left) => new PacketWriter(SItemCounts).U8(1).U32((uint)left.TypeId).U32((uint)left.Id).U16((ushort)left.Quantity);

    /// <summary>
    /// 0x59 -> 0x121 u8 0, u32 id da bolsa, u32 1, sPouchPrize {u32 tid 0x1A000010, u32 0, i32 pang, u32 0, u32 0}. O cliente
    /// desconta a bolsa e soma o pang sozinho (por isso não vai 0xA5); o 0xC6 depois fixa o valor certo.
    /// </summary>
    async Task LuckyPouchAsync()
    {
        var it = Player.FindType(LuckyPouch);
        int id = it?.Id ?? 0;
        if (await UseOneAsync(it, PouchPang) == null) { conn.Send(new PacketWriter(SLuckyPouch).U8(1).U32(2)); return; }
        conn.Send(new PacketWriter(SLuckyPouch).U8(0).U32((uint)id).U32(1).U32(PangPouchTid).U32(0).I32(PouchPang).U32(0).U32(0));
        conn.Send(PangUpdate());
    }

    /// <summary>0x90 u32 id -> 0xA5, 0xC6, 0xDA u32 id (tira da lista e libera a tela).</summary>
    async Task NewYearMoneyAsync(uint id)
    {
        if (await UseOneAsync(FindOf(id, [NewYearMoney]), NewYearPang) is { } left) { conn.Send(Count(left)); conn.Send(PangUpdate()); }
        conn.Send(new PacketWriter(SNewYearMoney).U32(id));
    }

    /// <summary>0x91 u32 id -> 0xA5 + 0xCB u8 6 ("não foi sorteado"; não há sorteio de evento) ou 1 (falhou).</summary>
    async Task EventBoxAsync(uint id)
    {
        if (await UseOneAsync(FindOf(id, EventBoxes)) is not { } left) { conn.Send(new PacketWriter(SEventBox).U8(1)); return; }
        conn.Send(Count(left));
        conn.Send(new PacketWriter(SEventBox).U8(6));
    }

    /// <summary>0xAA u32 tid -> 0xA5, 0xC6, 0x1A2 u32 0, u32 caixa, u32 0x1A000010, u32 pang (ou 8 = não tem).</summary>
    async Task S4BoxAsync(uint tid)
    {
        if (await UseOneAsync(FindOf(tid, S4Boxes), S4BoxPang) is not { } left)
        {
            conn.Send(new PacketWriter(SOpenBox).U32(8).U32(tid).U32(0).U32(0));
            return;
        }
        conn.Send(Count(left));
        conn.Send(PangUpdate());
        conn.Send(new PacketWriter(SOpenBox).U32(0).U32((uint)left.TypeId).U32(PangPouchTid).U32(S4BoxPang));
    }

    /// <summary>
    /// 0xE8/0xE9 u32 id: estender (+7 dias a partir do fim ou de agora) ou apagar um item com prazo.
    /// 0x194/0x195 u8 0, u32 tid, u32 id; 1 = não é alugado, 2 = não existe.
    /// </summary>
    async Task RentalAsync(uint id, bool extend)
    {
        ushort reply = extend ? SRentalExtend : SRentalDelete;
        var it = Player.Find((int)id);
        if (it == null) { conn.Send(new PacketWriter(reply).U8(2)); return; }
        if (it.ExpiresAt == null || PlayerActions.IsEquipped(Player, it) && !extend) { conn.Send(new PacketWriter(reply).U8(1)); return; }
        var ch = new PlayerChanges();
        if (extend)
        {
            var e = it.Clone();
            var from = e.ExpiresAt > DateTime.UtcNow ? e.ExpiresAt.Value : DateTime.UtcNow;
            e.ExpiresAt = from + RentalExtension;
            ch.Updated.Add(e);
            await ctx.Players.Store.ApplyAsync(Player.AccountId, ch);
            Player.Items[it.Id] = e;
        }
        else
        {
            ch.Removed.Add(it.Id);
            await ctx.Players.Store.ApplyAsync(Player.AccountId, ch);
            Player.Items.Remove(it.Id);
        }
        conn.Send(new PacketWriter(reply).U8(0).U32((uint)it.TypeId).U32(id));
    }

    /// <summary>0x4A u32 tid: o replay foi salvo, gasta uma fita (sem resposta além do 0xA5).</summary>
    async Task ReplayTapeAsync(int tid)
    {
        if (Array.IndexOf(ReplayTapes, tid) < 0) return;
        if (await UseOneAsync(Player.FindType(tid)) is { } left) conn.Send(Count(left));
    }

    /// <summary>
    /// 0xA6 u8 último, u8 categoria, u32 bit da missão -> grava e responde 0x11B u8 3, u8 presente 0, 3 × u32 (missões
    /// feitas por categoria: iniciante 0x01..0x80, básico 0x100..0x2000, avançado 0x8000..). Também vai no login.
    /// </summary>
    async Task TutorialMissionAsync(PacketReader p)
    {
        if (p.Remaining < 6) { p.Skip(p.Remaining); return; }
        p.U8();
        int cat = p.U8();
        uint bit = p.U32();
        if (cat >= Player.Tutorial.Length) cat = bit < 0x100 ? 0 : bit < 0x4000 ? 1 : 2;
        var flags = (int[])Player.Tutorial.Clone();
        flags[cat] |= (int)bit;
        await ctx.Players.Store.ApplyAsync(Player.AccountId, new PlayerChanges { Tutorial = flags });
        Player.Tutorial = flags;
        conn.Send(TutorialPacket(flags));
    }

    static PacketWriter TutorialPacket(int[] flags)
    {
        var w = new PacketWriter(STutorial).U8(3).U8(0);
        foreach (var f in flags) w.U32((uint)f);
        return w;
    }

    /// <summary>0x3B u32 escola -> grava e responde 0x4F u8 0, u32 escola (sUserInfo.info.school).</summary>
    async Task ChangeSchoolAsync(uint school)
    {
        int s = (int)Math.Min(school, 0xFF);
        await ctx.Players.Store.ApplyAsync(Player.AccountId, new PlayerChanges { School = s });
        Player.School = s;
        conn.Send(new PacketWriter(SSchool).U8(0).U32((uint)s));
    }

    /// <summary>0x43 -> 0x9D u8 n, n × sGameServerInfo, u8 m, m × sChannelInfo (servidores e canais deste).</summary>
    async Task ServerListAsync()
    {
        var servers = ctx.Registry != null ? await ctx.Registry.ListAsync("game") : [];
        int n = Math.Min(servers.Count, 255);
        var w = new PacketWriter(SServerList).U8((byte)n);
        for (int i = 0; i < n; i++)
        {
            var s = servers[i];
            var e = new sGameServerInfo { id = (uint)s.Id, maxUser = s.MaxUsers, curUser = s.CurUsers, port = s.Port, eventFlags = (uint)s.Flags };
            Cp949.Write(e.name, s.Name);
            Cp949.Write(e.addr, s.Address);
            w.Struct(e);
        }
        w.U8((byte)ctx.World.Channels.Count);
        foreach (var c in ctx.World.Channels)
        {
            var ci = new sChannelInfo { Max_Num = (ushort)c.MaxUsers, Current_Num = (ushort)c.Count, Uid = (byte)c.Id };
            Cp949.Write(ci.Name, c.Name);
            w.Struct(ci);
        }
        conn.Send(w);
    }
}
