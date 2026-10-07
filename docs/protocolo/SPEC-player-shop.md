# KR 645: character, inventory, shop and equipment (player.py, shop.py)

Tags: **[V]** read directly in the decompiled code; **[S]** the S->C packet id comes from the `summ.txt` jump-table mapping
(the decode order was checked in `opc.c`, the id<->case mapping was not re-derived); **[T]** exercised by `test/test_player_shop.py`
(scripted client only, not the real ProjectG); **[G]** guess.
"Lnnnn" = line in `opc.c` (full decompile of CTask::OnPacketCommon, scratchpad). SRC = /root/rebang/source, GH = /root/ghidra-out.
Detailed raw notes: scratchpad/shopre/notes.md; IFF dumps: scratchpad/iff/*.txt.

## 1. Structs (SRC/shared/globalgamedefine.h, pack 1) [V]
| struct | line | size | fields |
|---|---|---|---|
| sUserEquip | 233 | 0x6C | guidCaddie +0, guidChar +4, guidClubSet +8, tidBall +0xC, tidItemSlot[10] +0x10, guidSkin[6] +0x38, tidSkin[6] +0x50, guidMascot +0x68 |
| sCharacterInfo | 274 | 0x1BC | tid +0, guid +4, tidParts[24] +8, hairClr +0x68, shirtsClr:4/gift:4 +0x69, PCL[5] +0x6A, Purchase +0x6F, tidAuxParts[5] +0x70, UccIndexList[24][9] +0x84, ItemIdList[24] (part guids) +0x15C |
| sCaddieInfo | 304 | 0x19 | guid, tid, tidPart, Level u8 +0xC, Exp +0xD, flags +0x11, Remain_Date +0x12, Remain_Partdate +0x14, Purchase, checkWarning, PCBang +0x18 |
| sClubInfo | 320 | 0x12 | guid, tid, short PCL[5] |
| sMascotInfo | 327 | 0x3F | guid, tid, Level +8, Exp +9, szMsg[30] +0xD, Remain_Date +0x2B, Purchase +0x2D, endDate +0x2E, PCBang +0x3E |
| sItemInfo | 405 | 0xA8 | guid +0, tid +4, HourRemain +8, Common[5] +0xC (**Common[0] = quantity** for balls/usable items, L5100), Purchase +0x16, gift/Expired/ItemFlag/ItemType bits +0x17, ItemDate +0x18, Reserve/IsValid bits +0x28, ItemName[41] +0x29, UccIndex[9] +0x52, status +0x5B, Seq +0x5C, CopierNick +0x5E, attachCard[12] +0x74, CharacterSlotNum +0xA4, CaddieSlotNum +0xA6 |
| sBuyItem (C->S) | globalnetworkdefine.h:29 | 0x10 | i32 Idx (-1), u32 TypeCode, u16 DayCount, i16 ChildIdx, u32 ItemCount |
| sBuyItemResult (S->C) | globalnetworkdefine.h:7 | 0x26 | u32 Typeid, u32 guid, u16 Time, u8 ItemType, u16 Count, SYSTEMTIME endDate, char UccIndex[9] |

sUserInfo (0xB92) = sMyInfo + charInfo @0x96C + caddieInfo @0xB28 + clubInfo @0xB41 + mascotInfo @0xB53; sUserEquip @0x248, Pang i64 @0x15A.
The client copies sMyInfo into CSharedDoc (equip at Doc+0x628, L2620ff).
Item group = `tid >> 26` (itemmanager.cpp:2484): 1 char, 2 part, 3 club, 4 clubset, 5 ball, 6 usable item (0x18-0x1B), 7 caddie,
8 caddie item, 9 set item, 0xE skin, 0xF hair, 0x10 mascot, 0x12 furniture, 0x1F card.

## 2. Inventory at login (player.py `on_login`) [V]/[T]
0x42 calls `CSharedDoc::ClearInventory` (L2638), so the lists have to come **after** 0x42. on_login runs at that point. Order sent: 0x6E, 0x6F, 0x71, 0x70 (+0x94 if cookie != 0).
- **0x6E** (L4832): u16 total, u16 n, n × sCharacterInfo → AddChar. If total != n, the client returns early and waits for more, so always send total == n.
- **0x6F** (L4857): u16 total, u16 n, n × sCaddieInfo → AddCaddie.
- **0x71** (L4930): u16 total, u16 n, n × sItemInfo, dispatched on tid>>26 (2 parts list, 4 club map, 5 balls, 6 items, 0xE skins …); a duplicate guid is skipped.
- **0x70** (L4925): sUserEquip → SetCurEquipInfo.
- 0xDF mascots (L8305: u8 n, n × 0x3F) is not sent (no mascots).

## 3. IFF (game data) [V]
- **Location.** `projectg642.pak` → `data/pangya.iff`, a ZIP of 31 tables. Of all the paks that contain the file, the client uses the one whose name sorts last (commonutil.cpp:2708, westpak.cpp:419/537).
  - Pak format (client/Wangreal/source/westpak.cpp): the trailer is u32 tableOff, u32 count, u8 0x12. Names are XORed with 0x71. The data is LZ77, and type 3 adds an XOR on top.
  - Copied read-only to `emu/data/pangya.iff`; the parser is scratchpad/iff/parse_iff.py.
- **Table and record layout.** Each table is u16 count, u16 bind, u32 version 12, then fixed-size records. Every record starts with the 144-byte IFF_ITEM_COMMON (shared/classdefine.h):

  | offset | field |
  |---|---|
  | +0 | Final |
  | +4 | TypeId |
  | +8 | Name[40] |
  | +92 | Price |
  | +96 | SalePrice |
  | +104 | flags: bit0 IsCash (0 pang / 1 cookie), bits1-4 IsSalable, bits5-7 InStock |

  InStock values: 0 none, 1 buy+gift, 2 gift only, 3 buy only, 4 display, 5/6 hidden.
  - The shop shows an item only if InStock ∉ {0,5,6} and it is inside its sale period (GH/shopmain.c:5502). Checkout refuses {0,2,4} (shopmain.c:8862).
  - Record sizes: Character 372, Part 516, ClubSet 180, Ball 764, Item 196, Caddie 200, CaddieItem 236, SetItem 220, Skin 196, Mascot 252.
  - Item.iff in KR has no `RandomBox` field: COM[5] sits at +184. This was inferred from the record size only.
- **Price.** The server reproduces `CItemManager::GetItemSalePrice` (itemmanager.cpp:2833):
  - SalePrice if 0 < Sale < Price, else Price.
  - Usable items: unit × (count / COM[0]) when COM[0] > 0.
  - Caddie items, skins and mascots: priced by the number of days through COM[] (1/7/15/30/365).
  - Price 10,000,000 means not for sale.
- **Default parts.** Character.iff has **no** part list. The client computes them in `GetDefCombo` (itemmanager.cpp:2737) as `0x08000400 | (charIdx<<18) | (slot<<13)` for each slot that exists in Part.iff.

**What player.py gives a new account [T]:**
- Character **0x04000000 (Nuri)**, guid 100. tidParts:
  - slot 0: 08000400
  - slot 2: 08004400
  - slot 3: 08006400
  - slot 4: 08008400
  - slot 5: 0800A400
  - slot 7: 0800E400
  - slot 8: 08010400
  - slot 18: 08024400
  - all other slots: 0
- Club set **0x10000000 (Air Knight)**: not sold, but it is the set the tutorial hands out (golfruletutorial.cpp:125).
- Ball **0x14000000 (Pangya Aztec)**: price 0, the basic unlimited ball (`IsBasicAztecFamily`), given ×100.
- Equip: guidChar = char, guidClubSet = club, tidBall = 0x14000000. No caddie.
- 100000 pang, 0 cookies.

These were chosen because they are the first or basic entries the client itself falls back to (lobbymain.cpp:12339 for the ball).

## 4. Shop
### Entering and leaving [V]
Both are **client-only**: `CTaskMain::OnUnderBar_ShopUp` (GH/taskmain.c:8889) / `OnUnderBar_BackUp` (:8567) call ChangeTask, and no reply is needed.
- **On opening,** CShopMain::OpenLayout (GH/shopmain.c:3133) sends:
  - C->S **0x5C** (empty). No reply is known; it is ignored.
  - C->S **0x3D** (empty). The reply is **0x94** u64 cookie (L6163) [G strong: same pattern as browser.cpp:160, taskmain.c:7628].
- The item list comes entirely from the local IFF. The server supplies only the server time, inside 0x42.
- **On leaving,** the only packet is 0xB9 {u8 1}, sent when returning into a room (taskmain.c:8858). It is not handled here.

### Buy request: C->S 0x1D [V] (GH/checkoutdlg.c:5464-5770)
Layout: `u8 rental, u16 n, n × sBuyItem`.
- **No price and no currency are sent**, so the server must price from IFF and check the balance itself.
- ItemCount is what the user typed (1..99). For balls the client sends the IFF pack size (sBall COM[0], +0x2F0); for some bundled usable items it sends sItem+0xBC.
- DayCount comes from an .rdata table (DAT_00b7c588) that the decompile does not show.
- Gift: C->S 0x1F `Str nick, u32, Str msg, u8, u16 n, n × sBuyItem`. The result is 0x68, same layout as 0x66.

### Buy result [V]/[S]/[T]
The server sends **0xA8** [S] (L6735), then **0x66** [V] (shoptask.cpp:107).
- **0xA8:** u16 n, n × sBuyItemResult. There is **no** trailing pang/cookie (the S6/GB server's 0xAA has one; KR does not decode it).
  - Inside the shop, CShopMain::HandleMsg (shopmain.c:15059…) dispatches on tid>>26.
  - Character: created with the default parts, **and the client auto-equips it**.
  - Ball / usable item: `Common[0] = Count`, so **Count must be the new total**.
  - Timed items (ItemType 3/4/5) make the client send 0x20 type 7.
  - Caddie item: the record guid must be the caddie's guid.
- **0x66:** u32 code. Code 0 is followed by u64 pang, u64 cookie, which replace the client's balances.

  | code | meaning |
  |---|---|
  | 0 | success (+ u64 pang, u64 cookie) |
  | 1 | failed |
  | 2 | not enough pang |
  | 3 | wrong product code |
  | 4, 0x24-0x27 | already owned |
  | 0x13 | not for sale |
  | 0x15 | too many at once |
  | 0x17 | not enough cookies |
  | 0x2C/0x2D | level |
  | 0x09/0x0B | + SYSTEMTIME (sale period) |
  | 0x2F | bonus (+ u32 tid, guid, qty, bonus) |

  Other codes are listed in shopre/notes.md.

**shop.py behaviour:**
- **Checks:**
  - unknown tid → 3
  - InStock ∉ {1,3}, or no price → 0x13
  - pang/cookie short → 2 / 0x17
  - non-stackable item already owned → 4
  - more than 20 lines → 0x15
- **What each type grants:**
  - Set items (9) grant their ElemIds/ElemNum (sSetItem: nElems +144, ElemIds +148, ElemNum +188).
  - Characters get the default parts and become equipped.
  - Caddies go into the caddie list.
  - Balls and usable items stack.
- Group 0x1C → 1 (not implemented); 8/0xF/0x10/0x12/0x1F: see SPEC-myroom.md section 4.
- On success the inventory is saved to data/<id>.json.
- Everything is granted as permanent (ItemType 0, Time 0), even when the client asked for a rental or period.

### Equip: C->S 0x20 [V] → S->C 0x69 [S] (L4644)
Request: `u8 type` + payload. Reply: `u8 result` (**4 = OK**; 0/1/5/other show an error), then `u8 type` + payload.

| type | request | reply payload | client senders |
|---|---|---|---|
| 0 | sCharacterInfo 0x1BC (parts) | 0x1BC | shopmain.c:15392, shareddoc.c:43645 |
| 1 | u32 guidCaddie | — | realmyroommainui.c:24688 |
| 2 | u32 tidItemSlot[10] | 0x28 | lobbymain.cpp:6784 |
| 3 | u32 tidBall, u32 guidClubSet | — | realmyroommainui.c:16569 |
| 4 | u32 tidSkin[6] | 0x18 | realmyroommainui.c:20057 |
| 5 | u32 guidChar | u32 | shopmain.c:15298 |
| 7 | u32 guid (start period item) | (no success case) | shopmain.c:3333 |
| 8 | u32 guidMascot | sMascotInfo 0x3F | realmyroommainui.c:24746 |

- shop.py stores each change, persists it, and echoes the payload.
- Type 0 keeps the client's full 0x1BC record (raw), so aux parts and UCC data survive a reconnect.
- Type 5 with an unknown guid → result 0.
- Type 7 gets no reply.
- **C->S 0x0C** (`u8 type, u32`, lobbymain.cpp:11870) is the *in-room* equip change and is left to room.py.

## 5. Real-client feedback round 1 (avatar only after re-entering; no ball in game)
- **Ready bits [V].** Each list posts MsgObject(0x2C, bit) to the current task:
  - 0x6E → 1 (L4853), 0x6F → 2 (L4921), 0x71 → 4 (L5376), 0x78 gift box → 8 (L5749), 0xDF mascots → 0x10 (L8376, also when n = 0).
  - CLobbyMain ORs them into Doc.m_underBarMask (+0x4AB4). At 0x1F it only runs SortAllMyItemList (lobbymain.cpp:4311).
  - The only other reader is the notice→shop shortcut (taskmain.c:7627).
  - player.py now also sends **0xDF** (u8 0) and an empty **0x78** (u8 mode 1, u16 1, u16 0, u16 0). With mode ≠ 1 the client would request pages with 0x92.
  - Avatar code does not read the mask, so this is unlikely to be the avatar fix.
- **Own avatar in the shop [V].** CShopMain::OnShopMainFinish (GH/shopmain.c:14464) reads only Doc data: equip.guidChar → char map → ChangeCharacter.
  - The model is then built on a worker thread (CAnotherHand::Work → OnAnotherHand → ShowPet → CExhibition/CPetFrame::Build, shopmain.c, partsinfo.c).
  - The server sends nothing at that point, and the data is the same on the first and second entry. A first-entry failure is therefore client-side model load timing, not missing packets.
- **Room avatars [V]** come from 0x46 (sSlotInfo + sCharacterInfo) → msg 0x5A → CLobbyMain::ShowPet (lobbymain.cpp:3189, 7149).
  - ShowPet draws only when the current layout is "GAMEROOM" (pArea = m_pPet[connectionRank-1]); otherwise it returns.
  - OnGameRoom_Finish (lobbymain.cpp:11222) re-shows every slot once the layout is built.
  - If 0x46 is processed before the GAMEROOM layout exists and OnGameRoom_Finish ran earlier, the pet stays empty until the next re-entry. This is room.py's sequencing [G].
- **dwGuid.** sPangYaUserInfo.dwGuid (@0x5B, MyGuid()) was 0. patch_user_info now sets it to uid when it is 0, so the room slot guid, MyGuid() and roomSlotMap keys are non-zero and consistent.
- **Ball in game [V].** CBall::OnLoad (ball.cpp:105) calls AddBall(Doc.m_userInfo[i].userEquip.tidBall) for each player whose state ≠ 3, plus 0x14000000 when content 0x3F is on.
  - The model is `<sBall.Data>.pet`; for 0x14000000 that is data/ball/ball_low.pet, which exists in projectg600+.pak.
  - The drawn ball is FindBall(m_userInfo[GOLFDOC.m_currentPlayer].userEquip.tidBall) (ball.cpp:40/297). If it is not found, nothing is drawn.
  - So the ball comes from the 0x74 sUserInfo (offset 0x254), not from inventory. room.py's 0x74 has tidBall = 0x14000000 (checked by test/check_user_info.py).
  - If the ball is still missing, look at the in-game side: m_currentPlayer pointing at the wrong index, or a player state of 3.

## 6. Uncertainties
- The ids of 0xA8, 0x69 and 0x94 rest on the summ.txt mapping. 0x66 is certain (shoptask.cpp).
- That 0x3D is a cookie request is inferred, not proven.
- Whether the client needs sItemInfo.IsValid (+0x28 bit1) set for items in 0x71: player.py sets it (the client sets it itself for bonus items, shoptask.cpp:133).
- Whether default parts have to exist as owned items: assumed not. ItemIdList stays 0 and the client takes the parts from tidParts.
- Ball count 100 on the basic Aztec: the client probably treats it as unlimited anyway.
- Rental, period and timed purchases are granted as permanent. Mascots, caddie items, hair colour, furniture and cards are now granted: see SPEC-myroom.md.
- Nothing here has been run against the real client.
