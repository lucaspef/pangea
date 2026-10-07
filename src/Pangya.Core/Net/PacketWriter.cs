using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pangya.Core.Crypto;
using Pangya.Core.Text;

namespace Pangya.Core.Net;

/// <summary>
/// Monta um pacote servidor -> cliente num buffer do ArrayPool, já com espaço reservado para o cabeçalho e o
/// prefixo da cifra (o pacote é cifrado no próprio buffer, sem cópia). Uso:
/// <code>conn.Send(new PacketWriter(0x4C).U8(1));</code>
/// O buffer volta ao pool depois de enviado (Connection cuida disso) ou em <see cref="Dispose"/>.
/// </summary>
public sealed class PacketWriter : IDisposable
{
    byte[] buf;
    int len;                                   // fim do corpo (absoluto no buffer)

    public PacketWriter(ushort id, int capacity = 256)
    {
        buf = ArrayPool<byte>.Shared.Rent(capacity + PacketCipher.ServerOverhead);
        len = PacketCipher.ServerOverhead;
        U16(id);
    }

    public ushort Id => BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(PacketCipher.ServerOverhead));
    /// <summary>Tamanho do corpo (id + dados).</summary>
    public int BodyLength => len - PacketCipher.ServerOverhead;
    public ReadOnlySpan<byte> Body => buf.AsSpan(PacketCipher.ServerOverhead, BodyLength);

    Span<byte> Grow(int n)
    {
        if (len + n > buf.Length)
        {
            var nb = ArrayPool<byte>.Shared.Rent(Math.Max(buf.Length * 2, len + n));
            buf.AsSpan(0, len).CopyTo(nb);
            ArrayPool<byte>.Shared.Return(buf);
            buf = nb;
        }
        var s = buf.AsSpan(len, n);
        len += n;
        return s;
    }

    public PacketWriter U8(byte v) { Grow(1)[0] = v; return this; }
    public PacketWriter Bool(bool v) => U8(v ? (byte)1 : (byte)0);
    public PacketWriter U16(ushort v) { BinaryPrimitives.WriteUInt16LittleEndian(Grow(2), v); return this; }
    public PacketWriter I16(short v) { BinaryPrimitives.WriteInt16LittleEndian(Grow(2), v); return this; }
    public PacketWriter U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(Grow(4), v); return this; }
    public PacketWriter I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(Grow(4), v); return this; }
    public PacketWriter U64(ulong v) { BinaryPrimitives.WriteUInt64LittleEndian(Grow(8), v); return this; }
    public PacketWriter I64(long v) { BinaryPrimitives.WriteInt64LittleEndian(Grow(8), v); return this; }
    public PacketWriter F32(float v) { BinaryPrimitives.WriteSingleLittleEndian(Grow(4), v); return this; }
    public PacketWriter Bytes(ReadOnlySpan<byte> v) { v.CopyTo(Grow(v.Length)); return this; }
    public PacketWriter Zeros(int n) { Grow(n).Clear(); return this; }

    /// <summary>String do protocolo: u16 tamanho + bytes cp949.</summary>
    public PacketWriter Str(string v)
    {
        int n = Cp949.Encoding.GetByteCount(v);
        U16((ushort)n);
        Cp949.Encoding.GetBytes(v, Grow(n));
        return this;
    }

    /// <summary>char[n] terminado em zero.</summary>
    public PacketWriter Fixed(string v, int n) { Cp949.Write(Grow(n), v); return this; }

    /// <summary>Struct de layout fixo (gerada pelo StructGen), copiada byte a byte.</summary>
    public PacketWriter Struct<T>(in T v) where T : unmanaged
    {
        MemoryMarshal.Write(Grow(Unsafe.SizeOf<T>()), in v);
        return this;
    }

    /// <summary>Entrega o buffer (para a conexão cifrar e enviar); depois disso este objeto não pode ser usado.</summary>
    internal byte[] Detach(out int bodyLength)
    {
        var b = buf;
        bodyLength = BodyLength;
        buf = [];
        len = 0;
        return b;
    }

    public void Dispose()
    {
        if (buf.Length > 0) ArrayPool<byte>.Shared.Return(buf);
        buf = [];
    }
}
