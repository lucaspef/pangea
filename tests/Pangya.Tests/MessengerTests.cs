using Pangya.Core.Net;
using Pangya.Core.Text;
using Pangya.Domain.Accounts;
using Pangya.Domain.Messenger;
using Pangya.Messenger;
using Pangya.Protocol.KR645;

namespace Pangya.Tests;

/// <summary>Regras de amizade sem rede.</summary>
[Collection("db")]
public class FriendServiceTests(DbFixture fx)
{
    [Fact]
    public async Task RequestAcceptBlockAndRemove()
    {
        _ = fx;
        await using var env = await GameEnv.StartAsync();
        var (a, _) = await env.NewPlayerAsync();
        var (b, _) = await env.NewPlayerAsync();
        var svc = new FriendService(env.S.Friends, maxFriends: 1);
        Assert.Equal((FriendCode.Ok, b.Id), ((await svc.LookupAsync(a.Id, b.Nickname!.ToUpperInvariant())).Code, (await svc.LookupAsync(a.Id, b.Nickname!)).Id));
        Assert.Equal(FriendCode.Myself, (await svc.LookupAsync(a.Id, a.Nickname!)).Code);
        Assert.Equal(FriendCode.NoSuchNick, await svc.RequestAsync(a.Id, b.Id, a.Nickname!));     // uid e nick não batem
        Assert.Equal(FriendCode.Ok, await svc.RequestAsync(a.Id, b.Id, b.Nickname!));
        Assert.Equal(FriendCode.AlreadyRequested, await svc.RequestAsync(a.Id, b.Id, b.Nickname!));
        Assert.Equal(FriendCode.NotFriend, await svc.AcceptAsync(a.Id, b.Id));                  // quem pediu não aceita
        var (c, _) = await env.NewPlayerAsync();
        Assert.Equal(FriendCode.MyListFull, await svc.RequestAsync(a.Id, c.Id, c.Nickname!));   // limite 1
        Assert.Equal(FriendCode.Ok, await svc.AcceptAsync(b.Id, a.Id));
        Assert.Equal(FriendCode.AlreadyFriend, await svc.RequestAsync(b.Id, a.Id, a.Nickname!));
        Assert.Equal(FriendCode.Ok, await svc.BlockAsync(b.Id, a.Id, true));
        var seenByA = (await env.S.Friends.GetAsync(a.Id, b.Id))!;
        Assert.Equal((FriendState.Accepted, false, true), (seenByA.State, seenByA.Blocked, seenByA.BlockedMe));
        var (code, alias) = await svc.AliasAsync(a.Id, b.Id, "  amigo de longa data  ");
        Assert.Equal((FriendCode.Ok, "amigo de l"), (code, alias));                              // 10 caracteres
        Assert.Equal(FriendCode.Ok, await svc.RemoveAsync(b.Id, a.Id));
        Assert.Null(await env.S.Friends.GetAsync(a.Id, b.Id));                                  // os dois lados somem
        Assert.Equal(FriendCode.NotFriend, await svc.RemoveAsync(a.Id, b.Id));
    }
}

/// <summary>Mensageiro pela rede (SPEC-messenger.md), com o game server do mesmo processo.</summary>
[Collection("db")]
public class MessengerTests(DbFixture fx)
{
    sealed class Env : IAsyncDisposable
    {
        public required GameEnv Game { get; init; }
        public required MessengerServer Msn { get; init; }
        public required CancellationTokenSource Cts { get; init; }

        public static async Task<Env> StartAsync()
        {
            var game = await GameEnv.StartAsync();
            var msn = new MessengerServer(game.S, game.Game.World, 0);
            game.Game.Context.Messenger = msn.Context;
            var cts = new CancellationTokenSource();
            _ = msn.Tcp.StartAsync(cts.Token);
            await Task.Yield();
            return new Env { Game = game, Msn = msn, Cts = cts };
        }

        public async ValueTask DisposeAsync()
        {
            Cts.Cancel();
            await Game.DisposeAsync();
        }
    }

    /// <summary>Jogador no game (já no lobby) e conectado ao mensageiro (hello lido).</summary>
    static async Task<(Account Acc, TestClient Game, TestClient Msn)> PlayerAsync(Env env, bool login = true)
    {
        var (acc, key) = await env.Game.NewPlayerAsync();
        var g = await env.Game.ConnectAsync();
        await GameEnv.SendLoginAsync(g, acc, key);
        await g.ExpectAsync(0x94);
        var m = await TestClient.ConnectAsync(env.Msn.Tcp.Port);
        var (id, hello) = await m.ReceiveAsync();
        Assert.Equal(0x2C, id);
        hello.U8(); hello.U8();
        m.Key = (int)hello.U32();
        if (login)
        {
            await m.SendAsync(new PacketWriter(0x12).U32((uint)acc.Id).Str(acc.Nickname!));
            var r = await m.ExpectAsync(0x2D);
            Assert.Equal((0, (uint)acc.Id), (r.U8(), r.U32()));
            await m.SendAsync(new PacketWriter(0x23).Zeros(0x4B));
            await m.SendAsync(new PacketWriter(0x14));
            var list = await Sub(m, 0x102);
            Assert.Equal(1, list.U8());                                       // página 1
        }
        return (acc, g, m);
    }

    /// <summary>Próximo 0x2E com o sub-id pedido (o leitor já passou do sub).</summary>
    static async Task<PacketReader> Sub(TestClient c, ushort sub)
    {
        while (true)
        {
            var r = await c.ExpectAsync(0x2E);
            if (r.U16() == sub) return r;
        }
    }

    [Fact]
    public async Task LoginOnlyForWhoIsPlaying()
    {
        _ = fx;
        await using var env = await Env.StartAsync();
        var (acc, _, m) = await PlayerAsync(env, login: false);
        await m.SendAsync(new PacketWriter(0x12).U32((uint)acc.Id).Str("outro nick"));
        Assert.Equal(2, (await m.ExpectAsync(0x2D)).U8());
        Assert.True(await m.IsClosedByServerAsync());

        var (acc2, _) = await env.Game.NewPlayerAsync();                        // conta que não entrou no game
        var (_, _, m2) = await PlayerAsync(env, login: false);
        await m2.SendAsync(new PacketWriter(0x12).U32((uint)acc2.Id).Str(acc2.Nickname!));
        Assert.Equal(2, (await m2.ExpectAsync(0x2D)).U8());
    }

    [Fact]
    public async Task FriendshipChatStatusAndBlock()
    {
        _ = fx;
        await using var env = await Env.StartAsync();
        var (a, _, ma) = await PlayerAsync(env);
        var (b, _, mb) = await PlayerAsync(env);

        await ma.SendAsync(new PacketWriter(0x17).Str(b.Nickname!.ToLowerInvariant()));
        var look = await Sub(ma, 0x117);
        Assert.Equal((0u, b.Nickname, (uint)b.Id), (look.U32(), look.Str(), look.U32()));

        await ma.SendAsync(new PacketWriter(0x18).U32((uint)b.Id).Str(b.Nickname!));
        var req = await Sub(ma, 0x104);
        Assert.Equal(0u, req.U32());
        var fb = req.Struct<sFriend>();
        Assert.Equal(((uint)b.Id, 1u, 0u, 1u), (fb.Uid, fb.IsAgree, fb.IsAccept, fb.PangyaFriend));   // "(요청중)"
        var asked = (await Sub(mb, 0x106)).Struct<sFriend>();
        Assert.Equal(((uint)a.Id, a.Nickname, 0u, 0u, 1u), (asked.Uid, Cp949.Read(asked.NickName), asked.IsAgree, asked.IsAccept, asked.IsLogOn));
        await ma.SendAsync(new PacketWriter(0x18).U32((uint)b.Id).Str(b.Nickname!));
        Assert.Equal((uint)FriendCode.AlreadyRequested, (await Sub(ma, 0x104)).U32());

        await mb.SendAsync(new PacketWriter(0x19).U32((uint)a.Id));
        var acc = await Sub(mb, 0x109);
        Assert.Equal((0u, (uint)a.Id), (acc.U32(), acc.U32()));
        var they = await Sub(ma, 0x10A);
        Assert.Equal((0u, (uint)b.Id), (they.U32(), they.U32()));
        var pos = await Sub(ma, 0x123);                                         // onde B está, pela sessão de jogo
        Assert.Equal((0u, (uint)b.Id), (pos.U32(), pos.U32()));
        var where = pos.Struct<sUserPosition>();
        Assert.Equal(((ushort)0xFFFF, env.Game.S.Config.Game.Id), (where.iRoomIdx, where.iServerGUID));

        // lista nova de A: B online e aceito
        await ma.SendAsync(new PacketWriter(0x14));
        var list = await Sub(ma, 0x102);
        Assert.Equal((1, (ushort)1, (ushort)1), (list.U8(), list.U16(), list.U16()));
        var entry = list.Struct<sFriend>();
        Assert.Equal((1u, 1u, 1u, "Friend"), (entry.IsLogOn, entry.IsAccept, entry.IsAgree, Cp949.Read(entry.szAlias)));

        await ma.SendAsync(new PacketWriter(0x1E).U32((uint)b.Id).Str("oi, tudo bem?"));
        var chat = await Sub(mb, 0x113);
        Assert.Equal(((uint)a.Id, a.Nickname, "oi, tudo bem?", (byte)0), (chat.U32(), chat.Str(), chat.Str(), chat.U8()));

        await mb.SendAsync(new PacketWriter(0x1D).U8(3));                       // ocupado
        var st = await Sub(ma, 0x115);
        Assert.Equal((3u, (uint)b.Id), (st.U32(), st.U32()));

        await mb.SendAsync(new PacketWriter(0x1F).U32((uint)a.Id).Str("parceiro"));
        var al = await Sub(mb, 0x119);
        Assert.Equal((0u, (uint)a.Id, "parceiro"), (al.U32(), al.U32(), al.Str()));

        await mb.SendAsync(new PacketWriter(0x1A).U32((uint)a.Id));             // B bloqueia A: A vê B sair
        Assert.Equal(0u, (await Sub(mb, 0x10C)).U32());
        Assert.Equal((uint)b.Id, (await Sub(ma, 0x10F)).U32());
        await ma.SendAsync(new PacketWriter(0x1E).U32((uint)b.Id).Str("ei?"));
        var fail = await Sub(ma, 0x114);
        Assert.Equal((3, (uint)b.Id), (fail.U8(), fail.U32()));

        await mb.SendAsync(new PacketWriter(0x1B).U32((uint)a.Id));             // desbloqueia: B volta para A
        Assert.Equal(0u, (await Sub(mb, 0x10D)).U32());
        Assert.Equal((uint)b.Id, (await Sub(ma, 0x10E)).U32());

        await mb.SendAsync(new PacketWriter(0x16));                             // B sai: A recebe 0x10F
        Assert.Equal((uint)b.Id, (await Sub(ma, 0x10F)).U32());

        await ma.SendAsync(new PacketWriter(0x1C).U32((uint)b.Id).Str(b.Nickname!));
        var removed = await Sub(ma, 0x10B);
        Assert.Equal((0u, (uint)b.Id), (removed.U32(), removed.U32()));
        Assert.Null(await env.Game.S.Friends.GetAsync(b.Id, a.Id));
    }

    [Fact]
    public async Task AutoReplyDoesNotPingPong()
    {
        _ = fx;
        await using var env = await Env.StartAsync();
        var (a, _, ma) = await PlayerAsync(env);
        var (b, _, mb) = await PlayerAsync(env);
        await env.Game.S.Friends.RequestAsync(a.Id, b.Id);
        await env.Game.S.Friends.AcceptAsync(b.Id, a.Id);
        await ma.SendAsync(new PacketWriter(0x1E).U32((uint)b.Id).Str(Pangya.Protocol.KR645.Messenger.MessengerHandler.AutoReply));
        Assert.Equal((uint)a.Id, (await Sub(mb, 0x113)).U32());
        await mb.SendAsync(new PacketWriter(0x1E).U32((uint)a.Id).Str(Pangya.Protocol.KR645.Messenger.MessengerHandler.AutoReply));
        await mb.SendAsync(new PacketWriter(0x1E).U32((uint)a.Id).Str("marca"));
        var next = await Sub(ma, 0x113);                                        // a resposta automática de B não chegou
        next.U32(); next.Str();
        Assert.Equal("marca", next.Str());
    }

    [Fact]
    public async Task DuplicateLoginDropsTheOldSession()
    {
        _ = fx;
        await using var env = await Env.StartAsync();
        var (a, _, ma) = await PlayerAsync(env);
        var m2 = await TestClient.ConnectAsync(env.Msn.Tcp.Port);
        var (_, hello) = await m2.ReceiveAsync();
        hello.U8(); hello.U8();
        m2.Key = (int)hello.U32();
        await m2.SendAsync(new PacketWriter(0x12).U32((uint)a.Id).Str(a.Nickname!));
        Assert.Equal(0, (await m2.ExpectAsync(0x2D)).U8());
        await Sub(ma, 0x118);                                                   // "outro login com a mesma conta"
        Assert.True(await ma.IsClosedByServerAsync());
    }

    [Fact]
    public async Task GameSendsMessengerServersAndFriendList()
    {
        _ = fx;
        await using var env = await Env.StartAsync();
        var (a, ga, _) = await PlayerAsync(env);
        var (b, _, _) = await PlayerAsync(env);
        await env.Game.S.Friends.RequestAsync(b.Id, a.Id);                      // B pediu: A vê "(대기중)"
        await ga.SendAsync(new PacketWriter(0x88));
        var fa = await ga.ExpectAsync(0xFA);
        Assert.Equal(fa.U8() * 92, fa.Remaining);                               // n × sGameServerInfo
        await ga.SendAsync(new PacketWriter(0x3C).U16(0x11F));
        var list = await ga.ExpectAsync(0x2E);
        Assert.Equal(0x102, list.U16());
        Assert.Equal((1, (ushort)1, (ushort)1), (list.U8(), list.U16(), list.U16()));
        var e = list.Struct<sFriend>();
        Assert.Equal(((uint)b.Id, 0u, 0u), (e.Uid, e.IsAgree, e.IsAccept));
        await ga.SendAsync(new PacketWriter(0x3C).U16(0x111).U32((uint)b.Id).Str("bilhete").U8(0));
        var note = await ga.ExpectAsync(0x93);
        Assert.Equal((0x111, 1u), (note.U16(), note.U32()));
    }
}
