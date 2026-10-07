# Game modes (KR 645 QA) — modes.py

Line refs: `gt.c` = CGolfTask::OnPacket excerpt of `/root/ghidra-out/golftask.c` (scratchpad/md/gt.c, starts at golftask.c:3672),
`gr` = `/root/ghidra-out/golfrule.c`, sources under `/root/rebang/source/client/ProjectG/`.
[V] = read in the client code, [I] = inferred (reference servers / reasoning), [T] = covered by test/test_modes.py.

## Mode ids (eGameType, shared/globalgamedefine.h:3) and what the 645 make-room dialog offers

| id | name | dialog (makeroomdlg.cpp:1663-1870) | client rule class (gr 9650-9760) | server |
|----|------|------------------------------------|------------------------------------|--------|
| 0 | stroke | VS group, 2-4 players | CGolfRuleStroke | ingame.Game (unchanged) |
| 1 | team ("매치/match" picture, 2 or 4 players) | VS group | CGolfRuleTeam | modes.TeamGame |
| 2 | avatar chat | chat group | — (no golf) | room only |
| 3 | match 1v1 | only channels with Type&0x80 | CGolfRuleMatch | modes.MatchGame |
| 4 | 30S = tournament (대회), 10/20/30 players, 9/18 holes, 30-50 min | mass group | CGolfRule30Battle | modes.Tourney |
| 5 | 30S team (team tournament) | mass group | CGolfRule30Battle | modes.Tourney [I: same flow] |
| 6 | guild match (needs a guild) | mass group | CGolfRuleGuildMatch | modes.Tourney [I, untested] |
| 7 | skins = "Pang Battle" (팡배틀), 2/4 players | battle group | CGolfRuleSkins | modes.SkinsGame |
| 9 | old approach (not in the dialog) | — | CGolfRuleApproach (no type-9 branches) | modes.Tourney [V gr 30472] |
| 10 | new approach (어프로치), 6/20/30 players, 3/6/9 holes, 40 s | battle group | CGolfRuleNewApproach | modes.Approach |
| 14 | chaos (needs item 0x1a0000f7) | chaos button | (default) | modes.Tourney [I, untested] |

No Grand Prix, chip-in practice or "30 s short game" exists in this client: "30S" is the 30-player tournament.
IsMassGame (@0040fce0) = 4, 5, 6, 9, 10. Identification: 0x08 make room byte `type` (makeroomdlg.cpp:1301), sRoomInfo +0x48/+0xB1,
0x48 room settings, 0x50 byte 1 (doc[0x4668]) [V].

## Hook into ingame.py
- `ingame.HANDLERS = {pid: _mode_hook(pid, fn)}`: `await modes.dispatch(pid, sess, r)` first; True = handled (mass games).
- `ingame.get_game()`: `cls = modes.game_class(room)` (TeamGame / MatchGame / SkinsGame) else `Game`.
- `Game.pkt_next_turn(p)` / `Game.pkt_next_hole()` build 0x61 / 0x63 (overridden for type 7).
- `modes.HANDLERS = {0x35, 0x52}` (registered in game.FEATURE_MODULES). room._leave calls `modes.on_disconnect(sess)`.
- Diff of ingame.py: `modes_ingame.patch` (already applied).

## 1. Tournament (types 4/5/6/9/14) — the bug and the fix
**Bug [V + log]:** in a mass game the client has ONE local player (CGolfRule30Battle::SetPlayer, golfrule30battle.cpp:19
MakePlayer(1)); every other slot (bot included) is a rival (sRivalData, shareddoc.h:105, vector at CSharedDoc+0x4508, 400 bytes).
`CSharedDoc::AreWePlayingTogether` (@00413f80) returns true for every rival, and 0x51/0x61 (gr 30428: mass -> index 0),
0x53 (gt.c 748, copies the shot into the local ball), 0x62 (gt.c 1101: mass -> index 0), 0x55/0x57 all act on the LOCAL ball.
The stroke flow sent "next turn: Bot" + the bot's 0x53 -> the human's own ball flew the bot's shots (server.log 17:48:30-17:49:21:
every 0x1B after a bot shot carried the human's guid 0x302cb9).

**Flow (per player, nobody waits) [V packets / I order]**
| C->S | S->C |
|------|------|
| 0x1A hole data | (par/tee/pin stored) |
| 0x11 loaded | to him: 0x59 wind, 0x51 u32 own guid (msg 0x81, mass index 0) |
| 0x34 | to him: 0x8E (mass DoPlayerPreview does not wait, gr 29714) |
| 0xE7 | 0x192 (ingame.py) |
| 0x12 | nothing (see 'No echo' below) |
| 0x1B (sent twice) | to everybody: 0x6C (first copy only); no 0x62 |
| 0x1C (sent once, twice on the last hole) | not done: **nothing**; hole done (holed or strokes >= par+4): to everybody 0x6B, to him 0x63; last hole: 0x6B, 0x6A(guid,2) to everybody, then 0x63 to him |
| 0x13/0x14/0x15/0x16/0x42 | dropped (would drive the receivers' own ball) |
| 0x19 / 0x30 / 0x65 | 0x5E / 0x89 / 0xC5 to the sender only |
| 0x17 item | 0x58 tid, rand, guid to the sender only |

After the last hole: 0x6A state 2 for the own guid sets golfdoc+0x14c (gt.c 1244); 0x63 -> scoreboard (gr 30473) ->
DoScoreBoard mass branch (gr 27541) -> ReturnLobby -> CLobbyTask "GAMEROOM_EXTRES" (gr 14158) [V]. The lobby keeps applying
0x6A/0x6B (lobbytask.c case 0x6a/0x6b) so the result room ranking updates while others still play [V].
The game ends when every player finished or left -> room.finish_game (state 'waiting').

Packets [V, decode order]:
- **0x6B** (gt.c 1288): u32 guid, u8 hole (number just played; lobby requires == rival.hole), u8 totalStroke, i32 totalScore,
  i64 totalPang, i64 totalBonusPang, u8 1=hole finished (deltas into holeStroke/holeScore/holePang, rival.hole = next of order).
- **0x6A** (gt.c 1208): u32 guid, u8 state (2 finished, 3 quit).
- **0x6C** (gt.c 1412): u32 guid, u8 hole, f32 x, f32 z, [type 10: u32 dist (rival+0x178), u32 time (+0x17C, -1 = out)], u16.
- 0x8A (gt.c 1631) = time over (all playing rivals +5/hole) — **not sent** (no 30-min time limit implemented).

**No echo (fix after the real-client test 18:18-18:23) [V]:** in a mass game CGolfRule::HitShot starts the flight
locally (gr 26801-26822: types 4/5/6/9/10 -> ChangeGameMode(0x40) at once + net wait "modeDriveMotion_MassGame"), and
ChangeGameMode(0x40) applies its own result (gr 23617: offline or IsMassGame -> Player msg 0x77, this[0x264]=1) and
sends 0x1B (gr 24345) and bumps the shot counters (gr 24355-24375). Our former 0x53 echo reached gt.c 748 (shot copied
into the local ball + msg 0x7a -> ChangeGameMode(0x40) again): the flight was replayed, a 2nd 0x1B was sent ~5 ms after
our 0x53 and the HUD stroke/shot counters advanced twice (user: "hit once, counter jumped to 3"; the client's 0x31 hole
stats reported 6/10 strokes for real 3/4). The 0x62 echo (msg 0x79) re-applied the result too. Now neither is sent,
like the reference TourneyBase.requestInitShot / requestSyncShot. 0x61 after 0x1C is still needed: it clears
this[0x3cb] (the HitShot net wait) so DoToDefaultCamera (mode 0x800, gr 28770) can continue to the next shot; the
client re-enters 0x800 and sends 0x1C again (2nd copy, ignored).

**No 0x61 after 0x1C (fix after the 2nd real-client test 18:41) [V]:** with the echoes gone the client's own hole
stats were right (0x31 = the 0xEB stat block doc+0x4118: +0 non-putt shots, +4 putts, bumped in ChangeGameMode(0x40)
gr 24355-24375; 18:43:29 hole 1 = 2+1 = 3 real strokes, 18:44:18 cumulative 3+2 = 5 = 3+2; before the fix 4+2 = 6 for
3), but the HUD stroke counter still doubled. The HUD counter is sPlayerData stroke[hole] (+(hole+1)*0x10, WCrypticValue),
incremented when a shot's turn ends in DoToDefaultCamera (mode 0x800, gr 29046-29052, online branch -> ChangeTurn ->
mode 4). The mass client gets there by itself after the ball stops (DoFlyBall_Stopped -> 0x800); the HitShot net wait
"modeDriveMotion_MassGame" is already cleared by our 0x192 (golftask 0x192 -> msg 0x24d(0) -> SetNetMsgWait(false)),
which is why 30 s flights caused no 20 s error. Our 0x61 -> msg 0x7d (gr 30422) -> ChangeGameMode(0x800) re-entered
DoToDefaultCamera: second 0x1C ~5 ms after the 0x61 (log 18:42:55.398/.405) and stroke[hole] +1 again. Now the shooter
receives per shot exactly: 0x192 (reply to 0xE7) and 0x6C (rival position, broadcast) -- nothing after 0x1C unless the
hole ends (0x6B [0x6A] 0x63). Reference TourneyBase: requestInitShot/requestSyncShot/requestFinishShot send no turn
packet either (only the position 0x6E(=KR 0x6C), cube drops, and hole-end 0x6D(=KR 0x6B)/0x6C(=KR 0x6A)).
Approach (type 10) likewise no longer sends 0x61.

**Power-shot crash (Alt, hole 4, 18:23:28) [V mechanism / I cause]:** CGolfPlayer::Shot_PowerShot (@004e95f0,
golfplayer.c 8431-8464) asks GolfRule msg 0x24a for the desktop windows predict_normal/_tomahwak/_cobra/_spike into
UNINITIALISED locals; HandleMsg 0x24a (gr 31457) only writes them when FrWnd::FindChildByName (non-recursive, desktop
children = MAIN layout items) finds the window -> IObject::DynamicCast on stack garbage = the access violation at
DynamicCast+0xa called from Shot_PowerShot+0x309. Nothing in the server packets selects these windows (MAIN is opened
for every mode, gr 860; the predict buttons' init handlers have no mode check, gr 4769-4820) -> most likely the MAIN
layout in the installed paks has no predict_* buttons and the same crash happens in stroke (no 0x15 has ever been
seen in any log, so power shot was never exercised with the real client). Client-side fix needed (zero-init the
four locals in Shot_PowerShot, or a layout with those buttons); the server cannot avoid it.

**Bot:** does not shoot; simulated: finishes hole i `EMU_BOT_DELAY` s after the first human finished hole i (at least one hole per
`EMU_TOURNEY_BOT_HOLE`=90 s; quickly when all humans are done), strokes = par + {-1,0,0,0,1,1,2}, reported with 0x6C/0x6B/0x6A.

## 2. Approach (type 10) [V unless noted; agent research gr 25993-32264, gt.c 1874-2021]
Simultaneous: one shot per hole per player from his own random start (client GetRandomStartPos), closest to the pin wins.
- 0x11 -> to him 0x59, 0x147 u8 0 (no mission), 0x51 own guid (needed every hole).
- 0x34 -> when all humans sent it (or 12 s after the first) 0x8E to everybody (client 20 s wait, gr 29701).
- 0x12 -> nothing (block +0x1D i32 = remaining ms is recorded). 0x1B -> 0x6C with dist/time to everybody (no 0x53/0x62).
  dist = floor(hypot(ball-pin) * 0.3125 * 10) (tenths of yard), time = remaining ms; OB/water/no 0x12 (client time-out) = 0xFFFFFFFF.
- 0x1C -> nothing (the HitShot lock is released by our 0x192; see 'No 0x61' below).
- All answered (or limit + 15 s): 0x148 (n x sApproachResultData) to everybody, then 0x63 (approach_result dialog -> client
  DoApproachNextHole sends 0x1A). Last hole: 0x148, 0x146 totals, 0x149 -> end dialogs -> GAMEROOM_EXT. 0x64 not used.
- **sApproachResultData** (globalgamedefine.h:1477, 0x18): u8 bExit, u32 guid, u32 uid, u8 rank (255 none), u32 prizeCount,
  u32 remainDistance, u32 remainTime (-1 "Out"), u8 rankPrize, u8 luckPrize.
- Prizes [I]: rank 1/2/3 -> 3/2/1 boxes; totals = sum, ranked by prizes then distance.
- Bot: after 0x8E + delay, random 1.5-25 y, reported with 0x6C.
- Not implemented: missions (0x147 types 1..27), C->S 0x2F winner info request (logged as unhandled), 0xC3.

## 3. Team (type 1) — modes.TeamGame [V client rules, I server choices]
- Sides = room team (sSlotInfo bTeam, 0/1; split by slot if everyone is on one team). One ball per team, members alternate
  (alternate shot, GetNextTurn @0044b070): hole 1 the team of slot 1 tees; next holes the team with fewer strokes on the last
  hole; the tee shooter of each team flips every hole. Then the team farther from the pin plays.
- Hole decided (client SetHoleInPose_Team @00455320 uses the same rule): both teams holed/given up (team strokes >= par+4);
  one holed in S and the other has T >= S; or T == S-1 and holes-won lead > holes remaining (concession).
- 0x63 (empty) after a decided hole; C->S 0x35 (u8 pose, u8 collision) needs no answer.
- End: **0x8F** = u8 n, n x 33-byte record {u32 guid, u8 rank, i8 holes won, u8 strokes, u16 exp, i64 pang, i64 bonus, i64 0},
  u8 team0 holes won, u8 team1 holes won, u8 winner (0/1/2 draw) -> TEAM().score, golfdoc+0xc0 (scoreboard). Early end when
  lead > holes remaining.

## 4. Match (type 3) — modes.MatchGame
Same as team with every player a side (2 players). C->S 0x52 (u8,u8) ignored. End: 0x64 with record score = holes won.

## 5. Pang Battle (type 7, skins) — modes.SkinsGame [V layouts]
- Stroke turn order. **0x61** = u32 guid + u16 shot message (0 sent). **0x63** = u32 hole winner (0xFFFFFFFF = carry-over).
- Winner = unique lowest strokes among holed players; stake = GetHolePang(seq) (@004438d0: 20 x (1 + 0.5 x ((seq-1)//3)),
  last hole x2) x 2^carry (max 8); the client moves the pang itself (CalcSkinsHolePang @00455cf0).
- End: **0x64** = u8 n, u32 last-hole winner, u32 overall winner (-> 5% donation message for that player), records with
  i64 at +0x19 = pang won/lost [I: computed without balances / channel multiplier].

## Tests
`test/test_modes.py` (part 1 in-process through ingame.HANDLERS; part 2 TCP via `test/run_modes.sh`, ports 41000-41002):
tournament 1 human + bot, 2 humans + bot (no cross-talk), quit, give-up, stroke unaffected, approach 2 humans + bot,
team 2v2 alternate shot + concession + 0x8F, match halved hole, pang battle; e2e tournament room (mass 0x46/0x74, 3 holes).

## Wiz City (course 0x13) — fielditems.py
- Room/game: course 0x13 is accepted as-is by 0x08/0x0A and played as sent (room.start_game only resolves the
  random course 0x7F to 0..10, so Wiz City is never picked at random). Hole order/pars: as for every course
  (pars come from the client's 0x1A). [V] nothing else in room/ingame/modes checks the course id.
- 0x50 tail = CGimmickContainer::LoadFromPacket (contentsdoc.cpp:983) [V]: u32 seed, 18 x {u8 n, n x 0x14
  GimmickDispositionInformation (globalgamedefine.h:1594): i32 type 0 PANG / 1 BOX, u32 index, u32 flag 0,
  u32 unknownC 20, u8 hole, 3 pad}. Positions are NOT sent: every client places the items from the seed on
  "ct_green_b*" (coins, texture type 1) and "ct_booster.dds" (magic carpets, any type, texture type 2)
  triangles (GroundItemMan::OnInit / DisposeGroundItem, grounditemman.cpp:53/226).
  Counts [I, CubeCoinSystem.getAllCoinCubeInHoleWizCity]: holes 3/12 5 boxes + 55 coins, 14 2 + 46, 18 3 + 30,
  other holes 20 coins. Other courses: seed + 18 x 0.
- Magic carpet boost = client physics (ground type "booster", golfdoc.cpp:2286) [V]: no packet.
- Pick-up [V]: C->S 0x1C = u8 isMyShot, u8 n, n x {u8 type, u32 index, u8 count, u8 textureType}
  (CGolfRule::EncodePacket golfrule.c:20003, items from GroundItem::NSC_OnStep grounditem.cpp:420).
  Credited once, from the shooter's copy only (stroke: turn player's isMyShot=1; tournament/approach: own shot),
  index/type checked against the table, (player, hole, index) never twice.
- Rewards [I, community server GameBase.requestInitCubeCoin]: coin 1..50 pang on the green edge (tex 1),
  1..200 elsewhere (tex 2) -> account pang + S->C 0xC6 u64 pang, u64 0 (golftask.c case 0xc6 [V]);
  box -> one random consumable (fielditems.BOX_ITEMS) added to the saved inventory (0xA5 count update when the
  item was already in the bag; a brand-new item shows after re-login [I]).
- Tests: test/test_wizcity.py (run_wizcity.sh, ports 41100-41102).
