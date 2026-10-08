using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Pangya.Domain.Players;

/// <summary>
/// Peça Self Design (UCC) do Part.iff (SPEC-self-design.md §1): categoria 7/8 desenha, 7/9 copia. Texture = Tex[0]
/// (nome do arquivo do desenho); Clothes = chave da "mesma roupa" (Data + Tex + OrgTex, IsSameClothes).
/// </summary>
public sealed record UccPartInfo(int Category, string Texture, string Clothes)
{
    public bool CanDraw => Category is 7 or 8;
    public bool CanCopy => Category is 7 or 9;
}

/// <summary>
/// Desenho de uma peça UCC, guardado nos atributos do item: ucc_idx (índice do desenho; sem ele vale o id do item em
/// hexa, 8 dígitos), ucc_status (bit0 final, bit1 salvamento temporário), ucc_seq (1 = original, cópias 2, 3...),
/// ucc_name, ucc_copier e ucc_date.
/// </summary>
public static class Ucc
{
    public const int Final = 1, Temp = 2;
    public const int MaxName = 40;

    public static string Index(Item it) =>
        it.Attrs["ucc_idx"]?.GetValue<string>() is { Length: > 0 } s ? s : ((uint)it.Id).ToString("x8");
    public static int Status(Item it) => it.Int("ucc_status");
    public static int Seq(Item it) => it.Int("ucc_seq");
    public static string Name(Item it) => it.Attrs["ucc_name"]?.GetValue<string>() ?? "";
    public static string Copier(Item it) => it.Attrs["ucc_copier"]?.GetValue<string>() ?? "";
    public static DateTime? Date(Item it) => it.Attrs["ucc_date"]?.GetValue<DateTime>();

    /// <summary>Índice: 1 a 8 letras minúsculas/dígitos (o cliente copia 9 bytes sem garantir o NUL).</summary>
    public static bool ValidIndex(string s)
    {
        if (s.Length is < 1 or > 8) return false;
        foreach (var c in s) if (c is not (>= '0' and <= '9' or >= 'a' and <= 'z')) return false;
        return true;
    }

    /// <summary>
    /// Nome do arquivo (GenerateTextureName_F): Tex[0] sem extensão, sem os caracteres que o cliente filtra, + "_" +
    /// índice + ".jpg", tudo minúsculo. Ex.: M_TS_u01f-01.jpg + 0000f424 -> m_ts_u01f01_0000f424.jpg.
    /// </summary>
    public static string FileName(string texture, string index)
    {
        int dot = texture.LastIndexOf('.');
        var b = new StringBuilder();
        foreach (var c in dot > 0 ? texture[..dot] : texture)
            if ("[]{}`~!@#$%^&()-+=\\|<>/?".IndexOf(c) < 0) b.Append(c);
        return (b + "_" + index + ".jpg").ToLowerInvariant();
    }

    /// <summary>Nome de arquivo aceito no download (sem caminho): [a-z0-9_]+.jpg.</summary>
    public static bool ValidFileName(string name)
    {
        if (name.Length is < 5 or > 80 || !name.EndsWith(".jpg", StringComparison.Ordinal)) return false;
        for (int i = 0; i < name.Length - 4; i++)
            if (name[i] is not (>= '0' and <= '9' or >= 'a' and <= 'z' or '_')) return false;
        return true;
    }

    /// <summary>Nome do desenho: 1..40 caracteres visíveis (sem aspas simples).</summary>
    public static bool ValidName(string s)
    {
        if (s.Length is < 1 or > MaxName || s.Contains('\'')) return false;
        foreach (var c in s) if (char.IsControl(c)) return false;
        return true;
    }
}

/// <summary>
/// Chaves de upload do Self Design (0xC1 -> 0x14B) e uploads recebidos, só em memória. O game server cria a chave para
/// (conta, item) com o nome de arquivo esperado; o web confere e grava; o 0xB1 sub 0/3 só registra o que subiu.
/// </summary>
public sealed class UccUploads
{
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromMinutes(5);

    sealed record Ticket(string Key, string File, DateTime Until);
    readonly ConcurrentDictionary<(long Account, int Item), Ticket> keys = new();
    readonly ConcurrentDictionary<(long Account, int Item), DateTime> uploaded = new();

    /// <summary>Nova chave (a anterior daquele item deixa de valer).</summary>
    public string Issue(long account, int item, string file, DateTime now)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        keys[(account, item)] = new Ticket(key, file, now + KeyLifetime);
        return key;
    }

    /// <summary>Arquivo esperado se a chave vale (uso único); null = recusado.</summary>
    public string? Consume(long account, int item, string key, DateTime now)
    {
        if (!keys.TryGetValue((account, item), out var t) || t.Key != key || t.Until < now) return null;
        keys.TryRemove(new KeyValuePair<(long, int), Ticket>((account, item), t));
        return t.File;
    }

    public void MarkUploaded(long account, int item, DateTime now) => uploaded[(account, item)] = now + KeyLifetime;

    /// <summary>O upload daquele item chegou (e ainda não foi registrado pelo 0xB1)?</summary>
    public bool TakeUploaded(long account, int item, DateTime now) =>
        uploaded.TryRemove((account, item), out var until) && until >= now;
}
