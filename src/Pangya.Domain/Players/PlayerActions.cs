using Pangya.Core.Text;

namespace Pangya.Domain.Players;

public enum UpgradeResult : byte { Up = 1, Down = 2, NoPang = 3, NoSlot = 4, CannotDowngrade = 5, Failed = 6 }

/// <summary>
/// Ações de My Room e inventário (docs/protocolo/SPEC-myroom.md): upgrades de atributo, armário (itens e pang),
/// mensagem do mascote, aviso do caddie, móveis, privacidade e troca rápida de equipamento.
/// Tudo é conferido contra o que o jogador possui e gravado antes de mudar o estado em memória.
/// </summary>
public sealed class PlayerActions(IPlayerStore store, IGameData data)
{
    public const int FlagMyRoomPrivate = 2;

    /// <summary>Sobe/desce um atributo (0 força .. 4 curva) do personagem ou do club set. Devolve o resultado e o pang gasto.</summary>
    public async Task<(UpgradeResult Result, long Cost)> UpgradeAsync(Player p, bool club, bool down, int stat, int id)
    {
        if (stat is < 0 or > 4 || p.Find(id) is not { Location: ItemLocation.Inventory } obj
            || obj.Group != (club ? ItemGroup.ClubSet : ItemGroup.Character)) return (UpgradeResult.Failed, 0);
        int cap = club ? data.GetShopItem(obj.TypeId)?.UpgradeSlots is { Length: 5 } s ? s[stat] : 0 : 255;
        var pcl = obj.IntArray("pcl", 5);
        int cur = pcl[stat];
        long cost = 0;
        if (down)
        {
            if (cur <= 0) return (UpgradeResult.CannotDowngrade, 0);
            pcl[stat] = cur - 1;
        }
        else
        {
            if (data.UpgradePrice(stat, cur) is not { } price || cur >= cap) return (UpgradeResult.NoSlot, 0);
            if (price > p.Pang) return (UpgradeResult.NoPang, 0);
            cost = price;
            pcl[stat] = cur + 1;
        }
        var edit = obj.Clone();
        edit.Set("pcl", pcl);
        var ch = new PlayerChanges { Pang = p.Pang - cost };
        ch.Updated.Add(edit);
        await store.ApplyAsync(p.AccountId, ch);
        p.Items[edit.Id] = edit;
        p.Pang -= cost;
        return (down ? UpgradeResult.Down : UpgradeResult.Up, cost);
    }

    /// <summary>Guarda um item no armário (não pode estar em uso). null = recusado.</summary>
    public async Task<Item?> LockerPutAsync(Player p, int id)
    {
        if (p.Find(id) is not { Location: ItemLocation.Inventory } it || !IsStorable(p, it)) return null;
        var edit = it.Clone();
        edit.Location = ItemLocation.Locker;
        var ch = new PlayerChanges();
        ch.Updated.Add(edit);
        await store.ApplyAsync(p.AccountId, ch);
        p.Items[id] = edit;
        return edit;
    }

    static bool IsStorable(Player p, Item it)
    {
        if (it.Group is ItemGroup.Character or ItemGroup.Caddie or ItemGroup.Mascot or ItemGroup.Card or ItemGroup.Furniture) return false;
        var e = p.Equip;
        return !IsEquipped(p, it) && it.TypeId != e.BallTypeId && Array.IndexOf(e.ItemSlots, it.TypeId) < 0;
    }

    /// <summary>Objeto em uso: club set, caddie, mascote ou personagem equipados, ou peça vestida por algum personagem.</summary>
    public static bool IsEquipped(Player p, Item it)
    {
        var e = p.Equip;
        if (it.Id == e.ClubSetId || it.Id == e.CaddieId || it.Id == e.MascotId || it.Id == e.CharacterId) return true;
        if (it.Group != ItemGroup.Part) return false;
        foreach (var c in p.Items.Values)
            if (c.Group == ItemGroup.Character && Array.IndexOf(c.IntArray("part_ids", 24), it.Id) >= 0) return true;
        return false;
    }

    /// <summary>Tira um item do armário de volta para o inventário. null = não está lá.</summary>
    public async Task<Item?> LockerTakeAsync(Player p, int id)
    {
        if (p.Find(id) is not { Location: ItemLocation.Locker } it) return null;
        var edit = it.Clone();
        edit.Location = ItemLocation.Inventory;
        var ch = new PlayerChanges();
        ch.Updated.Add(edit);
        await store.ApplyAsync(p.AccountId, ch);
        p.Items[id] = edit;
        return edit;
    }

    public static List<Item> LockerItems(Player p)
    {
        var list = new List<Item>();
        foreach (var it in p.Items.Values)
            if (it.Location == ItemLocation.Locker) list.Add(it);
        list.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return list;
    }

    /// <summary>Deposita (true) ou saca pang do armário. false = valor inválido ou sem saldo.</summary>
    public async Task<bool> LockerBankAsync(Player p, bool deposit, long amount)
    {
        if (amount <= 0 || amount > (deposit ? p.Pang : p.LockerPang)) return false;
        long pang = deposit ? p.Pang - amount : p.Pang + amount, locker = deposit ? p.LockerPang + amount : p.LockerPang - amount;
        await store.ApplyAsync(p.AccountId, new PlayerChanges { Pang = pang, LockerPang = locker });
        p.Pang = pang;
        p.LockerPang = locker;
        return true;
    }

    /// <summary>Mensagem do mascote (até 29 bytes cp949). null = não tem o mascote.</summary>
    public async Task<string?> MascotMessageAsync(Player p, int id, string message)
    {
        if (p.Find(id) is not { Group: ItemGroup.Mascot, Location: ItemLocation.Inventory } m) return null;
        var field = new byte[30];
        Cp949.Write(field, message);
        var msg = Cp949.Read(field);
        var edit = m.Clone();
        edit.Attrs["msg"] = msg;
        var ch = new PlayerChanges();
        ch.Updated.Add(edit);
        await store.ApplyAsync(p.AccountId, ch);
        p.Items[id] = edit;
        return msg;
    }

    /// <summary>Aviso de validade do caddie (sCaddieInfo.checkWarning).</summary>
    public async Task CaddieWarningAsync(Player p, int id, bool flag)
    {
        if (p.Find(id) is not { Group: ItemGroup.Caddie } c) return;
        var edit = c.Clone();
        edit.Set("warning", flag ? 1 : 0);
        var ch = new PlayerChanges();
        ch.Updated.Add(edit);
        await store.ApplyAsync(p.AccountId, ch);
        p.Items[id] = edit;
    }

    /// <summary>Posições novas dos móveis (só os do jogador).</summary>
    public async Task SaveFurnitureAsync(Player p, List<(int Id, float X, float Y, float Z, float R, bool Arranged)> moves)
    {
        var ch = new PlayerChanges();
        var edits = new List<Item>();
        foreach (var (id, x, y, z, r, arranged) in moves)
        {
            if (p.Find(id) is not { Group: ItemGroup.Furniture } f) continue;
            var e = f.Clone();
            e.Attrs["x"] = x; e.Attrs["y"] = y; e.Attrs["z"] = z; e.Attrs["r"] = r; e.Attrs["arranged"] = arranged ? 1 : 0;
            ch.Updated.Add(e);
            edits.Add(e);
        }
        await store.ApplyAsync(p.AccountId, ch);
        foreach (var e in edits) p.Items[e.Id] = e;
    }

    public async Task SetMyRoomPrivateAsync(Player p, bool isPrivate)
    {
        int flags = isPrivate ? p.Flags | FlagMyRoomPrivate : p.Flags & ~FlagMyRoomPrivate;
        await store.ApplyAsync(p.AccountId, new PlayerChanges { Flags = flags });
        p.Flags = flags;
    }

    /// <summary>
    /// Troca rápida de equipamento (barra de equipamento / setas na sala): 1 caddie, 2 bola, 3 club set, 4 personagem,
    /// 5 mascote. false = não possui.
    /// </summary>
    public async Task<bool> QuickEquipAsync(Player p, int kind, int value)
    {
        var e = p.Equip;
        bool Owns(ItemGroup g) => p.Find(value) is { Location: ItemLocation.Inventory } it && it.Group == g;
        switch (kind)
        {
            case 1 when value == 0 || Owns(ItemGroup.Caddie): e.CaddieId = value; break;
            case 2 when value == Item.BasicBall || p.FindType(value) is { Group: ItemGroup.Ball }: e.BallTypeId = value; break;
            case 3 when Owns(ItemGroup.ClubSet): e.ClubSetId = value; break;
            case 4 when Owns(ItemGroup.Character): e.CharacterId = value; break;
            case 5 when value == 0 || Owns(ItemGroup.Mascot): e.MascotId = value; break;
            default: return false;
        }
        await store.ApplyAsync(p.AccountId, new PlayerChanges { Equip = e });
        return true;
    }
}
