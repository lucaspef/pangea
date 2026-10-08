using Pangya.Core.Net;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Saída dos modos em massa para o cliente 645 (docs/protocolo/SPEC-modes.md §1-2). Os pacotes que falam de uma
/// bola (0x51/0x8E/0x63) vão só para o dono; posição e resultado de buraco dos rivais (0x6C/0x6B/0x6A) para todos.
/// </summary>
public sealed class MassOutput(Room room) : IMassOutput
{
    const ushort SWind = 0x59, SHoleStart = 0x51, STeeReady = 0x8E, SNextHole = 0x63, SNoMission = 0x147,
        SRivalPos = 0x6C, SRivalHole = 0x6B, SRivalState = 0x6A, SApproachHole = 0x148, SApproachTotals = 0x146, SApproachEnd = 0x149;

    MassGame Game => (MassGame)room.Game!;

    static void To(MassPlayer p, PacketWriter w)
    {
        if (p.RoomPlayer.Session is GameHandler h) h.Connection.Send(w);
        else w.Dispose();
    }

    void All(PacketWriter w) => InGameOutput.Broadcast(room, w);

    public void HoleStart(MassPlayer to, byte wind, byte direction, bool approach)
    {
        To(to, new PacketWriter(SWind).U8(wind).U8(0).U16(direction).U8(1));
        if (approach) To(to, new PacketWriter(SNoMission).U8(0));      // approach sem missão
        To(to, new PacketWriter(SHoleStart).U32(to.Guid));              // em massa: índice 0 = a própria bola
    }

    public void TeeReady(MassPlayer? to)
    {
        if (to == null) All(new PacketWriter(STeeReady));
        else To(to, new PacketWriter(STeeReady));
    }

    /// <summary>0x6C: u32 guid, u8 buraco, f32 x, f32 z, [approach: u32 distância (décimos de jarda), u32 tempo], u16 0.</summary>
    public void RivalPos(MassPlayer p, byte hole, float x, float z, bool approach)
    {
        var w = new PacketWriter(SRivalPos).U32(p.Guid).U8(hole).F32(x).F32(z);
        if (approach) w.U32(p.Dist).U32(p.Time);
        else if (room.Settings.Mode == GameMode.NewApproach) w.U32(0).U32(MassGame.Out);
        All(w.U16(0));
    }

    /// <summary>0x6B: u32 guid, u8 buraco jogado, u8 tacadas totais, i32 placar, u64 pang, u64 bônus, u8 1 (buraco terminado).</summary>
    public void RivalHole(MassPlayer p, byte hole) =>
        All(new PacketWriter(SRivalHole).U32(p.Guid).U8(hole).U8((byte)Math.Min(p.Total, 255)).I32(p.Score).U64(p.Pang).U64(p.Bonus).U8(1));

    public void RivalState(MassPlayer p, byte state) => All(new PacketWriter(SRivalState).U32(p.Guid).U8(state));

    public void NextHole(MassPlayer? to)
    {
        if (to == null) All(new PacketWriter(SNextHole));
        else To(to, new PacketWriter(SNextHole));
    }

    static PacketWriter Entries(ushort id, List<ApproachEntry> list)
    {
        // sApproachResultData (0x18): u8 saiu, u32 guid, u32 uid, u8 posição, u32 prêmios, u32 distância, u32 tempo, u8, u8
        var w = new PacketWriter(id).U8((byte)list.Count);
        foreach (var e in list)
            w.U8((byte)(e.Left ? 1 : 0)).U32(e.Guid).U32(e.Uid).U8(e.Rank).U32(e.Prize).U32(e.Dist).U32(e.Time).U8(0).U8(0);
        return w;
    }

    public void ApproachHole(List<ApproachEntry> entries) => All(Entries(SApproachHole, entries));

    public void ApproachEnd(List<ApproachEntry> totals)
    {
        All(Entries(SApproachTotals, totals));
        All(new PacketWriter(SApproachEnd));                            // diálogos de fim -> volta para a sala
    }

    public void GameOver(List<MassPlayer> players)
    {
        foreach (var p in players)                                      // recompensa de quem terminou (humanos)
            if (p.RoomPlayer.Session is GameHandler h)
            {
                int exp = h.BeginGameEnd(p.Pang, p.Bonus, Game.HoleCount, p.Finished, Game is ApproachGame ? null : (room.CoursePlayed, p.Score),
                    players: Game.Players.Count, positionPenalty: false, coursePlayed: room.CoursePlayed);   // torneio: sem desconto por posição
                h.SendMassResult(exp, room.Field?.WonBy(p.Guid) ?? [], room.Settings.Mode == GameMode.GuildMatch);
            }
    }
}
