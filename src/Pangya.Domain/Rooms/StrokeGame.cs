namespace Pangya.Domain.Rooms;

/// <summary>Resultado de uma tacada como o cliente reporta (já decifrado pela camada de protocolo).</summary>
public readonly record struct ShotResult(uint Guid, float X, float Y, float Z, byte State, uint Pang, uint BonusPang)
{
    public const byte StateWaterOrOut = 3, StateHoled = 4;
}

/// <summary>Linha do placar final.</summary>
public readonly record struct GameResult(uint Guid, int Rank, int ScoreVsPar, int TotalStrokes, uint Pang, uint BonusPang);

/// <summary>O que a partida manda para os clientes (a camada de protocolo transforma em pacotes).</summary>
public interface IGameOutput
{
    void Wind(byte wind, byte direction);
    void HoleStart(GamePlayer first);
    void TeeReady();
    void NextTurn(GamePlayer p);
    void NextHole();
    void GameEnd(List<GameResult> results);
    void PlayerLeft(GamePlayer p);
    /// <summary>Vez do bot: a camada de protocolo monta e manda a tacada e chama <see cref="StrokeGame.BotShoot"/>.</summary>
    void BotTurn(GamePlayer bot);
}

/// <summary>Estado de um participante na partida.</summary>
public sealed class GamePlayer(RoomPlayer rp, int order)
{
    public RoomPlayer RoomPlayer { get; } = rp;
    /// <summary>Posição na ordem dos slots (desempate da ordem do tee).</summary>
    public int Order { get; } = order;
    public uint Guid => RoomPlayer.Guid;
    public bool IsBot => RoomPlayer.IsBot;
    public int[] Strokes { get; } = new int[18];
    public bool Done { get; set; }
    public bool Holed { get; set; }
    public bool Left { get; set; }
    /// <summary>Bola no buraco atual; HasPos = false enquanto está no tee.</summary>
    public bool HasPos { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public uint Pang { get; set; }
    public uint Bonus { get; set; }

    public int TotalStrokes(int holes)
    {
        int t = 0;
        for (int i = 0; i < holes; i++) t += Strokes[i];
        return t;
    }
}

/// <summary>Dados de um buraco que o cliente manda ao carregar (par, tee e bandeira).</summary>
public readonly record struct HoleInfo(byte Par, float TeeX, float TeeZ, float PinX, float PinZ);

/// <summary>
/// Partida Stroke (porte do ingame.py validado com o cliente real; docs/protocolo/SPEC-ingame.md).
/// A física e o julgamento da tacada ficam no cliente; o servidor controla turnos, buracos e placar:
/// - o buraco começa quando todos os humanos carregaram: vento + primeiro jogador;
/// - durante o buraco joga quem está mais longe da bandeira (quem ainda está no tee joga antes, na ordem do tee);
/// - o buraco acaba para o jogador quando ele acerta (estado 4) ou chega a par + 4 tacadas.
/// Todos os métodos são chamados sob o lock <see cref="RoomManager.Sync"/>.
/// </summary>
public sealed class StrokeGame
{
    public const int GiveUpOverPar = 4;

    readonly Room room;
    readonly IGameOutput output;
    readonly object sync;
    readonly TimeSpan botDelay, teeFallback;
    readonly CancellationTokenSource cts = new();
    readonly Dictionary<uint, GamePlayer> byGuid = [];
    readonly HashSet<uint> loaded = [], teeReady = [], synced = [];
    readonly Random rng = new();
    int teeAcked = -1;
    bool resultSent;

    /// <summary>Participantes na ordem dos slots (no máximo 4: o cliente tem m_userInfo[4]).</summary>
    public List<GamePlayer> Players { get; } = [];
    public IGameOutput Output => output;
    public int HoleCount { get; }
    public int HoleIndex { get; private set; }
    public byte Hole => HoleIndex < room.HoleOrder.Length ? room.HoleOrder[HoleIndex] : (byte)(HoleIndex + 1);
    public Dictionary<byte, HoleInfo> Holes { get; } = [];
    public GamePlayer? Turn { get; private set; }
    public bool ShotOpen { get; private set; }
    public bool Started { get; private set; }
    public bool Over { get; private set; }

    public StrokeGame(Room room, IGameOutput output, object sync, TimeSpan botDelay, TimeSpan teeFallback)
    {
        this.room = room;
        this.output = output;
        this.sync = sync;
        this.botDelay = botDelay;
        this.teeFallback = teeFallback;
        for (int i = 0; i < room.Players.Count && i < 4; i++)
        {
            var gp = new GamePlayer(room.Players[i], i);
            Players.Add(gp);
            byGuid[gp.Guid] = gp;
        }
        HoleCount = Math.Clamp((int)room.Settings.Holes, 1, 18);
    }

    public GamePlayer? Find(uint guid) => byGuid.GetValueOrDefault(guid);

    bool HasHumans()
    {
        foreach (var p in Players)
            if (!p.IsBot && !p.Left) return true;
        return false;
    }

    bool AllHumansIn(HashSet<uint> set)
    {
        bool any = false;
        foreach (var p in Players)
        {
            if (p.IsBot || p.Left) continue;
            if (!set.Contains(p.Guid)) return false;
            any = true;
        }
        return any;
    }

    int ParOf(byte hole) => Holes.TryGetValue(hole, out var h) ? h.Par : 4;

    // ------------------------------------------------------------------ entradas (pacotes do cliente)

    public void HoleData(byte hole, HoleInfo info) => Holes[hole] = info;

    public void Loaded(GamePlayer p)
    {
        loaded.Add(p.Guid);
        TryStartHole();
    }

    public void TeeShotReady(GamePlayer p)
    {
        teeReady.Add(p.Guid);
        if (AllHumansIn(teeReady) && teeAcked != HoleIndex)
        {
            teeAcked = HoleIndex;
            output.TeeReady();
            MaybeBot();
        }
    }

    /// <summary>Tacada de um humano. false = repetida (ignorar). Quem tacou passa a ser o da vez.</summary>
    public bool Shoot(GamePlayer p)
    {
        if (Over || (ShotOpen && Turn == p)) return false;
        Turn = p;
        BeginShot(p);
        return true;
    }

    /// <summary>Resultado da tacada (todos os clientes mandam; vale o primeiro, do jogador da vez).</summary>
    public bool Result(ShotResult r)
    {
        var p = Find(r.Guid);
        if (p == null || resultSent || !ShotOpen || p != Turn) return false;
        resultSent = true;
        p.HasPos = true;
        p.X = r.X;
        p.Z = r.Z;
        p.Pang = r.Pang;
        p.Bonus = r.BonusPang;
        if (r.State == ShotResult.StateHoled) p.Holed = p.Done = true;
        else if (r.State == ShotResult.StateWaterOrOut) p.Strokes[HoleIndex]++;         // água/OB: +1 tacada
        if (!p.Done && p.Strokes[HoleIndex] >= ParOf(Hole) + GiveUpOverPar) p.Done = true;
        return true;
    }

    /// <summary>Um cliente terminou de mostrar a tacada; quando todos terminam, passa a vez.</summary>
    public void ShotFinished(GamePlayer p)
    {
        synced.Add(p.Guid);
        TryCloseShot();
    }

    void TryCloseShot()
    {
        if (!ShotOpen || !AllHumansIn(synced)) return;
        if (!resultSent && Turn is { } t && t.Strokes[HoleIndex] >= ParOf(Hole) + GiveUpOverPar) t.Done = true;
        ShotOpen = false;
        Advance();
    }

    /// <summary>É a vez do jogador e não há tacada em andamento (ex.: para usar item).</summary>
    public bool IsTurnOf(GamePlayer p) => Turn == p && !ShotOpen && !Over;

    public void PlayerLeft(RoomPlayer rp)
    {
        var p = Find(rp.Guid);
        if (p == null || p.Left) return;
        p.Left = p.Done = true;
        if (Over) return;
        output.PlayerLeft(p);
        if (!HasHumans()) { Cancel(); return; }
        if (!Started) TryStartHole();
        else if (ShotOpen) TryCloseShot();      // quem saiu não precisa mais confirmar
        else if (Turn == p) Advance();
    }

    public void Cancel()
    {
        Over = true;
        cts.Cancel();
    }

    // ------------------------------------------------------------------ fluxo

    void TryStartHole()
    {
        if (Started || Over || !AllHumansIn(loaded)) return;
        Started = true;
        foreach (var p in Players) { p.Done = p.Left; p.Holed = false; p.HasPos = false; }
        teeReady.Clear();
        synced.Clear();
        ShotOpen = false;
        Turn = NextPlayer();
        if (Turn == null) { Finish(); return; }
        NewWind();
        output.HoleStart(Turn);
        int idx = HoleIndex;
        Later(teeFallback, () =>
        {
            // um cliente que não manda o "pronto para o tee" não pode travar a partida
            if (HoleIndex != idx || teeAcked == idx || !Started) return;
            teeAcked = idx;
            output.TeeReady();
            MaybeBot();
        });
    }

    void BeginShot(GamePlayer p)
    {
        ShotOpen = true;
        resultSent = false;
        synced.Clear();
        p.Strokes[HoleIndex]++;
    }

    void NewWind() => output.Wind((byte)rng.Next(9), (byte)rng.Next(256));

    void Advance()
    {
        if (Over) return;
        var next = NextPlayer();
        if (next != null)
        {
            Turn = next;
            NewWind();
            output.NextTurn(next);
            MaybeBot();
            return;
        }
        if (HoleIndex + 1 >= HoleCount) { Finish(); return; }
        HoleIndex++;
        loaded.Clear();
        Started = false;
        Turn = null;
        output.NextHole();     // o cliente mostra o placar, manda os dados do próximo buraco e o "carregado" de novo
    }

    GamePlayer? NextPlayer()
    {
        // 1) alguém ainda no tee: o primeiro na ordem do tee
        //    (1º buraco = ordem dos slots; depois, menos tacadas no buraco anterior primeiro)
        GamePlayer? tee = null;
        int prev = HoleIndex - 1;
        foreach (var p in Players)
        {
            if (p.Left || p.Done || p.HasPos) continue;
            if (tee == null || (prev >= 0 && p.Strokes[prev] < tee.Strokes[prev])) tee = p;
        }
        if (tee != null) return tee;
        // 2) todos já tacaram: stroke = joga quem está mais longe da bandeira
        GamePlayer? far = null;
        float farDist = -1;
        bool hasPin = Holes.TryGetValue(Hole, out var h);
        foreach (var p in Players)
        {
            if (p.Left || p.Done) continue;
            float d = hasPin ? (p.X - h.PinX) * (p.X - h.PinX) + (p.Z - h.PinZ) * (p.Z - h.PinZ) : -p.TotalStrokes(18);
            if (far == null || d > farDist) { far = p; farDist = d; }
        }
        return far;
    }

    public int Score(GamePlayer p)
    {
        int s = 0;
        for (int i = 0; i < Math.Min(HoleIndex + 1, HoleCount); i++)
            s += p.Strokes[i] - ParOf(i < room.HoleOrder.Length ? room.HoleOrder[i] : (byte)(i + 1));
        return s;
    }

    void Finish()
    {
        if (Over) return;
        Cancel();
        // ranking: quem saiu por último; depois menor placar; empate = ordem do slot
        var ranked = new List<GamePlayer>(Players);
        ranked.Sort((a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left)
            : Score(a) != Score(b) ? Score(a).CompareTo(Score(b)) : a.Order.CompareTo(b.Order));
        var results = new List<GameResult>(Players.Count);
        foreach (var p in Players)
            results.Add(new GameResult(p.Guid, ranked.IndexOf(p) + 1, Score(p), p.TotalStrokes(HoleCount), p.Pang, p.Bonus));
        output.GameEnd(results);
        RoomManager.FinishGame(room);
    }

    // ------------------------------------------------------------------ bot

    void MaybeBot()
    {
        if (Turn is not { IsBot: true } bot || ShotOpen || Over) return;
        Later(botDelay, () =>
        {
            if (Turn != bot || ShotOpen || !HasHumans()) return;
            output.BotTurn(bot);
        });
    }

    /// <summary>A camada de protocolo mandou a tacada do bot (ou o estouro de tempo dele).</summary>
    public void BotShoot(GamePlayer bot) => BeginShot(bot);

    void Later(TimeSpan delay, Action action)
    {
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delay, token); }
            catch (OperationCanceledException) { return; }
            lock (sync)
                if (!Over) action();
        });
    }
}
