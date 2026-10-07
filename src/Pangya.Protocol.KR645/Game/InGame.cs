using System.Buffers.Binary;
using Pangya.Core.Logging;
using Pangya.Core.Net;
using Pangya.Domain.Rooms;

namespace Pangya.Protocol.KR645.Game;

/// <summary>
/// Saída da partida para o cliente 645 (docs/protocolo/SPEC-ingame.md): transforma os eventos do
/// <see cref="StrokeGame"/> em pacotes para os humanos da sala, e monta a tacada do bot.
/// </summary>
public sealed class InGameOutput(Room room, bool botPasses, BotGolfer golfer) : IGameOutput
{
    // ids S->C
    public const ushort SLoading = 0xA1, SWind = 0x59, SHoleStart = 0x51, STeeReady = 0x8E, SShot = 0x53, SShotResult = 0x62,
        SNextTurn = 0x61, SNextHole = 0x63, SGameEnd = 0x64, STimeOut = 0x5A, SPlayerLeft = 0x5F, SCutIn = 0x192,
        SAim = 0x54, SGauge = 0x55, SPowerShot = 0x56, SClub = 0x57, SUseItem = 0x58, SDrop = 0x5E, SPause = 0x89,
        STimeBooster = 0xC5, SShotCommand = 0x9A;

    public const int ShotLength = 0x2E;     // bloco da tacada (CGolfRule::HitShot)
    public const int ResultLength = 0x25;   // sShotResult

    /// <summary>Última tacada humana (o bot usa como base) e o que vinha depois do bloco (tempo de sincronia).</summary>
    byte[]? template;
    byte[] trailer = [];
    StrokeGame Game => room.Game!;

    /// <summary>Manda para todos os humanos da sala (menos <paramref name="except"/>); o writer é liberado.</summary>
    public static void Broadcast(Room room, PacketWriter w, GameHandler? except = null)
    {
        foreach (var s in room.Humans)
            if (s != except) ((GameHandler)s).Connection.Send(w.Body);
        w.Dispose();
    }

    void All(PacketWriter w) => Broadcast(room, w);

    public void Wind(byte wind, byte direction) => All(new PacketWriter(SWind).U8(wind).U8(0).U16(direction).U8(1));
    public void HoleStart(GamePlayer first) => All(new PacketWriter(SHoleStart).U32(first.Guid));
    public void TeeReady() => All(new PacketWriter(STeeReady));
    public void NextTurn(GamePlayer p) => All(new PacketWriter(SNextTurn).U32(p.Guid));
    public void NextHole() => All(new PacketWriter(SNextHole));
    public void PlayerLeft(GamePlayer p) => All(new PacketWriter(SPlayerLeft).U32(p.Guid));

    /// <summary>0x64: u8 n, n × 33 bytes {guid, rank, placar vs par, tacadas, u16, pang, u32, bônus, 12 bytes}.</summary>
    public void GameEnd(List<GameResult> results)
    {
        var w = new PacketWriter(SGameEnd).U8((byte)results.Count);
        foreach (var r in results)
            w.U32(r.Guid).U8((byte)r.Rank).U8((byte)(sbyte)r.ScoreVsPar).U8((byte)Math.Min(r.TotalStrokes, 255)).U16(0)
             .U32(r.Pang).U32(0).U32(r.BonusPang).Zeros(12);
        All(w);
        Log.Info($"sala {room.Index}: fim de jogo");
    }

    public void BotTurn(GamePlayer bot)
    {
        if (botPasses)
            All(new PacketWriter(STimeOut).U32(bot.Guid));         // estouro de tempo: +1 tacada, os clientes confirmam
        else
        {
            var block = BotBlock(template, PlanBot(bot));
            OnShot(bot, block);
            All(new PacketWriter(SShot).U32(bot.Guid).Bytes(block).Bytes(trailer));
        }
        Game.BotShoot(bot);
    }

    /// <summary>Tacada do bot (BotGolfer); sem os dados do buraco (0x1A), um drive reto na mira da última tacada.</summary>
    BotShot PlanBot(GamePlayer bot)
    {
        if (!Game.Holes.TryGetValue(Game.Hole, out var h))
            return new BotShot(ShotModel.Driver, 0.9f, template != null ? BinaryPrimitives.ReadSingleLittleEndian(template.AsSpan(0x19)) : 0);
        var (x, z) = StartOf(bot);
        return golfer.Plan(x, z, h.PinX, h.PinZ, Game.WindStrength, Game.WindDirection, cautious: botOutHole == Game.HoleIndex);
    }

    /// <summary>
    /// Bloco de 46 bytes de uma tacada reta (docs/protocolo/SPEC-ingame.md, "Bot"): barra, mira e taco do plano;
    /// impacto (+0x04) = centro do impacto (+0x21); sem efeito (+0x08/+0x0C), fase 4, sem tacada especial (+0x11),
    /// +0x26 e +0x2A zerados (o +0x2A soma na mira). +0x15, +0x1D e +0x21 vêm da última tacada humana, se houver.
    /// </summary>
    public static byte[] BotBlock(byte[]? template, BotShot shot)
    {
        var b = template != null ? (byte[])template.Clone() : new byte[ShotLength];
        var s = b.AsSpan();
        float center = BinaryPrimitives.ReadSingleLittleEndian(s[0x21..]);
        if (template == null || !(center is >= 100 and <= 180))
        {
            center = ShotModel.BarStart;
            BinaryPrimitives.WriteSingleLittleEndian(s[0x21..], center);
        }
        if (template == null) BinaryPrimitives.WriteInt32LittleEndian(s[0x1D..], 3000);
        BinaryPrimitives.WriteSingleLittleEndian(s, shot.Bar);
        BinaryPrimitives.WriteSingleLittleEndian(s[0x04..], center);
        BinaryPrimitives.WriteSingleLittleEndian(s[0x08..], 0f);
        BinaryPrimitives.WriteSingleLittleEndian(s[0x0C..], 0f);
        b[0x10] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x11..], 0);
        BinaryPrimitives.WriteSingleLittleEndian(s[0x19..], shot.Aim);
        b[0x25] = (byte)shot.Club;
        BinaryPrimitives.WriteSingleLittleEndian(s[0x26..], 0f);
        BinaryPrimitives.WriteSingleLittleEndian(s[0x2A..], 0f);
        return b;
    }

    /// <summary>Guarda a tacada humana (base para o bot).</summary>
    public void Remember(ReadOnlySpan<byte> block, ReadOnlySpan<byte> after)
    {
        template = block.ToArray();
        trailer = after.ToArray();
    }

    // ---- calibração: valores da tacada x deslocamento real da bola (log + BotGolfer.Calibration)
    float shotAim, shotBar, shotStartX, shotStartZ;
    int shotClub;
    byte shotWind, shotWindDir;
    bool shotClean, shotByBot;
    /// <summary>Buraco (índice) em que a bola do bot foi para a água/OB: joga com cautela até o fim dele.</summary>
    int botOutHole = -1;

    /// <summary>Registra a tacada (humana ou do bot), de onde a bola saiu e o vento.</summary>
    public void OnShot(GamePlayer p, ReadOnlySpan<byte> block)
    {
        shotBar = BinaryPrimitives.ReadSingleLittleEndian(block);
        shotAim = BinaryPrimitives.ReadSingleLittleEndian(block[0x19..]);
        shotClub = block[0x25];
        (shotStartX, shotStartZ) = StartOf(p);
        (shotWind, shotWindDir, shotByBot) = (Game.WindStrength, Game.WindDirection, p.IsBot);
        // limpa = sem efeito, sem tacada especial, fase 4 e sem o ajuste extra de mira (+0x2A)
        shotClean = BinaryPrimitives.ReadUInt32LittleEndian(block[0x11..]) == 0 && block[0x10] == 4
            && MathF.Abs(BinaryPrimitives.ReadSingleLittleEndian(block[0x08..])) < 0.01f
            && MathF.Abs(BinaryPrimitives.ReadSingleLittleEndian(block[0x0C..])) < 0.01f
            && MathF.Abs(BinaryPrimitives.ReadSingleLittleEndian(block[0x2A..])) < 0.001f;
        Log.Info($"sala {room.Index} tacada {(p.IsBot ? "BOT" : p.Guid.ToString())}: barra={shotBar:F1} impacto={BinaryPrimitives.ReadSingleLittleEndian(block[4..]):F1} " +
                 $"fase={block[0x10]} especial=0x{BinaryPrimitives.ReadUInt32LittleEndian(block[0x11..]):X} mira={shotAim:F4} taco={shotClub} " +
                 $"+0x15={BinaryPrimitives.ReadUInt32LittleEndian(block[0x15..])} +0x21={BinaryPrimitives.ReadSingleLittleEndian(block[0x21..]):F1} " +
                 $"+0x26={BinaryPrimitives.ReadSingleLittleEndian(block[0x26..]):F3} +0x2A={BinaryPrimitives.ReadSingleLittleEndian(block[0x2A..]):F3} " +
                 $"de=({shotStartX:F1},{shotStartZ:F1})");
    }

    (float, float) StartOf(GamePlayer p) =>
        p.HasPos ? (p.X, p.Z) : Game.Holes.TryGetValue(Game.Hole, out var h) ? (h.TeeX, h.TeeZ) : (0f, 0f);

    /// <summary>Registra para onde a bola foi de fato (direção e distância reais) e alimenta a calibração do bot.</summary>
    public void OnResult(ShotResult r)
    {
        float dx = r.X - shotStartX, dz = r.Z - shotStartZ;
        var pin = Game.Holes.TryGetValue(Game.Hole, out var h) ? $" bandeira=({h.PinX:F1},{h.PinZ:F1})" : "";
        bool learned = shotClean && golfer.Calibration.Observe(shotClub, shotBar, shotAim, shotStartX, shotStartZ, r.X, r.Z,
            shotWind, shotWindDir, r.State, learnDistance: shotByBot || shotClub > 2);
        if (shotByBot && r.State == ShotResult.StateWaterOrOut) botOutHole = Game.HoleIndex;
        var cal = golfer.Calibration;
        Log.Info($"sala {room.Index} resultado {r.Guid}: pos=({r.X:F1},{r.Y:F1},{r.Z:F1}) estado={r.State} " +
                 $"direção real={ShotModel.AimTo(dx, dz):F4} (mira {shotAim:F4}) distância={MathF.Sqrt(dx * dx + dz * dz):F1} " +
                 $"(prevista {ShotModel.Distance(ShotModel.RangeYards(shotClub), shotBar):F1}, barra {shotBar:F1}, taco {shotClub}, " +
                 $"vento {shotWind}/{shotWindDir}){pin} calibração{(learned ? "+" : "")}: fator={cal.DistanceFactor:F3} " +
                 $"desvio={cal.AimOffset:F4} n={cal.Samples}");
    }

    /// <summary>
    /// Separa o C->S 0x12 em (bloco de 46 bytes, resto). Layout (CGolfRule::HitShot): u16 0, ou u16 1 + u32 n + n×8
    /// (cards) + vento (u8+u32 ou u32); depois o bloco; depois, às vezes, um f32 de sincronia que tem de ir junto no eco.
    /// </summary>
    public static bool SplitShot(ReadOnlySpan<byte> rest, out int start, out int trailerLength)
    {
        start = trailerLength = 0;
        int n = rest.Length;
        if (n < 2 + ShotLength) return false;
        Span<int> starts = stackalloc int[2];
        int count = 0;
        if (BinaryPrimitives.ReadUInt16LittleEndian(rest) == 0) starts[count++] = 2;
        else if (n >= 6)
        {
            long cards = BinaryPrimitives.ReadUInt32LittleEndian(rest[2..]);
            long b = 6 + 8 * cards;
            if (b + 5 <= n) starts[count++] = (int)b + 5;     // vento u8 + u32 (KR)
            if (b + 4 <= n) starts[count++] = (int)b + 4;     // só u32
        }
        for (int i = 0; i < count; i++)
        {
            int tail = n - starts[i] - ShotLength;
            if (tail is 0 or 4) { start = starts[i]; trailerLength = tail; return true; }
        }
        start = n - ShotLength;                              // formato desconhecido: os últimos 46 bytes
        return true;
    }
}
