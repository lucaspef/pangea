using System.Net;
using System.Net.Sockets;
using Pangya.Core.Config;
using Pangya.Core.Crypto;
using Pangya.Core.Net;

namespace Pangya.Tests;

/// <summary>Cliente de teste que fala o protocolo como o cliente real (cifra, cabeçalhos).</summary>
public sealed class TestClient : IAsyncDisposable
{
    readonly TcpClient tcp = new();
    NetworkStream stream = null!;
    byte counter;
    public int Key { get; set; } = -1;

    public static async Task<TestClient> ConnectAsync(int port)
    {
        var c = new TestClient();
        await c.tcp.ConnectAsync(IPAddress.Loopback, port);
        c.stream = c.tcp.GetStream();
        return c;
    }

    /// <summary>Lê um pacote do servidor: (id, corpo sem o id). Antes de ter chave, lê pacote cru.</summary>
    public async Task<(ushort Id, PacketReader Body)> ReceiveAsync(int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var head = new byte[3];
        await stream.ReadExactlyAsync(head, cts.Token);
        int len = BitConverter.ToUInt16(head, 1);
        var data = new byte[len];
        await stream.ReadExactlyAsync(data, cts.Token);
        byte[] body;
        if (Key < 0) body = data[1..];
        else
        {
            PacketCipher.Decrypt(data, PacketCipher.PublicKey(Key, head[0]));
            Assert.Equal(PacketCipher.PrivateKey(Key, head[0]), data[0]);
            Assert.Equal(1, data[1]);
            Assert.Equal(len - 5, data[2] * 255 * 255 + data[3] * 255 + data[4]);
            body = data[5..];
        }
        var r = new PacketReader();
        r.Reset(body, 0, body.Length);
        return (r.Id, r);
    }

    /// <summary>Espera um pacote específico, ignorando os outros.</summary>
    public async Task<PacketReader> ExpectAsync(ushort id, int timeoutMs = 5000)
    {
        while (true)
        {
            var (got, r) = await ReceiveAsync(timeoutMs);
            if (got == id) return r;
        }
    }

    public Task SendAsync(PacketWriter w)
    {
        var wire = PacketCipher.SealClient(w.Body, Key, (byte)(counter * 7), counter);
        counter++;
        w.Dispose();
        return stream.WriteAsync(wire).AsTask();
    }

    public Task SendRawBytesAsync(byte[] b) => stream.WriteAsync(b).AsTask();

    /// <summary>true se o servidor fechou a conexão (lê até EOF).</summary>
    public async Task<bool> IsClosedByServerAsync(int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var b = new byte[4096];
        try
        {
            while (await stream.ReadAsync(b, cts.Token) > 0) { }
            return true;
        }
        catch (IOException) { return true; }
        catch (OperationCanceledException) { return false; }
    }

    public ValueTask DisposeAsync()
    {
        tcp.Dispose();
        return ValueTask.CompletedTask;
    }
}

public class TcpServerTests
{
    /// <summary>Handler de eco: hello cru com a chave; devolve cada pacote com id + 1.</summary>
    sealed class Echo(Connection c) : IConnectionHandler
    {
        public ValueTask OnConnectedAsync()
        {
            c.ParseKey = 5;
            c.SendRaw(new PacketWriter(0).U32(5));
            return ValueTask.CompletedTask;
        }

        public ValueTask OnPacketAsync(PacketReader p)
        {
            c.Send(new PacketWriter((ushort)(p.Id + 1)).Bytes(p.Bytes(p.Remaining)));
            return ValueTask.CompletedTask;
        }

        public ValueTask OnDisconnectedAsync() => ValueTask.CompletedTask;
    }

    static async Task<(TcpServer, CancellationTokenSource)> StartAsync(LimitsConfig? limits = null)
    {
        var cts = new CancellationTokenSource();
        var s = new TcpServer("ECHO", new IPEndPoint(IPAddress.Loopback, 0), limits ?? new LimitsConfig(), c => new Echo(c));
        _ = s.StartAsync(cts.Token);
        await Task.Yield();
        return (s, cts);
    }

    static async Task<TestClient> HelloAsync(int port)
    {
        var c = await TestClient.ConnectAsync(port);
        var (id, r) = await c.ReceiveAsync();
        Assert.Equal(0, id);
        c.Key = (int)r.U32();
        return c;
    }

    [Fact]
    public async Task EchoesEncryptedPackets()
    {
        var (s, cts) = await StartAsync();
        await using var c = await HelloAsync(s.Port);
        for (int i = 0; i < 20; i++)
        {
            await c.SendAsync(new PacketWriter(0x10).U32((uint)i).Zeros(i * 37));
            var r = await c.ExpectAsync(0x11);
            Assert.Equal((uint)i, r.U32());
            Assert.Equal(i * 37, r.Remaining);
        }
        cts.Cancel();
    }

    [Fact]
    public async Task BadCheckByteClosesConnection()
    {
        var (s, cts) = await StartAsync();
        await using var c = await HelloAsync(s.Port);
        var wire = PacketCipher.SealClient([1, 0, 9, 9, 9, 9], c.Key, 1);
        wire[4] ^= 0x55;                       // o 1º byte cifrado vira exatamente o byte de conferência decifrado
        await c.SendRawBytesAsync(wire);
        Assert.True(await c.IsClosedByServerAsync());
        cts.Cancel();
    }

    [Fact]
    public async Task OversizedPacketClosesConnection()
    {
        var (s, cts) = await StartAsync(new LimitsConfig { MaxPacketSize = 100 });
        await using var c = await HelloAsync(s.Port);
        await c.SendAsync(new PacketWriter(1).Zeros(500));
        Assert.True(await c.IsClosedByServerAsync());
        cts.Cancel();
    }

    [Fact]
    public async Task FloodClosesConnection()
    {
        var (s, cts) = await StartAsync(new LimitsConfig { MaxPacketsPerSecond = 10 });
        await using var c = await HelloAsync(s.Port);
        try { for (int i = 0; i < 50; i++) await c.SendAsync(new PacketWriter(1).U8(1)); }
        catch (IOException) { }                       // o servidor pode fechar antes do último envio
        Assert.True(await c.IsClosedByServerAsync());
        cts.Cancel();
    }

    [Fact]
    public async Task IdleConnectionIsClosed()
    {
        var (s, cts) = await StartAsync(new LimitsConfig { IdleTimeoutSeconds = 1 });
        await using var c = await HelloAsync(s.Port);
        Assert.True(await c.IsClosedByServerAsync(4000));
        cts.Cancel();
    }

    [Fact]
    public async Task ConnectionsPerIpAreLimited()
    {
        var (s, cts) = await StartAsync(new LimitsConfig { MaxConnectionsPerIp = 2 });
        await using var a = await HelloAsync(s.Port);
        await using var b = await HelloAsync(s.Port);
        await using var c = await TestClient.ConnectAsync(s.Port);
        Assert.True(await c.IsClosedByServerAsync());
        cts.Cancel();
    }
}
