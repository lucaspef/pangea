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
        foreach (var x in iff.Characters) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Clubs) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.ClubSets) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Balls) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Items) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Caddies) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.CaddieItems) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.SetItems) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Skins) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.HairStyles) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Mascots) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.AuxParts) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Cards) typeIds.Add((int)x.c.TypeId);
        foreach (var x in iff.Furniture) typeIds.Add((int)x.c.TypeId);
    }

    public static Kr645GameData Load(string iffPath) => new(Kr645Iff.Load(iffPath));

    public bool Exists(int typeId) => typeIds.Contains(typeId);

    /// <summary>CItemManager::GetDefCombo (itemmanager.cpp:2737): 0x08000400 | índice&lt;&lt;18 | slot&lt;&lt;13, se existir no Part.iff.</summary>
    public int[] DefaultParts(int characterTypeId)
    {
        int idx = characterTypeId & 0xFF;
        var result = new int[24];
        for (int slot = 0; slot < 24; slot++)
        {
            int t = 0x08000400 | (idx << 18) | (slot << 13);
            result[slot] = parts.Contains(t) ? t : 0;
        }
        return result;
    }
}
