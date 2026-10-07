# KR 645: My Room, caddies, mascots, cards, upgrades, user info (myroom.py + player.py/shop.py)

Tags: **[V]** read in the decompiled client (pseudocode and, where noted, disassembly of ProjectG_ReleaseQA.exe);
**[T]** exercised by `test/test_myroom.py` (scripted client that decodes like the client, NOT the real ProjectG);
**[I]** inferred (JP 983 reference server semantics, or reasoning). Nothing here was run against the real client.

Paths: `gh/` = /root/ghidra-out, `src/` = /root/rebang/source/client/ProjectG, `shared/` = /root/rebang/source/shared,
`opc.c` = full decompile of CTask::OnPacketCommon (scratchpad; its `case N` are jump-table indexes, id map in summ.txt),
`RMT` = gh/realmyroomtask.c (CRealMyRoomTask::OnPacket from line 4382; its case labels ARE packet ids, and those
packets are only understood while the My Room task is current).

## 1. Handled C->S ids

| module | ids |
|---|---|
| myroom.py | 0x0C 0x2F 0x4B 0x6B 0x73 0x92 0x95 0xAD 0xAE 0xAF 0xB0 0xB5 0xB9 0xBB 0xBC 0xBD 0xBE 0xBF 0xC0 0xC2 0xCE 0xCF 0xD0 0xD1 0xD2 0xD3 0xD4 0xD5 0xD6 0xD7 0xD9 0xE2 |
| shop.py (unchanged ids) | 0x1D 0x1F 0x20 0x3D 0x5C |
| player.py | none (login lists only) |

No overlap with room.py (0x03 08 09 0A 0D 0E 0F 10 63 69 81 82), ingame.py (0x11-0x17 0x19-0x1C 0x22 0x30 0x31 0x33
0x34 0x42 0x48 0x65 0xE7) or modes.py (0x35 0x52); game.py aborts at startup on a duplicate.

## 2. Entering My Room [V]/[T]
1. Under-bar "My Room": `CTaskMain::MakeRealMyRoom` (gh/taskmain.c:10917) sends **0xAD u32 myUid, u32 targetUid** and
   switches to CRealMyRoomTask immediately (no avatar, no furniture until the server answers; gh/realmyroommain.c:4036).
2. **S->C 0x123** (opc.c L11355): u32 result (0 = error) + sRealMyRoomAuthority 0x6B {u32 ownerUID, u16, u8 bPrivate,
   char password[100]} (shared/globalgamedefine.h:1743) -> Doc+0x47D8. Client posts 0x21F -> ChangeRealMyRoom.
3. `ChangeRealMyRoom` (taskmain.c:7977) sends **0xAF u32 uid, u8 1** and **0xB9 u8 2**.
4. Reply to 0xAF(…,1): **0x16E** sSlotInfo 0x152 + sCharacterInfo 0x1BC (RMT L5137, posts 0xE4 = create avatar);
   sSlotInfo.dwGuid must equal MyGuid (sUserInfo+0x5B), tidChar @0x38, level @0x56, dwUserUID @0x69, tidMascot @0xC7.
   Then **0x125** u32 1, u16 n, n x sFurniture_List 0x1B {u32 id, u32 typeId, u16, float x,y,z,r, u8 bArrange}
   (RMT L6113; the only source of owned furniture). 0xAF(uid,0) (shop, shoptask.c:247) = left the room, no reply.
5. 0xB9 u8 = location update from many dialogs, no reply. 0xB0 (raw authority 0x6B) = privacy saved, no reply.
6. Furniture: **0xAE** u16 n, n x 0x1B (changed entries, realmyroommainui.c:27779) -> **0x124** u32 0 (RMT L6095).
   0xD9 (function furniture) has no prize here -> no reply [I]. Shop furniture (group 0x12) goes to inv['furniture']
   with the Furniture.iff Pos[4] (@0xC4) and bArrange 0.

## 3. Locker / gift / mail [V] (replies in RMT)
| C->S | reply |
|---|---|
| 0xD5 (UI init, realmyroommainui.c:25550) | 0x175 u32 0, u32 flags=0x14 (no password) L7333 |
| 0xCE Str pw | 0x171 u32 0 L7227 |
| 0xD7 | 0x177 u64 locker pang L7398 |
| 0xCF u32 99, u16 page | 0x172 u16 pages, u16 page, u8 n, n x sStoredItemInfo 0xAE (u32 id + sTradeItem 0xAA, globalnetworkdefine.h:44) L7259 |
| 0xD0 u8 1 + 0xAE (deposit) | 0xA5 {tid, guid, count 0} (removes it client-side, opc.c L6367 [I]) + 0x173 u32 0 |
| 0xD1 u8 1 + 0xAE (withdraw) | 0x71 list of one (re-adds it) [I] + 0x174 u32 0 |
| 0xD6 u8 dir(1 in), u64 | 0x176 u32 0, 0xC6 u64 pang + u64 0 (opc.c L7818: delta 0 = set), 0x177 u64 |
| 0xD2 / 0xD3 / 0xD4 (passwords) | 0x17B / 0x179 / 0x178 (+u8) u32 0 (no password is ever stored) |
| 0x92 u32 page | 0x78 mode 1 empty (mode != 1 loops) |
| 0xBC page / 0xBD / 0xBE / 0xBF / 0xBB | 0x140 (1,1,0) empty / 0x143 u8 1 / 0x15C / 0x145 u8 1 / 0x13F u8 1 (mail not supported) |

## 4. Shop groups that used to fail (shop.py `grant`) [V] shopmain.c HandleMsg, [T]
0xA8 record = sBuyItemResult 0x26 {u32 tid, u32 guid, u16 Time, u8 ItemType, u16 Count, SYSTEMTIME endDate, ucc[9]}.
- **Caddie item (8)**, msg 0xb8 (shopmain.c L15491): looked up by **record guid = the caddie's guid**; sets tidPart=tid,
  Remain_Partdate=Time (hours). Owner caddie tid = 0x1C000000 | ((tid>>21)&0x1F) (checkoutdlg.c L5924). Priced by days
  (COM short[5] @224: 1/–/15/30/365 days). Stored as caddie['part'], ['part_hours'] -> sCaddieInfo.tidPart @8,
  Remain_Date(days) @0x12, Remain_Partdate(hours) @0x14 in 0x6F and sUserInfo+0xB28 (golfrulebase.cpp:160 draws it).
- **Hair colour (0xF)**, msg 0xbd (L15675): HairStyle.iff cHairID @0x90, cCharID @0x91; applied to the first
  character (guid order) with (tid & 0x3FFFFFF) == cCharID. Server stores hairClr (sCharacterInfo+0x68). Same colour
  again -> code 4.
- **Mascot (0x10)**, msg 0xbf (L15760) -> AddMascot{guid, tid, msg "PANGYA!", Remain_Date = Time (hours),
  endDate = record endDate}. Priced by days (char COM[5] @224: 1/7/–/30/365). Re-buying extends the same guid.
- **Cards / packs / boxes (0x1F)**: one stack per tid, record guid = stack uid, Count = new total (shop path sets the
  count, shopmain.c:15917). Packs 0x7CC00000 "1탄" / 0x7CC00004 "2탄" (15 cookies), boxes 0x7D00000x.
- **Furniture (0x12)**: see section 2. Group 0x1C is still refused (code 1).

## 5. Mascots [V]/[T]
sMascotInfo 0x3F (globalgamedefine.h:327): guid, tid, u8 Level @8, u32 Exp @9, szMsg[30] @0xD, u16 Remain_Date @0x2B
(**hours**, mascotinfodlg.cpp:185), u8 Purchase @0x2D, SYSTEMTIME endDate @0x2E, u8 PCBang @0x3E (must be 0).
The client unequips a mascot whose endDate <= server time (realmyroommainui.c:24724) -> we send the real end date.
- Login: **0xDF** u8 n, n x 0x3F (opc.c L8305). sUserEquip.guidMascot (+0x68) only if it is in that list
  (lobbymain.cpp 12420 reads past end() otherwise). sUserInfo.mascotInfo @0xB53 is filled (mascot.cpp:57 draws it).
- Equip: 0x20 {u8 8, u32 guid|0} -> **0x69 u8 4, u8 8, sMascotInfo 0x3F** (opc.c L4770; zeros for 0).
- Message: **0x73 u32 guid, Str msg** -> **0xE0 u8 4, u32 guid, Str msg, u64 pang** (RMT L5833); 1/2/3 errors.

## 6. Caddies [V]/[T]
Equip 0x20 {u8 1, u32 guid} -> 0x69 u8 4, u8 1 (nothing more, opc.c L4704); unknown guid -> 0x69 u8 0.
sCaddieInfo flags @0x11 (bit0 gift, bit1 rent): 0 + Remain_Date 0 = never expires. **0x6B u32 guid, u8 flag**
(caddieinfodlg.cpp:303) = checkWarning @0x17, stored, no reply. No packet equips a caddie item (it is applied on purchase).

## 7. Cards [V] (opc.c / ccardbook.c / cardboxdlg.c / carddlg.c), [T]
Subtype = (tid>>22)&0xF: 0 character, 1 caddie, 2 special, 3 pack/ticket, 4 box. Card.iff (328-byte records):
RareType @144, COM short[5] @186, Avility @196, AvilityValue @198, UseTime @320, Volume @322 (= pack series), CardIndex @324.
- **sCards 0x3A**: u32 uid, u32 typeId, 12x0, i32 count @0x14, 32x0, u8 type @0x38 (=1). The UI reads only this list
  (Doc+0x123c). **sSCardAvilityPeriodInfo 0x41**: uid, tid, partsTid, partsUid, Avility, AvilityValue, slotNum,
  SYSTEMTIME start, SYSTEMTIME end, cardType, u8 valid (attached / active cards, Doc+0x1248).
- Login (player.card_packets): **0x12D** (clear) **0x130** u32 0, u16 n, n x sCards (L11983) **0x12E** (clear)
  **0x12F** u16 n, n x 0x41 (L11925). 0x17D card stacks are write-only in the client (never read) -> not sent.
- Open: **0xC2 u32 packTid, u32 packUid** -> **0x14C** u32 0, sCards pack, u8 n, n x {sCards card, u32 qty}
  (opc.c L12368); u32 1 = failed. We draw 3 cards of the pack's series (Volume 1 / 2), weights normal 70 / rare 22 /
  super rare 7 / secret 1; tickets 0x7CC00001-03 give one card of rarity >= 2/1/0 [I: real odds unknown].
- Use special card: **0xB5 u32 cardTid** -> **0x158** u32 0 + 0x3A {uid, tid, partsTid, partsUid, slot, u32 1,
  SYSTEMTIME start, SYSTEMTIME end, u16} (opc.c L12880); u32 1 + u8 = error. Avility 1 exp / 4 pang / 17 random pang are
  instant (server adds exp/pang; pang pushed with 0xC6); others become a timed buff, UseTime minutes [I].
- Attach: **0xC0** raw 0x3A {cardUid, cardTid, partTid, partUid, slot 1-4 char card / 5-8 caddie card} -> 0x158 echo
  (client does slot-=4 for caddie cards, disasm 0x7380a6); stored with slot 1-4 in card_periods.
- Remove: **0xE2 u32 removerTid, removerUid, partTid, partUid** (removers 0x1A0000C2/CD/CE/CF) -> **0x18D** u8 1,
  u32 partUid, u32 removerTid | u8 0, u8 err (opc.c L14509). Cards are destroyed.
- Card book/album: no server data (IFF + sCards list), ccardbook.c:3645.

## 8. Stat upgrades ("enchant") [V] (enchantdlg.cpp:316 disasm 0x7D08C7, RMT L5598), [T]
- **0x4B** u8 type (bit0 club set, bit1 downgrade), u8 stat (0 power, 1 control, 2 accuracy, 3 spin, 4 curve),
  u32 guid (equipped char or club), sCharacterInfo 0x1BC (garbage for clubs; ignored).
- **0xA3** u8 result; 1 (up) / 2 (down): u8 type, u8 stat, u32 guid, i64 pang **spent** (client subtracts it and does
  PCL[stat] +-1 on charMap[guid] (+0x6A) or clubSet Common[stat] (sItemInfo+0x0C)). 3 no pang, 4 no slot, 5 cannot
  downgrade, 6 failed.
- Cost = Enchant.iff ReqdPang for key 0x34000000|stat<<20|current (power 2100*(n+1)…, downgrade free).
  Club limit = ClubSet Slot[i] - Attr[i] (@170/@160). Character limit is enforced by the client (itemmanager.cpp:4107);
  the server only requires an Enchant.iff row.
- Persisted as chars[].pcl / items[].pcl -> 0x6E sCharacterInfo PCL, 0x71 club Common[5], sUserInfo char @0x96C+0x6A
  and sClubInfo PCL @0xB41+8 (room 0x74 / game).

## 9. User info dialog + level [V] (user_info.c, opc.c), [T]
- **0x2F u32 uid, u8 season** (5 current, 0 previous; user_info.c:4637/9356; repeated each frame until answered).
- Replies (0x14F first: it creates the cache entry): **0x14F** u8 season, u32 uid, u16 roomIndex, sPangYaUserInfo 0x10D,
  u32 guildPoint; **0x14E** u8, u32 uid, sUserEquip 0x6C; **0x156** u32 uid, sCharacterInfo; **0x150** u8 season,
  u32 uid, sPangYaUserStatistics 0xEB (own uid: also copied into Doc stats); **0x151** u8, u32, trophies 0x4E;
  **0x154** u8 kind, u32, u16 n (map stats 0x2B); **0x152** / **0x153** u8, u32, u16 0; **0x87** u32 1, u8 season, u32 uid
  (3 = close silently, used for unknown uids).
- Level shown = statistics Level (+0x4A) and "dwExp / need[Level]" (dwExp @+0x46 is the exp inside the level).
  EXP_NEED table from CSharedDoc::LoadLevelTable (rdata 0x9E6468, = shared/sharedtables.h) is in player.py;
  patch_user_info writes Level @0x159 and dwExp @0x155 clamped to [0, need[L]) (saved inv['exp'], default 0),
  cBestScore[6] = 127 (no record). Level 50 (MASTER_A) needs 21444 exp to level 51.
- 0x95 (Bongdari / point-event button) -> **0x109** u32, u32, u32 (opc.c L10427).

## 10. In-room equipment change [V] (lobbymain.c:8172, opc.c L3766), [T] (outside a room only)
0x0C u8 type, u32 value: 1 caddie guid, 2 ball tid, 3 club guid, 4 char guid, 5 mascot guid -> stored, the room
player's sUserInfo is re-patched, and **0x49** u8 type, u32 MyGuid, payload (0x19 caddie / u32 ball / 0x12 club /
0x1BC char / 0x3F mascot) is sent to everyone in the room (lobbymain.cpp 4243 updates m_userInfo).

## 11. Saved data (data/<id>.json) and guid invariant
New keys: chars[].pcl, chars[].hair, items[].pcl (club sets), caddies[].part/part_hours/warning, mascots[]
{guid, tid, end (epoch), msg}, cards[] {guid, tid, count}, card_periods[], furniture[], locker {pang, items},
myroom_auth, exp. Every new guid comes from player.alloc_guid (per-account range guid_base(uid) = 100 + (uid&0x7FFFF)*4096).
**Migration (player.migrate, at load, logged):** a save whose next_guid is outside its range (all accounts made before the
range existed, e.g. "123" with guids 100-110) gets every guid (chars, part guids inside the saved raw sCharacterInfo,
items, caddies, mascots, cards, equip refs) renumbered into its range once.

## 12. Open points
- Real-client checks still needed: avatar/furniture appearing in My Room, locker deposit/withdraw (0xA5/0x71 refresh is
  [I]), card-pack odds and pack sizes [I], special-card durations (minutes [I]), mascot in game.
- Visiting another player's My Room: 0x123 is answered with the target as owner and our own avatar; untested.
- Character upgrade limit (part slots, cards) is only enforced client-side.
