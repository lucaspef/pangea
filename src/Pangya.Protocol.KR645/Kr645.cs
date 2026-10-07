using System.Runtime.InteropServices;
using Pangya.Core.Iff;

namespace Pangya.Protocol.KR645;

/// <summary>Constantes da versão do cliente KR "645 QA".</summary>
public static class Kr645
{
    /// <summary>Versão anunciada no 0x42 (maior que a do cliente bloqueia criar sala).</summary>
    public const string ClientVersion = "645.00";
    /// <summary>Versão de pacote que o cliente manda no login do game server.</summary>
    public const uint PacketVersion = 0x2A8ED069;
}

/// <summary>
/// Registro do Item.iff nos dados 642 que o cliente usa: 196 bytes, sem o campo RandomBox que o header
/// do 645 (classdefine.h, Iff.sItem = 200 bytes) declara.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 196)]
public struct IffItem642
{
    public IFF_ITEM_COMMON c;
    public ByteArray40 Data;
    public Int16Array5 COM;
    public short Point;
}

/// <summary>Tabelas do pangya.iff com o tipo de registro de cada uma (nos dados que o cliente 645 usa).</summary>
public sealed class Kr645Iff(IffArchive archive)
{
    public IffArchive Archive { get; } = archive;
    public Iff.sChar[] Characters { get; } = archive.Table<Iff.sChar>("Character.iff");
    public Iff.sPart[] Parts { get; } = archive.Table<Iff.sPart>("Part.iff");
    public Iff.sClub[] Clubs { get; } = archive.Table<Iff.sClub>("Club.iff");
    public Iff.sClubSet[] ClubSets { get; } = archive.Table<Iff.sClubSet>("ClubSet.iff");
    public Iff.sBall[] Balls { get; } = archive.Table<Iff.sBall>("Ball.iff");
    public IffItem642[] Items { get; } = LoadItems(archive);
    public Iff.sCaddie[] Caddies { get; } = archive.Table<Iff.sCaddie>("Caddie.iff");
    public Iff.sCadItem[] CaddieItems { get; } = archive.Table<Iff.sCadItem>("CaddieItem.iff");
    public Iff.sSetItem[] SetItems { get; } = archive.Table<Iff.sSetItem>("SetItem.iff");
    public Iff.sCourse[] Courses { get; } = archive.Table<Iff.sCourse>("Course.iff");
    public Iff.sMatch[] Matches { get; } = archive.Table<Iff.sMatch>("Match.iff");
    public Iff.sSkin[] Skins { get; } = archive.Table<Iff.sSkin>("Skin.iff");
    public Iff.sHairStyle[] HairStyles { get; } = archive.Table<Iff.sHairStyle>("HairStyle.iff");
    public Iff.sMascot[] Mascots { get; } = archive.Table<Iff.sMascot>("Mascot.iff");
    public Iff.sAuxPart[] AuxParts { get; } = archive.Table<Iff.sAuxPart>("AuxPart.iff");
    public Iff.sCard[] Cards { get; } = archive.Table<Iff.sCard>("Card.iff");
    public Iff.sFurniture[] Furniture { get; } = archive.Table<Iff.sFurniture>("Furniture.iff");
    public Iff.sEnchant[] Enchants { get; } = archive.Table<Iff.sEnchant>("Enchant.iff");

    public static Kr645Iff Load(string path) => new(IffArchive.Load(path));

    /// <summary>
    /// Item.iff existe em dois formatos: dados 642 (196 bytes) e o convertido para o exe 645 (200 bytes, com o
    /// RandomBox; projectg_zzfix.pak do make_iff_fix.py). Os dois viram o formato 642.
    /// </summary>
    static IffItem642[] LoadItems(IffArchive a)
    {
        if (a.RecordSize("Item.iff") == 196) return a.Table<IffItem642>("Item.iff");
        var full = a.Table<Iff.sItem>("Item.iff");
        var list = new IffItem642[full.Length];
        for (int i = 0; i < full.Length; i++)
            list[i] = new IffItem642 { c = full[i].c, Data = full[i].Data, COM = full[i].COM, Point = full[i].Point };
        return list;
    }
}
