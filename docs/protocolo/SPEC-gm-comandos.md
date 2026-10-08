# KR 645: comandos de GM (texto e pacotes)

Pesquisa somente leitura no cliente KR 645 QA. Complementa `SPEC-chat-gm.md` (chat azul, `/notice`, `/kick`, `/disconnect`,
`/identity` já implementados em `GameHandler.Gm.cs`). Legenda: **[C]** confirmado no cliente, **[R]** servidor de referência,
**[P]** palpite.

Fontes: `gh/` = `/root/ghidra-out`; `src/` = `/root/rebang/source/client/ProjectG`; `shared/` = `/root/rebang/source/shared`;
`GB/` = `/root/pangya-server/Server/GB/GameServer` (ids **C->S iguais** aos do KR; ids S->C do GB = KR **+2** nesta faixa:
GB 0x40 = KR 0x3E, GB 0x5B = KR 0x59, GB 0x9E = KR 0x9C, GB 0x4B = KR 0x49).

## 0. Bits de `dwIdentity` (doc+0x433) que o cliente testa

| bit | nome no GB [R] (`GB/Models/pangya_game_st.cs:772-904`) | uso no cliente [C] |
|---|---|---|
| 0x01 | A_I_MODE | — |
| **0x02** | GALLERY | **modo galeria/espectador**. Ligado pelo próprio cliente ao receber 0x47 com galeria ≠ 0 (`gh/lobbytask.c:3686-3700`). `GalleryMode()` = este bit. Também libera o duplo clique 0x3F (`src/lobbymain.cpp:11800-11811`) |
| **0x04** | GAME_MASTER | **GM**: toolkit `/...` (`CGMToolkit::IsAdministrator`, `gh/gmtoolkit.c:1303`), F10/Ctrl+Insert (`src/lobbymain.cpp:2531-2564`), entrar em sala cheia/privada (`src/lobbymain.cpp:9547`, `src/detailedroominfodlg.cpp:376-378`) |
| 0x08 | GM_EDIT_SITE | só entra na combinação 0x0E |
| 0x10 | (GB: block_give_item_gm) | junto com 0x04 (`& 0x14`) esconde o usuário nas listas (ver `SPEC-chat-gm.md` §5) |
| **0x40** | mod_system_event / GOD | `/itemdrop` (C->S 0x4E) (`src/hatmanager.cpp` CheckChatCommand, `shr edx,6`) |
| 0x80 | gm_normal (no KR o PC-bang soma 0x80 no 0x98) | `/identity user` manda 0x80 |
| **0x0E** (= 0x02\|0x04\|0x08) | observer | "admin observador": duplo clique na lista de salas entra direto como galeria com 0x3E (`src/lobbymain.cpp:9620-9632`); `CGolfRule::ProcessObserver` só roda com `& 0xE == 0xE` (`gh/golfrule.c:26975`) |

Regra do servidor: **a identidade vem só do banco** (`Player.IdentityFlags`), nunca do pacote. O bit 0x02 é estado de sessão
(galeria), não deve ser gravado.

## 1. Comandos de texto (toolkit) — C->S 0x8C `u16 tag` + dados

Já descrito em `SPEC-chat-gm.md` §3.1; aqui ficam os layouts exatos e a resposta esperada. Enum `eGMCommandTag` em
`src/gmtoolkit.h` (DUMMY 0 … MATCHHOLE 30); `eGMStatusFlag` VISIBLE 1, WHISPER 2, CHANNEL 4. [C]
O toolkit só roda se `dwIdentity & 0x04` (`gh/gmtoolkit.c:1303`). Depois de executar, o cliente mostra o resultado localmente
(verde `0xC6FF85` / laranja `0xFF7802`); **nenhum S->C de confirmação é esperado** [C]. O GB responde com 0x40 (= KR **0x3E tipo 7**)
"Executed Command." / "Nao conseguiu executar o comando." (`GB/Game/System/GameMasterSystem.cs:283-319`) [R] — útil, opcional.

| tag | comando | layout após o u16 | fonte [C] | o que o servidor faz |
|---|---|---|---|---|
| 3 | `/visible on\|off` | `u16 flags` (bit0 = visível; manda o estado inteiro dos 3 bits) | `SendStatus` `gh/gmtoolkit.c:520-548`, `CommandVisible` :728 | §2.1 |
| 4 | `/whisper on\|off` | `u16 flags` (bit1) | :786 | GB: `whisper = bit1`, `channel = !whisper` (`GameMasterSystem.cs:51-62`) [R]. GM passa a receber sussurros de todos |
| 5 | `/channel on\|off` | `u16 flags` (bit2) | :839 | GB: `channel = bit2`, `whisper = !channel` [R] |
| 8 / 9 | `/open nick`, `/close nick` | `str nick` | :4165, :4094 | lista de sussurros "abertos" do GM [R] |
| 10 | `/kick nick [-p]` | `u32 guid, u8 -p` | :2267 | **feito** |
| 11 | `/disconnect nick`, `/discon_uid uid` | `u32 guid` (+ C->S 0x61 `u32 guid` logo depois) | :2404, :1894 | **feito**; 0x61 é duplicata, ignorar |
| 13 | `/destroy n` | nada (vem junto de **C->S 0x60 `u16 sala`**) | `CommandDestroy` :1341 (só fora de sala, doc+0x49E1 == 0xFFFF) | §2.4 |
| **14** | **`/wind vel dir`** | **`u8 vel-1, u8 dir`** | `CommandWind` :1388-1456 | §2.2 |
| **15** | **`/weather fine\|cloud\|rain\|snow`** | **`u8 0..3`** | `CommandWeather` :924-1022 | §2.3 |
| 16 | `/identity admin\|user` | não usa 0x8C: **C->S 0x41** `u32 0x484\|0x80, str nick` | :1458 | **feito** (resposta 0x98) |
| 17 | `/notice texto` | não usa 0x8C: **C->S 0x57 `str`** | :1024 | **feito** |
| **18** | **`/giveitem nick tid qtd`** (pede "/Y") | **`u32 guid, u32 tid, u32 qtd`** | `CommandGiveItem` :2522-2700 (guid vem da lista do lobby doc+0x4A50; qtd 0 = erro de sintaxe) | §2.5 |
| **19** | **`/goldenbell tid qtd`** (pede "/Y") | **`u32 tid, u32 qtd`** | `CommandGoldenBell` :1589-1720 (só dentro de sala) | §2.5 |
| 20 | `/setprize` (lista) | por prêmio: `u32 tid, u32, u32, u8, u8` | `SendPrize` :1722-1862 | prêmios de torneio do GM; opcional |
| 25 | `/noticeprize n` | `u32 n, 20 bytes` | :2221 | opcional |
| 27 | `/setmission n` | `u32` | :1123 | opcional |
| 30 | `/matchmap n` (0..20) | `u32` | :1182 | opcional |
| 31 | `/matchhole n` | `u32` | :1221 | opcional |

Sem pacote (só locais): `/help`, `/command`, `/status`, `/list`, `/finditem`, `/category`, `/gettid` [C]. `loadscript` (23) e
`gettid` (25) estão desativados na tabela [C].

## 2. Detalhe dos comandos que faltam

### 2.1 `/visible` (tag 3) [C]+[R]

- GM com `& 0x04` (ou `& 0x14`) **não aparece** na lista de usuários do lobby para quem não é GM, a menos que
  `sBriefUserInfo.state & 1` (offset 0x3E) [C] (`SPEC-chat-gm.md` §5).
- Servidor: guardar `GmVisible = (flags & 1) != 0` na sessão; montar `sBriefUserInfo.state` com bit0 = `GmVisible`; mandar
  **0x44 `u8 3` (atualizar), `u8 1`, sBriefUserInfo** a todos do canal (`GameHandler.Room.cs` já tem `Lobby(...)` com sub).
  Se estiver numa sala, reenviar o slot (**0x46 sub 3**, `RoomPackets.SlotUpdate`) [R: `GB/Game/Channel.cs:4990-5030`
  `updatePlayerInfo` + `sendUpdatePlayerInfo(…,3)` + `r.sendCharacter(…,3)`].
- Guardar também `GmWhisper` (bit1) e `GmChannel` (bit2) que chegam nos tags 4/5 (o cliente sempre manda os 3 bits juntos).

### 2.2 `/wind vel dir` (tag 14) [C]

C->S `0x8C u16 14, u8 v, u8 d`:
- `v = vel - 1` (cliente aceita vel 0..18; **vel 0 vira 0xFF**) [C `gmtoolkit.c:1432-1440`];
- `d = (byte)atoi(dir)`: o cliente checa `< 0x169`, mas já truncou para byte, então 300 vira 44 [C].
- O cliente **não manda** nos tipos 4 Tournament, 5 Team30s, 6 GuildMatch, 9 Approach, 10 NewApproach, 14; no tipo 2 (lounge)
  mostra erro (`doc+0x49E0` = tipo da sala, enum em `shared/globalgamedefine.h:3-20` = `GameMode` do servidor) [C].

Resposta: atualizar o vento do buraco atual no servidor e mandar a todos da partida o **S->C 0x59** que já existe
(`InGame.Wind`: `u8 vento 0..8, u8 0, u16 direção, u8 1`; `SPEC-ingame.md` §0x59; `CWind::Init`). GB faz o mesmo com 0x5B
(`GB/Game/Base/VersusBase.cs:384-430`) e muda o "degree" do jogador da vez [R].
Regras: só GM, só com partida em andamento de um tipo permitido, `v` limitado a 0..8 (escala normal; até 17 é aceito pelo
`CWind`, mas o servidor/bot calculam com 0..8) [P], `d` como está (0..255). O vento novo vale para a próxima tacada; não
mandar no meio de uma tacada (esperar o 0x1B/fim da tacada) [P]. Atualizar o vento que o servidor usa (bot/física).

### 2.3 `/weather` (tag 15) [C]+[R]

C->S `0x8C u16 15, u8 w` (0 bom, 1 nublado, 2 chuva, 3 neve).
Resposta: **S->C 0x9C `u8 clima, u8, u8`** (os dois últimos são lidos e ignorados) [C]:
- lounge: `ntAvatarChatTask::OnPacket` → `CSceneManager::SetWeather(clima, false)` (`gh/avatar_task.c:2247-2253`);
- partida: `CGolfTask` case 0x9c → msg 0x142 ao "GolfBg" → `SetRenderWeather` + `SetWeather` (`gh/golftask.c:6195-6210`,
  `gh/golfbg.c:2077-2083`).
- GB: lounge guarda `m_weather_lounge` e manda a toda a sala; em partida muda o clima do buraco e manda a todos do jogo
  (`GB/Game/Room.cs:7554-7605`, `VersusBase.cs:3179-3215`, `TourneyBase.cs:2766-2790`; GB escreve `u16 clima, u8 1` =
  mesmos 3 bytes) [R].
Regras: só GM, `w ≤ 3`, só dentro de sala (lounge ou partida); guardar na sala para mandar a quem entrar depois.

### 2.4 `/destroy n` (C->S 0x60 `u16 sala` + 0x8C 13) [C]

Só fora de sala [C]. Nenhum S->C específico. Servidor [P]: tirar todos da sala `n` do canal do GM (0x4A `u16 0xFFFF` a cada um,
como na saída normal), apagar a sala e mandar a atualização da lista de salas. Tratar só o 0x60 (o 0x8C 13 não traz o número).
GB não implementa (`CCG_DESTROY: break`) [R].

### 2.5 `/giveitem` (18) e `/goldenbell` (19) [C]+[R]

- giveitem: `u32 guid` (do alvo, pego da lista do lobby), `u32 tid`, `u32 qtd` (> 0).
- goldenbell: `u32 tid`, `u32 qtd`, para **todos da sala** do GM.
- Nenhuma resposta esperada pelo cliente. GB valida tid no IFF, `qtd ≤ 20000`, e entrega **pelo correio**
  (`MailBoxManager.sendMessageWithItem`, texto "GM Send Gift: item[ nome ]") (`GameMasterSystem.cs:199-255`,
  `Room.cs:7608-7660`) [R].
- Servidor: usar o sistema de correio já existente (`GameHandler.Mail.cs`), avisar o alvo (aviso de correio novo) e responder ao
  GM com 0x3E tipo 7. Validar tid no IFF (só itens compráveis/existentes), qtd 1..limite por tipo, alvo online.

### 2.6 `/itemdrop` (C->S 0x4E) [C]

Comando de chat (não do toolkit), exige `dwIdentity & 0x40` (`gh/hatmanager.c:1631,1650`):
- `/itemdrop` → `u32 3, u8 0` (desliga);
- `/itemdrop taxa` → `u32 3, u8 1, u32 taxa` (cliente recusa < 100; "%" sobre o padrão 100).
GB chama o pacote `CLIENT_SET_SYSTEM` e não trata (`GB/PangyaEnums/PacketGame.cs:587`) [R]. O `u32 3` parece ser o id do
"sistema" (3 = drop de item) [P]. Sem resposta conhecida.
Servidor [P]: guardar uma taxa global de drop (itens de campo/caixas do jogo) em memória, 100..1000 %, só para quem tem 0x40;
log de auditoria. Opcional.

## 3. Pacotes de GM fora do toolkit

### 3.1 C->S 0x3E — entrar como galeria (espectador) [C]

Layout: **`u16 sala, str senha`** (igual ao 0x09 de entrar na sala).
Origens:
1. botão "galeria" do diálogo de detalhes da sala — **qualquer jogador**, se `nGalleryNum != nGalleryLimit` ou GM, e se
   sala pública ou GM (`src/detailedroominfodlg.cpp:372-390`);
2. duplo clique na lista de salas por "admin" (`dwIdentity & 0x0E == 0x0E`), sem testar cheia/senha
   (`src/lobbymain.cpp:9620-9632`).

GB trata como entrada normal com checagem de senha (`GB/Game/Channel.cs:1269-1300` `requestEnterSpyRoom`) [R].

Resposta [C] (`gh/lobbytask.c:3670-3700`): **S->C 0x47 `u8 0, u8 galeria=1, u32 guidAlvo, sRoomInfo 0xB2`**. Com
galeria ≠ 0 o cliente liga `dwIdentity |= 2` e grava `dwGalleryGuid = guidAlvo` (doc+0x437, quem a câmera segue);
galeria = 0 desliga o bit e põe 0xFFFFFFFF. Depois vêm 0x48 e 0x46 como numa entrada normal (`SPEC-room.md`). O espectador
**não ocupa slot**; a sala conta `nGalleryNum`/`nGalleryLimit` (sRoomInfo 0x45/0x46).
Hoje `RoomPackets.EnterRoom` manda sempre `U8(0).U8(0)`.

### 3.2 C->S 0x3F — trocar o alvo da galeria [C]

Layout: **`u32 id`**. Origens:
- sala de espera: duplo clique num jogador, só com `dwIdentity & 0x02` e alvo ≠ `dwGalleryGuid`; manda `m_selUID`
  (`src/lobbymain.cpp:11800-11811`);
- partida (`CGolfRule::ProcessObserver`, só `& 0x0E == 0x0E`): troca automática a cada ≥ 3 s em jogos em massa
  (`gh/golfrule.c:27180-27196`), manda o id do jogador escolhido (sPlayerData +4);
- início da partida em galeria nos tipos 4/5/6/9/10: manda `sUserInfo.dwGuid` do alvo atual (`gh/golftask.c:1817-1829`).

Resposta [C]: **S->C 0x96**, só processado com `GalleryMode()`:
- lobby/sala: `sUserInfo 0xB92` do novo alvo → copia para `m_userInfo[0]` e `dwGalleryGuid = sUserInfo.dwGuid`
  (`gh/lobbytask.c:4872-4888`);
- partida não-massa: **`u32 guid`** → `dwGalleryGuid` (`gh/golftask.c:5799-5806`);
- partida em massa: `sUserInfo 0xB92` (`gh/golftask.c:5807-5830`).
Servidor: aceitar `id` como uid **ou** guid de um membro da mesma sala; recusar se o remetente não está em galeria.

### 3.3 C->S 0x4C — F10 na sala (GM) [C send / P efeito]

`CLobbyMain::ProcessAdministrator`: com `& 0x04`, layout "GAMEROOM"/"GAMEROOM_EXT", tecla F10 e um slot selecionado
(`m_selOID != -1`) → **`u32 oid`** (guid do slot) (`src/lobbymain.cpp:2537-2546`, `gh/lobbymain.c:10856-10864`).
GB chama de `CLIENT_BANISH_ALL` e não trata [R]; emulador chama de "gm_kick_room" [R]. Sem resposta conhecida.
Efeito sugerido [P]: **expulsar o jogador selecionado da sala** mesmo sem ser dono (mesmo fluxo do 0x26: 0x4A ao alvo, 0x46 sub 2
aos outros). Não desconectar.

### 3.4 C->S 0x5D — "espionar" sala (GM) [C send / P efeito]

- lista de salas, Ctrl+Insert com sala selecionada: **`u16 sala`** (`src/lobbymain.cpp:2549-2562`);
- dentro da partida, admin `& 0x0E == 0x0E`, sala Stroke (tipo 0), Ctrl+Insert: **sem dados** (`gh/golfrule.c:26985-26994`).
GB: `CLIENT_SPY_ENTER_ROOM`, não tratado [R].
Efeito sugerido [P]: com `u16` = entrar como galeria na sala (mesma resposta do 0x3E, sem senha e sem contar no limite);
vazio (em jogo) = ignorar/registrar.

### 3.5 Outros já cobertos

- 0x41 `/identity` (todos podem mandar; servidor só aceita GM e não grava) — feito.
- 0x57 `/notice` — feito. 0x61 — duplicata do 0x8C 11. 0x60 — §2.4.

## 4. Segurança (valer para todos)

1. **Toda** ação confere `Player.IdentityFlags` do banco (0x04; 0x40 para 0x4E). O cliente libera comandos só pela identidade que
   o servidor mandou, mas qualquer cliente adulterado pode mandar 0x8C/0x4E/0x4C/0x5D/0x60/0x61.
2. Nunca aceitar identidade vinda do cliente (0x41 só muda a tela do próprio GM; já é assim).
3. guid/uid de alvo: procurar no servidor, recusar alvo GM (kick/disconnect/0x4C), recusar alvo fora do canal/sala quando o
   comando é de sala.
4. Itens (giveitem/goldenbell): tid existente no IFF, `qtd` limitada, entrega por correio dentro de transação, log com GM, alvo,
   tid e qtd. Considerar um bit/flag separado para "pode dar item" (GB usa `block_give_item_gm`).
5. Limitar valores (`vento ≤ 8`, `clima ≤ 3`, `taxa 100..1000`), checar tipo de sala e se há partida.
6. Galeria (0x3E): jogador comum só em sala pública (ou com senha certa) e com vaga de galeria; GM pode sempre.
7. Rate limit simples por sessão (ex.: 5 comandos/s) e log `Log.Info` de todo comando aceito ou recusado.
8. Pacote de GM de não-GM: ignorar e registrar (opcional: contador para punir).

## 5. Plano de implementação

| # | item | onde | prioridade |
|---|---|---|---|
| 1 | 0x8C 15 `/weather` → 0x9C `u8 w,0,0` para a sala (lounge) ou partida; guardar `Room.Weather` e mandar ao entrar no lounge | `GameHandler.Gm.cs` + `Lounge` | alta (simples, visível) |
| 2 | 0x8C 14 `/wind` → atualizar vento do buraco + `InGame.Wind` (0x59) a todos; só tipos permitidos | `GameHandler.Gm.cs` + `InGame.cs` | alta |
| 3 | 0x8C 3/4/5 `/visible`,`/whisper`,`/channel`: estado na sessão; `state` bit0 no `BriefUser`; 0x44 sub 3 ao canal e 0x46 sub 3 na sala | `Gm`, `RoomPackets.BriefUser` | média |
| 4 | 0x8C 18/19 giveitem/goldenbell via correio + 0x3E tipo 7 ao GM | `Gm` + `Mail` | média |
| 5 | 0x4C F10 → banish do slot (reaproveitar o 0x26) | `Gm` + `Room` | média |
| 6 | 0x60 `/destroy` → esvaziar e apagar a sala | `Gm` + `Room` | baixa |
| 7 | Galeria: 0x3E (todos) e 0x5D `u16` (GM) → 0x47 com `u8 1, u32 alvo`; contar `nGalleryNum`; 0x3F → 0x96; espectador fora dos slots e fora do resultado | `Room`, `RoomPackets`, `InGame` | baixa (grande) |
| 8 | 0x4E `/itemdrop` (bit 0x40) → taxa global de drop em memória | `Gm` | baixa |
| 9 | Confirmação "Executed Command." (0x3E tipo 7) em todos os comandos aceitos e recusa com motivo | `Gm` | baixa |
| 10 | Tags 8/9 (lista de sussurro), 20/25/27/30/31 (prêmios/missão/match) | — | opcional |

Testes: unidade para cada layout (leitura do 0x8C por tag, 0x9C/0x59/0x47-galeria gerados), e teste de recusa para não-GM.
