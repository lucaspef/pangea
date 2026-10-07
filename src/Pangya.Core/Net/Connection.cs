using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Pangya.Core.Config;
using Pangya.Core.Crypto;
using Pangya.Core.Limits;
using Pangya.Core.Logging;

namespace Pangya.Core.Net;

/// <summary>O que um servidor faz com uma conexão (uma instância por conexão).</summary>
public interface IConnectionHandler
{
    /// <summary>Logo após conectar (ex.: mandar o hello com a chave).</summary>
    ValueTask OnConnectedAsync();
    /// <summary>Um pacote do cliente, já decifrado e conferido. O leitor só vale até o retorno.</summary>
    ValueTask OnPacketAsync(PacketReader packet);
    /// <summary>Conexão encerrada (chamado uma única vez).</summary>
    ValueTask OnDisconnectedAsync();
}

/// <summary>
/// Uma conexão TCP de cliente: lê e decifra pacotes (com limites de tamanho, taxa e inatividade) e envia por
/// uma fila própria (uma tarefa de escrita junta vários pacotes num único envio). Qualquer pacote inválido
/// ou abuso derruba só esta conexão.
/// </summary>
public sealed class Connection
{
    static int nextId;
    readonly Socket socket;
    readonly LimitsConfig limits;
    public LimitsConfig Limits => limits;
    readonly Channel<(byte[] Buf, int Start, int Len)> outbox =
        Channel.CreateBounded<(byte[], int, int)>(new BoundedChannelOptions(4096) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    readonly CancellationTokenSource cts = new();
    int closed;

    public int Id { get; } = Interlocked.Increment(ref nextId);
    public IPEndPoint Remote { get; }
    public string Name { get; }
    /// <summary>Chave da cifra (0..15), escolhida pelo servidor e mandada no hello.</summary>
    public int ParseKey { get; set; }
    public bool IsClosed => closed != 0;
    public IConnectionHandler Handler { get; set; } = null!;
    /// <summary>Fecha a conexão sem pacotes por este tempo (0 = nunca). Começa com Limits.IdleTimeoutSeconds.</summary>
    public int IdleTimeoutSeconds { get; set; }

    internal Connection(Socket socket, string name, LimitsConfig limits)
    {
        this.socket = socket;
        this.limits = limits;
        IdleTimeoutSeconds = limits.IdleTimeoutSeconds;
        Name = name;
        Remote = (IPEndPoint)socket.RemoteEndPoint!;
        socket.NoDelay = true;
    }

    public override string ToString() => $"{Name}#{Id} {Remote}";

    /// <summary>Envia um pacote cifrado; o writer passa a ser da conexão (não reutilizar).</summary>
    public void Send(PacketWriter w)
    {
        var buf = w.Detach(out var bodyLen);
        if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"{this} -> 0x{BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(PacketCipher.ServerOverhead)):X4} ({bodyLen} bytes)");
        int len = PacketCipher.SealServer(buf, bodyLen, ParseKey, (byte)Random.Shared.Next(256));
        Enqueue(buf, 0, len);
    }

    /// <summary>Envia uma cópia de um corpo pronto (id + dados): para o mesmo pacote ir a várias conexões.</summary>
    public void Send(ReadOnlySpan<byte> body)
    {
        var w = new PacketWriter(BinaryPrimitives.ReadUInt16LittleEndian(body), body.Length);
        w.Bytes(body[2..]);
        Send(w);
    }

    /// <summary>Envia sem cifra (só o hello, antes de o cliente ter a chave).</summary>
    public void SendRaw(PacketWriter w)
    {
        var buf = w.Detach(out var bodyLen);
        var (start, len) = PacketCipher.SealRaw(buf, bodyLen);
        Enqueue(buf, start, len);
    }

    void Enqueue(byte[] buf, int start, int len)
    {
        if (IsClosed || !outbox.Writer.TryWrite((buf, start, len)))
        {
            ArrayPool<byte>.Shared.Return(buf);
            if (!IsClosed) Close("fila de envio cheia (cliente não está lendo)");
        }
    }

    /// <summary>Fecha a conexão. Com afterSend = true, o que já está na fila ainda é enviado antes.</summary>
    public void Close(string reason, bool afterSend = false)
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        Log.Info($"{this} fechada: {reason}");
        outbox.Writer.TryComplete();
        cts.Cancel();
        if (!afterSend) Shutdown();
    }

    void Shutdown()
    {
        try { socket.Shutdown(SocketShutdown.Both); } catch { /* já fechado */ }
    }

    internal async Task RunAsync()
    {
        var sendTask = SendLoopAsync();
        var buf = ArrayPool<byte>.Shared.Rent(limits.MaxPacketSize + PacketCipher.ClientHeader);
        var reader = new PacketReader();
        var bucket = new TokenBucket(limits.MaxPacketsPerSecond);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        try
        {
            await Handler.OnConnectedAsync();
            while (!IsClosed)
            {
                idle.CancelAfter(IdleTimeoutSeconds > 0 ? TimeSpan.FromSeconds(IdleTimeoutSeconds) : Timeout.InfiniteTimeSpan);
                if (!await ReadExactlyAsync(buf.AsMemory(0, PacketCipher.ClientHeader), idle.Token)) break;
                int len = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(1));
                if (len < 3 || len > limits.MaxPacketSize) { Close($"tamanho de pacote inválido ({len})"); break; }
                if (!await ReadExactlyAsync(buf.AsMemory(PacketCipher.ClientHeader, len), idle.Token)) break;
                if (!bucket.TryTake()) { Close("pacotes demais por segundo"); break; }
                var payload = buf.AsSpan(PacketCipher.ClientHeader, len);
                if (!PacketCipher.OpenClient(payload, ParseKey, buf[0])) { Close("byte de conferência da cifra inválido"); break; }
                reader.Reset(buf, PacketCipher.ClientHeader + 1, len - 1);
                if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"{this} <- 0x{reader.Id:X4} ({len - 1} bytes)");
                await Handler.OnPacketAsync(reader);
            }
        }
        catch (PacketException e) { Close("pacote inválido: " + e.Message); }
        catch (OperationCanceledException) when (!IsClosed) { Close("inativa por tempo demais"); }
        catch (OperationCanceledException) { }
        catch (SocketException) { Close("conexão perdida"); }
        catch (Exception e)
        {
            Log.Error($"{this} erro no tratamento de pacote", e);
            Close("erro interno");
        }
        finally
        {
            Close("fim da leitura");
            ArrayPool<byte>.Shared.Return(buf);
            try { await Handler.OnDisconnectedAsync(); }
            catch (Exception e) { Log.Error($"{this} erro ao desconectar", e); }
            await sendTask;
            socket.Dispose();
        }
    }

    async ValueTask<bool> ReadExactlyAsync(Memory<byte> dst, CancellationToken ct)
    {
        while (dst.Length > 0)
        {
            int n = await socket.ReceiveAsync(dst, SocketFlags.None, ct);
            if (n == 0) { Close("cliente desconectou"); return false; }
            dst = dst[n..];
        }
        return true;
    }

    async Task SendLoopAsync()
    {
        var batch = new List<(byte[] Buf, int Start, int Len)>();
        var segments = new List<ArraySegment<byte>>();
        try
        {
            while (await outbox.Reader.WaitToReadAsync())
            {
                while (batch.Count < 64 && outbox.Reader.TryRead(out var item)) batch.Add(item);
                segments.Clear();
                foreach (var (b, s, l) in batch) segments.Add(new ArraySegment<byte>(b, s, l));
                try { await socket.SendAsync(segments, SocketFlags.None); }
                catch (Exception) { Close("erro ao enviar"); }
                foreach (var item in batch) ArrayPool<byte>.Shared.Return(item.Buf);
                batch.Clear();
            }
        }
        finally
        {
            while (outbox.Reader.TryRead(out var item)) ArrayPool<byte>.Shared.Return(item.Buf);
            Shutdown();
        }
    }
}
