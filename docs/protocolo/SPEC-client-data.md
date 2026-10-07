# KR 645 QA exe + KR 642 data: misplaced/frozen scene objects

## Root cause (verified statically, not yet confirmed in-game)
- `wangreal_ReleaseQA.dll` in the KR642 folder (and the identical `wangreal.dll`, sha256 99a602fa...) is the
  **645 QA renderer** (PE timestamp 2011-10-21 03:23, same build as ProjectG_ReleaseQA.exe; the 642 live exe is 2011-08-18).
  It is byte-identical to rebang's rebuilt `build/wangreal/wangreal_ReleaseQA.dll`, so `source/client/device/wd3d8.cpp` is authoritative.
- That renderer draws through D3DX effects built from the data file **`pangya.fx`**
  (`CProjectG::UploadShaderSource`, projectg.cpp:1581 -> `WDirect3D8::SetShaderSource`, wd3d8.cpp:7556 -> `CreateEffect`, wd3d8.cpp:5258).
- **No `pangya.fx` exists in any of the 642 paks** (24,954 entries indexed). The 642 live exe has no "pangya.fx" string; the 645 QA exe does.
  So `SetShaderSource(NULL)`, every `D3DXCreateEffect` fails, `m_Effect=0` -> fixed-function draw.
- But the GPU reports VS>=1.1 / PS>=2.0, and `WDirect3D8::_SetTransform` (wd3d8.cpp:5307) then **does not pass D3DTS_WORLD to D3D**
  (`if (IsSupportVS() || state != D3DTS_WORLD) return;` – the effect was supposed to consume it). Blend matrices 1..n are still set.
  Result: anything whose world matrix is not identity (props/trees/NPCs, static-pet groups, animated objects) is drawn with a stale/identity
  matrix -> misplaced, stretched (mixed blend matrices), animated objects look frozen. Terrain (identity world) and sky look fine.
  Independent of GPU/driver/dgVoodoo/software T&L (all still report VS support).

## Refuted leads
- Pak encryption: `InitPakedFile2` only downloads the QA updatelist from patch.pangya.ntreev.com + CRC-checks, then calls the same
  `HookPAKFile`; entry decryption (XTEA key 0485b576..., LZ2 secutable) lives in `OpenPAKFILE2`/`cFileLZ2` and is identical in the
  642 and 645 exes. Pak parser `cd/pak.py` reads all 642 paks fine (course files are type 0x13 = LZ2).
- gbin object placement: 642 `*.gbin` are version 0x71 with 172-byte `sBgModel` records, matching 645 `WPolySoup::Load`; decoded matrices are sane.
- Timing: `CMainFrame::Main` uses `g_CurrentTime - m_baseTime` (unsigned subtract before float); uptime/affinity tests negative.
- Test patches: Ready+0x78 forces InitPakedFile (no updatelist server), jbe->jmp at file 0x39c5b5 skips the multi-CPU gbin CRC thread
  (crcdef.cpp CRCs are for 645 data), "wizcity"->"wizz" (no Wiz City in 642 data; 642 live uses "blue" as lobby BG, LOBBYBG_MAP still 19).

## Fix (new files only) – `emu/make_ff_client.py`
- `wangfixd_ReleaseQA.dll` = original DLL + 1 byte: file 0x1c625 `74 06` -> `eb 06` (always take the DLL's own "no shader GPU"
  branch that zeroes VertexShaderVersion/PixelShaderVersion after CreateDevice, wd3d8.cpp:2835) -> pure fixed-function, world matrices set.
- `LocalServerFix_ReleaseQA.exe` = ProjectG_ReleaseQA.exe + patch_client.py strings (127.0.0.1) + "wangreal" -> "wangfixd" at file
  0x62cc00 (WDeviceManager::LoadModule search prefix, so it loads `wangfixd_ReleaseQA.dll`). Differs from LocalServer_ReleaseQA.exe by 4 bytes.
- Launcher: `ABRIR_TESTE_FIX.bat`.
- Alternative fix: obtain the real `pangya.fx` from KR 645+ data and drop it in (as data/pangya.fx or a pak) – restores the shader path.

## Not explained by this
- Short shots (ball 37 units, state 2/3): physics/collision is CPU-side using gbin/pet data, not affected by D3D world matrices. Re-test after fix; if still short, look at server-sent shot/wind/terrain data.
- Two "9" glyphs bottom-right and intermittent avatar load: not investigated to root cause.
