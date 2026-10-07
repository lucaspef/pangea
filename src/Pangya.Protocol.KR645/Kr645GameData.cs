using Pangya.Domain.Players;

namespace Pangya.Protocol.KR645;

/// <summary>Dados de jogo do cliente 645 (pangya.iff) vistos pelo Domain.</summary>
public sealed class Kr645GameData : IGameData
{
    readonly HashSet<int> typeIds = [];
    readonly HashSet<int> parts = [];

    public Kr645Iff Iff { get; }

    public Kr645GameData(Kr645Iff iff)
    {
        Iff = iff;
        foreach (var p in iff.Parts) parts.Add((int)p.c.TypeId);
        typeIds.UnionWith(parts);
        typeIds.UnionWith(iff.Characters.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.Clubs.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.ClubSets.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.Balls.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.Items.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.Caddies.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.CaddieItems.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.SetItems.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.Skins.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.HairStyles.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.Mascots.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.AuxParts.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.Cards.Select(x => (int)x.c.TypeId));
        typeIds.UnionWith(iff.Furniture.Select(x => (int)x.c.TypeId));
    }

    public static Kr645GameData Load(string iffPath) => new(Kr645Iff.Load(iffPath));

    public bool Exists(int typeId) => typeIds.Contains(typeId);

    /// <summary>CItemManager::GetDefCombo (itemmanager.cpp:2737): 0x08000400 | índice&lt;&lt;18 | slot&lt;&lt;13, se existir no Part.iff.</summary>
    public int[] DefaultParts(int characterTypeId)
    {
        int idx = characterTypeId & 0xFF;
        return Enumerable.Range(0, 24).Select(slot => 0x08000400 | (idx << 18) | (slot << 13)).Select(t => parts.Contains(t) ? t : 0).ToArray();
    }
}
