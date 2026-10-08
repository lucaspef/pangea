using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Game;
using Pangya.Domain.Players;
using Pangya.Domain.Ranking;

namespace Pangya.Protocol.KR645.Ranking;

/// <summary>Serviços do ranking (um por processo): o retrato, quem está jogando e o endereço anunciado no 0xA0.</summary>
public sealed class RankingContext(RankingService ranking, Func<long, IGameSession?> findGame, IPlayerStore players, string address, int port)
{
    public RankingService Ranking { get; } = ranking;
    public IPlayerStore Players { get; } = players;
    /// <summary>IP numérico e porta que o game manda no 0xA0 (o cliente usa inet_addr).</summary>
    public string Address { get; } = address;
    public int Port { get; set; } = port;
    public IGameSession? FindGame(long uid) => findGame(uid);
}

/// <summary>
/// Servidor de ranking do cliente 645 (docs/protocolo/SPEC-ranking.md): hello cru 0x1F4; pedidos 0x00 (página + minha
/// posição) -> 0x1F5, 0x01 (ficha) -> 0x1F6, 0x02 (busca) -> 0x1F8; 0x1F7 enquanto o retrato é recalculado. Sem login:
/// todo pedido traz uid e login, conferidos contra a sessão de jogo (mesmo nick/login e IP). O servidor nunca fecha a
/// conexão por conta própria (o cliente cairia para o lobby), só em pacote inválido.
/// </summary>
public sealed class RankingHandler(Connection conn, RankingContext ctx) : IConnectionHandler
{
    const ushort CPage = 0x00, CUser = 0x01, CSearch = 0x02;
    const ushort SHello = 0x1F4, SPage = 0x1F5, SUser = 0x1F6, SBusy = 0x1F7, SSearch = 0x1F8;
    const int FirstRequestTimeout = 30, UserInfoSize = 0x22C;
    const byte MyRowFollows = 0, NotRanked = 1, NoMyRow = 2;

    long uid;

    public ValueTask OnConnectedAsync()
    {
        conn.ParseKey = Random.Shared.Next(16);
        conn.IdleTimeoutSeconds = FirstRequestTimeout;
        conn.SendRaw(new PacketWriter(SHello).U32((uint)conn.ParseKey).U8(5).Str("645"));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnDisconnectedAsync() => ValueTask.CompletedTask;

    public async ValueTask OnPacketAsync(PacketReader p)
    {
        switch (p.Id)
        {
            case CPage:
            {
                uint who = p.U32();
                string login = p.Str(22);
                byte type = p.U8(), sub = p.U8(), term = p.U8(), cls = p.U8();
                uint page = p.U32();
                bool withMe = p.U8() != 0;
                if (!Check(who, login)) { conn.Send(Header(1, type, sub, term, cls, 0, 1)); return; }
                Page(type, sub, term, cls, page, withMe);
                return;
            }
            case CUser:
            {
                uint target = p.U32();
                p.Str(22);
                p.U8();
                if (uid == 0) { conn.Send(new PacketWriter(SUser).U8(2)); return; }
                await UserAsync(target);
                return;
            }
            case CSearch:
            {
                byte kind = p.U8();
                string nick = "";
                uint position = 0;
                if (kind == 0) nick = p.Str(22); else position = p.U32();
                byte type = p.U8(), sub = p.U8(), term = p.U8(), cls = p.U8();
                p.U32();
                if (uid == 0) { conn.Send(new PacketWriter(SSearch).U8(1)); return; }
                Search(kind, nick, position, type, sub, term, cls);
                return;
            }
            default:
                Log.Info($"{conn} RANK: pacote 0x{p.Id:X4} sem tratamento ({p.Remaining} bytes)");
                p.Skip(p.Remaining);
                return;
        }
    }

    /// <summary>uid + login do pedido batem com uma sessão de jogo deste processo (mesmo IP). Fixa o uid da conexão.</summary>
    bool Check(uint who, string login)
    {
        if (uid != 0) return who == uid;
        var game = ctx.FindGame(who);
        if (game == null || !string.Equals(game.Player.Login, login, StringComparison.OrdinalIgnoreCase)
            || (game.RemoteAddress is { } ip && !ip.Equals(conn.Remote.Address)))
        {
            Log.Info($"{conn} RANK: pedido recusado uid={who} login={login}");
            return false;
        }
        uid = who;
        conn.IdleTimeoutSeconds = 0;                                           // sem heartbeat no ranking
        Log.Info($"{conn} RANK: {game.Player.Nickname} ({who}) abriu o ranking");
        return true;
    }

    static PacketWriter Header(byte result, byte type, byte sub, byte term, byte cls, uint page, uint pages) =>
        new PacketWriter(SPage, 64).U8(result).U8(type).U8(sub).U8(term).U8(cls).U32(page).U32(pages);

    /// <summary>sListInfo: u32 uid, u32 atual, u32 anterior, i32 valor, u16 nível (0-based), u8 classe, str id, str nick.</summary>
    static void Row(PacketWriter w, RankEntry e, RankPlayer p, byte cls) =>
        w.U32((uint)e.Uid).U32((uint)e.Position).U32((uint)e.Previous).I32(e.Value).U16((ushort)Math.Clamp(p.Level, 0, 255)).U8(cls)
            .Str(p.Nickname).Str(p.Nickname);                                // no lugar do login vai o nick: o login não sai do servidor

    /// <summary>
    /// 0x00 -> 0x1F5: cabeçalho sempre (o cliente aplica antes de olhar o código); página além do fim = código 2 com a
    /// última página válida; tabela vazia = 1.
    /// </summary>
    void Page(byte type, byte sub, byte term, byte cls, uint page, bool withMe)
    {
        if (ctx.Ranking.Refreshing || ctx.Ranking.Current is not { } snap) { conn.Send(new PacketWriter(SBusy)); return; }
        var board = snap.Board(type, sub, cls);
        int pages = RankingService.Pages(board);
        if (board == null || board.Entries.Count == 0) { conn.Send(Header(1, type, sub, term, cls, 0, 1)); return; }
        if (page >= pages) { conn.Send(Header(2, type, sub, term, cls, (uint)(pages - 1), (uint)pages)); return; }
        int from = (int)page * RankingService.PageSize, n = Math.Min(RankingService.PageSize, board.Entries.Count - from);
        var w = Header(0, type, sub, term, cls, page, (uint)pages).U16((ushort)n);
        for (int i = 0; i < n; i++)
        {
            var e = board.Entries[from + i];
            Row(w, e, snap.Players[e.Uid], cls);
        }
        if (!withMe) w.U8(NoMyRow);
        else if (board.IndexOf.TryGetValue(uid, out int mine)) Row(w.U8(MyRowFollows), board.Entries[mine], snap.Players[uid], cls);
        else w.U8(NotRanked);
        conn.Send(w);
    }

    /// <summary>0x01 -> 0x1F6: ficha de 0x22C bytes (personagem atual + as 5 posições do ranking geral, classe "todos").</summary>
    async Task UserAsync(uint target)
    {
        if (ctx.Ranking.Refreshing || ctx.Ranking.Current is not { } snap) { conn.Send(new PacketWriter(SBusy)); return; }
        if (!snap.Players.TryGetValue(target, out var rp)) { conn.Send(new PacketWriter(SUser).U8(1)); return; }
        var p = await ctx.Players.LoadAsync(target);
        if (p == null) { conn.Send(new PacketWriter(SUser).U8(2)); return; }
        var w = new PacketWriter(SUser, UserInfoSize + 8).U8(0).U32(target).Fixed(rp.Nickname, 22).Fixed(rp.Nickname, 22)
            .U8((byte)Math.Clamp(rp.Level, 0, 255)).U8(0);
        w.Struct(p.Character is { } ch ? Game.PlayerStructs.Character(ch) : default(sCharacterInfo)).U16(0);
        foreach (int sub in UserLines)
        {
            var b = snap.Board(0, sub, 0);
            if (b != null && b.IndexOf.TryGetValue(target, out int i)) { var e = b.Entries[i]; w.U32((uint)e.Position).U32((uint)e.Previous).I32(e.Value); }
            else w.U32(0).U32(0).I32(0);
        }
        conn.Send(w);
    }

    /// <summary>Linhas da ficha: 종합, 스코어, 트로피, 획득팡, 플레이홀 (subtipos do ranking geral, ordem do GB).</summary>
    static readonly int[] UserLines = [0, 1, 2, 3, 4];

    /// <summary>0x02 -> 0x1F8: busca por nick (sem diferenciar caixa) ou posição, na tabela pedida; devolve a página e o índice.</summary>
    void Search(byte kind, string nick, uint position, byte type, byte sub, byte term, byte cls)
    {
        if (ctx.Ranking.Refreshing || ctx.Ranking.Current is not { } snap) { conn.Send(new PacketWriter(SBusy)); return; }
        var board = snap.Board(type, sub, cls);
        int index = -1;
        if (board != null)
        {
            if (kind == 0) { if (snap.ByNick.TryGetValue(nick.Trim(), out long who) && board.IndexOf.TryGetValue(who, out int i)) index = i; }
            else if (position >= 1 && position <= board.Entries.Count) index = (int)position - 1;
        }
        if (index < 0) { conn.Send(new PacketWriter(SSearch).U8(1)); return; }
        int page = index / RankingService.PageSize, from = page * RankingService.PageSize;
        int n = Math.Min(RankingService.PageSize, board!.Entries.Count - from);
        var w = new PacketWriter(SSearch, 64 + n * 80).U8(0).U8(type).U8(sub).U8(term).U8(cls).U32((uint)page).U32((uint)RankingService.Pages(board))
            .U16((ushort)n);
        for (int i = 0; i < n; i++)
        {
            var e = board.Entries[from + i];
            Row(w, e, snap.Players[e.Uid], cls);
        }
        conn.Send(w.U16((ushort)(index - from)));
    }
}
