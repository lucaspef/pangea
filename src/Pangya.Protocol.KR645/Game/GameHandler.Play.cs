using System.Buffers.Binary;
using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Players;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>Partida (CGolfTask do cliente; docs/protocolo/SPEC-ingame.md). A lógica fica no StrokeGame (Domain).</summary>
public sealed partial class GameHandler
{
    // ids C->S
    const ushort CLoading = 0x48, CHoleData = 0x1A, CLoaded = 0x11, CTeeReady = 0x34, CShot = 0x12, CShotResult = 0x1B,
        CShotFinished = 0x1C, CHoleStats = 0x31, CAim = 0x13, CGauge = 0x14, CPowerShot = 0x15, CClub = 0x16, CUseItem = 0x17,
        CDrop = 0x19, CPause = 0x30, CTimeBooster = 0x65, CCutIn = 0xE7, CTurnClock = 0x22, CShotCommand = 0x42, CErrorReport = 0x33;

    static bool IsPlayPacket(ushort id) => id is CLoading or CHoleData or CLoaded or CTeeReady or CShot or CShotResult
        or CShotFinished or CHoleStats or CAim or CGauge or CPowerShot or CClub or CUseItem or CDrop or CPause or CTimeBooster
        or CCutIn or CTurnClock or CShotCommand or CErrorReport;

    /// <summary>Trata os pacotes da partida; false = não é pacote de partida.</summary>
    bool HandlePlay(PacketReader p)
    {
        if (!IsPlayPacket(p.Id)) return false;
        if (p.Id == CErrorReport)
        {
            var kind = p.U8();
            Log.Warn($"{conn} relatório de erro do cliente (tipo {kind}): {(p.Remaining >= 2 ? p.Str(1024) : "")}");
            return true;
        }
        if (p.Id == CCutIn)
        {
            // a tacada só anda depois desta resposta (golftask.c case 0x192): u8 0, u16 0 = sem animação de corte
            p.Skip(p.Remaining);
            conn.Send(new PacketWriter(InGameOutput.SCutIn).U8(0).U16(0));
            return true;
        }
        PendingSave? save = null;
        lock (Rooms.Sync)
        {
            var g = room?.Game;
            var me = g?.Find((uint)Player.AccountId);
            if (g == null || me == null || g.Over) return true;        // fora de uma partida: ignora
            var r = room!;
            uint guid = me.Guid;
            switch (p.Id)
            {
                case CLoading:
                    var pct = p.U8();
                    InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SLoading).U32(guid).U8(pct), except: this);
                    if (r.Bot is { } bot) conn.Send(new PacketWriter(InGameOutput.SLoading).U32(bot.Guid).U8(pct));   // barra do bot acompanha
                    break;
                case CHoleData:
                    var hole = p.U8(); p.U32(); p.U32();
                    var par = p.U8();
                    g.HoleData(hole, new HoleInfo(par, p.F32(), p.F32(), p.F32(), p.F32()));
                    break;
                case CLoaded: g.Loaded(me); break;
                case CTeeReady: g.TeeShotReady(me); break;
                case CShot: Shot(g, r, me, p.Bytes(p.Remaining)); break;
                case CShotResult: HandleShotResult(g, r, p.Bytes(p.Remaining)); break;
                case CShotFinished: g.ShotFinished(me); break;
                case CHoleStats or CTurnClock: p.Skip(p.Remaining); break;
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
                case CUseItem: save = UseItem(g, r, me, p.U32()); break;
            }
        }
        if (save is { } s) _ = SaveAsync(s);
        return true;
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
    void HandleShotResult(StrokeGame g, Room r, ReadOnlySpan<byte> rest)
    {
        if (rest.Length < InGameOutput.ResultLength) return;
        Span<byte> res = stackalloc byte[InGameOutput.ResultLength];
        var key = rest.Length >= InGameOutput.ResultLength + 16 ? rest.Slice(InGameOutput.ResultLength, 16) : r.Key;
        for (int i = 0; i < res.Length; i++) res[i] = (byte)(rest[i] ^ key[i % 16]);
        var sr = new ShotResult(BinaryPrimitives.ReadUInt32LittleEndian(res), BinaryPrimitives.ReadSingleLittleEndian(res[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(res[8..]), BinaryPrimitives.ReadSingleLittleEndian(res[12..]), res[0x10],
            BinaryPrimitives.ReadUInt32LittleEndian(res[0x13..]), BinaryPrimitives.ReadUInt32LittleEndian(res[0x17..]));
        if (!g.Result(sr)) return;
        ((InGameOutput)g.Output).OnResult(sr);
        InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SShotResult).Bytes(res));
    }

    readonly record struct PendingSave(Item? Item, bool Equip);

    /// <summary>
    /// Item na partida (0x17 u32 tid; itemwindow.cpp): só na própria vez e se o item está nos slots equipados.
    /// Tira um slot e uma unidade (como o ProcessItem do cliente) e manda 0x58 para todos; o dono recebe a contagem nova.
    /// </summary>
    PendingSave? UseItem(StrokeGame g, Room r, GamePlayer me, uint tid)
    {
        if (Item.GroupOf((int)tid) != ItemGroup.Usable || !g.IsTurnOf(me))
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
        InGameOutput.Broadcast(r, new PacketWriter(InGameOutput.SUseItem).U32(tid).U32(rnd).U32(me.Guid));
        conn.Send(new PacketWriter(SItemCounts).U8(1).U32(tid).U32((uint)item.Id).U16((ushort)item.Quantity));
        conn.Send(new PacketWriter(SEquip).Struct(PlayerStructs.Equip(Player)));
        return new PendingSave(item, true);
    }

    async Task SaveAsync(PendingSave s)
    {
        try
        {
            if (s.Item != null) await ctx.Players.Store.SaveItemAsync(Player.AccountId, s.Item);
            if (s.Equip) await ctx.Players.Store.SaveEquipAsync(Player.AccountId, Player.Equip);
        }
        catch (Exception e) { Log.Error($"{conn} falha ao salvar o inventário", e); }
    }
}
