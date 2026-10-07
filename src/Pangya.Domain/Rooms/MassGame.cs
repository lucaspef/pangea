using System.Diagnostics;

namespace Pangya.Domain.Rooms;

/// <summary>Participante de um modo em massa (cada um joga a própria bola).</summary>
public sealed class MassPlayer(RoomPlayer rp)
{
    public RoomPlayer RoomPlayer { get; } = rp;
    public uint Guid => RoomPlayer.Guid;
    public bool IsBot => RoomPlayer.IsBot;
    public int HoleIndex { get; set; }
    public int[] Strokes { get; } = new int[18];
    /// <summary>Soma de (tacadas - par) dos buracos terminados.</summary>
    public int Score { get; set; }
    public int Total { get; set; }
    public uint Pang { get; set; }
    public uint Bonus { get; set; }
    public int Started { get; set; } = -1;
    public bool ShotOpen { get; set; }
    public bool HasResult { get; set; }
    public byte ResultState { get; set; }
    public bool Finished { get; set; }
    public bool Left { get; set; }
    public long LastProgress { get; set; } = Stopwatch.GetTimestamp();
    // approach
    public int ShotTimeMs { get; set; } = -1;
    public uint Dist { get; set; } = MassGame.Out;
    public uint Time { get; set; } = MassGame.Out;
    public bool Answered { get; set; }
    public List<ApproachEntry> History { get; } = [];
}

/// <summary>Resultado de um jogador num buraco do approach (sApproachResultData).</summary>
public readonly record struct ApproachEntry(uint Guid, uint Uid, bool Left, byte Rank, uint Prize, uint Dist, uint Time);

/// <summary>Saída dos modos em massa (a camada de protocolo transforma em pacotes; "To" = só para aquele jogador).</summary>
public interface IMassOutput
{
    void HoleStart(MassPlayer to, byte wind, byte direction, bool approach);
    void TeeReady(MassPlayer? to);                       // null = todos
    void RivalPos(MassPlayer p, byte hole, float x, float z, bool approach);
    void RivalHole(MassPlayer p, byte hole);
    void RivalState(MassPlayer p, byte state);           // 2 terminou, 3 saiu
    void NextHole(MassPlayer? to);                       // null = todos
    void ApproachHole(List<ApproachEntry> entries);
    void ApproachEnd(List<ApproachEntry> totals);
    /// <summary>Fim do jogo: recompensa de cada um que terminou.</summary>
    void GameOver(List<MassPlayer> players);
}

/// <summary>
/// Modos em massa (docs/protocolo/SPEC-modes.md §1-2): no cliente só a própria bola é local, os outros são "rivais".
/// Nada de eco de tacada (0x53/0x62): o cliente voa e aplica a própria tacada; o servidor só repassa posição (0x6C)
/// e resultado do buraco (0x6B/0x6A) dos rivais.
/// </summary>
public abstract class MassGame : RoomGame
{
    public const uint Out = 0xFFFFFFFF;

    protected readonly IMassOutput Output;
    protected readonly TimeSpan BotDelay;
    readonly Dictionary<uint, MassPlayer> byGuid = [];
    public List<MassPlayer> Players { get; } = [];

    protected MassGame(Room room, IMassOutput output, object sync, TimeSpan botDelay) : base(room, sync)
    {
        Output = output;
        BotDelay = botDelay;
        foreach (var rp in room.Players)
        {
            var p = new MassPlayer(rp);
            Players.Add(p);
            byGuid[p.Guid] = p;
        }
    }

    public static MassGame For(Room room, IMassOutput output, object sync, TimeSpan botDelay) =>
        room.Settings.Mode == GameMode.NewApproach ? new ApproachGame(room, output, sync, botDelay) : new TourneyGame(room, output, sync, botDelay);

    public static bool IsMass(GameMode m) => m is GameMode.Tournament or GameMode.Team30s or GameMode.GuildMatch
        or GameMode.Approach or GameMode.NewApproach or (GameMode)14;

    public MassPlayer? Find(uint guid) => byGuid.GetValueOrDefault(guid);

    protected bool HasHumans()
    {
        foreach (var p in Players)
            if (!p.IsBot && !p.Left) return true;
        return false;
    }

    protected (byte Wind, byte Dir) NewWind() => ((byte)Rng.Next(9), (byte)Rng.Next(256));

    public abstract void Loaded(MassPlayer p);
    public abstract void TeeShotReady(MassPlayer p);
    /// <summary>Tacada (sem eco). shotTimeMs = tempo restante do relógio no bloco da tacada (approach).</summary>
    public abstract void Shoot(MassPlayer p, int shotTimeMs);
    public abstract void Result(MassPlayer p, ShotResult r);
    public abstract void ShotFinished(MassPlayer p);
    public bool CanUseItem(MassPlayer p) => !p.Finished && !p.ShotOpen && !Over;

    protected void EndGame()
    {
        if (Over) return;
        Cancel();
        var done = new List<MassPlayer>();
        foreach (var p in Players)
            if (!p.Left) done.Add(p);
        Output.GameOver(done);
        RoomManager.FinishGame(Room);
    }
}

/// <summary>
/// Torneio (modos 4/5/6/9/14): cada humano avança sozinho. 0x11 -&gt; vento + 0x51 a ele; 0x1B -&gt; 0x6C a todos;
/// 0x1C com o buraco terminado (acertou ou par+4) -&gt; 0x6B a todos e 0x63 a ele (último: 0x6A estado 2).
/// O bot não taca: termina cada buraco algum tempo depois do primeiro humano (par -1..+2).
/// </summary>
public sealed class TourneyGame : MassGame
{
    static readonly int[] BotOverPar = [-1, 0, 0, 0, 1, 1, 2];
    readonly TimeSpan botHoleMax = TimeSpan.FromSeconds(90);
    bool botsRunning;

    public TourneyGame(Room room, IMassOutput output, object sync, TimeSpan botDelay) : base(room, output, sync, botDelay) { }

    public byte HoleOf(MassPlayer p) => HoleAt(p.HoleIndex);

    public override void Loaded(MassPlayer p)
    {
        if (p.Finished || p.Started == p.HoleIndex) return;
        p.Started = p.HoleIndex;
        p.ShotOpen = false;
        p.LastProgress = Stopwatch.GetTimestamp();
        var (w, d) = NewWind();
        Output.HoleStart(p, w, d, approach: false);
        StartBots();
    }

    public override void TeeShotReady(MassPlayer p) => Output.TeeReady(p);   // o cliente em massa não espera

    public override void Shoot(MassPlayer p, int shotTimeMs)
    {
        if (p.Finished || p.ShotOpen) return;
        p.ShotOpen = true;
        p.HasResult = false;
        p.Strokes[p.HoleIndex]++;
    }

    public override void Result(MassPlayer p, ShotResult r)
    {
        if (!p.ShotOpen || p.HasResult || r.Guid != p.Guid) return;
        p.HasResult = true;
        p.ResultState = r.State;
        p.Pang = r.Pang;
        p.Bonus = r.BonusPang;
        if (r.State == ShotResult.StateWaterOrOut) p.Strokes[p.HoleIndex]++;
        Output.RivalPos(p, HoleOf(p), r.X, r.Z, approach: false);
    }

    public override void ShotFinished(MassPlayer p)
    {
        if (!p.ShotOpen || p.Finished) return;
        p.ShotOpen = false;
        byte hole = HoleOf(p);
        bool holed = p.HasResult && p.ResultState == ShotResult.StateHoled;
        if (!holed && p.Strokes[p.HoleIndex] < ParOf(hole) + GiveUpOverPar) return;   // continua sozinho (sem 0x61)
        FinishHole(p, hole);
    }

    void FinishHole(MassPlayer p, byte hole)
    {
        int st = p.Strokes[p.HoleIndex];
        p.Total += st;
        p.Score += st - ParOf(hole);
        p.LastProgress = Stopwatch.GetTimestamp();
        Output.RivalHole(p, hole);
        p.HoleIndex++;
        if (p.HoleIndex >= HoleCount)
        {
            p.Finished = true;
            Output.RivalState(p, 2);
        }
        if (!p.IsBot) Output.NextHole(p);
        CheckEnd();
    }

    void CheckEnd()
    {
        foreach (var p in Players)
            if (!p.Finished && !p.Left) return;
        EndGame();
    }

    public override void PlayerLeft(RoomPlayer rp)
    {
        var p = Find(rp.Guid);
        if (p == null || p.Left) return;
        p.Left = true;
        if (Over) return;
        Output.RivalState(p, 3);
        if (!HasHumans()) { Cancel(); return; }
        CheckEnd();
    }

    // ------------------------------------------------------------------ bot simulado

    void StartBots()
    {
        if (botsRunning) return;
        bool any = false;
        foreach (var p in Players) any |= p.IsBot;
        if (!any) return;
        botsRunning = true;
        foreach (var p in Players) p.LastProgress = Stopwatch.GetTimestamp();
        BotTick();
    }

    void BotTick()
    {
        var tick = TimeSpan.FromMilliseconds(Math.Clamp(BotDelay.TotalMilliseconds / 4, 10, 250));
        Later(tick, () =>
        {
            if (!HasHumans()) return;
            int target = 0;
            bool allDone = true;
            foreach (var h in Players)
            {
                if (h.IsBot || h.Left) continue;
                target = Math.Max(target, h.HoleIndex);
                allDone &= h.Finished;
            }
            foreach (var b in Players)
            {
                if (!b.IsBot || b.Finished || Over) continue;
                var wait = Stopwatch.GetElapsedTime(b.LastProgress);
                if ((b.HoleIndex < target && wait >= BotDelay) || wait >= botHoleMax || (allDone && wait >= TimeSpan.FromSeconds(Math.Min(BotDelay.TotalSeconds, 1))))
                    BotHole(b);
            }
            if (!Over) BotTick();
        });
    }

    void BotHole(MassPlayer b)
    {
        byte hole = HoleOf(b);
        int par = ParOf(hole);
        b.Strokes[b.HoleIndex] = Math.Max(1, par + BotOverPar[Rng.Next(BotOverPar.Length)]);
        b.Pang += (uint)Rng.Next(30, 121);
        if (Holes.TryGetValue(hole, out var h)) Output.RivalPos(b, hole, h.PinX, h.PinZ, approach: false);
        FinishHole(b, hole);
    }
}

/// <summary>
/// Approach (modo 10): em cada buraco cada jogador dá UMA tacada da própria posição aleatória; ganha quem fica mais
/// perto da bandeira. 0x11 -&gt; vento, 0x147 (sem missão), 0x51; todos 0x34 (ou 12 s) -&gt; 0x8E a todos; 0x1B -&gt; 0x6C
/// com distância/tempo; todos responderam (ou tempo + 15 s) -&gt; 0x148 + 0x63 (último buraco: 0x148, 0x146, 0x149).
/// </summary>
public sealed class ApproachGame : MassGame
{
    readonly HashSet<uint> loaded = [], teeReady = [];
    readonly int limitMs;
    bool acked, closed;
    public int HoleIndex { get; private set; }
    public byte Hole => HoleAt(HoleIndex);

    public ApproachGame(Room room, IMassOutput output, object sync, TimeSpan botDelay) : base(room, output, sync, botDelay)
    {
        limitMs = room.Settings.GameTimeMs > 0 ? (int)room.Settings.GameTimeMs : 40000;
    }

    void ResetHole()
    {
        loaded.Clear();
        teeReady.Clear();
        acked = closed = false;
        foreach (var p in Players)
        {
            p.ShotOpen = p.HasResult = p.Answered = false;
            p.ShotTimeMs = -1;
            p.Dist = p.Time = Out;
        }
    }

    public override void Loaded(MassPlayer p)
    {
        if (closed || !loaded.Add(p.Guid)) return;
        var (w, d) = NewWind();
        Output.HoleStart(p, w, d, approach: true);
    }

    public override void TeeShotReady(MassPlayer p)
    {
        if (acked) { Output.TeeReady(p); return; }
        bool first = teeReady.Count == 0;
        teeReady.Add(p.Guid);
        if (AllHumansReady()) AckTee();
        else if (first)
        {
            int idx = HoleIndex;
            Later(TimeSpan.FromSeconds(12), () => { if (HoleIndex == idx) AckTee(); });
        }
    }

    bool AllHumansReady()
    {
        foreach (var h in Players)
            if (!h.IsBot && !h.Left && !teeReady.Contains(h.Guid)) return false;
        return true;
    }

    void AckTee()
    {
        if (acked || closed) return;
        acked = true;
        Output.TeeReady(null);                                  // relógio da tacada começa (cliente espera até 20 s)
        int idx = HoleIndex;
        foreach (var b in Players)
            if (b.IsBot) Later(BotDelay, () => { if (HoleIndex == idx) BotShot(b); });
        Later(TimeSpan.FromMilliseconds(limitMs + 15000), () => { if (HoleIndex == idx) CloseHole(); });
    }

    public override void Shoot(MassPlayer p, int shotTimeMs)
    {
        if (p.ShotOpen || p.Answered || closed) return;
        p.ShotOpen = true;
        p.ShotTimeMs = shotTimeMs;
    }

    public override void Result(MassPlayer p, ShotResult r)
    {
        if (r.Guid != p.Guid || p.HasResult || closed) return;
        p.HasResult = true;
        if (p.ShotTimeMs < 0 || r.State == ShotResult.StateWaterOrOut || !Holes.TryGetValue(Hole, out var h))
            p.Dist = p.Time = Out;                                // estouro de tempo / água / OB = "Out"
        else
        {
            double dx = r.X - h.PinX, dz = r.Z - h.PinZ;
            p.Dist = (uint)Math.Floor(Math.Sqrt(dx * dx + dz * dz) * 0.3125 * 10);   // décimos de jarda
            p.Time = (uint)Math.Max(0, p.ShotTimeMs);
        }
        p.Pang = r.Pang;
        Output.RivalPos(p, Hole, r.X, r.Z, approach: true);
    }

    public override void ShotFinished(MassPlayer p)
    {
        if (p.Answered) return;                                   // o cliente manda o 0x1C duas vezes
        p.Answered = true;
        p.ShotOpen = false;
        if (!p.HasResult) p.Dist = p.Time = Out;
        CheckHole();
    }

    void BotShot(MassPlayer b)
    {
        if (closed || b.Answered) return;
        Holes.TryGetValue(Hole, out var h);
        b.Dist = (uint)Rng.Next(15, 251);                           // 1,5 a 25 jardas
        b.Time = (uint)Rng.Next(5000, Math.Max(5001, limitMs - 3000));
        double a = Rng.NextDouble() * Math.PI * 2, r = b.Dist / 3.125;
        b.Answered = true;
        Output.RivalPos(b, Hole, (float)(h.PinX + r * Math.Sin(a)), (float)(h.PinZ + r * Math.Cos(a)), approach: true);
        CheckHole();
    }

    void CheckHole()
    {
        foreach (var p in Players)
            if (!p.Answered && !p.Left) return;
        CloseHole();
    }

    List<ApproachEntry> Entries()
    {
        var ok = new List<MassPlayer>();
        foreach (var p in Players)
            if (!p.Left && p.Dist != Out) ok.Add(p);
        ok.Sort((a, b) => a.Dist != b.Dist ? a.Dist.CompareTo(b.Dist) : b.Time.CompareTo(a.Time));
        var list = new List<ApproachEntry>(Players.Count);
        foreach (var p in Players)
        {
            int rk = ok.IndexOf(p) + 1;
            byte rank = (byte)(rk == 0 ? 255 : rk);
            uint prize = rk is >= 1 and <= 3 ? (uint)(4 - rk) : 0;   // 1º/2º/3º = 3/2/1 prêmios [suposição]
            list.Add(new ApproachEntry(p.Guid, (uint)p.RoomPlayer.Player.AccountId, p.Left, rank, prize, p.Dist, p.Time));
        }
        return list;
    }

    void CloseHole()
    {
        if (closed || Over) return;
        closed = true;
        var entries = Entries();
        foreach (var e in entries) Find(e.Guid)?.History.Add(e);
        Output.ApproachHole(entries);
        if (HoleIndex + 1 >= HoleCount) { End(); return; }
        HoleIndex++;
        ResetHole();
        Output.NextHole(null);
    }

    void End()
    {
        var totals = new List<(MassPlayer P, uint Prize, uint Dist, uint Time)>();
        foreach (var p in Players)
        {
            uint prize = 0, dist = 0, time = 0;
            foreach (var e in p.History)
            {
                prize += e.Prize;
                if (e.Dist != Out) dist += e.Dist;
                if (e.Time != Out) time += e.Time;
            }
            totals.Add((p, prize, dist, time));
        }
        var ranked = new List<(MassPlayer P, uint Prize, uint Dist, uint Time)>();
        foreach (var t in totals) if (!t.P.Left) ranked.Add(t);
        ranked.Sort((a, b) => a.Prize != b.Prize ? b.Prize.CompareTo(a.Prize) : a.Dist.CompareTo(b.Dist));
        var list = new List<ApproachEntry>(totals.Count);
        foreach (var t in totals)
        {
            int rk = t.P.Left ? 255 : ranked.IndexOf(t) + 1;
            list.Add(new ApproachEntry(t.P.Guid, (uint)t.P.RoomPlayer.Player.AccountId, t.P.Left, (byte)rk, t.Prize, t.Dist, t.Time));
            t.P.Finished = true;
        }
        Output.ApproachEnd(list);
        EndGame();
    }

    public override void PlayerLeft(RoomPlayer rp)
    {
        var p = Find(rp.Guid);
        if (p == null || p.Left) return;
        p.Left = true;
        if (Over) return;
        if (!HasHumans()) { Cancel(); return; }
        if (!closed && acked) CheckHole();
        else if (!acked && teeReady.Count > 0 && AllHumansReady()) AckTee();
    }
}
