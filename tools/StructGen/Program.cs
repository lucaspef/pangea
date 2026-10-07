// StructGen: lê tools/StructGen/layouts.txt (layout MSVC x86 dos headers do cliente, gerado pelo clang em dump.sh)
// e escreve:
//   src/Pangya.Protocol.KR645/Structs.g.cs      structs C# com o mesmo layout byte a byte
//   tests/Pangya.Tests/StructLayoutTests.g.cs   teste que confere tamanho e offset de cada campo
// Uso (na raiz do repositório): dotnet run --project tools/StructGen
using System.Text;
using System.Text.RegularExpressions;

var root = args.Length > 0 ? args[0] : FindRoot();
var records = Parser.Parse(File.ReadAllLines(Path.Combine(root, "tools/StructGen/layouts.txt")));
var gen = new Generator(records);
File.WriteAllText(Path.Combine(root, "src/Pangya.Protocol.KR645/Structs.g.cs"), gen.Structs());
File.WriteAllText(Path.Combine(root, "tests/Pangya.Tests/StructLayoutTests.g.cs"), gen.Tests());
Console.WriteLine($"StructGen: {gen.Emitted.Count} structs gerados, {records.Count - gen.Emitted.Count} ignorados");

static string FindRoot()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "Pangya.slnx"))) return d.FullName;
    throw new DirectoryNotFoundException("Pangya.slnx");
}

/// <summary>Linha do dump: offset (e bits, se bitfield), profundidade, tipo e nome.</summary>
sealed record Node(int Offset, int BitStart, int BitEnd, int Depth, string Type, string Name, bool IsBase)
{
    public List<Node> Children { get; } = [];
    public bool IsBitfield => BitStart >= 0;
}

sealed record Record(string CName, int Size, int Align, List<Node> Fields, bool HasPointer);

static class Parser
{
    static readonly Regex Line = new(@"^\s*(\d+)(?::(\d+)-(\d+))?\s\|( *)(.*)$");

    public static List<Record> Parse(string[] lines)
    {
        var result = new List<Record>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("*** Dumping AST Record Layout")) continue;
            var head = Line.Match(lines[++i]);
            var cname = Regex.Replace(head.Groups[5].Value, @"^(struct|class|union) ", "");
            var stack = new List<Node>();
            var top = new List<Node>();
            bool pointer = cname.Contains("(empty)");
            for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("| [sizeof="); i++)
            {
                var m = Line.Match(lines[i]);
                if (!m.Success) continue;
                var text = m.Groups[5].Value;
                pointer |= text.Contains('*') || text.Contains("std::");
                int depth = m.Groups[4].Value.Length / 2;
                bool isBase = text.EndsWith(" (base)");
                if (isBase) text = text[..^7];
                var sp = text.LastIndexOf(' ');
                var node = new Node(int.Parse(m.Groups[1].Value),
                    m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : -1,
                    m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : -1,
                    depth, isBase ? text : text[..sp], isBase ? "" : text[(sp + 1)..], isBase);
                while (stack.Count > 0 && stack[^1].Depth >= depth) stack.RemoveAt(stack.Count - 1);
                (stack.Count == 0 ? top : stack[^1].Children).Add(node);
                stack.Add(node);
            }
            var size = Regex.Match(lines[i], @"sizeof=(\d+), align=(\d+)");
            result.Add(new Record(cname, int.Parse(size.Groups[1].Value), int.Parse(size.Groups[2].Value), Flatten(top), pointer));
        }
        return result;
    }

    /// <summary>Herança C++ vira campos achatados (C# não tem herança de struct).</summary>
    static List<Node> Flatten(List<Node> nodes) => nodes.SelectMany(n => n.IsBase ? Flatten(n.Children) : [n]).ToList();
}

sealed class Generator(List<Record> records)
{
    public List<Record> Emitted { get; } = [];
    readonly SortedDictionary<string, (string Elem, int Count)> arrays = new(StringComparer.Ordinal);
    readonly Dictionary<string, Record> byName = records.ToDictionary(r => r.CName);

    static readonly HashSet<string> Keywords = ["base", "object", "string", "params", "event", "fixed", "checked", "operator", "class", "struct", "new", "out", "ref", "in", "is", "as", "lock", "default", "case"];

    // Namespace C++ -> classe C# que agrupa (IFF_STRUCT::sChar -> Iff.sChar)
    static string CsName(string cname) => cname switch
    {
        "_SYSTEMTIME" => "SYSTEMTIME",
        _ when cname.StartsWith("IFF_STRUCT::") && cname.Count(c => c == ':') == 2 => "Iff." + cname[12..],
        _ => cname,
    };

    bool Emittable(Record r) => !r.HasPointer && !r.CName.StartsWith("__") && r.Fields.Count > 0
        && (!r.CName.Contains("::") || CsName(r.CName).StartsWith("Iff.")) && r.CName != "WSendPacket";

    static string Primitive(string t) => t switch
    {
        "unsigned long" or "unsigned int" => "uint",
        "long" or "int" => "int",
        "unsigned short" => "ushort",
        "short" => "short",
        "unsigned char" or "_Bool" => "byte",
        "char" or "signed char" => "sbyte",
        "float" => "float",
        "double" => "double",
        "long long" => "long",
        "unsigned long long" => "ulong",
        _ => "",
    };

    /// <summary>Tipo C# de um campo; arrays viram structs [InlineArray] (ex.: char[22] -> ByteArray22).</summary>
    string TypeOf(string ctype)
    {
        var m = Regex.Match(ctype, @"^(.*?)((?:\[\d+\])+)$");
        if (!m.Success) return Scalar(ctype);
        var dims = Regex.Matches(m.Groups[2].Value, @"\d+").Select(x => int.Parse(x.Value)).ToList();
        var elem = Scalar(m.Groups[1].Value);
        if (elem == "sbyte") elem = "byte";                      // char[] = texto/bytes
        for (int i = dims.Count - 1; i >= 0; i--)
        {
            var name = Pascal(elem) + "Array" + dims[i];
            arrays[name] = (elem, dims[i]);
            elem = name;
        }
        return elem;
    }

    string Scalar(string ctype)
    {
        ctype = Regex.Replace(ctype.Trim(), @"^(struct|class|enum) ", "");
        var p = Primitive(ctype);
        if (p != "") return p;
        if (byName.ContainsKey(ctype)) return CsName(ctype);
        return "int";                                            // enum: 4 bytes no MSVC
    }

    static string Pascal(string t) => t switch
    {
        "byte" => "Byte", "sbyte" => "SByte", "ushort" => "UInt16", "short" => "Int16", "uint" => "UInt32",
        "int" => "Int32", "float" => "Single", "double" => "Double", "long" => "Int64", "ulong" => "UInt64",
        _ => t.Replace("Iff.", "Iff"),
    };

    static string Id(string n) => Keywords.Contains(n) ? "@" + n : n;

    public string Structs()
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            // <auto-generated> Gerado por tools/StructGen a partir dos headers do cliente (rebang/source/shared).
            // NÃO EDITAR: rode tools/StructGen/dump.sh e `dotnet run --project tools/StructGen`. </auto-generated>
            // Layout idêntico ao MSVC x86 do cliente (conferido por tests/Pangya.Tests/StructLayoutTests.g.cs).
            #pragma warning disable CS0169, CS0649, IDE1006
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace Pangya.Protocol.KR645;

            """);
        var iff = new StringBuilder();
        foreach (var r in records.Where(Emittable))
        {
            Emitted.Add(r);
            var target = CsName(r.CName).StartsWith("Iff.") ? iff : sb;
            var ind = target == iff ? "    " : "";
            var name = CsName(r.CName).Replace("Iff.", "");
            target.AppendLine($"{ind}/// <summary>{r.CName} ({r.Size} bytes).</summary>");
            target.AppendLine($"{ind}[StructLayout(LayoutKind.Sequential, Pack = {(r.Align == 1 ? 1 : 8)}, Size = {r.Size})]");
            target.AppendLine($"{ind}public partial struct {name}\n{ind}{{");
            foreach (var g in Group(r.Fields))
            {
                if (!g[0].IsBitfield)
                {
                    target.AppendLine($"{ind}    public {TypeOf(g[0].Type)} {Id(g[0].Name)};");
                    continue;
                }
                var t = Primitive(g[0].Type) is var p && p != "" ? p : "uint";
                var store = "Bits_" + g[0].Name;
                target.AppendLine($"{ind}    public {t} {store};");
                foreach (var b in g.Where(b => b.Name != ""))
                {
                    var mask = (1u << (b.BitEnd - b.BitStart + 1)) - 1;
                    target.AppendLine($"{ind}    public uint {Id(b.Name)} {{ readonly get => ((uint){store} >> {b.BitStart}) & {mask}u; " +
                                      $"set => {store} = ({t})(({store} & ~({mask}u << {b.BitStart})) | ((value & {mask}u) << {b.BitStart})); }}");
                }
            }
            target.AppendLine($"{ind}}}\n");
        }
        sb.AppendLine("/// <summary>Registros das tabelas do pangya.iff (classdefine.h, namespace IFF_STRUCT).</summary>");
        sb.AppendLine("public static class Iff\n{");
        sb.Append(iff.ToString().TrimEnd()).AppendLine("\n}\n");
        sb.AppendLine("// Arrays de tamanho fixo (char[N] etc.); convertem para Span<T> implicitamente.");
        foreach (var (name, (elem, count)) in arrays)
            sb.AppendLine($"[InlineArray({count})] public struct {name} {{ {elem} _e; }}");
        return sb.ToString();
    }

    /// <summary>Agrupa bitfields que dividem a mesma unidade de armazenamento (mesmo offset).</summary>
    static List<List<Node>> Group(List<Node> fields)
    {
        var groups = new List<List<Node>>();
        foreach (var f in fields)
        {
            if (f.IsBitfield && groups.Count > 0 && groups[^1][0].IsBitfield && groups[^1][0].Offset == f.Offset)
                groups[^1].Add(f);
            else
                groups.Add([f]);
        }
        return groups;
    }

    public string Tests()
    {
        var sb = new StringBuilder("""
            // <auto-generated> Gerado por tools/StructGen. Confere tamanho e offset de cada campo contra o layout do clang (MSVC x86). </auto-generated>
            using System.Runtime.CompilerServices;
            using Pangya.Protocol.KR645;

            namespace Pangya.Tests;

            public partial class StructLayoutTests
            {
                static int Off<T, F>(ref T s, ref F f) => (int)Unsafe.ByteOffset(ref Unsafe.As<T, byte>(ref s), ref Unsafe.As<F, byte>(ref f));

            """);
        foreach (var r in Emitted)
        {
            var cs = CsName(r.CName);
            sb.AppendLine($"    [Fact] public void {cs.Replace(".", "_")}()\n    {{");
            sb.AppendLine($"        Assert.Equal({r.Size}, Unsafe.SizeOf<{cs}>());");
            sb.AppendLine($"        var s = new {cs}();");
            foreach (var g in Group(r.Fields))
            {
                var field = g[0].IsBitfield ? "Bits_" + g[0].Name : Id(g[0].Name);
                sb.AppendLine($"        Assert.Equal({g[0].Offset}, Off(ref s, ref s.{field}));");
            }
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }
}
