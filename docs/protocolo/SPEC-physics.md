# KR 645 QA + KR 642 data: "ball barely moves" investigation (2026-10-07)

Status: **narrowed, not yet pinned to one field.** User confirmed (17:29 logs): after equipping a bought character
(Kooh 0x04000005, guid 104) *and* a bought club set (0x1000000D, guid 103) every shot flies normally - the human's
and the bot's. The bot itself still has the starter kit (pangbot = Nuri 0x04000000 + Air Knight 0x10000000, guids 100/101).

## What the logs prove (verified, decrypted 0x1B, u16 frame count = ball+100 at sShotResult+35)
| time | shooter | block phase/power | result | frames |
|---|---|---|---|---|
| 16:26 | human (Nuri/AK) | 3 / 500 | (-63.2,2.6,-621.5) | 340 |
| 16:27 | bot (Nuri/AK) | 3 / 500, aim -0.044 | (-63.2,2.6,-621.5) | 289 |
| 17:06 | bot (Nuri/AK) | 4 / 494, aim +0.042 | (-63.2,2.6,-621.5) | 289 |
| 16:36 | human | 3 / 493 | tee, state 3 (OB) | 3 |
| 17:05 | human | 4 / 500 | 1.7 u ahead | 78 |
| 17:29+ | human Kooh/1000000D, bot Nuri/AK | same block format | normal (hundreds of units) | - |
- End point independent of power and aim, same frame count for two different bot blocks -> the launch velocity never
  reaches the simulation in a usable form (ball drops off the tee box and rolls into the same hollow).
- The 0x12/0x53 block format is identical in broken and working shots -> the server echo is not the cause.

## Refuted (verified)
- Course data: 177/198 KR642 `*.gbin` CRC32-match the 645 table in crcdef.cpp (mismatches only blue_01/11/16, wind_06,
  silv_*); property XMLs parse with the same 12 items the 645 CPropertyInfo::Init reads; impact.png rows match the exe.
- Test patch 0x39c6b5 (CProjectG::Ready, jbe->jmp): only skips the CRC_MT worker (crc result unused by physics).
  Patch 0x34ec82 is DevFinalStockPrice (shop). Both unrelated.
- Club stats: hardcoded in CClub::Init (1W range 230, power 236, loft 10 deg); club *sets* only load pets/camera vectors.
- Player stats (GetLevel) are clamped (power 0..100-15 etc.) and cannot zero the launch speed.
- Shooter's own starter kit: the bot (Nuri+Air Knight) shoots fine now -> the kit is not intrinsically broken for the
  shooter's data (GetPower/BallSpeedFactor/GetInitBallState use doc+0x175 = shooter index).

## Remaining hypotheses (one user test decides)
A. The **local** player's equipped character or club set feeds every simulated shot (local-only actor/club state).
B. **Human == bot duplicate**: both had Nuri + Air Knight with identical guids (char 100, club 101, ball tid) - shared
   puppet/motion or guid-keyed state. (No direct guid use found in golfrule/golfplayer/golftask, so weaker.)
Test matrix (all items are already in account 123's inventory, no code change needed):
1. Nuri + Air Knight, `!nobot` (solo) -> broken = A, works = B.
2. Kooh + Air Knight (bot on) and 3. Nuri + 1000000D (bot on) -> tells character vs club set under A.

## Fix
Not applied yet: changing player.py defaults / migrating data/*.json without knowing which field would be guesswork.
After the test: A-char -> new default character or parts fix in player.DEFAULT_*; A-club -> default club set;
B -> per-account unique guids (e.g. next_guid seeded from uid) and a bot kit that differs from the player's.

## Unexplained
"BAD"/"SPIN" labels: CAccuracyInfo::SetAccuracy shows "@Bad" only for phase 0/1 and the logged blocks are 3/4.
