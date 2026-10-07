using System.Buffers.Binary;
using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Partida (CGolfTask do cliente; docs/protocolo/SPEC-ingame.md e SPEC-modes.md). A lógica fica no Domain:
/// StrokeGame/SideGame/SkinsGame (por vez) e TourneyGame/ApproachGame (em massa).
/// </summary>
public sealed partial class GameHandler
{
    // ids C->S
    const ushort CLoading = 0x48, CHoleData = 0x1A, CLoaded = 0x11, CTeeReady = 0x34, CShot = 0x12, CShotResult = 0x1B,
        CShotFinished = 0x1C, CHoleStats = 0x31, CAim = 0x13, CGauge = 0x14, CPowerShot = 0x15, CClub = 0x16, CUseItem = 0x17,
        CDrop = 0x19, CPause = 0x30, CTimeBooster = 0x65, CCutIn = 0xE7, CTurnClock = 0x22, CShotCommand = 0x42, CErrorReport = 0x33,
        CTeamHoleIn = 0x35, CMatchHoleIn = 0x52;

    static bool IsPlayPacket(ushort id) => id is CLoading or CHoleData or CLoaded or CTeeReady or CShot or CShotResult
        or CShotFinished or CHoleStats or CAim or CGauge or CPowerShot or CClub or CUseItem or CDrop or CPause or CTimeBooster
        or CCutIn or CTurnClock or CShotCommand or CErrorReport or CTeamHoleIn or CMatchHoleIn;

    /// <summary>O que gravar depois de sair do lock: item usado e/ou prêmios dos itens de campo.</summary>
    sealed class PendingSave
    {
        public Item? Item;
        public bool Equip;
        public long FieldPang;
        public List<int> FieldItems = [];
    }

    /// <summary>Trata os pacotes da partida; false = não é pacote de partida.</summary>
    bool HandlePlay(PacketReader p)
    {
        if (!IsPlayPacket(p.Id)) return false;
        switch (p.Id)
        {
            case CErrorReport:
                var kind = p.U8();
                Log.Warn($"{conn} relatório de erro do cliente (tipo {kind}): {(p.Remaining >= 2 ? p.Str(1024) : "")}");
                return true;
            case CCutIn:
                // a tacada só anda depois desta resposta (golftask.c case 0x192): u8 0, u16 0 = sem animação de corte
                p.Skip(p.Remaining);
                conn.Send(new PacketWriter(InGameOutput.SCutIn).U8(0).U16(0));
                return true;
            case CHoleStats or CTurnClock or CTeamHoleIn or CMatchHoleIn:   // estatísticas / relógio / pose: sem resposta
                p.Skip(p.Remaining);
                return true;
        }
        PendingSave? save = null;
        lock (Rooms.Sync)
        {
            if (room?.Game is not { Over: false } game) return true;          // fora de uma partida: ignora
            if (game is MassGame mg) save = HandleMass(mg, room, p);
            else if (game is StrokeGame g) save = HandleTurn(g, room, p);
        }
        if (save != null) _ = SaveAsync(save);
        return true;
    }

    PendingSave? HandleTurn(StrokeGame g, Room r, PacketReader p)
    {
        var me = g.Find((uint)Player.AccountId);
        if (me == null) return null;
        uint guid = me.Guid;
        switch (p.Id)
        {
            case CLoading:
                var pct = p.U8();
                InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SLoading).U32(guid).U8(pct), except: this);
                if (r.Bot is { } bot) conn.Send(new PacketWriter(InGameOutput.SLoading).U32(bot.Guid).U8(pct));   // barra do bot acompanha
                break;
            case CHoleData: g.HoleData(p.U8(), ReadHole(p)); break;
            case CLoaded: g.Loaded(me); break;
            case CTeeReady: g.TeeShotReady(me); break;
            case CShot: Shot(g, r, me, p.Bytes(p.Remaining)); break;
            case CShotResult:
                if (DecodeResult(r, p.Bytes(p.Remaining), out var sr, out var raw) && g.Result(sr))
                {
                    ((InGameOutput)g.Output).OnResult(sr);
                    InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SShotResult).Bytes(raw));
                }
                break;
            case CShotFinished:
                // itens de campo: só a cópia de quem tacou (isMyShot = 1) vale
                var field = g.ShotOpen && g.Turn == me ? CreditField(r, guid, g.Hole, p) : null;
                g.ShotFinished(me);
                return field;
            case CAim: Relay(r, InGameOutput.SAim, guid, p, toSelf: false); break;
            case CGauge: Relay(r, InGameOutput.SGauge, guid, p, toSelf: false); break;
            case CClub: Relay(r, InGameOutput.SClub, guid, p, toSelf: false); break;
            case CPowerShot:
                // ativar (1 simples, 2 dupla, 3 item) ou cancelar (0): os outros simulam a tacada com isso
                InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SPowerShot).U32(guid).U8(p.U8()), except: this);
                break;
            case CDrop: InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SDrop).Bytes(p.Bytes(Math.Min(p.Remaining, 12)))); break;
            case CPause: Relay(r, InGameOutput.SPause, guid, p, toSelf: true); break;
            case CTimeBooster: InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.STimeBooster).Bytes(p.Bytes(4)).U32(guid)); break;
            case CShotCommand: InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SShotCommand).Bytes(p.Bytes(p.Remaining)), except: this); break;
            case CUseItem: return UseItem(g.IsTurnOf(me), r, guid, p.U32(), toAll: true);
        }
        return null;
    }

    /// <summary>
    /// Modos em massa: cada um joga a própria bola. Sem eco de tacada/resultado; as retransmissões que mexeriam na bola
    /// dos outros (mira, força, taco, power shot, comandos) não são repassadas; queda/pausa/acelerador e item só voltam a quem mandou.
    /// </summary>
    PendingSave? HandleMass(MassGame g, Room r, PacketReader p)
    {
        var me = g.Find((uint)Player.AccountId);
        if (me == null || me.Left) return null;
        switch (p.Id)
        {
            case CHoleData: g.HoleData(p.U8(), ReadHole(p)); break;
            case CLoaded: g.Loaded(me); break;
            case CTeeReady: g.TeeShotReady(me); break;
            case CShot:
                var rest = p.Bytes(p.Remaining);
                if (InGameOutput.SplitShot(rest, out int start, out _))
                    g.Shoot(me, BinaryPrimitives.ReadInt32LittleEndian(rest.Slice(start + 0x1D, 4)));   // +0x1D = tempo restante (approach)
                break;
            case CShotResult:
                if (DecodeResult(r, p.Bytes(p.Remaining), out var sr, out _)) g.Result(me, sr);
                break;
            case CShotFinished:
                var hole = g is TourneyGame t ? t.HoleOf(me) : ((ApproachGame)g).Hole;
                var field = me.ShotOpen ? CreditField(r, me.Guid, hole, p) : null;
                g.ShotFinished(me);
                return field;
            case CDrop: conn.Send(new PacketWriter(InGameOutput.SDrop).Bytes(p.Bytes(Math.Min(p.Remaining, 12)))); break;
            case CPause: conn.Send(new PacketWriter(InGameOutput.SPause).U32(me.Guid).Bytes(p.Bytes(Math.Min(p.Remaining, 64)))); break;
            case CTimeBooster: conn.Send(new PacketWriter(InGameOutput.STimeBooster).Bytes(p.Bytes(4)).U32(me.Guid)); break;
            case CUseItem: return UseItem(g.CanUseItem(me), r, me.Guid, p.U32(), toAll: false);
            default: p.Skip(p.Remaining); break;                         // 0x48/0x13/0x14/0x15/0x16/0x42
        }
        return null;
    }

    static HoleInfo ReadHole(PacketReader p)
    {
        p.U32(); p.U32();
        var par = p.U8();
        return new HoleInfo(par, p.F32(), p.F32(), p.F32(), p.F32());
    }

    /// <summary>Repassa "id + guid + resto do pacote" para os outros (ou todos).</summary>
    void Relay(Room r, ushort id, uint guid, PacketReader p, bool toSelf) =>
        InGameOutput.Broadcast(r, new PacketWriter(id).U32(guid).Bytes(p.Bytes(Math.Min(p.Remaining, 64))), except: toSelf ? null : this);

    void Shot(StrokeGame g, Room r, GamePlayer me, ReadOnlySpan<byte> rest)
    {
        if (!InGameOutput.SplitShot(rest, out int start, out int tail))
        {
            Log.Warn($"{conn} tacada curta demais ({rest.Length} bytes)");
            return;
        }
        if (g.Turn != null && g.Turn != me) Log.Warn($"{conn} tacou fora da vez (vez de {g.Turn.Guid})");
        if (!g.Shoot(me)) { Log.Warn($"{conn} tacada repetida ignorada"); return; }
        var block = rest.Slice(start, InGameOutput.ShotLength);
        var after = rest.Slice(start + InGameOutput.ShotLength, tail);
        var output = (InGameOutput)g.Output;
        output.OnShot(me, block);
        output.Remember(block, after);
        // para TODOS, inclusive quem tacou: o voo da própria bola começa com este eco (byte a byte, com as flags especiais)
        InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SShot).U32(me.Guid).Bytes(block).Bytes(after));
    }

    /// <summary>0x1B: sShotResult (37 bytes) cifrado com XOR pela chave da sala, seguido da própria chave.</summary>
    static bool DecodeResult(Room r, ReadOnlySpan<byte> rest, out ShotResult sr, out byte[] raw)
    {
        sr = default;
        raw = [];
        if (rest.Length < InGameOutput.ResultLength) return false;
        raw = new byte[InGameOutput.ResultLength];
        var key = rest.Length >= InGameOutput.ResultLength + 16 ? rest.Slice(InGameOutput.ResultLength, 16) : r.Key;
        for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(rest[i] ^ key[i % 16]);
        var res = raw.AsSpan();
        sr = new ShotResult(BinaryPrimitives.ReadUInt32LittleEndian(res), BinaryPrimitives.ReadSingleLittleEndian(res[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(res[8..]), BinaryPrimitives.ReadSingleLittleEndian(res[12..]), res[0x10],
            BinaryPrimitives.ReadUInt32LittleEndian(res[0x13..]), BinaryPrimitives.ReadUInt32LittleEndian(res[0x17..]));
        return true;
    }

    /// <summary>
    /// Wiz City: 0x1C = u8 é minha tacada, u8 n, n × {u8 tipo, u32 índice, u8 quantidade, u8 textura}. Moeda = pang
    /// (atualizado na hora com 0xC6), caixa = um consumível. Gravado depois, fora do lock.
    /// </summary>
    PendingSave? CreditField(Room r, uint guid, byte hole, PacketReader p)
    {
        if (r.Field is not { } field || p.Remaining < 2) return null;
        bool mine = p.U8() == 1;
        int n = p.U8();
        if (!mine) return null;
        var save = new PendingSave();
        for (int i = 0; i < n && p.Remaining >= 7; i++)
        {
            int type = p.U8();
            uint index = p.U32();
            p.U8();
            int texture = p.U8();
            if (field.Take(guid, hole, type, index, texture, Random.Shared) is not { } prize) continue;
            if (prize.Pang > 0) save.FieldPang += prize.Pang;
            else save.FieldItems.Add(prize.ItemTypeId);
        }
        if (save.FieldPang == 0 && save.FieldItems.Count == 0) return null;
        if (save.FieldPang > 0)
        {
            Player.Pang += save.FieldPang;                          // mostra na hora (antes da próxima vez); grava logo depois
            conn.Send(PangUpdate());
        }
        Log.Info($"{conn} itens de campo no buraco {hole}: +{save.FieldPang} pang, {save.FieldItems.Count} caixa(s)");
        return save;
    }

    /// <summary>
    /// Item na partida (0x17 u32 tid; itemwindow.cpp): só na própria vez e se o item está nos slots equipados.
    /// Tira um slot e uma unidade (como o ProcessItem do cliente) e manda 0x58 (para todos, ou só para quem usou nos
    /// modos em massa); o dono recebe a contagem nova.
    /// </summary>
    PendingSave? UseItem(bool allowed, Room r, uint guid, uint tid, bool toAll)
    {
        if (Item.GroupOf((int)tid) != ItemGroup.Usable || !allowed)
        {
            Log.Warn($"{conn} uso de item 0x{tid:X8} recusado (não é item usável ou não é a vez)");
            return null;
        }
        var slots = Player.Equip.ItemSlots;
        int slot = Array.IndexOf(slots, (int)tid);
        var item = Player.FindType((int)tid);
        if (slot < 0 || item == null || item.Quantity <= 0)
        {
            Log.Warn($"{conn} uso de item 0x{tid:X8} recusado (não está equipado/não tem)");
            return null;
        }
        for (int i = slot; i < slots.Length - 1; i++) slots[i] = slots[i + 1];   // compacta os slots
        slots[^1] = 0;
        item.Quantity--;
        if (item.Quantity == 0) Player.Items.Remove(item.Id);
        var rnd = (uint)Random.Shared.Next();                       // o cliente falha o item se rnd % 100 < COM[1]
        var w = new PacketWriter(InGameOutput.SUseItem).U32(tid).U32(rnd).U32(guid);
        if (toAll) InGameOutput.Broadcast(r, w); else conn.Send(w);
        conn.Send(new PacketWriter(SItemCounts).U8(1).U32(tid).U32((uint)item.Id).U16((ushort)item.Quantity));
        conn.Send(new PacketWriter(SEquip).Struct(PlayerStructs.Equip(Player)));
        return new PendingSave { Item = item, Equip = true };
    }

    async Task SaveAsync(PendingSave s)
    {
        try
        {
            var store = ctx.Players.Store;
            if (s.Item != null) await store.SaveItemAsync(Player.AccountId, s.Item);
            if (s.Equip) await store.SaveEquipAsync(Player.AccountId, Player.Equip);
            if (s.FieldPang > 0 || s.FieldItems.Count > 0) await SaveFieldPrizesAsync(s);
        }
        catch (Exception e) { Log.Error($"{conn} falha ao salvar o inventário", e); }
    }

    /// <summary>Prêmios dos itens de campo: pang (0xC6) e consumíveis empilhados (0xA5 se já tinha).</summary>
    async Task SaveFieldPrizesAsync(PendingSave s)
    {
        var p = Player;
        var ch = new PlayerChanges();
        if (s.FieldPang > 0) ch.Pang = p.Pang;                     // já somado em memória pelo CreditField
        var touched = new Dictionary<int, Item>();
        foreach (var tid in s.FieldItems)
        {
            if (!touched.TryGetValue(tid, out var it))
            {
                var existing = p.FindType(tid);
                it = existing?.Clone() ?? new Item { Id = (await ctx.Players.Store.NewIdsAsync(1))[0], TypeId = tid, Quantity = 0 };
                (existing != null ? ch.Updated : ch.Added).Add(it);
                touched[tid] = it;
            }
            it.Quantity++;
        }
        await ctx.Players.Store.ApplyAsync(p.AccountId, ch);
        foreach (var it in touched.Values)
        {
            bool had = p.Find(it.Id) != null;
            p.Items[it.Id] = it;
            if (had) conn.Send(new PacketWriter(SItemCounts).U8(1).U32((uint)it.TypeId).U32((uint)it.Id).U16((ushort)it.Quantity));
            else conn.Send(new PacketWriter(SItems).U16(1).U16(1).Struct(PlayerStructs.ItemInfo(it)));
        }
    }
}
