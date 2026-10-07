# KR 645: room list, game room, start game (implemented in room.py)

All S→C layouts come from the KR 645 client decompile. "opc Lnnn" = line in `opc.c` (CTask::OnPacketCommon; the `case` labels there are
jump-table indices, so id = the summ.txt mapping, e.g. case 0x5d = id 0xF3). "lt Lnnn" = `/root/ghidra-out/lobbytask.c` (CLobbyTask::OnPacket,
where `case` labels are the real ids). C++ sources are under `/root/rebang/source/client/ProjectG/`.
Cross-check: S→C ids in the S6 GB/US server (`ref/.../S6`) are these KR ids **+2** in this range (S6 0x40 chat = KR 0x3E, 0x47 room list = 0x45,
0x48 slots = 0x46, 0x49 enter room = 0x47, 0x4A room update = 0x48, 0x4C leave = 0x4A, 0x4E = 0x4C, 0x52 game init = 0x50, 0x76 = 0x74,
0x78 ready = 0x76, 0x7C new master = 0x7A, JP 0xF5/0xF6 = KR 0xF3/0xF4). The C→S ids are the same as in S6.

Struct sizes (pack 1, `shared/globalgamedefine.h`): sRoomInfo 0xB2 (:893), sSlotInfo 0x152 (:946), sCharacterInfo 0x1BC (:274),
sUserInfo 0xB92, sBriefUserInfo 0xC4 (:837). Local contents that change decoding in KR (`shared/localize_kor.h`): 0x54 INVITE_FRIEND = on,
0x5E = off (full 0xB2 room entries), 0x51 CARD_SYSTEM = on, 0x70 = off, 0x2E INTRUSION = on, 0x57 UCC = on.
`IsMassGame(type)` = type in {4,5,6,9,10,14} (@004238c0). MyGuid() = sUserInfo.info.dwGuid (**offset 0x5B**, `taskmain.c:8505`).

## Flow
1. TOPPAGE "게임하기": C→S **0x81** (no payload; `taskmain.c:9191`, sent only if doc[0x4b7f]==0) → S→C **0xF3** (no payload; sets
   doc[0x4b7f]=1, switches to CLobbyTask layout "ROOMLIST", opc case 0x5d L9896) + **0x45** room list + (optional) **0x44** lobby user list.
   Back to TOPPAGE: C→S **0x82** → S→C **0xF4** (opc case 0x5e).
2. Make room: C→S **0x08** (`makeroomdlg.cpp:1296`): u8 quick, u32 shotTime ms, u32 gameTime ms, u8 userLimit, u8 gameType, u8 holes,
   u8 course, u8 holeType, str title, str password. (Also `lobbymain.cpp:4636/6300` match/quick variants, same layout.)
   Join: C→S **0x09** u16 roomIdx, str password (`detailedroominfodlg.cpp:340`, `lobbymain.cpp:9655`). Spectate 0x3E same layout (not handled).
   Reply (both): **0x47**, **0x48**, **0x46 sub 0** (in that order).
3. In room: C→S **0x0A** settings, **0x0D** ready, **0x10** team, **0x03** chat, **0x0F** leave, **0x0E** start (owner only).
4. Start: S→C **0x74** (player sUserInfos → m_userInfo[4] @doc+0x12d0) then **0x50** (game init → `ChangeTask("CGolfTask")`, opc L4641).
   **Hand-off to ingame.py happens here.** Client then loads the course and sends C→S **0x48** u8 percent (loading progress,
   `SendPacket(uchar)` taskmanager.c:4320) and, when loaded, C→S **0x11** (no payload, `CGolfTask::OnInitFinished` golftask.c:1795;
   only when OnlinePlay()). For mass games with gallery flag it also sends 0x3F u32 guid. Everything after that is in-game.

## S→C layouts
**0xF3 / 0xF4**: no payload.

**0x45 room list** (opc case 0x13 L3384): u8 count, u8 sub, **u16 must be 0xFFFF** (else the whole packet is ignored), count × sRoomInfo.
sub 0 = clear + add all, 1 = add or update (by roomGuid), 2 = remove, 3 = update existing.

**sRoomInfo** (0xB2): 0x00 title[32], 0x20 password[16], 0x30 bPublic, 0x31 bAvailable (joinable/waiting; sorts and enables "join",
`lobbymain.cpp:8514,9222`), 0x32 bIntrusion, 0x33 nUserLimit, 0x34 nUserNum, 0x35 RoomKey[16], 0x45 nGalleryNum, 0x46 nGalleryLimit, 0x47 nHole,
0x48 gameType, 0x49 u16 roomGuid, 0x4B holeType, 0x4C mapType, 0x4D u32 shotTimeLimit, 0x51 u32 gameTimeLimit, 0x55 u32 tidMatch, 0x59 bSleep,
0x5A bAdminMaster, 0x5B sGuildRoomInfo[74], 0xA5 u32 pangMultiply, 0xA9 u32 expMultiply, 0xAD i32 masterUID, 0xB1 realGameType.
Client copy: doc+0x4998 (so doc[0x49e0]=gameType, doc+0x49e1=roomGuid, doc[0x4a49]=realGameType).

**0x44 lobby users** (opc case 0x12 L3163): u8 sub (1 add, 2 remove, 3 update), u8 n, n × sBriefUserInfo 0xC4
(0 dwUid, 4 dwGuid, 8 u16 roomIndex, 0x0A sNick[22], 0x20 level, 0x21 dwIdentity, 0x25 dwTitle, 0x29 ladder, 0x2D flags, 0x2E guildId,
0x32 emblem[12], 0x3E u16 state, 0x40 channeling, 0x44 reserved[0x80]). Optional (only fills the user list window).

**0x47 enter room** (lt L3670): u8 result (0 OK; non-zero → error message, msg 0x3B), [if 0: u8 gallery; if gallery≠0: u32 galleryGuid],
sRoomInfo 0xB2. Clears the slot list, sets myInfo.roomIndex = roomGuid, shows "GAMEROOM" (or "GAMEROOM_EXT" for mass games;
gameType 2 switches to ntAvatarChatTask). Error codes used by room.py: 1 full/playing, 2 not found, 3 wrong password (texts not verified).

**0x48 room settings** (lt L3829): u16 roomIdx (unused), u8 gameType, u8 mapType, u8 nHole, u8 holeType, u8 nUserLimit, u8 nGalleryLimit,
u8 bSleep (==1), u32 shotTime ms, u32 gameTime ms, u32 tidMatch, str password (≤15), str title (≤31).

**0x46 room slots** (lt L3454): u8 sub, u16 (unused, room.py sends room index), then
- sub 0 (full list, also 5 = append): u8 n, n × (sSlotInfo 0x152 [+ sCharacterInfo 0x1BC if !IsMassGame]), then (INVITE_FRIEND) u8 m, m × sSlotInfo.
- sub 1 add: sSlotInfo [+ sCharacterInfo unless mass or slot.IsInvite]. sub 2 remove: u32 guid. sub 3 update: u32 guid, sSlotInfo.
After every sub the lobby re-evaluates the master (`lobbymain.cpp:3729`: first slot with bMaster) and the start button
(`SetStartBtn`, `lobbymain.cpp:7541`: owner can start with **1 player** — solo stroke works).

**sSlotInfo** (0x152): 0x00 dwGuid (must equal that player's sUserInfo.dwGuid), 0x04 sNick[22], 0x1A sGuild[21], 0x2F connectionRank
(slot order; decremented on removals), 0x30 dwIdentity, 0x34 dwTitle, 0x38 tidChar, 0x3C tidSkin[6], 0x54 bits bTeam:2 bSleep:1 bMaster:2 gender:3,
0x55 bits manner:1 bReady:1, 0x56 level, 0x57 wings bits, 0x58 ladderGrade, 0x59 GuildId, 0x5D emblem[12], 0x69 dwUserUID, 0x6D state,
0x71 u16 subRoomIndex, 0x73 action, 0x77 float location[3], 0x83 iStateTrade, 0x87 tradeTitle[64], 0xC7 tidMascot, 0xCB pangma[2],
0xCD channeling, 0xD1 sDisplayID[128], 0x151 IsInvite:1.

**0x76 ready** (lt L4261): u32 guid (0xFFFFFFFF = all), u8 state: **0 = ready, 1 = not ready**, 2 = "cannot ready now" (`lobbymain.cpp:4074`).
**0x7A new master** (lt L4318): u32 guid, u16. **0x7B team** (lt L4349): u32 guid, u8 team. **0x7D start failed** (lt L4378): u8 code (1..13).
**0x4A left room** (opc case 0x15 L3990): u16 0xFFFF → roomIndex=0xFFFF, back to ROOMLIST (if doc[0x4b7f]) else TOPPAGE.
**0x3E chat** (opc case 0xc L2236): u8 type (bit7 separate flag; 0 = normal; own nick shown in a different colour), str nick, str message.
(0x3F str = system line, 0x40 str = notice board, 0x82 = whisper u8,str,str in lobbytask.)

**0x74 game players** (opc case 0x22 L5416): u8 (ignored), u8 n (≤4), then if !Gallery && IsMassGame(roomType): u8,u8,u8 flags + SYSTEMTIME;
else n × { sUserInfo 0xB92 (→ m_userInfo[i]), SYSTEMTIME 16, u8 cardCount, cardCount × 0x41 }. MyGuid must be among them.

**0x50 game init** (opc case 0x1b L4553): u8 map (real course 0..19), u8 gameType, u8 holeType, u8 holes, u32 (doc+0x4568, seed),
u32 shotTime ms, u32 gameTime ms, 18 × { u32 holeSeed (seed%3 → doc[0x45ce+i]), [u8 map if realGameType==14], u8 holeNumber (doc[0x45f2+i]) },
then CGimmickContainer::LoadFromPacket (`contentsdoc.c` @006a18f0): u32, 18 × { u8 n, n × 0x14 GimmickDispositionInformation }.
Hole numbers per holeType, same as the offline family mode (`familymain.c:5148`): 0 → 1..18, 1 → 10..18,1..9, 2 → random start, 3 → shuffle.

## C→S layouts
| id | payload | source |
|---|---|---|
| 0x81 | – | taskmain.c:9191 |
| 0x82 | – | (S6 LEAVE_MULTIGAME_LIST) |
| 0x08 | see Flow 2 | makeroomdlg.cpp:1296 |
| 0x09 | u16 room, str pw | detailedroominfodlg.cpp:340 |
| 0x0A | u16 0xFFFF, u8 n, n × (u8 key, value): 0 title str, 1 password str, 2 gameType u8, 3 map u8, 4 holes u8, 5 holeType u8, 6 shot time s u8, 7 userLimit u8, 8 game time min (s for type 10) u8, 9 sleep u8 | changeroominfodlg.cpp:4491, lobbymain.cpp:2960,13377,13450 |
| 0x0D | u8 0 = ready / 1 = cancel | lobbymain.cpp:13172 |
| 0x0E | u32 MyGuid | lobbymain.cpp:13156 |
| 0x0F | u8 0, u16 0xFFFF, u64 0, u64 0 | lobbymain.cpp:2928 |
| 0x10 | u8 team | lobbymain.cpp:13515 |
| 0x03 | str myNick, str text | hatmanager.cpp:2912 |
| 0x69 | sGameOptionInfo 0x240 (options + chat macros) — no reply | clientsetting.c COption::SendUpdateGameOptionPacket |
| 0x0C | u8 kind, u32 guid (equipment change in room) — **not handled here** (player.py) | lobbymain.cpp:7662 |
| 0x32 | u8 idle — not handled (also used in game) | lobbymain.cpp:2968 |

## sess.state['room'] (one dict shared by all members; documented at the top of room.py)
`index, title, password, mode, course, course_played, holes, hole_type, hole_order[18], hole_seeds[18], game_seed, shot_time_ms, game_time_ms,
max_players, key (16 bytes RoomKey; shot results are XOR'd with it — S3_SHOTRESULT_ENCRYPT is on), state ('waiting'|'playing'), owner_uid, bot,
players[ {uid, guid, nick, char_tid, slot, master, ready, team, bot, user_info (0xB92 bytes sent in 0x74), sess (None for the bot)} ]`.
Helpers: `room.get_room(sess)`, `room.broadcast(room, w, except_sess=None)`, `room.finish_game(room)` (back to 'waiting').

## Bot
No AI/CPU/NPC player support was found in the client (no such strings; the only "npc" hits are 3D model node names in polysoup.cpp).
The offline "family" mode (`familymain.c`) runs a local multi-player game without a server, which is not a bot either. So the bot is a
**fake second user**: an extra sSlotInfo/sCharacterInfo in 0x46 and an extra sUserInfo in 0x74 (uid/guid 0x7F000001, nick "Bot",
character tid 0x04000001, always ready). Toggle in a room with chat `!bot` / `!bot off` (`/bot` too, if the client does not eat it).
In game the client will wait for the bot's turn: **ingame.py must synthesize the bot's shots/turn packets** (players with `bot=True`).

## Uncertainties
- MyGuid = sUserInfo.dwGuid (0x5B) is **0** in the current game.user_info(); all humans then share guid 0 (ready/leave/master for one player
  hits everyone in a multi-human room). Core change needed: set dwGuid = uid in game.user_info (room.py reads it from there automatically).
- connectionRank 1-based; dwTitle 0; pang/exp multipliers 0; subRoomIndex 0xFFFF; error codes for 0x47/0x7D texts not mapped.
- 0x50 u32 seed and hole seeds are random; the meaning of seed%3 (pin position/weather?) not verified. Gimmicks sent empty.
- 0x77 (lt L4275 / golftask L5158: u32,u32,u8,u8,[4×u32 if type 6],0x60 bytes) and 0x75 (u32, rival vars) are not sent; they may be
  expected around game start (if loading hangs, check these first — likely ingame.py's business).
- 0x44 lobby user list is cosmetic; if it misbehaves, drop it.
