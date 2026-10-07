# KR 645 — login server protocol (from the decompiled client)
`lt.c` = /root/ghidra-out/lobbytask.c, `sd.c` = /root/ghidra-out/serverdlg.c. LE integers, str = u16 len + bytes.

## Hello / request
- S→C raw `0x0000`: u32 parseKey (0..15), u32 serverUID (`loginunit.cpp:120-128`). Client waits ≤3 s (`loginunit.cpp:96`).
- C→S `0x0001`: str id, str pw(=HTTP AuthKey), u32 provType=2, u8 isWebLogin, u32 authUid(MemberNo), u64 0x7fffffffffffffff, u8 pcbang (`loginunit.cpp:138-168`).

## S→C 0x0001 login result: u8 sub + payload (`lt.c:2686-2944`)
sub 0 success: str id (must equal typed id, case-insens., else state 12), u32 uid (MyUID, non-zero), u32 identity (0; bits 0x4/0x10/0x8000 = GM),
u8 level, u8 adult(1), u32 flagBlock(0), u32 timeBlock(0), str nick (≤21 bytes, KR reads it: local content 0x81). → state 4.
Errors → FrLoginDlg state (`logindlg.cpp:399-711`): 2→6 bad id, 4→8 already logged (client sends 0x0004 to kick), 5 str→9 blocked,
6→7 wrong pw, 7 u32 str→10, 8→11 quit, 9→14, 0xA→15, 0xC→16, 0xD→17, 0xE→18, 0xF→19, 0x10→20, 0x13→23, 0xCF→26, 0xD4→24,
0xD9→13 create character, 0xDF u32→12, default→12; 0x12/200/0xE0 ignored.
Second password: **0xDE = confirmed (required before 0x0002)**; 0xDD confirm+dialog; 0xDC u8 create dialog; 0xD8 u32 forced nick change.

## S→C 0x0002 game server list (`lt.c:2945-3001`)
Dropped silently unless 0xDE was received (provType 2, `secondarypassword.cpp:351-366`).
u8 n + n × 92-byte `sGameServerInfo` (pack 1, `shared/globalgamedefine.h:1037`):
name char[40] @0, id u32 @0x28 (non-zero), maxUser i32 @0x2C, curUser i32 @0x30 (< max-200), addr char[18] @0x34,
port i32 @0x46, property u32 @0x4A (0), angelicWings u32 @0x4E, eventFlags u32 @0x52 (0; 0x10 hides for non-GM), eventValue u32 @0x56, icon u16 @0x5A.

## Other S→C
0x03 close login socket cleanly; 0x04 u8 needParentAgree; 0x05 u8(3) u32 u32 PC-bang item; 0x06 576 bytes macros; 0x08 u32 pcbang;
0x09 u8 n + n×92 messenger servers; 0x0B str; 0x0D/0x0E u32 code [str nick]; 0x0F u8 str; **0x10 str login auth key** (sent later by gameunit.cpp:165); 0x11 u8 error.

## C→S
0x01 login; 0x04 kick previous session; 0x06 str create nick; 0x07 str check nick; 0x08 u32 charTid u8 colors; 0x09 str / 0x0A str second password.
Selecting a server sends nothing to login; client connects to addr:port (`sd.c:1729,2944`).
Never close the login socket server-side (client shows "login server disconnected", `projectg.cpp:1359`).
