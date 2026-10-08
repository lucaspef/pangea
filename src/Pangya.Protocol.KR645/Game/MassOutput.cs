using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Game;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Saída dos modos em massa para o cliente 645 (docs/protocolo/SPEC-modes.md §1-2). Os pacotes que falam de uma
/// bola (0x51/0x8E/0x63) vão só para o dono; posição e resultado de buraco dos rivais (0x6C/0x6B/0x6A) para todos.
/// </summary>
public sealed class MassOutput(Room room, Core.Config.TreasureHunterConfig? treasure = null) : IMassOutput
{
    const ushort SWind = 0x59, SHoleStart = 0x51, STeeReady = 0x8E, SNextHole = 0x63, SNoMission = 0x147,
        SRivalPos = 0x6C, SRivalHole = 0x6B, SRivalState = 0x6A, SApproachHole = 0x148, SApproachTotals = 0x146, SApproachEnd = 0x149,
        SGuildPairs = 0xBD, SGuildScore = 0xC0;

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

    /// <summary>Fim de buraco de um jogador no torneio: 0x12A com os pontos do Treasure Hunter dele.</summary>
    public void HoleDone(MassPlayer p)
    {
        if (TreasureOn) To(p, new PacketWriter(InGameOutput.STreasurePoints).U32((uint)TreasurePoints(p)));
    }

    // ---- Treasure Hunter no torneio (tipos 4/5): pontos de cada jogador, caixas só dele (SPEC-treasure-hunter.md §3/§4)

    bool TreasureOn => treasure is { Enabled: true } && room.Settings.Mode is GameMode.Tournament or GameMode.Team30s;

    /// <summary>Pontos do jogador nos buracos que ele já terminou (teto 1000).</summary>
    int TreasurePoints(MassPlayer p)
    {
        int total = 0;
        for (int i = 0; i < Math.Min(p.HoleIndex, Game.HoleCount); i++)
            if (p.Strokes[i] > 0) total += TreasureHunter.HolePoints(p.Strokes[i], Game.ParOf(Game.HoleAt(i)));
        return Math.Min(total, TreasureHunter.MaxPoints);
    }

    /// <summary>
    /// Caixas do jogador que terminou: 0x12A (total) e 0x12B (u8 n, n × {u32 uid, u32 tid, u16 qtd, u8 0}) só para ele,
    /// antes do 0xCC/0x77 (a tela de resultado do torneio abre as caixas se a marca do 0x12B estiver ligada).
    /// </summary>
    List<(int TypeId, int Count)> TreasureDraw(MassPlayer p, out byte[]? boxes)
    {
        boxes = null;
        if (!TreasureOn || !p.Finished || p.Left) return [];
        int points = TreasurePoints(p);
        To(p, new PacketWriter(InGameOutput.STreasurePoints).U32((uint)points));
        var data = PlayerStructs.Data;
        int n = TreasureHunter.BoxCount(points, treasure!.RatePercent, Random.Shared);
        var prizes = TreasureHunter.Draw(n, treasure.Prizes,
            tid => ((uint)tid >> 26) == 6 && (data == null || data.Exists(tid)), Random.Shared);
        if (prizes.Count == 0) return prizes;
        var w = new PacketWriter(InGameOutput.STreasureBoxes).U8((byte)prizes.Count);
        foreach (var (tid, count) in prizes) w.U32(p.Guid).U32((uint)tid).U16((ushort)count).U8(0);
        boxes = w.Body.ToArray();
        To(p, w);
        Log.Info($"sala {room.Index}: treasure hunter de {p.Guid}: {points} pontos, {prizes.Count} caixa(s)");
        return prizes;
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

    /// <summary>0xBD: u8 n, n × sGuildMatchup {u8 grupo, u32 guid vermelho, u32 guid azul} (antes do 0x50).</summary>
    public static PacketWriter GuildPairs(List<(byte Group, MassPlayer Red, MassPlayer Blue)> pairs)
    {
        var w = new PacketWriter(SGuildPairs, 4 + pairs.Count * 9).U8((byte)pairs.Count);
        foreach (var (g, red, blue) in pairs) w.U8(g).U32(red.Guid).U32(blue.Guid);
        return w;
    }

    /// <summary>0xC0: u32 guid, i16 placar vermelho, i16 placar azul, u8 pontos do guid, u8 pontos do adversário.</summary>
    public void GuildScore(MassPlayer p, short red, short blue) =>
        All(new PacketWriter(SGuildScore).U32(p.Guid).I16(red).I16(blue).U8((byte)Math.Min(p.GuildPoints, 255))
            .U8((byte)Math.Min(p.Opponent?.GuildPoints ?? 0, 255)));

    public void GameOver(List<MassPlayer> players, TourneyResult result)
    {
        if (result.Guild is { } guild)                                  // grava pontos/pang das guildas (uma vez por partida)
            foreach (var p in players)
                if (p.RoomPlayer.Session is GameHandler h0) { h0.RecordGuildMatch(room, Game, guild); break; }
        foreach (var p in players)                                      // recompensa de quem terminou (humanos)
            if (p.RoomPlayer.Session is GameHandler h)
            {
                int trophy = result.TrophyOf(p);
                var treasureWon = TreasureDraw(p, out var boxes);
                int exp = h.BeginGameEnd(p.Pang, p.Bonus, Game.HoleCount, p.Finished, Game is ApproachGame ? null : (room.CoursePlayed, p.Score),
                    players: Game.Players.Count, positionPenalty: false, coursePlayed: room.CoursePlayed,   // torneio: sem desconto por posição
                    trophy: result.MatchTid != 0 ? (result.MatchTid, trophy) : null, awardItems: result.ItemsOf(p),
                    treasure: treasureWon, treasureBoxes: boxes);
                (int Winner, uint PangWin, uint Points, uint PangRed, uint PangBlue)? g = result.Guild is { } go
                    ? (go.Winner, (uint)go.PangWin.GetValueOrDefault(p), (uint)p.GuildPoints, (uint)go.Pang[0], (uint)go.Pang[1]) : null;
                h.SendMassResult(exp, room.Field?.WonBy(p.Guid) ?? [], room.Settings.Mode == GameMode.GuildMatch,
                    result.MatchTid, trophy, result.Awards, result.Medals, g);
            }
    }
}
