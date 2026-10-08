using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Lounge / sala de avatar (modo 2; docs/protocolo/SPEC-lounge-loja.md). O cliente troca para a ntAvatarChatTask ao
/// receber o 0x47, e só essa task cria avatares a partir do 0x46 (que tem de levar u16 0xFFFF). Os pacotes que já
/// estavam na fila vão para a task antiga, então a lista de avatares é reenviada até o cliente confirmar o próprio
/// avatar (0x63 sub 4). Movimento/ação/emote/estado (0x63 sub 4..8) são guardados e repassados aos outros (0xC2).
/// </summary>
public sealed partial class GameHandler
{
    const ushort SAvatarAction = 0xC2, CAvatarData = 0xED;
    const byte ActAppear = 4, ActPose = 5, ActMove = 6, ActMotion = 7, ActState = 8;
    /// <summary>Maior deslocamento aceito num 0x63 sub 6 (o cliente manda a cada ~16 unidades andadas).</summary>
    const float MaxStep = 200;
    const int LoungeResends = 15;

    /// <summary>Já recebeu o 0x63 sub 4 do próprio avatar nesta entrada no lounge.</summary>
    bool loungeReady;

    static bool IsLounge(Room? r) => r?.Settings.Mode == GameMode.AvatarChat;

    /// <summary>Depois do 0x47: manda a lista a cada 1 s até o cliente confirmar o avatar (ou sair da sala).</summary>
    void StartLoungeSync(Room r)
    {
        loungeReady = false;
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < LoungeResends; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                lock (Rooms.Sync)
                {
                    if (loungeReady || room != r || conn.IsClosed) return;
                    conn.Send(RoomPackets.SlotsFull(r));
                }
            }
        });
    }

    /// <summary>0x63 u8 sub + dados. Fora do lounge: só o reenvio único dos slots (como antes).</summary>
    void RoomAction(PacketReader p)
    {
        lock (Rooms.Sync)
        {
            var r = room;
            if (!IsLounge(r) || r!.Find(this) is not { } me || p.Remaining < 1)
            {
                p.Skip(p.Remaining);
                ResendSlotsOnceLocked();
                return;
            }
            byte sub = p.U8();
            var w = new PacketWriter(SAvatarAction).U32(me.Guid).U8(sub);
            switch (sub)
            {
                case ActAppear when p.Remaining >= 12:
                {
                    float x = p.F32(), z = p.F32(), a = p.F32();
                    if (!Finite(x, z, a)) return;
                    (me.X, me.Z, me.Angle) = (x, z, a);
                    w.F32(x).F32(z).F32(a);
                    if (!loungeReady && r.Weather is { } weather) conn.Send(WeatherPacket(weather));   // a cena do lounge já existe
                    loungeReady = true;
                    break;
                }
                case ActMove when p.Remaining >= 12:
                {
                    float dx = p.F32(), dz = p.F32(), a = p.F32();
                    if (!Finite(dx, dz, a) || MathF.Abs(dx) > MaxStep || MathF.Abs(dz) > MaxStep) return;
                    (me.X, me.Z, me.Angle) = (me.X + dx, me.Z + dz, a);
                    w.F32(dx).F32(dz).F32(a);
                    break;
                }
                case ActPose when p.Remaining >= 4: me.Action = p.U32(); w.U32(me.Action); break;
                case ActState when p.Remaining >= 4: me.State = p.U32(); w.U32(me.State); break;
                case ActMotion when p.Remaining >= 2: w.Str(p.Str(64)); break;
                default: p.Skip(p.Remaining); w.Dispose(); return;
            }
            InGameOutput.Broadcast(r, w, except: this);
        }
    }

    static bool Finite(float a, float b, float c) => float.IsFinite(a) && float.IsFinite(b) && float.IsFinite(c);

    // ------------------------------------------------------------------ itens SP (SPEC-lounge-sp.md)

    const byte QuickSpItem = 6;
    const ushort SSpState = 0x19B;
    const int SpKinds = 5, SpShine = 3;
    static readonly TimeSpan SpCooldown = TimeSpan.FromSeconds(1);

    /// <summary>A peça (24 partes) ou anel (5) equipado no personagem atual tem o efeito <paramref name="kind"/> e é do jogador.</summary>
    bool HasSpItem(int kind, out float rate)
    {
        rate = 1;
        if (Player.Character is not { } c) return false;
        var sp = ctx.Data.SpItems;
        var parts = c.IntArray("parts", 24);
        var aux = c.IntArray("aux", 5);
        foreach (var arr in new[] { parts, aux })
            foreach (var tid in arr)
                if (tid != 0 && sp.TryGetValue(tid, out var e) && e.Ability == kind && Player.FindType(tid) != null)
                {
                    rate = MathF.Max(MathF.Floor(e.Rate), 1);                 // o cliente trunca o f32 para inteiro
                    return true;
                }
        return false;
    }

    static PacketWriter SpPacket(uint guid, int kind, float value) =>
        new PacketWriter(SQuickEquip).U8(QuickSpItem).U32(guid).U32((uint)kind).F32(value);

    /// <summary>
    /// 0x0C u8 6, u32 guid (ignorado), u32 efeito: comando de chat /거인, /왕머리, /광속, /반짝이 no lounge. Confere a peça
    /// equipada, alterna o efeito (brilho: sempre o efeito) e manda 0x49 u8 6, u32 guid, u32 efeito, f32 valor à sala toda.
    /// </summary>
    void LoungeSpItem(uint kind)
    {
        lock (Rooms.Sync)
        {
            var r = room;
            if (!IsLounge(r) || !loungeReady || r!.Find(this) is not { } me || kind >= SpKinds) return;
            if (me.SpLastUse != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(me.SpLastUse) < SpCooldown) return;
            if (!HasSpItem((int)kind, out float rate)) { Log.Info($"{conn} item SP {kind} recusado: sem a peça equipada"); return; }
            me.SpLastUse = System.Diagnostics.Stopwatch.GetTimestamp();
            float value = rate;
            if (kind != SpShine) me.SpValues[kind] = value = me.SpValues[kind] == 1 ? rate : 1;
            InGameOutput.Broadcast(r, SpPacket(me.Guid, (int)kind, value));
        }
    }

    /// <summary>0xED u32 guid: estado SP de um avatar que acabou de aparecer -> 0x19B u32 guid, 5 × f32 (só ao pedinte).</summary>
    void LoungeSpState(uint guid)
    {
        lock (Rooms.Sync)
        {
            if (!IsLounge(room) || room!.Find(guid) is not { } who) return;
            var w = new PacketWriter(SSpState).U32(guid);
            foreach (var v in who.SpValues) w.F32(v);
            conn.Send(w);
        }
    }

    /// <summary>Trocou a roupa no lounge: efeitos sem a peça desligam (0x49 sub 6 com 1).</summary>
    void LoungeSpRecheckLocked()
    {
        if (!IsLounge(room) || room!.Find(this) is not { } me) return;
        for (int k = 0; k < SpKinds; k++)
            if (me.SpValues[k] != 1 && !HasSpItem(k, out _))
            {
                me.SpValues[k] = 1;
                InGameOutput.Broadcast(room, SpPacket(me.Guid, k, 1));
            }
    }
}
