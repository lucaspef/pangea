using Pangya.Core.Text;
using Pangya.Domain.Players;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Converte os dados do jogador (Domain) nas structs do cliente 645 (globalgamedefine.h).
/// Layouts e significados: docs/protocolo/SPEC-player-shop.md e SPEC-myroom.md.
/// </summary>
public static class PlayerStructs
{
    /// <summary>"Permanente" para o cliente: 2099-01-01.</summary>
    static readonly DateTime Forever = new(2099, 1, 1);

    public static SYSTEMTIME SystemTime(DateTime? t)
    {
        if (t is not { } v) return default;
        return new SYSTEMTIME
        {
            wYear = (ushort)v.Year, wMonth = (ushort)v.Month, wDayOfWeek = (ushort)v.DayOfWeek, wDay = (ushort)v.Day,
            wHour = (ushort)v.Hour, wMinute = (ushort)v.Minute, wSecond = (ushort)v.Second, wMilliseconds = (ushort)v.Millisecond,
        };
    }

    public static sCharacterInfo Character(Item c)
    {
        var s = new sCharacterInfo { tid = (uint)c.TypeId, guid = (uint)c.Id, hairClr = (byte)c.Int("hair") };
        s.shirtsClr = (uint)c.Int("shirt");
        var parts = c.IntArray("parts", 24);
        var partIds = c.IntArray("part_ids", 24);
        for (int i = 0; i < 24; i++) { s.tidParts[i] = (uint)parts[i]; s.ItemIdList[i] = (uint)partIds[i]; }
        var pcl = c.IntArray("pcl", 5);
        var aux = c.IntArray("aux", 5);
        for (int i = 0; i < 5; i++) { s.PCL[i] = (byte)pcl[i]; s.tidAuxParts[i] = (uint)aux[i]; }
        return s;
    }

    public static sItemInfo ItemInfo(Item it)
    {
        var s = new sItemInfo { guid = (uint)it.Id, tid = (uint)it.TypeId };
        if (it.Group is ItemGroup.Ball or ItemGroup.Usable)
            s.Common[0] = (short)Math.Min(it.Quantity, short.MaxValue);          // Common[0] = quantidade
        else if (it.Group == ItemGroup.ClubSet)
        {
            var pcl = it.IntArray("pcl", 5);                                      // upgrades do club set
            for (int i = 0; i < 5; i++) s.Common[i] = (short)pcl[i];
        }
        s.IsValid = 1;
        return s;
    }

    public static sCaddieInfo Caddie(Item c)
    {
        int part = c.Int("part");
        var s = new sCaddieInfo { guid = (uint)c.Id, tid = (uint)c.TypeId, tidPart = (uint)part, Level = (byte)c.Int("level"), Exp = (uint)c.Int("exp") };
        if (part != 0 && c.Attrs["part_until"]?.GetValue<DateTime>() is { } until)
        {
            var hours = Math.Max(0, (int)Math.Ceiling((until - DateTime.UtcNow).TotalHours - 1e-6));
            s.Remain_Partdate = (ushort)Math.Min(hours, ushort.MaxValue);
            s.Remain_Date = (ushort)Math.Min(hours / 24, 255);
        }
        s.byCheckCaddieWarning = (byte)(c.Int("warning") & 1);
        return s;
    }

    public static sClubInfo Club(Item c)
    {
        var s = new sClubInfo { guid = (uint)c.Id, tid = (uint)c.TypeId };
        var pcl = c.IntArray("pcl", 5);
        for (int i = 0; i < 5; i++) s.PCL[i] = (short)pcl[i];
        return s;
    }

    /// <summary>Remain_Date em HORAS (mascotinfodlg.cpp:185); o cliente desequipa se endDate já passou.</summary>
    public static sMascotInfo Mascot(Item? m)
    {
        if (m == null) return default;
        var end = m.ExpiresAt?.ToLocalTime() ?? Forever;
        var s = new sMascotInfo
        {
            guid = (uint)m.Id, tid = (uint)m.TypeId, Level = (byte)m.Int("level"),
            Remain_Date = (ushort)Math.Clamp((int)Math.Ceiling((end - DateTime.Now).TotalHours - 1e-6), 0, ushort.MaxValue),
            endDate = SystemTime(end),
        };
        Cp949.Write(s.szMsg, m.Attrs["msg"]?.GetValue<string>() ?? "PANGYA!");
        return s;
    }

    public static sUserEquip Equip(Player p)
    {
        var e = p.Equip;
        var s = new sUserEquip
        {
            guidCaddie = (uint)(p.Find(e.CaddieId) != null ? e.CaddieId : 0),
            guidChar = (uint)(p.Character?.Id ?? 0),
            guidClubSet = (uint)(p.Find(e.ClubSetId) != null ? e.ClubSetId : 0),
            tidBall = (uint)(e.BallTypeId != 0 ? e.BallTypeId : Item.BasicBall),
            // só um mascote que está na lista 0xDF (lobbymain.cpp:12420 lê além do fim se não estiver)
            guidMascot = (uint)(p.Find(e.MascotId) != null ? e.MascotId : 0),
        };
        for (int i = 0; i < 10 && i < e.ItemSlots.Length; i++) s.tidItemSlot[i] = (uint)e.ItemSlots[i];
        for (int i = 0; i < 6 && i < e.SkinTypeIds.Length; i++) s.tidSkin[i] = (uint)e.SkinTypeIds[i];
        return s;
    }

    /// <summary>
    /// Cards em vigor como o cliente guarda (sSCardAvilityPeriodInfo 0x41): 0x12F do próprio jogador e, no 0x74, os de
    /// cada jogador da sala, onde o cliente usa cardType/Avility como vêm (docs/protocolo/SPEC-cards-efeitos.md).
    /// </summary>
    public static List<sSCardAvilityPeriodInfo> ActiveCards(Player p, IReadOnlyDictionary<int, Domain.Shop.CardInfo> cards)
    {
        var list = new List<sSCardAvilityPeriodInfo>();
        foreach (var it in Domain.Shop.CardService.Active(p, DateTime.UtcNow))
        {
            var a = Domain.Shop.CardService.ToActive(it);
            var ci = cards.GetValueOrDefault(a.TypeId);
            list.Add(new sSCardAvilityPeriodInfo
            {
                uid = (uint)a.Id, tid = (uint)a.TypeId, partsTid = (uint)a.PartTypeId, partsUid = (uint)a.PartId, slotNum = a.Slot,
                Avility = ci?.Ability ?? 0, AvilityValue = (uint)(ci?.AbilityValue ?? 0),
                useStartTime = SystemTime(a.Start?.ToLocalTime()), useEndTime = SystemTime(a.End?.ToLocalTime()),
                cardType = (a.TypeId >> 22) & 0xF, valid = 1,
            });
        }
        return list;
    }

    /// <summary>Número de registros de mapa que o cliente guarda (posição i = curso i).</summary>
    public const int MapStatCount = 20;

    /// <summary>
    /// sMapStatistics do curso i (docs/protocolo/SPEC-perfil-mapas.md): bMap = i sempre (0xFF esconde a linha, 0 em todas
    /// faz todas virarem o curso 0); sem partida = cBestScore 127 ("-").
    /// </summary>
    public static sMapStatistics MapStat(Player? p, int course)
    {
        var s = new sMapStatistics { bMap = (byte)course, cBestScore = CourseRecord.NoRecord };
        if (p == null || !p.Courses.TryGetValue(course, out var r)) return s;
        s.dwHole = (uint)r.Holes;
        s.iTotalScore = r.TotalScore;
        s.cBestScore = (sbyte)Math.Clamp(r.BestScore, -128, CourseRecord.NoRecord);
        s.i64MaxPang = r.MaxPang;
        s.tidChar = (uint)r.CharacterTypeId;
        return s;
    }

    /// <summary>sUserInfo (0xB92): dados completos do jogador que vão no 0x42 e nas salas.</summary>
    public static sUserInfo UserInfo(Player p)
    {
        var u = new sUserInfo { roomIndex = 0xFFFF };
        Cp949.Write(u.info.sID, p.Login);
        Cp949.Write(u.info.sNick, p.Nickname);
        u.info.dwIdentity = (uint)p.IdentityFlags;
        u.info.dwGuid = (uint)p.AccountId;          // MyGuid(): chave do jogador nas salas, não pode ser 0
        u.info.dwUID = (uint)p.AccountId;
        u.info.DoTutorial = 1;
        u.stat.Level = (byte)p.Level;
        u.stat.dwExp = (uint)p.Exp;
        u.stat.i64Pang = p.Pang;
        for (int i = 0; i < 6; i++) u.stat.cBestScore[i] = 127;           // 127 = sem recorde
        for (int i = 0; i < MapStatCount; i++) { u.mapStat[i] = MapStat(p, i); u.classicMapStat[i] = MapStat(null, i); }
        u.userEquip = Equip(p);
        if (p.Character is { } ch) u.charInfo = Character(ch);
        if (p.Find(p.Equip.CaddieId) is { } cad) u.caddieInfo = Caddie(cad);
        if (p.Find(p.Equip.ClubSetId) is { } club) u.clubInfo = Club(club);
        u.mascotInfo = Mascot(p.Find(p.Equip.MascotId));
        return u;
    }
}
