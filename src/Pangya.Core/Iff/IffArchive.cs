using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pangya.Core.Iff;

/// <summary>
/// Leitor do pangya.iff (dados do jogo): um ZIP de tabelas "Nome.iff". Cada tabela é
/// u16 quantidade, u16 bind, u32 versão, e depois registros de tamanho fixo. O tipo do registro (struct gerada
/// pelo StructGen) depende da versão do cliente; o tamanho é conferido na leitura.
/// </summary>
public sealed class IffArchive
{
    readonly Dictionary<string, byte[]> tables = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => tables.Keys;

    public static IffArchive Load(string path)
    {
        var a = new IffArchive();
        using var zip = ZipFile.OpenRead(path);
        foreach (var e in zip.Entries)
        {
            using var s = e.Open();
            var data = new byte[e.Length];
            s.ReadExactly(data);
            a.tables[e.FullName] = data;
        }
        return a;
    }

    public bool Has(string name) => tables.ContainsKey(name);

    /// <summary>Tamanho do registro de uma tabela (calculado do arquivo).</summary>
    public int RecordSize(string name)
    {
        var d = tables[name];
        int count = BinaryPrimitives.ReadUInt16LittleEndian(d);
        return count == 0 ? 0 : (d.Length - 8) / count;
    }

    /// <summary>Lê todos os registros de uma tabela como T; erro se o tamanho não bate (versão errada dos dados).</summary>
    public T[] Table<T>(string name) where T : unmanaged
    {
        if (!tables.TryGetValue(name, out var d)) throw new InvalidDataException($"pangya.iff sem a tabela {name}");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(d);
        int size = Unsafe.SizeOf<T>();
        if (d.Length != 8 + count * size)
            throw new InvalidDataException($"{name}: {count} registros em {d.Length - 8} bytes, mas {typeof(T).Name} tem {size} bytes");
        return MemoryMarshal.Cast<byte, T>(d.AsSpan(8)).ToArray();
    }
}
