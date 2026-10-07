using System.Buffers.Binary;

namespace Pangya.Core.Crypto;

/// <summary>
/// Cifra dos pacotes do PangYa (porte de jrencrypt.cpp / packet.cpp do cliente). Validada contra o código do
/// cliente pelos vetores em tests/Pangya.Tests/Data/crypto_vectors.txt (tools/CryptoCheck).
///
/// Servidor -> cliente: [seed][u16 len] + Alpha([Private][1][tamanho 3 dígitos base 255][u16 id][dados])
///   (o "1" é a flag "sem compressão" do CCompressBuffer; o cliente aceita).
/// Cliente -> servidor: [seed][u16 len][u8 contador] + Alpha([Private][u16 id][dados]).
/// Chave do Alpha = Public[parseKey][seed]; Private[parseKey][seed] é o byte de conferência.
/// </summary>
public static class PacketCipher
{
    /// <summary>Bytes antes do corpo (id + dados) num pacote servidor -> cliente: 3 de cabeçalho + 5 de prefixo.</summary>
    public const int ServerOverhead = 8;
    /// <summary>Cabeçalho do pacote cliente -> servidor (seed, len, contador).</summary>
    public const int ClientHeader = 4;

    static readonly byte[] Tables = LoadTables();

    static byte[] LoadTables()
    {
        using var s = typeof(PacketCipher).Assembly.GetManifestResourceStream("keytables.bin")!;
        var t = new byte[8192];
        s.ReadExactly(t);
        return t;
    }

    public static byte PublicKey(int parseKey, byte seed) => Tables[(parseKey << 8) + seed];
    public static byte PrivateKey(int parseKey, byte seed) => Tables[4096 + (parseKey << 8) + seed];

    /// <summary>SimpleStreamEncrypt_Alpha: XOR encadeado por dword (pode ser no mesmo buffer).</summary>
    public static void Encrypt(Span<byte> data, uint key)
    {
        int n = data.Length >> 2;
        uint prev = key, last = key;
        for (int i = 0; i < n; i++)
        {
            var d = BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 4)..]);
            BinaryPrimitives.WriteUInt32LittleEndian(data[(i * 4)..], d ^ prev);
            prev = last = d;
        }
        XorTail(data, n * 4, last);
    }

    /// <summary>SimpleStreamDecrypt_Alpha (inverso de <see cref="Encrypt"/>, no mesmo buffer).</summary>
    public static void Decrypt(Span<byte> data, uint key)
    {
        int n = data.Length >> 2;
        uint prev = key;
        for (int i = 0; i < n; i++)
        {
            prev ^= BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 4)..]);
            BinaryPrimitives.WriteUInt32LittleEndian(data[(i * 4)..], prev);
        }
        XorTail(data, n * 4, prev);
    }

    // Os 1-3 bytes finais são XOR com o último dword em claro (ou com a chave, se não houver dword).
    static void XorTail(Span<byte> data, int pos, uint x)
    {
        for (int j = 0; pos + j < data.Length; j++)
            data[pos + j] ^= (byte)(x >> (8 * j));
    }

    /// <summary>
    /// Monta, no próprio buffer, o pacote servidor -> cliente. O corpo (id + dados) está em
    /// buf[ServerOverhead..ServerOverhead+bodyLen]; devolve o tamanho total (o pacote começa em buf[0]).
    /// </summary>
    public static int SealServer(Span<byte> buf, int bodyLen, int parseKey, byte seed)
    {
        int plainLen = bodyLen + 5;
        if (plainLen > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(bodyLen), "pacote grande demais");
        buf[0] = seed;
        BinaryPrimitives.WriteUInt16LittleEndian(buf[1..], (ushort)plainLen);
        buf[3] = PrivateKey(parseKey, seed);
        buf[4] = 1;
        buf[5] = (byte)(bodyLen / (255 * 255));
        buf[6] = (byte)(bodyLen / 255 % 255);
        buf[7] = (byte)(bodyLen % 255);
        Encrypt(buf.Slice(3, plainLen), PublicKey(parseKey, seed));
        return plainLen + 3;
    }

    /// <summary>
    /// Pacote "cru" (antes de haver chave, ex.: hello): [0][u16 len][0][corpo]. O corpo está em buf[ServerOverhead..];
    /// o pacote é montado em buf[4..]; devolve o offset de início (4) e o tamanho.
    /// </summary>
    public static (int Start, int Length) SealRaw(Span<byte> buf, int bodyLen)
    {
        buf[4] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(buf[5..], (ushort)(bodyLen + 1));
        buf[7] = 0;
        return (4, bodyLen + 4);
    }

    /// <summary>
    /// Decifra no lugar o conteúdo de um pacote cliente -> servidor (os len bytes após o cabeçalho de 4).
    /// Devolve false se o byte de conferência não bate (pacote forjado/corrompido). O corpo (id + dados) é payload[1..].
    /// </summary>
    public static bool OpenClient(Span<byte> payload, int parseKey, byte seed)
    {
        if (payload.Length < 1) return false;
        Decrypt(payload, PublicKey(parseKey, seed));
        return payload[0] == PrivateKey(parseKey, seed);
    }

    /// <summary>Monta um pacote como o cliente monta (para testes e ferramentas).</summary>
    public static byte[] SealClient(ReadOnlySpan<byte> body, int parseKey, byte seed, byte counter = 0)
    {
        var p = new byte[body.Length + 5];
        p[0] = seed;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)(body.Length + 1));
        p[3] = counter;
        p[4] = PrivateKey(parseKey, seed);
        body.CopyTo(p.AsSpan(5));
        Encrypt(p.AsSpan(4), PublicKey(parseKey, seed));
        return p;
    }
}
