# KR 645 — in-game protocol (CGolfTask), as implemented by `ingame.py`

Sources (read-only): `gt` = /root/ghidra-out/golftask.c (`CGolfTask::OnPacket` L3679-6920), `gr` = /root/ghidra-out/golfrule.c
(`CGolfRule::HandleMsg` L29747, `ChangeGameMode` L22159, `OnProcess` L31847), `opc` = scratchpad/opc.c (OnPacketCommon),
`gb` = golfbg.c, rebang sources `golfrulebase.cpp`, `golfrulestroke.cpp`, `golfdoc.h`, `shared/globalgamedefine.h`, `shared/localize_kor.h`.
Flow names (`PM_NET_*`) are the client's own debug strings. Structure/ordering hints from the JP 983 server (KR S->C id = JP id - 2 in
0x4x-0x9x) and the S6 GB server (C->S ids identical to KR); **all byte layouts below are from the KR decompile**.
LE integers, f32 = IEEE float.

## Hand-off from room.py
room.py sends (OnPacketCommon) `0x74` (game player list: u8 0, u8 n, n x {sUserInfo 0xB92, SYSTEMTIME, u8 cards}; opc L5416) and
`0x50` (game init; opc L4553: u8 map, u8 gameType, u8 holeType, u8 holes, u32 seed (doc+0x4568), u32 shotTime, u32 gameTime,
18 x {u32 holeSeed, [u8 map if type 0xE], u8 holeNumber}, gimmicks u32 seed + 18 x u8 count (contentsdoc.cpp:983)) -> `ChangeTask("CGolfTask")`.
Everything after that is ingame.py. Room state is read from `sess.state['room']` (schema in room.py docstring): `players[]` (uid, guid,
nick, bot, sess), `holes`, `hole_order`, `key` (16-byte sRoomInfo.RoomKey), `game_seed`, `state`. At game end ingame calls
`room.finish_game(room)` (sets the room back to 'waiting').

**Player id used in every in-game packet = guid** = sSlotInfo.dwGuid = sUserInfo.dwGuid (@0x5B) = MyGuid = sPlayerData.oid (@0x4a8).
`CSharedDoc::AreWePlayingTogether` compares it with sPlayerData+0x4a8 (shareddoc.c:34894) and `GetIndex` maps it to the slot index
(shareddoc.c:52395, filled by `SetIndex(dwGuid)` in CGolfRuleStroke::SetPlayer). It must be unique and must not be 0xFFFFFFFF.

## Packet ids handled by ingame.py (C->S)
`0x11 0x12 0x13 0x14 0x15 0x16 0x17 0x19 0x1A 0x1B 0x1C 0x22 0x30 0x31 0x33 0x34 0x42 0x48 0x65 0xE7` (+ `on_disconnect`).
Not handled (logged by game.py as unhandled): 0x18 chat icon, 0x36 go/stop.

## Sequence (stroke, solo or with bot)
```
client                                   server
-- load ------------------------------------------------------------
0x48 u8 pct (several)                    0xA1 u32 guid,u8 pct  -> other players (gt 6212 UpdateLoadingInfo)
0x1A hole data (SetPlayer->SendHoleData)
0x11 load ok (OnInitFinished gt:1795)    when all humans sent 0x11:
                                         0x59 wind            (gt 4583)
                                         0x51 u32 guid first  (gt 4353 -> msg 0x81 PM_NET_GAMESYNC_START, gr 30619:
                                                               doc+0x175 = GetIndex(guid), mode 2 PlayerPreview)
0x34 tee-shot ready (DoPlayerPreview,    when all humans sent 0x34:
     gr 29705, waits 20 s)               0x8E (no payload)    (gt 4196 -> msg 0x94 PM_NET_TEESHOT_READY_ACK, gr 31119)
-- each shot --------------------------------------------------------
[0x13/0x14/0x15/0x16 while aiming]       relays 0x54/0x55/0x56/0x57 to the others
0x12 shot (HitShot gr 26758,             0x53 u32 guid + 46-byte shot data to ALL incl. shooter (gt 4419 -> msg 0x7a gr 30261:
     waits 20 s "HitShot")                     copies data to doc+0x440.., ChangeGameMode(0x40) Drive)
0x1B shot result from EVERY client       0x62 37-byte sShotResult, decrypted, once (gt 4765 -> msg 0x79 PM_NET_SHOT_RESULT,
     (ChangeGameMode(0x40) precomputes         gr 30175; only accepted while mode&0x40; clears the "HitShot" wait;
      the flight and calls SendShotResult)     CorrectShotResult)
   ball flies (0x80), stops -> mode 0x800 / 0x1000 hole-in / 0x80000 despair / 0x4000 time-out
0x1C shot finished from EVERY client     when all humans sent 0x1C (client waits 20 s, else error dialog + back to lobby):
   (DoToDefaultCamera gr 28721,            - someone still playing the hole: 0x59 wind + 0x61 u32 guid
    DoHoleInPose 27651, DoDespairPose        (gt 4739 -> msg 0x7d PM_NET_NEXT_TURN, mode 0x800)
    27331, DoTimeOut 29600)                 - hole over, not last: 0x63 (gt 4800 -> msg 0x7e PM_NET_NEXT_HOLE, mode 0x2000)
                                            - hole over, last hole: 0x64 game end (gt 4818 -> msg 0x80 PM_NET_GAME_END)
-- next hole ---------------------------------------------------------
DoScoreBoard (gr 27489): advances doc+0x168 itself (hole order from 0x50), sends 0x1A, waits 40 s "DoScoreBoard";
GolfBg msg 0x140 -> CGolfBg::DoReLoad (gb 1716) -> 0x11  ==> server again 0x59 + 0x51 (clears the wait) -> 0x34/0x8E ...
-- end ---------------------------------------------------------------
0x64 -> doc+0x14c = 1, mode 0x2000 -> DoScoreBoard sees 0x14c -> 0x20000 DoGameOver (gr 15537): after 10 s sends msg 0x8f(1)
to itself -> ReturnLobby(false) (gr 14093): ChangeTask("CLobbyTask"), "GAMEROOM". No server packet needed to leave the course.
```
Turn order used by the server (stroke play): hole start = slot order on hole 1, then fewest strokes on the previous hole; during a hole the
player farthest from the pin (positions from 0x1B, pin from 0x1A) plays; players still on the tee go first in tee order.
A player is done with the hole when the result state is 4 (holed) or strokes >= par+4 (CGolfRuleBase::CheckGiveUp, golfrulebase.cpp).

## Layouts
**C->S 0x1A** hole data (`CGolfRule::SendHoleData` gr 7963): u8 hole number, then 0x19 bytes copied from sHoleData (golfdoc.h:183):
u32 0, u32 checkSum (sHoleData+0x30), u8 par, f32 tee.x, f32 tee.z, f32 pin.x, f32 pin.z.

Confirmed by a real-client capture (after 0x74+0x50): `0100000000700e000004b85e6ec385eb38c4d7a36e420a670b44` = hole 1, 0, 0xe70, par 4,
tee (-238.4, -739.7), pin (59.7, 557.6); preceded by 0x48 progress 02..0b. Every per-player sUserInfo in 0x74 needs a valid club tid
(@0xB45), character and parts, or CScreen::OnInit crashes while CGolfTask loads (room.py now uses player.patch_user_info).

**C->S 0x12** shot (gr 26758): `AddSomeInfo` (gr 14589): u16 0 (~70% of shots, nothing else) or u16 1 + u32 n + n x 8 card bytes
+ wind info (u32 0, or u8 kind + u32 intensity); then the **46-byte shot data**; then, when the GroundItemMan actor exists,
an **f32 sync time** appended by msg 0x278 (NetSyncActor::HandleMsg, netsyncactor.cpp:39). `ingame.split_shot` finds the block
from the length and echoes the trailing f32 after the block in 0x53 (the 0x53 receiver passes the packet rest to GroundItemMan
msg 0x279 = Decode4 sync time). All real captures so far are `u16 0 + 46` (48 bytes, no trailer).

**shot data (46)** = S->C 0x53 payload after the guid; read back by msg 0x7a (gr 30261) into doc+0x440:
| off | type | meaning (source field) |
|---|---|---|
|0x00|f32|power bar position doc+0xd0 (140..500, BAR_START/BAR_END golfdoc.h:180; power = sqrt((x-140)/360), gr ChangeGameMode(0x40))|
|0x04|f32|impact bar position doc+0xd4 (also copied to d8/dc)|
|0x08|f32|spin x (ball+0x34)|
|0x0C|f32|spin y / curve (ball+0x38)|
|0x10|u8 |bar phase doc+0xe0 (4 counts as "pangya" in ChangeGameMode(0x40))|
|0x11|u32|special-shot flags (ball+0x44)|
|0x15|u32|PVS+0x21c0|
|0x19|f32|aim angle doc+0x10c, radians from world +Z: dir = (-sin a, 0, cos a); angle to a target = atan2(-dx, dz) (wmath.cpp:283, rival.cpp:271). The old `(sin,0,cos)` / `atan2(dx,dz)` here was wrong (mirrored). Final yaw = a + f32(+0x2A) + phase/curve delta|
|0x1D|i32|shot time doc+0x170|
|0x21|u32|doc+0xec|
|0x25|u8 |club index|
|0x26|f32|club+100|
|0x2A|f32|CGolfRule+0x278|

**C->S 0x1B** (gr 14329): 0x25-byte sShotResult XOR-encrypted (S3_SHOTRESULT_ENCRYPT is on in KR, localize_kor.h) with
`key[i % 16]` (`CEncryption::Encrypt<sShotResult>` gr 6252), followed by the 16-byte key itself (doc+0x49cd = sRoomInfo.RoomKey @0x35).
**sShotResult (37)** = S->C 0x62 payload (plain): u32 guid of the shooter (turn player), f32 x, f32 y, f32 z (final ball pos),
u8 state (3 = water/OB ball[0x58], 4 = holed m_holeIn==1, 2 / 5 = normal), u8 player[0x4ca], u8 CGolfRule[0x5a8], u32 totalPang
(player 0x4e8), u32 bonusPang (player 0x4f8), u32 player+0x4d0, u32 flags (ball+0x48 bits -> 2/4/8/0x10/0x20/0x40/0x80), u16 ball+100.

**C->S 0x1C** (`EncodePacket` gr 20003): u8 (turn player == me), u8 n, n x {u8, u32, u8, u8} picked ground items.
**C->S 0x11, 0x34**: no payload. **0x48**: u8 percent. **0x31**: hole statistics (ignored).
**Relays** (C->S -> S->C): 0x13 (4 bytes, camera.c:2022) -> 0x54 u32 guid + 4; 0x14 (5 bytes, gr 16746) -> 0x55 guid + 5;
0x15 u8 (golfplayer.c:8541) -> 0x56 guid + u8; 0x16 u8 (club.c:1388) -> 0x57 guid + u8; 0x19 WVector 12 (camera.c:695) -> 0x5E 12 bytes
(to all, msg 0x8d Drop); 0x30 u8 (gr 9306) -> 0x89 guid + u8 (msg 0x84 pause dialog); 0x65 4 bytes (gr OnProcess) -> 0xC5 4 bytes + u32 guid.

**S->C 0x59** wind (gt 4583): u8 wind, u8 card flag, u8 b3, u8 b4, u8 b5 -> `CWind::Init(wind, b3, b4, b5)`; sent as wind 0-8, 0, u16 direction
(0..255), 1 (JP layout wind/flag/u16 degree/reset).
**S->C 0x51 / 0x61 / 0x5A / 0x5F**: u32 guid (0x61 and 0x63 carry extra fields only in Pang Battle, type 7).
**S->C 0x5A** time-out (gt 4605 -> msg 0x7c: mode 0x4000 DoTimeOut: +1 stroke, client sends 0x1C). Used for the bot in 'pass' mode.
**S->C 0x5F** player left (gt 4680, removes the slot, msg 0x82). **S->C 0x8E, 0x63**: no payload.
**S->C 0x64** game end (gt 4818; non-mass, type != 7, id != 0x8f): u8 n, n x 33-byte record {u32 guid, u8 rank (high nibble of
player 0x4e0), i8 score vs par (0x4e4), u8 total strokes (0x4e1), u16 ?, u32 totalPang (0x4e8), u32 ?, u32 bonusPang (0x4f8),
12 bytes (Pang Battle u64 at +0x19)} (consumer: HandleMsg 0x80 gr 30480).

## Timeouts / keep-alive
- Every "wait" in CGolfRule is `SetNetMsgWait(..., 20.0)` (40 s for DoScoreBoard); on expiry `OnProcess` (gr ~32170) reports an error and
  opens a notify dialog whose OK returns to the lobby (`ReturnToLobbyDlgResult`). The server therefore answers 0x12 (0x53), 0x1B (0x62),
  0x1C (0x61/0x63/0x64), 0x34 (0x8E), 0x11 (0x51) immediately. A 15 s fallback sends 0x8E anyway if a client never sends 0x34.
- No heartbeat is required in-game (0xF6 from the client is ignored by game.py; nothing in CGolfTask checks server traffic).
- The shot clock (shotTime in 0x50) is not enforced by the server; the client's own clock UI is cosmetic unless the server sends 0x5A.

## Bot
Room player with `bot=True`, `sess=None` (room.py `!bot` chat command). On its turn (after `EMU_BOT_DELAY`, default 4 s):
- `EMU_BOT_MODE=shoot` (default): server sends 0x53 with a fabricated 46-byte block. Each human client simulates the bot's ball
  physics and reports it in 0x1B (guid = bot), which is relayed as 0x62 like any shot; bot gives up at par+4 like everyone else.
- `EMU_BOT_MODE=pass`: server sends 0x5A (time-out); clients add a stroke and send 0x1C; turn rotates; the bot gives up at par+4.

**Servidor C# (2026-10-07): o bot segue o modelo do oponente de computador do próprio cliente** (CRival::SetVariable, rival.cpp).
Lógica pura em `src/Pangya.Domain/Rooms/BotGolfer.cs` (ShotModel, BotGolfer, ShotCalibration); o bloco é montado em
`InGameOutput.BotBlock` (src/Pangya.Protocol.KR645/Game/InGame.cs).
- Mira: a = atan2(-dx, dz) (direção (-sen a, 0, cos a)); +0x2A = 0 (soma na mira final).
- Distância linear na barra (rival.cpp:87): unidades = alcance_jd × 3,2 × (barra − 140)/360 × lie, ou seja
  barra = 140 + 360 × D / (alcance × 3,2). 1 jarda = 3,2 unidades (confirmado). O lie (rough etc.) não chega ao servidor: lie = 1.
- Tacos (+0x25) e alcance em jardas: 0 1W 230, 1 2W 210, 2 3W 190, 3 2I 180, 4 3I 170, 5 4I 160, 6 5I 150, 7 6I 140, 8 7I 130,
  9 8I 120, 10 9I 110, 11 PW 100, 12 SW 80, 13 1PT 20 (40 "putt longo" fora do green ou a 15+ jd da bandeira), 14 2PT 10 (30).
  Madeiras: + 2 × stat de força (o bot tem o kit de um jogador novo: 0). PW/SW têm alcances especiais perto da bandeira
  (até 30 jd -> 30; 30..58 -> 60): o bot não usa esses tacos (9I até 110 jd fora do green).
- Escolha: putter 1PT a até 20 jd da bandeira (**suposição de green**: o lie da bola não é conhecido no servidor; o 0x1B não diz
  "green" de forma confiável); senão o taco mais curto de 1W..9I que alcança; longe demais = 1W com o alvo na linha da bandeira
  limitado ao alcance (buracos longos em etapas; a geometria do campo — dogleg, árvores, água — não é conhecida no servidor).
  Putt: força = (distância + 1 jd)/alcance, direto na bandeira, sem vento (o cliente usa a inclinação do green, que o servidor
  não tem). Depois de água/OB (estado 3) no buraco, usa 2 tacos mais curtos (como o CRival, até o 9I).
- Vento (0x59, wind.cpp): W = (byte+1) × (-sen(d × 0,02464), 0, cos(d × 0,02464)); o oponente do cliente mira em alvo − 3 × W
  (madeiras/ferros; putts não). A força também usa |alvo − pos − 3W|.
- Bloco (tacada reta): +0x00 barra; +0x04 = f32 em +0x21 (centro do impacto, 140); +0x08/+0x0C = 0 (sem efeito);
  +0x10 = 4; +0x11 = 0 (sem tacada especial); +0x15, +0x1D, +0x21 da última tacada humana (sem modelo: +0x21 = 140f,
  +0x1D = 3000); +0x19 mira; +0x25 taco; +0x26 = 0; +0x2A = 0.
- Imprecisão: `Game.BotAccuracy` (0..1, padrão 0,85): mira ± (1 − p) × 0,15 rad e força × (1 ± (1 − p) × 0,3), uniformes.
- Calibração online (ShotCalibration, média móvel α = 0,3): para cada tacada limpa (fase 4, sem efeito/especial, +0x2A = 0) que não
  é putt, água/OB nem bola no buraco, compara o deslocamento real (0x1B, menos 3W) com o previsto (mira, barra, taco): desvio de mira
  (limitado a ±0,15 rad) de todos; fator de distância (0,8..1,25) só das tacadas do bot e dos ferros humanos (madeiras dependem do
  stat de força de cada um). Desvios > 0,3 rad ou razão fora de 0,5..2 são descartados (obstáculo/curva). O log
  "resultado" mostra direção real x mira, distância real x prevista e o estado da calibração.

## Uncertainties
- Not verified against the real client: whether the client counts the OB/water penalty exactly like the server (+1 for state 3),
  the semantics of state 2 vs 5, the meaning of the 0x64 fields marked `?`, and the wind byte meaning (direction encoding).
- Bot shots (C#): angle sign, linear bar, yard scale and club ranges come from the decompiled client; still to confirm with real-client
  logs: the 3 × W wind drift, the putter range near the green (20 vs 40 jd under 15 jd depends on the lie, unknown to the server),
  the lie factor on rough/bunker, and whether +0x15/+0x1D from the human template are fine for the bot.
- 0x34/0x8E is assumed once per hole (doc+0x14e); if the client only sends it on hole 1 the 15 s fallback covers the bot case.
- Game result/EXP screen packets (0x77 doc+0x456e, 0xC6 pang update, 0xCA prizes) are not sent; pang/EXP are not credited to the account.
- Only stroke rules are implemented (match/team/skins/30s modes use other ids/fields).

## Fixes after the first real-client test (2026-10-07 16:17 / 16:18 logs)
Symptom: after the swing nothing happened for 20 s, then client error report `0x33 "C HitShot(...) / PM_NET_TEESHOT_READY_ACK"`.
Cause: the shooter sends **0xE7** (cut-in query) immediately before 0x12; HitShot then sets the net wait "HitShot" (golfrule.c 26829,
20 s). In online play the 0x62 shot result does NOT clear it (verified in the binary: HandleMsg case 0x79 @0x46402d, OnlinePlay()!=0 ->
only Cutin msg 0x78; the clear at 0x46404f is the offline branch). The only clear is **S->C 0x192** (golftask.c case 0x192 ->
GolfRule msg 0x24d(0) -> `SetNetMsgWait(false)`, golfrule.c 31468). While the wait is set `DoDrive`/`DoFlyBall` do not advance
(golfrule.c 27867/27934), i.e. no swing/flight until the 20 s error.
- **C->S 0xE7** (S5::UTIL::SendCutInPacket, utilities.obj @0x8695d0, symbolized with ProjectG_ReleaseQA.map): u32 guid (doc+0x4eb),
  u16 cut-in type, u32 (doc+0x4560 or 6), u32 character tid. Reply to the shooter only: **S->C 0x192 u8 0, u16 0** ("NON CUTIN_MODE").
  (u8 1 + u32 skinTid + u32 + 0xB8 bytes would play a cut-in animation instead.)
- **C->S 0x22** (no payload): sent by CClock::HandleMsg @0x4cc3e6 when the local player's shot clock starts (shot time set, my turn).
  No reply (S6 PLAYER_MY_TURN / JP requestStartTurnTime).
- **C->S 0x42**: u8 n, n x u32 (HitShot @0x45e17b, list doc+0x80, only when doc+0x84 != 0). Relayed to the other players as
  **S->C 0x9A** (same layout; golftask.c case 0x9a -> doc+0x7c list, Cutin msg 0x78(8)). Real sample `02 01000000 10000000` = [1, 0x10].
- **C->S 0x33**: u8 kind, str text (error report) -- logged as a warning.
- Verified byte content: 0x59 = `59 00 | wind | 00 | dir | 00 | 01` -> CWind::Init(wind, dir, 0, reset=1) (wind.cpp:901: intensity =
  wind+1 (1..9), direction = dir*0.02464 rad, dir also seeds the PVS rand); 0x51 = `51 00 | guid u32`; 0x53 = `53 00 | guid u32 |
  last 46 bytes of the 0x12` (byte-identical to what the client sent, checked with the captured 0x12).
- Real 0x12 is `u16 0 + 46 bytes` (no wind block: the HACKINGTOOL u8/u32 branch is only written when the wind differs).

Open points from that test (not explained by the server side yet):
- The 0x1B results put the ball at/near the tee (room 1: 5 units ahead, state 2; room 2: exactly the tee, state 3 = OB flag). The whole
  flight is simulated client-side inside ChangeGameMode(0x40) (golfrule.c 22159+, loop over CQuadTree::BallProcess) **before** 0x1B is
  sent, from the client's own shot values (493/500 power bar, 1W, angle -0.016 rad) -- nothing the server sends influences it. Re-test
  after the 0x192 fix; if it persists, suspect the KR645-exe + KR642-data mismatch (course collision/ball spawn y) or the per-player
  sUserInfo stats (character/club PCL) used by BallSpeedFactor.
- "No wind shown / no ball visible": the 0x59 bytes are correct per CWind::Init; may be a side effect of the stuck HitShot state or of
  the same data mismatch. "Character only loads after re-entering the room" is in the room screen (room.py 0x46/0x74), not in-game.

## Shot judgement (2026-10-07 17:05 investigation: "every shot is BAD / SPIN")
Verified in the decompile + captures; the judgement is 100% client-side and happens BEFORE 0x12 is sent:
- Gauge: CGolfRule::DriveGauge (gr 28078) moves doc+0xd8 (140 -> 500 -> back; speed = club bar speed - control*10, per frame dt);
  1st key = power (doc+0xd0), 2nd key = impact (doc+0xd4/dc). The client sends both clicks as C->S 0x14 (u8 stage, f32 value).
- CGolfRule::GetBarCondition (gr ~8620) sets the phase doc+0xe0 (= shot block byte 0x10): 4 PangYa if |impact - doc+0xec(140)| <
  PangYaArea+0.5 (CPowerGauge::GetPangYaArea = 2 + item/part/card bonuses), else 3 good if |impact-140| < NarrowAccuracy+0.5,
  2 normal if < WideAccuracy+0.5, else 1 bad (CClub::GetNarrowAccuracy = max(2, ceil(acc)), acc = player accuracy stat + lie).
- CAccuracyInfo::SetAccuracy (fontinfo.c 5011, via Screen msg 0x1ac in ChangeGameMode(0x40)) shows "@Bad" only for phase 0/1;
  phase 4 -> "@Pangya"/"@PowerSpin"(ball flags&3)/...; phase 2/3 -> no label when IsLocalContent(0x50). Power exactly 500 -> "@Full"
  (row "Max"). Sprites: [impact.png (128x256, rows of 18 px: Bad, PangYa, PowerSpin!, PowerCurve, Tomahawk, Max, ...) -- the KR642 file
  matches the exe's rects.
- Real phases sent by the client: 16:17 143->3, 16:18 135->3, 16:26 144->3, 16:36 133->3, 16:43 151->3, 17:05 142->**4 (PangYa)**.
  No shot was ever phase 1 (bad). The 0x53 echo is byte-identical to the block (layout checked field by field against HitShot's
  locals and the msg 0x7a reader gr 30261), so the server cannot turn a shot into "Bad".
- The ball flight is precomputed in ChangeGameMode(0x40) with a fixed 0.02 s step (CQuadTree::PreProcess/BallProcess), power =
  CClub::GetPower * sqrt((bar-140)/360) * GetBarClickPowerFactor (lie power from the course PVS material table) * ball factor.
  Every logged shot (human and server-made bot shot, any aim) ends 0-37 units from the tee or OB-at-tee after 3 sim frames; two
  different shots on course 16 ended at the identical point (-63.2, 2.6, -621.5). Not explained by any server value (sUserInfo stats
  can lower 1W power by at most ~20%; wind 1..9). Suspect course collision/material data (KR645 exe + KR642 data) or the engine.

## Power shot and special shots (2026-10-07 investigation)
Everything below is client-side logic; the server's job is relay/echo, plus the in-game item use reply.
[V] = read in the decompile (file:line / address), [I] = inferred, not seen in a real capture yet.

**Gauge** [V]: sPlayerData+0x4b8 (WCrypticValue<float>), per player, kept by EVERY client for the turn player in
ChangeGameMode(0x40) (golfrule.c ~24380-24690, the same routine that precomputes the flight and sends 0x1B):
gain by bar phase (Bar()+0x10 = shot block +0x10) when no power shot is used: 0/1/2 (bad/normal) -1, 3 (good) +4
(+7 with an aux part), 4 PangYa +12 (+14 with Power Spin/Curve), + level handicap and card bonuses; a power shot
costs -33 (type 1) / -66 (type 2/3) unless it came from an item; time-out (mode 0x4000) -30. Clamped to
[0, CSharedDoc::GetComboGaugeLimit] = 99 (132 with some parts). Starts at 0 (golfdoc ctor). Only filled when
doc+0x78 is 0xf/3 (set to 0xf at game start) and channel Type bit 8 is clear (game.py channel(): Type 0).
No server packet carries it in a normal game (only the resume/observer packets and 0x5C caddie "reset gauge").

**Power shot input** [V] CGolfPlayer::PowerShotInput @0x4e9b40: Alt (LALT/RALT) released once -> after 0.5 s
mode 1 = power shot (needs gauge >= 33, Decide_PowerShot @0x4e5e50); Alt tapped twice within 0.5 s -> mode 2 =
double power shot (gauge >= 66); Alt again while armed -> cancel. When the charge animation completes the client
sends **C->S 0x15 u8 type** (golfplayer.c:8947, type = 1/2/3); cancel sends **0x15 u8 0** (Cancel_PowerShot :8541).
Server: **S->C 0x56 u32 guid, u8 type** to the other players (golftask.c case 0x56 -> Player msg 0x11f ->
CClub::SetPowerShot on the turn player's club; own guid ignored). The power shot is NOT in the 46-byte block:
receivers must get 0x56 (or 0x58 for items) before 0x53, which TCP order guarantees (0x15 precedes 0x12).
GB/JP reference servers do the same (broadcast, no validation).

**Special shots** [V] CGolfRule::CommandBox @0x44f7e0 records keys into doc+0x7c list while the shot is being
made: only after the power click, while the power bar is >= 140+0.8*L (L = bar length, 360 [I]) and the returning
cursor is in the lower half (<= 140+L/2), until the impact click. Key names 상/하/좌/우 = arrow keys or W/S/A/D
(projectg.cpp:569 bindings); entry values 1 UP, 2 DOWN, 4 LEFT, 8 RIGHT; a key held > 100 ms appends 0x10 and
ends the input; two keys at once append 0x40. At impact (GetBarCondition true = phase 2/3/4) CalcInputCommand
folds the first two entries into a code (shift by 8 except for 0x10) and SetSpecialShot @0x4448c0 maps it to
ball+0x44 (= shot block +0x11):
| keys | code | flag | name |
|---|---|---|---|
| UP, DOWN | 0x102 | 0x10 | Tomahawk |
| RIGHT, UP | 0x801 | 0x20 | Cobra |
| RIGHT, DOWN | 0x802 | 0x40 | Spike |
| UP held / DOWN held | 0x11 / 0x12 | 0x01 / 0x02 | Power Spin top/back (PangYa only) |
| LEFT held / RIGHT held | 0x14 / 0x18 | 0x04 / 0x08 | Power Curve (PangYa only) |
| special + 3rd key held | + 0x11..0x18 | 0x10/0x20/0x40 \| 1..8 | e.g. Tomahawk+Power Spin (PangYa only) |
CheckSpecialShotSpinAndCurve @0x444b80 clears Tomahawk/Cobra/Spike without an armed power shot, and Cobra/Spike
unless the club category is 0 (woods); Power Spin/Curve also need the spin/curve set to the matching side.
HitShot then sends 0x12 (block with +0x10 phase, +0x11 flags) and **after it 0x42 u8 n, n x u32 (the key list)**.
Server: 0x53 echo byte-exact (flags included) to all; 0x42 -> **0x9A** (same bytes) to the others (cut-in UI only).
Label shown: CAccuracyInfo::SetAccuracy fontinfo.c:5040 ("@Power" prefix for double PS + Tomahawk/Cobra/Spike).

**In-game items** [V] itemwindow.cpp UseItem/NetUseItem/ProcessItem: C->S **0x17 u32 tid** (only on your turn);
the client does nothing until **S->C 0x58 u32 tid, u32 random, u32 guid** arrives (golftask.c case 0x58, online:
msg 0x19f -> NetUseItem); sent to EVERY player incl. the user (each client applies the effect for the turn
player; the user's client consumes the slot). Fails when (random % 100) < Item.iff COM[1]. Relevant tids (KR642
Item.iff): 0x18000004 체력 보조제 (power shot, 130 pang, in the shop), 0x18000010 체력안정제 / 0x18000022 (power
shot), 0x18000027 체력 강화제 (double power shot, needs S3_STRENGTH), 0x18000025 체력 보충제 (gauge +33).
ingame.py checks the tid is a 0x18xxxxxx item, that it is the user's turn and that it is in the account's equipped
item slots, removes one slot + one unit (like ProcessItem) and saves; room.py now refreshes tidItemSlot in 0x74 from
the current inventory so consumed items are not offered again in the next game.
