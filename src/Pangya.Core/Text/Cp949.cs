using System.Text;

namespace Pangya.Core.Text;

/// <summary>Texto no formato do cliente coreano: codepage 949, char[N] terminado em zero.</summary>
public static class Cp949
{
    public static readonly Encoding Encoding = Load();

    static Encoding Load()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }

    /// <summary>Lê um char[N]: até o primeiro zero.</summary>
    public static string Read(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return Encoding.GetString(end < 0 ? field : field[..end]);
    }

    /// <summary>Grava em char[N], cortando em N-1 bytes (sem partir caractere de 2 bytes) e completando com zeros.</summary>
    public static void Write(Span<byte> field, string value)
    {
        field.Clear();
        var bytes = Encoding.GetBytes(value);
        int n = Math.Min(bytes.Length, field.Length - 1);
        // não deixar meio caractere coreano no fim: conta bytes de lead (>= 0x81) até o corte
        int i = 0;
        while (i < n) i += bytes[i] >= 0x81 ? 2 : 1;
        if (i > n) n -= 1;
        bytes.AsSpan(0, n).CopyTo(field);
    }
}
