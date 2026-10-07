namespace Pangya.Domain.Rooms;

/// <summary>
/// Dois lados buraco a buraco (docs/protocolo/SPEC-modes.md §3-4; cliente SetHoleInPose_Team @00455320 / _Match @00454a40).
/// Team (modo 1): lado = time da sala, UMA bola por lado, os membros alternam as tacadas. Match (modo 3): cada jogador
/// é um lado. O buraco é decidido quando: os dois lados acertaram/desistiram (menos tacadas ganha, empate = dividido);
/// um lado acertou em S e o outro já tem T &gt;= S; ou T == S-1 e o lado que acertou já não pode ser alcançado.
/// O placar são os buracos ganhos; acaba no último buraco ou quando a vantagem passa dos buracos restantes.
/// </summary>
public sealed class SideGame : StrokeGame
{
    readonly bool perPlayer;
    readonly Dictionary<uint, int> side = [];
    readonly int[] won = new int[2];
    readonly int[] teeShooter = new int[2];
    readonly bool[] holed = new bool[2], gaveUp = new bool[2], hasPos = new bool[2];
    readonly float[] posX = new float[2], posZ = new float[2];
    readonly GamePlayer?[] last = new GamePlayer?[2];
    int firstSide, scored = -1;

    public SideGame(Room room, IGameOutput output, object sync, TimeSpan botDelay, TimeSpan teeFallback, bool perPlayer)
        : base(room, output, sync, botDelay, teeFallback)
    {
        this.perPlayer = perPlayer;
        bool split = true;
        for (int i = 0; i < Players.Count; i++)
        {
            int s = perPlayer ? i % 2 : Players[i].RoomPlayer.Team & 1;
            side[Players[i].Guid] = s;
            if (i > 0 && s != side[Players[0].Guid]) split = false;
        }
        if (split && Players.Count > 1)                  // todos no mesmo time: divide pela ordem dos slots
            for (int i = 0; i < Players.Count; i++) side[Players[i].Guid] = i % 2;
        firstSide = Players.Count > 0 ? side[Players[0].Guid] : 0;
    }

    public int SideOf(GamePlayer p) => side[p.Guid];
    public int HolesWon(int s) => won[s];

    List<GamePlayer> Members(int s, bool active)
    {
        var list = new List<GamePlayer>();
        foreach (var p in Players)
            if (side[p.Guid] == s && (!active || !p.Left)) list.Add(p);
        return list;
    }

    int SideStrokes(int s, int hole)
    {
        int t = 0;
        foreach (var p in Players)
            if (side[p.Guid] == s) t += p.Strokes[hole];
        return t;
    }

    bool Finished(int s) => holed[s] || gaveUp[s] || Members(s, true).Count == 0;
    int Remaining => HoleCount - (HoleIndex + 1);

    protected override void OnHoleStart()
    {
        for (int s = 0; s < 2; s++) { holed[s] = gaveUp[s] = hasPos[s] = false; last[s] = null; }
    }

    protected override void OnResult(GamePlayer p, ShotResult r)
    {
        int s = side[p.Guid];
        hasPos[s] = true;
        posX[s] = r.X;
        posZ[s] = r.Z;
        last[s] = p;
        if (r.State == ShotResult.StateHoled) holed[s] = true;
        Refresh();
    }

    /// <summary>Desistência por lado e o estado de cada jogador (posição/acertou/pronto) a partir do lado dele.</summary>
    void Refresh()
    {
        int par = ParOf(Hole);
        for (int s = 0; s < 2; s++)
            if (!holed[s] && SideStrokes(s, HoleIndex) >= par + GiveUpOverPar) gaveUp[s] = true;
        bool dec = Decided();
        foreach (var p in Players)
        {
            int s = side[p.Guid];
            p.HasPos = hasPos[s];
            p.X = posX[s];
            p.Z = posZ[s];
            p.Holed = holed[s];
            p.Done = p.Left || dec || Finished(s);
        }
    }

    bool Decided()
    {
        if (Finished(0) && Finished(1)) return true;
        for (int a = 0; a < 2; a++)
        {
            int b = 1 - a;
            if (!holed[a] || Finished(b)) continue;
            int S = SideStrokes(a, HoleIndex), T = SideStrokes(b, HoleIndex);
            if (T >= S || (T == S - 1 && won[a] > won[b] + Remaining)) return true;
        }
        return false;
    }

    /// <summary>0/1 = lado vencedor do buraco, 2 = dividido.</summary>
    int HoleWinnerSide()
    {
        int s0 = SideStrokes(0, HoleIndex), s1 = SideStrokes(1, HoleIndex);
        if (holed[0] && holed[1]) return s0 == s1 ? 2 : s0 < s1 ? 0 : 1;
        if (holed[0] != holed[1]) return holed[0] ? 0 : 1;
        return 2;
    }

    protected override GamePlayer? NextPlayer()
    {
        if (Decided()) return null;
        int s = -1;
        foreach (var c in new[] { firstSide, 1 - firstSide })      // primeiro quem ainda está no tee
            if (!Finished(c) && !hasPos[c]) { s = c; break; }
        if (s < 0)
        {
            bool open0 = !Finished(firstSide), open1 = !Finished(1 - firstSide);
            if (!open0 && !open1) return null;
            if (open0 && open1)                                       // os dois jogando: o mais longe da bandeira
                s = PinDistance2(posX[firstSide], posZ[firstSide]) >= PinDistance2(posX[1 - firstSide], posZ[1 - firstSide])
                    ? firstSide : 1 - firstSide;
            else s = open0 ? firstSide : 1 - firstSide;
        }
        var mem = Members(s, true);
        if (mem.Count == 0) return null;
        int i = last[s] is { } l ? mem.IndexOf(l) : -1;               // tacadas alternadas dentro do lado
        return i < 0 ? mem[teeShooter[s] % mem.Count] : mem[(i + 1) % mem.Count];
    }

    protected override bool OnTurnOver()
    {
        Refresh();
        if (!Decided() || scored == HoleIndex) return false;
        scored = HoleIndex;
        int w = HoleWinnerSide();
        if (w < 2) won[w]++;
        int s0 = SideStrokes(0, HoleIndex), s1 = SideStrokes(1, HoleIndex);
        if (s0 != s1) firstSide = s0 < s1 ? 0 : 1;                    // quem fez menos tacadas sai primeiro no próximo
        teeShooter[0]++;
        teeShooter[1]++;
        if (Math.Abs(won[0] - won[1]) > Remaining && Remaining > 0) { Finish(); return true; }
        return false;
    }

    protected override GameEnd BuildEnd()
    {
        int win = won[0] == won[1] ? 2 : won[0] > won[1] ? 0 : 1;
        var results = new List<GameResult>(Players.Count);
        foreach (var p in Players)
        {
            int s = side[p.Guid], total = 0;
            for (int h = 0; h < Math.Min(HoleIndex + 1, HoleCount); h++) total += SideStrokes(s, h);
            results.Add(new GameResult(p.Guid, win == 2 || win == s ? 1 : 2, won[s], total, p.Pang, p.Bonus));
        }
        return new GameEnd { Kind = perPlayer ? GameEndKind.Match : GameEndKind.Team, Results = results, SideWins = [won[0], won[1]], Winner = win };
    }
}

/// <summary>
/// Pang Battle (skins, modo 7; SPEC-modes.md §5): ordem do stroke; cada buraco vale GetHolePang (20 × (1 + 0,5 × ((n-1)/3)),
/// último buraco × 2) × 2^acumulado (máx. 8). O vencedor é o único com menos tacadas entre os que acertaram; se ninguém
/// (ou empate), o prêmio acumula. O servidor diz o vencedor no 0x63; o cliente move o pang sozinho.
/// </summary>
public sealed class SkinsGame(Room room, IGameOutput output, object sync, TimeSpan botDelay, TimeSpan teeFallback)
    : StrokeGame(room, output, sync, botDelay, teeFallback)
{
    int carry, scored = -1;
    readonly Dictionary<uint, long> net = [];

    public long Net(GamePlayer p) => net.GetValueOrDefault(p.Guid);

    public int HolePang(int seq)
    {
        double v = (1 + 0.5 * ((seq - 1) / 3)) * 20.0;
        if (seq == HoleCount) v *= 2;
        return (int)Math.Round(v);
    }

    protected override bool OnTurnOver()
    {
        if (NextPlayer() == null && scored != HoleIndex)
        {
            scored = HoleIndex;
            ScoreHole();
        }
        return false;
    }

    void ScoreHole()
    {
        int best = int.MaxValue, count = 0;
        GamePlayer? winner = null;
        foreach (var p in Players)
        {
            if (p.Left || !p.Holed) continue;
            int st = p.Strokes[HoleIndex];
            if (st < best) { best = st; winner = p; count = 1; }
            else if (st == best) count++;
        }
        long stake = HolePang(HoleIndex + 1) * Math.Min(8, 1 << Math.Min(carry, 3));
        if (winner != null && count == 1)
        {
            foreach (var p in Players)
                if (p != winner)
                {
                    net[p.Guid] = net.GetValueOrDefault(p.Guid) - stake;
                    net[winner.Guid] = net.GetValueOrDefault(winner.Guid) + stake;
                }
            HoleWinner = winner.Guid;
            carry = 0;
        }
        else
        {
            HoleWinner = 0xFFFFFFFF;
            carry++;
        }
    }

    protected override GameEnd BuildEnd()
    {
        var ranked = new List<GamePlayer>(Players);
        ranked.Sort((a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left)
            : Net(a) != Net(b) ? Net(b).CompareTo(Net(a)) : Score(a).CompareTo(Score(b)));
        var results = new List<GameResult>(Players.Count);
        foreach (var p in Players)
            results.Add(new GameResult(p.Guid, ranked.IndexOf(p) + 1, Score(p), p.TotalStrokes(HoleCount), p.Pang, p.Bonus, Net(p)));
        return new GameEnd
        {
            Kind = GameEndKind.PangBattle, Results = results, LastHoleWinner = HoleWinner,
            OverallWinner = ranked.Count > 0 ? ranked[0].Guid : 0xFFFFFFFF,
        };
    }
}
