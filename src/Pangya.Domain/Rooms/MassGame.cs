using Pangya.Domain.Players;
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
    /// <summary>Quando terminou o último buraco (Stopwatch; medalha "mais rápido").</summary>
    public long FinishedAt { get; set; }
    /// <summary>Melhores da partida pelo 0x31 do cliente (já limitados): drive, chip-in e putt (jardas).</summary>
    public float BestDrive { get; set; }
    public float BestChipIn { get; set; }
    public float BestLongPutt { get; set; }
    // GuildMatch: lado (0 vermelho, 1 azul), adversário do par, grupo ("N 조"), buracos fechados e pontos
    public int Side { get; set; } = -1;
    public MassPlayer? Opponent { get; set; }
    public byte Group { get; set; }
    public bool[] HoleDone { get; } = new bool[18];
    public int GuildPoints { get; set; }
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

/// <summary>Premiado do torneio: posição (0 = 1º), troféu (Trophy.Gold..Bronze) e item sorteado.</summary>
public readonly record struct TourneyAward(MassPlayer Player, int Position, int Trophy, int ItemTid);

/// <summary>Medalha do torneio: posição no 0x77 (<see cref="Medal"/>) e item sorteado.</summary>
public readonly record struct TourneyMedal(MassPlayer Player, int Slot, int ItemTid);

/// <summary>Medalhas, na ordem do 0x77 (m_awardItem[0..5]).</summary>
public static class Medal
{
    public const int Lucky = 0, Speediest = 1, BestDrive = 2, BestChipIn = 3, BestLongPutt = 4, BestRecovery = 5, Count = 6;
    /// <summary>Só a partir deste número de jogadores (GB Tourney.requestMakeMedal).</summary>
    public const int MinPlayers = 18;
}

/// <summary>
/// Fim do GuildMatch: vencedor (0 vermelho, 1 azul, 2 empate), pontos e "pang de guilda" de cada lado e o de cada
/// jogador (GB GuildRoomManager/DuplaManager: vencedor (pares + saídas) × 50, os outros saídas × 50 + 50).
/// </summary>
public sealed record GuildOutcome(int Winner, int[] Points, long[] Pang, Dictionary<MassPlayer, int> PangWin);

/// <summary>Troféu da sala (Match.iff; 0 = modo sem troféu), premiados, medalhas e o resultado do GuildMatch.</summary>
public sealed record TourneyResult(int MatchTid, List<TourneyAward> Awards, List<TourneyMedal> Medals, GuildOutcome? Guild = null)
{
    public static readonly TourneyResult Empty = new(0, [], []);

    public int TrophyOf(MassPlayer p)
    {
        foreach (var a in Awards) if (a.Player == p) return a.Trophy;
        return 0;
    }

    /// <summary>Itens que o jogador ganhou (troféu e medalhas).</summary>
    public List<int> ItemsOf(MassPlayer p)
    {
        var list = new List<int>();
        foreach (var a in Awards) if (a.Player == p) list.Add(a.ItemTid);
        foreach (var m in Medals) if (m.Player == p) list.Add(m.ItemTid);
        return list;
    }
}

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
    /// <summary>
    /// Fim do jogo: recompensa e tela de resultado (0xCC/0x77) de cada um que terminou. Vem ANTES do último 0x6A
    /// (closing), porque é ele que abre a tela de resultado do torneio, e ela lê o 0x77 nesse momento.
    /// </summary>
    void GameOver(List<MassPlayer> players, TourneyResult result);
    /// <summary>GuildMatch: placar depois de um buraco decidido no par de p (0xC0 a todos da sala).</summary>
    void GuildScore(MassPlayer p, short red, short blue);
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

    /// <param name="trophiesCountBots">bots contam para o número de jogadores dos troféus (config Rewards.TrophiesCountBots).</param>
    public static MassGame For(Room room, IMassOutput output, object sync, TimeSpan botDelay, bool trophiesCountBots = false) =>
        room.Settings.Mode == GameMode.NewApproach ? new ApproachGame(room, output, sync, botDelay)
            : new TourneyGame(room, output, sync, botDelay) { TrophiesCountBots = trophiesCountBots };

    /// <summary>Troféu e premiados no fim (só o torneio individual tem).</summary>
    protected virtual TourneyResult Results() => TourneyResult.Empty;

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

    /// <summary>closing = quem terminou por último: o 0x6A dele (estado 2) só sai depois do resultado.</summary>
    protected void EndGame(MassPlayer? closing = null)
    {
        if (Over) return;
        Cancel();
        var done = new List<MassPlayer>();
        foreach (var p in Players)
            if (!p.Left) done.Add(p);
        Output.GameOver(done, Results());
        if (closing != null) Output.RivalState(closing, 2);
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

    /// <summary>Prêmio de cada troféu: item comum sorteado entre 0x18000000..0x1800000E (GB: ITEM &lt;&lt; 26 + 0..14).</summary>
    public const int AwardItemBase = 0x18000000, AwardItemCount = 15;

    public TourneyGame(Room room, IMassOutput output, object sync, TimeSpan botDelay) : base(room, output, sync, botDelay)
    {
        if (room.Settings.Mode == GameMode.GuildMatch) MakePairs();
        if (room.Settings.Mode != GameMode.Tournament) return;
        var levels = new List<int>(Players.Count);
        foreach (var p in Players) levels.Add(p.RoomPlayer.Player.Level);
        MatchTid = Trophy.RoomTid(levels);                               // fixo na partida: quem sai não muda o troféu
    }

    /// <summary>GuildMatch: pares (grupo, vermelho, azul), na ordem dos slots (SPEC-guildmatch.md §2.1).</summary>
    public List<(byte Group, MassPlayer Red, MassPlayer Blue)> Pairs { get; } = [];

    void MakePairs()
    {
        var reds = new List<MassPlayer>();
        var blues = new List<MassPlayer>();
        foreach (var p in Players)
        {
            if (p.IsBot) continue;
            p.Side = p.RoomPlayer.Team == 0 ? 0 : 1;
            (p.Side == 0 ? reds : blues).Add(p);
        }
        reds.Sort((a, b) => a.RoomPlayer.Slot.CompareTo(b.RoomPlayer.Slot));
        blues.Sort((a, b) => a.RoomPlayer.Slot.CompareTo(b.RoomPlayer.Slot));
        for (int i = 0; i < reds.Count && i < blues.Count; i++)
        {
            byte group = (byte)(i + 1);
            (reds[i].Opponent, blues[i].Opponent, reds[i].Group, blues[i].Group) = (blues[i], reds[i], group, group);
            Pairs.Add((group, reds[i], blues[i]));
        }
    }

    public short SidePoints(int side)
    {
        int sum = 0;
        foreach (var p in Players) if (p.Side == side) sum += p.GuildPoints;
        return (short)Math.Min(sum, short.MaxValue);
    }

    /// <summary>
    /// p fechou o buraco idx: se o adversário já fechou, menos tacadas = 2 (empate 1/1); adversário fora do jogo = 2 para p.
    /// Sem decisão ainda (o adversário não chegou), nada.
    /// </summary>
    void ScoreGuildHole(MassPlayer p, int idx)
    {
        p.HoleDone[idx] = true;
        if (p.Opponent is not { } o) return;
        if (o.Left) p.GuildPoints += 2;
        else if (o.HoleDone[idx])
        {
            int c = p.Strokes[idx].CompareTo(o.Strokes[idx]);
            if (c < 0) p.GuildPoints += 2;
            else if (c > 0) o.GuildPoints += 2;
            else { p.GuildPoints++; o.GuildPoints++; }
        }
        else return;
        Output.GuildScore(p, SidePoints(0), SidePoints(1));
    }

    /// <summary>Saiu no meio: cada buraco que o adversário já fechou e ele não vale 2 para o adversário.</summary>
    void GuildLeft(MassPlayer p)
    {
        if (p.Opponent is not { Left: false } o) return;
        bool changed = false;
        for (int i = 0; i < HoleCount && i < o.HoleIndex; i++)
            if (o.HoleDone[i] && !p.HoleDone[i]) { o.GuildPoints += 2; p.HoleDone[i] = true; changed = true; }
        if (changed) Output.GuildScore(o, SidePoints(0), SidePoints(1));
    }

    GuildOutcome GuildResult()
    {
        bool[] active = new bool[2];
        long[] gamePang = new long[2];
        int left = 0;
        foreach (var p in Players)
        {
            if (p.Side < 0) continue;
            if (p.Left) left++; else active[p.Side] = true;
            gamePang[p.Side] += p.Pang;
        }
        int red = SidePoints(0), blue = SidePoints(1);
        int winner = active[0] && !active[1] ? 0 : active[1] && !active[0] ? 1
            : red != blue ? (red > blue ? 0 : 1)
            : gamePang[0] != gamePang[1] ? (gamePang[0] > gamePang[1] ? 0 : 1) : 2;
        var pangWin = new Dictionary<MassPlayer, int>();
        long[] sidePang = new long[2];
        foreach (var p in Players)
        {
            if (p.Side < 0) continue;
            int v = p.Side == winner ? (Pairs.Count + left) * 50 : left * 50 + 50;
            pangWin[p] = v;
            sidePang[p.Side] += v;
        }
        return new GuildOutcome(winner, [red, blue], sidePang, pangWin);
    }

    /// <summary>Troféu da sala (Match.iff), 0 fora do torneio individual.</summary>
    public int MatchTid { get; }
    public bool TrophiesCountBots { get; init; }

    public byte HoleOf(MassPlayer p) => HoleAt(p.HoleIndex);

    /// <summary>
    /// Classificação de quem terminou (sem quem saiu; bots só se TrophiesCountBots): menor placar, depois menos tacadas.
    /// As primeiras posições levam os troféus de Trophy.ByPosition e um item cada.
    /// </summary>
    protected override TourneyResult Results()
    {
        if (Pairs.Count > 0) return new TourneyResult(0, [], [], GuildResult());
        if (MatchTid == 0) return TourneyResult.Empty;
        var ranked = new List<MassPlayer>();
        foreach (var p in Players)
            if (p.Finished && !p.Left && (!p.IsBot || TrophiesCountBots)) ranked.Add(p);
        var order = new Dictionary<MassPlayer, int>(Players.Count);
        for (int i = 0; i < Players.Count; i++) order[Players[i]] = i;
        ranked.Sort((a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score)
            : a.Total != b.Total ? a.Total.CompareTo(b.Total) : order[a].CompareTo(order[b]));
        var kinds = Trophy.ByPosition(ranked.Count, HoleCount);
        var awards = new List<TourneyAward>(kinds.Length);
        for (int i = 0; i < kinds.Length && i < ranked.Count; i++)
            awards.Add(new TourneyAward(ranked[i], i, kinds[i], AwardItemBase + Rng.Next(AwardItemCount)));
        return new TourneyResult(MatchTid, awards, Medals(ranked));
    }

    /// <summary>
    /// Medalhas (só com 18+ jogadores, GB requestMakeMedal): sorte = sorteio; mais rápido = terminou primeiro; melhor
    /// drive, chip-in e putt longo = maiores valores do 0x31 do cliente (limitados); recuperação (18 buracos) = maior
    /// melhora dos 9 últimos sobre os 9 primeiros. Quem não fez bogey vem antes, como no GB. Sem valor (0) não há medalha.
    /// </summary>
    List<TourneyMedal> Medals(List<MassPlayer> eligible)
    {
        var medals = new List<TourneyMedal>();
        if (eligible.Count < Medal.MinPlayers) return medals;
        void Give(int slot, MassPlayer? p) { if (p != null) medals.Add(new TourneyMedal(p, slot, AwardItemBase + Rng.Next(AwardItemCount))); }
        Give(Medal.Lucky, eligible[Rng.Next(eligible.Count)]);
        Give(Medal.Speediest, Best(eligible, p => -(double)p.FinishedAt));
        Give(Medal.BestDrive, Best(eligible, p => p.BestDrive));
        Give(Medal.BestChipIn, Best(eligible, p => p.BestChipIn));
        Give(Medal.BestLongPutt, Best(eligible, p => p.BestLongPutt));
        if (HoleCount == 18) Give(Medal.BestRecovery, Best(eligible, p => Recovery(p)));
        return medals;
    }

    /// <summary>Quem tem o maior valor (0 = sem valor, fica de fora), preferindo quem não fez bogey.</summary>
    MassPlayer? Best(List<MassPlayer> list, Func<MassPlayer, double> value)
    {
        MassPlayer? best = null;
        bool bestGood = false;
        double bestValue = 0;
        foreach (var p in list)
        {
            double v = value(p);
            if (v == 0) continue;
            bool good = NoBogey(p);
            if (best == null || (good && !bestGood) || (good == bestGood && v > bestValue)) (best, bestGood, bestValue) = (p, good, v);
        }
        return best;
    }

    bool NoBogey(MassPlayer p)
    {
        for (int i = 0; i < HoleCount; i++)
            if (p.Strokes[i] > ParOf(HoleAt(i))) return false;
        return true;
    }

    /// <summary>Tacadas acima do par nos 9 primeiros menos nos 9 últimos (positivo = melhorou).</summary>
    int Recovery(MassPlayer p)
    {
        int first = 0, last = 0;
        for (int i = 0; i < 9; i++) first += p.Strokes[i] - ParOf(HoleAt(i));
        for (int i = 9; i < 18; i++) last += p.Strokes[i] - ParOf(HoleAt(i));
        return Math.Max(first - last, 0);
    }

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
        if (Pairs.Count > 0) ScoreGuildHole(p, p.HoleIndex);              // 0x6B primeiro, depois o 0xC0
        p.HoleIndex++;
        bool last = false;
        if (p.HoleIndex >= HoleCount)
        {
            p.Finished = true;
            p.FinishedAt = Stopwatch.GetTimestamp();
            last = AllDone();
            if (!last) Output.RivalState(p, 2);                     // o último sai no EndGame, depois do resultado
        }
        if (!p.IsBot) Output.NextHole(p);
        if (last) EndGame(p); else CheckEnd();
    }

    bool AllDone()
    {
        foreach (var p in Players)
            if (!p.Finished && !p.Left) return false;
        return true;
    }

    void CheckEnd()
    {
        if (AllDone()) EndGame();
    }

    public override void PlayerLeft(RoomPlayer rp)
    {
        var p = Find(rp.Guid);
        if (p == null || p.Left) return;
        p.Left = true;
        if (Over) return;
        Output.RivalState(p, 3);
        if (Pairs.Count > 0) GuildLeft(p);
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
