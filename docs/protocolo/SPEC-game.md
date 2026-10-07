# KR 645 — game server until lobby (from the decompiled client)
Most game packets go through `CTask::OnPacketCommon` (Ghidra timed out in /root/ghidra-out/taskmanager.c:14675; re-decompiled read-only
from a copy: `opc.c`, `opc.lst`, `summ.txt`, `jt2.py` kept in /tmp and the session scratchpad). "Lnnn" = line in opc.c.

## Framing
S→C `[seed][u16 len][Alpha(plain)]`, plain = [Private[k][seed]][0x01][size 3 digits base 255, MSB first][u16 id][payload] (`packet.cpp:892`, `shared/uselzo.cpp`).
C→S `[seed][u16 len][u8 counter][Alpha([Private][u16 id][payload])]` (`packet.cpp:688-731`). Key = Public[k][seed].
Hello raw: `[0][u16 len][0][3D 00][u8][u8][u8 parseKey]` (`gameunit.cpp:137-150`), client timeout 30 s.
Packet version on the wire 0x2A8ED069 (= 0x77def61b through Decrypt(), `shared/encryption.h`).

## S→C 0x0042 sub 0 (L2619-2860)
u8 0, str latestVer ("645.00"; greater blocks room creation, lobbymain.c:11815), str "", sUserInfo 0xB92 bytes, SYSTEMTIME 16,
u8 flag(0), u8, u16×3 bongdari, u32 flagBlock, u32 controlServerService, u32 (doc+0x5060), u32 serverProperty, GUILD_USER_INFO 0x119.
sUserInfo (`shared/globalgamedefine.h:139-346`): u16 roomIndex @0; sPangYaUserInfo @0x002 (sID[22] @0x002, sNick[22] @0x018, dwIdentity @0x053,
dwGuildId @0x06B keep 0 — non-zero triggers HTTP RSS, bitfield DoTutorial.. @0x073, dwUID @0x10B); statistics @0x10F (Level u8 @0x159, i64 Pang @0x15A);
sUserEquip @0x248 (guidChar @0x24C); mapStat[20]×43 @0x2B4 and @0x610; sCharacterInfo @0x96C (tid, guid, parts...); caddie @0xB28; club @0xB41; mascot @0xB53.
Errors: 0x0B version differs, 0x05/0x07 banned, 0x0E, 0x10, 0x13, 0xD6, 0xD7, 0xDB busy, 0x03/0xE0 + u32, 0xC9; 0xD2 u32 progress, 0xD3 u8 overlay.

## Other S→C
**0x4B** channel list (L4053): u8 n + n×77 (Name[64], u16 max, u16 cur, u8 id @0x44, u32 type @0x45 (0), i32 property) — mandatory, push after 0x42.
**0x4C** enter result (L4087): u8 1 OK / 2 full / 3 not found / 4 bad — reply to C→S 0x04.
Optional: 0x6E chars (u16 total,u16 n, n×0x1BC), 0x6F caddies (n×25), 0x70 equip (0x6C), 0x71 items (n×0xA8), 0x78 gift box (u8 mode=1,u16,u16,u16),
0xDF mascots (u8 n, n×63), 0x94 cookies u64, 0x10C (0x104) reply to 0x99.

## C→S
0x02 login; 0x04 u8 channel (0x83 if 0xF3 was received); 0x99 after 0x4C; 0xF6 u32 heartbeat; 0x55/0xB1/0x97/0x92 conditional; 0x81 = go to room list (past lobby).
