using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pangya.Core.Text;

namespace Pangya.Core.Net;

/// <summary>Pacote inválido vindo do cliente: derruba só aquela conexão.</summary>
public sealed class PacketException(string message) : Exception(message);

/// <summary>
/// Lê o corpo de um pacote do cliente (little-endian, como WReceivedPacket). Toda leitura confere os limites:
/// pacote curto ou string grande demais vira <see cref="PacketException"/> (nunca confiar no cliente).
/// Um leitor por conexão, reaproveitado a cada pacote.
/// </summary>
public sealed class PacketReader
{
    byte[] buf = [];
    int pos, end;

    public ushort Id { get; private set; }
    public int Remaining => end - pos;

    /// <summary>Aponta para o corpo (id u16 + dados) em buf[start..start+length].</summary>
    public void Reset(byte[] buffer, int start, int length)
    {
        buf = buffer;
        pos = start;
        end = start + length;
        Id = U16();
    }

    ReadOnlySpan<byte> Take(int n)
    {
        if ((uint)n > (uint)(end - pos)) throw new PacketException($"pacote 0x{Id:X4} curto: pediu {n}, restam {end - pos}");
        var s = buf.AsSpan(pos, n);
        pos += n;
        return s;
    }

    public byte U8() => Take(1)[0];
    public bool Bool() => U8() != 0;
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short I16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public ReadOnlySpan<byte> Bytes(int n) => Take(n);
    public void Skip(int n) => Take(n);

    /// <summary>String do protocolo: u16 tamanho + bytes cp949. maxBytes limita o que o cliente pode mandar.</summary>
    public string Str(int maxBytes = 64)
    {
        int n = U16();
        if (n > maxBytes) throw new PacketException($"pacote 0x{Id:X4}: string de {n} bytes (máx {maxBytes})");
        return Cp949.Encoding.GetString(Take(n));
    }

    /// <summary>Lê uma struct de layout fixo (ex.: sCharacterInfo), byte a byte como o cliente mandou.</summary>
    public T Struct<T>() where T : unmanaged => MemoryMarshal.Read<T>(Take(Unsafe.SizeOf<T>()));
}
