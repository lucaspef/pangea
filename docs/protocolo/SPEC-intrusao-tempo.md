# SPEC — Entrar em partida em andamento ("intrusão") e limite de tempo da partida (KR 645 QA)

Marcas: **[C]** confirmado no cliente (reconstrução `rebang`, decompilação Ghidra ou asm do exe), **[R]** servidor de
referência (GB `/root/pangya-server/Server/GB/GameServer`), **[P]** palpite. Abreviações: `IC` =
`/root/rebang/source/client/ProjectG/intrusion.cpp`, `LM` = `lobbymain.cpp` (rebang), `gt` = `/root/ghidra-out/golftask.c`,
`lt` = `/root/ghidra-out/lobbytask.c`, `gr` = `/root/ghidra-out/golfrule.c`, `gui` = `/root/ghidra-out/gui_control.c`,
`lmc` = `/root/ghidra-out/lobbymain.c`, `asm` = `/root/pg645.asm`. Inteiros LE. `str` = string PangYa (u16 tamanho + bytes CP949).
Tamanhos: `sRoomInfo` = 0xB2, `sSlotInfo` = 0x152, `SYSTEMTIME` = 16.

Ids GB → KR nesta faixa: GB = KR + 2 (GB 0x113 = KR **0x111**, 0x8C = **0x8A**, 0x8D = **0x8B**, 0x6D = 0x6B, 0x6C = 0x6A) [C+R].

---------------------------------------------------------------------------------------------------------------------
## 0. Resumo

| assunto | pacote | direção | onde no cliente |
|---|---|---|---|
| pedir/confirmar entrada, mandar placar | **0x9A** u8 sub | C->S | `IC` 756-764 (sub 0), 602-609 (sub 1), 461-464 (sub 2) [C] |
| equipamento do intruso | 0x0C u8 7 + 16 B | C->S | `IC` 624-635 [C] |
| todas as respostas da intrusão | **0x111** u8 tipo, u8 código, … | S->C | `OnPacketCommon` caso 0x111 @0x743548 → `CIntrusion::PacketAnalysis` @0x61A9A0 [C] |
| tempo decorrido da partida (acerta o relógio) | **0x8B** u32 ms | S->C | `OnPacketCommon` caso 0x8B @0x73997F [C] |
| fim do tempo (torneio) | **0x8A** (vazio) | S->C | `gt` 5302-5352, `lt` 4781-4832 [C] |
| estouro do relógio de tacada | 0x5A u32 guid | S->C | `gt` 4605, `gr` 30395-30421 [C] |

**S->C 0xBA não é a intrusão.** É o "둘러보기" (assistir a uma partida em andamento, modo galeria): `lt` 5128-5225 lê
u8 código (0x0B = "알림 : 둘러보기가 가능한 게임중인 방을 찾지 못했습니다.", string @0x9EAE94; 0x10 = "알림 : 로딩되기
전에 게임이 종료될 우려가 있습니다.", @0x9EAE60) ou, nos outros códigos, sRoomInfo + u32 + u8 n + n × sSlotInfo + sUserInfo +
mapa/modo/buracos/tempos + u8 buraco atual; `gt` 6407 trata o mesmo id com `GalleryMode()` [C]. Fora deste documento.

Como achei o 0x111: no asm, `CTask::OnPacketCommon` @0x732640 faz `add eax,-0x2d` e indexa as tabelas 0x746118/0x745E1C
(asm 0x732696-0x7326AB); o único `call 0x61a9a0` (PacketAnalysis, PDB `?PacketAnalysis@CIntrusion@@…` [0001:002199A0])
está em 0x74359A, no caso de índice 0xE4 → id **0x111**. O mesmo método resolve 0x8B (índice 0x5E) e 0x50 [C].

---------------------------------------------------------------------------------------------------------------------
## Parte 1 — Intrusão (entrar numa partida já em andamento)

### 1.1 O que é e onde vale
- Os textos do cliente chamam de "30인 대회 게임중 입장" (entrar em jogo do torneio de 30) (`IC` 316, 401, 672) [C].
  O conteúdo local 0x2E (S3_INTRUSION) está ligado no KR (SPEC-room.md) e o 0x111 só é processado com ele ligado
  (asm 0x743548: `IsLocalContent(0x2e)`) [C].
- O GB só aceita salas `TIPO.TOURNEY` (Channel.cs:4452), públicas, em jogo, não cheias, dentro da janela de entrada
  (Room.cs:7316: 5 min com 9 buracos, 10 min com 18) [R]. O texto KR do código 2 diz "게임 시작 후 **5분**이 지난 방에는
  입장 하실 수 없습니다" [C `IC` 688] → usar 5 min.
- Recomendo só o **modo 4** (torneio). Modo 6 (GuildMatch) tem pares fixos; modo 14 (chaos) **não pode**: o 0x50 do
  modo 14 tem um u8 de mapa por buraco, mas o pacote de início da intrusão não tem (`IC` 251-256) [C]; 10 (approach) tem
  buracos sincronizados [P].

### 1.2 Lista de salas [C]
- `sRoomInfo` +0x31 `bAvailable`, +0x32 `bIntrusion`.
- Cor da linha: `bAvailable && bPublic && bIntrusion` → `CIntrusion::GetActiveColor()` = 0xC3E5FFD8 (azul claro);
  `bAvailable && bPublic` → branco; senão 0xB2FFFFFF (apagada) (`LM` 9219-9233, 9255-9270). Ordenação: entre salas
  disponíveis, as com `bIntrusion` vêm depois (`LM` 8505-8540).
- Duplo clique (`LM` 9536-9690):
  - `bAvailable = 1`: confere lotação ("정원이 초과 되었습니다") e as regras de modo; se `bPublic` (ou GM/convite) e
    `bIntrusion` e **não** GM → `CIntrusion::DoJoinRoom` (0x9A sub 0). GM (dwIdentity & 0x14) usa o 0x09 normal.
    Sala com senha → diálogo de senha → 0x09 (o servidor recusa com "em jogo").
  - `bAvailable = 0`: se `bIntrusion` → copia a sala para `Doc()->m_roomInfo` e `DoJoinRoom`; senão "게임 진행중인 방입니다".
- O GB, durante a janela, põe `state = 1` (bAvailable) **e** `flag = 1` (bIntrusion) e manda 0x45 sub 3; ao fechar a
  janela, os dois voltam a 0 e manda 0x45 sub 3 de novo (Channel.cs:3041-3050, 18240-18256) [R].

### 1.3 C->S 0x9A [C]
| sub | layout | quando |
|---|---|---|
| 0 | `u8 0, u16 sala` | duplo clique (`IC` 756-764) |
| 1 | `u8 1, u16 sala` | "예" no diálogo de confirmação (`IC` 602-609) |
| 2 | `u8 2, sIntrusionScore (242 B)` | resposta a 0x111 tipo 9 (`IC` 419-466) |

`sIntrusionScore` (`IC` 18-31, pack 1): `u8 holeStroke[18]` +0, `i32 holeScore[18]` +18, `i64 holePang[18]` +90,
`u32 uid` +234 (= guid do intruso, copiado do tipo 9), `u32 sender` +238 (meu guid, ou 0xFFFFFFFF se não estou na
lista de rivais; aí os arrays vão zerados).

Junto com o sub 1 o cliente manda **C->S 0x0C** `u8 7, u32 guidChar, u32 guidCaddie, u32 guidClubSet, u32 tidBall`
(`IC` 594-635); o caddie vai 0 se for alugado com mensalidade e vencido [C]. Nenhuma resposta é esperada [P].

### 1.4 S->C 0x111 — layout comum [C `IC` 293-476]
`u8 tipo, u8 código, …`. **Os dois bytes são sempre lidos**, mesmo quando o código não importa (mandar 0).
⚠ Se o diálogo da intrusão (`m_pDlg`) estiver aberto, **todo** 0x111 é descartado (`IC` 295-296). Antes do
PacketAnalysis o OnPacketCommon manda msg 2 ao ator "Lobby" (asm 0x74355A-0x743593).

| tipo | código | resto | efeito no cliente |
|---|---|---|---|
| 3 | 0 | `u16 sala, u32 decorrido_ms, u32 limite_ms, sRoomInfo` | grava a sala em `Doc()->m_roomInfo` e `roomIndex`; abre "IntrusionAlarm" (`IC` 303-337) |
| 4 | 0..4 | ver 1.6 | entrada propriamente dita (`IC` 119-291) |
| 5 | (0) | — | "유저가 많아 입장에 실패 하였습니다. 다시 시도 하시겠습니까?"; "예" só fecha (não reenvia nada), "아니오" → Lobby msg 0x3B (`IC` 397-413, 647-654) |
| 6 | código de recusa | — | "IntrusionDeny" com a mensagem do código; OK → Lobby msg 0x3B (`IC` 415-416, 656-739) |
| 7 | 0 | `str nick, sRoomInfo, u8 n, u32 bônus, n × sSlotInfo` | para quem já está jogando: alguém entrou (`IC` 339-391) |
| 8 | 0 | `u8 n` | `m_calcExpMemberSize` = n (`IC` 472-474) |
| 9 | 0 | `u32 uid, u32 quitOrder` | pede o placar (`IC` 419-466) |
| 10 | 0 | `u32 oid, u8 totalStroke, u8 buraco, i32 totalScore, i64 totalPang, sIntrusionScore` | placar de um rival para o intruso (`IC` 478-514) |

#### Tipo 3 — confirmação
Diálogo "30인 대회 게임중 입장 확인" / " ※ '예'를 누르면 즉시 게임이 시작됩니다." / "해당 게임은 게임 시작 후
'MM분SS초'가 지났고 'MM분SS초'가 남아 있습니다. 진행중인 방에 입장하시겠습니까?" com `GetInfoTime(decorrido, limite)`
(segundos inteiros; restante = limite − decorrido, **fica negativo** se decorrido > limite) (`IC` 581-592) [C].
- "예" → 0x9A sub 1 + 0x0C sub 7.
- "아니오" → Lobby msg 0x3B e `roomIndex = 0xFFFF`; **nenhum pacote** (`IC` 637-641) → o servidor não pode reservar
  vaga no sub 0.

#### Tipo 6 — códigos de recusa (`IC` 20-30, 676-736) [C]; códigos GB [R]
| código | enum | mensagem | GB |
|---|---|---|---|
| 1 | FULL | "인원이 가득 차서 방에 입장 하실 수 없습니다." | — (GB manda 1 para erros genéricos) |
| 2 | TIMEOUT | "게임 시작 후 5분이 지난 방에는 입장 하실 수 없습니다." | Room.cs:7320 |
| 3 | PRIVATE | "비공개 방에는 게임중 입장 하실 수 없습니다." | — |
| 4 | ALREADY_ENTERED | "이전에 게임중 입장한 기록이 있어서 당분간 입장하실 수 없습니다." | — |
| 5 | NOT_ALLOWED | "현재 게임중 입장이 불가능한 방입니다." | — |
| 6 | ALREADY_PLAYED | "해당 방에서 플레이한 기록이 있어서 게임중 입장하실 수 없습니다." | Room.cs:7310, TourneyBase.cs:2003 |
| 7 | KICKED | "해당 방에서 강제 퇴장당한 기록이 있어서 입장하실 수 없습니다." | Room.cs:7298 |
| outro | — | diálogo sem texto | — |

### 1.5 Ordem dos pacotes (servidor) [C layouts / R ordem]
```
intruso                                   servidor
0x9A {0, sala}                    ->      valida (1.7)  -> 0x111 {3,0, sala, decorrido, limite, sRoomInfo}
                                                         ou 0x111 {6, código}   (ou {5,0} "ocupado")
[diálogo "예"]
0x9A {1, sala}  +  0x0C {7, equip} ->     valida de novo; põe na sala e na partida
                                  <-      0x111 {4,0} sRoomInfo        (ENTER)
                                  <-      0x111 {4,1} slots            (PLAYERS)
                                  <-      0x111 {4,2} u32 100          (taxa)
                                  <-      0x111 {4,3} asas + SYSTEMTIME (RIVALS)
                                  <-      0x111 {4,4} início           (START -> ChangeTask CGolfTask)
                    para quem está no campo: 0x111 {7,0, nick, sRoomInfo, n, bônus, slots}
                    lista do lobby: 0x45 sub 3 (nUserNum novo)
[CGolfTask carrega: CGolfRule30Battle::SetPlayer cria os rivais a partir dos slots e manda 0x1A]
0x48.. 0x1A, 0x11                 ->      (primeiro 0x11 do intruso)
                                  <-      0x59 vento, 0x51 guid próprio      (como hoje)
                                  <-      0x8B u32 decorrido                  (relógio, 2.3)
                                  <-      para cada outro jogador: 0x111 {10,0,…}; terminou: 0x6A(guid,2)
                                  <-      0x111 {9,0, guid DO INTRUSO, n}   (só acerta quitOrder)
... segue o fluxo mass normal (SPEC-modes.md §1)
```
GB: monta {4,0} {4,1} {4,2 rate_pang} {4,3} {4,4} e o {7} para todos menos o intruso (Room.cs:4476-4535,
TourneyBase.cs:89-130); no `requestFinishLoadHole` do intruso faz broadcast de {9, oid do intruso, nº jogadores}
(TourneyBase.cs:240-255); cada cliente responde 0x9A sub 2 e o servidor devolve {10} ao intruso e a quem mandou
(TourneyBase.cs:2048-2093); 0x8D (KR 0x8B) vai a cada início de buraco (TourneyBase.cs:216-219) [R].
**Recomendação [P]:** o C# já tem tacadas por buraco, placar, pang de todos → montar o {10} no servidor (sem o
vai-e-volta 9 → 0x9A sub 2 → 10, que confia no cliente). O tipo 9 só para o intruso (uid = guid dele) acerta o
`quitOrder` de todos os rivais (`IC` 425-433). Se um 0x9A sub 2 chegar mesmo assim, ignorar e logar.

### 1.6 Tipo 4 — layouts [C `IC` 119-291]
| código | resto | o que o cliente faz |
|---|---|---|
| 0 ENTER | `sRoomInfo` | `dwIdentity &= ~2`, `dwGalleryGuid = -1`, limpa slots, rivais e pares do GuildMatch, `m_bGameOver = 0`; grava a sala; manda MSN 0x23 (`sIntrusionEnterInfo` 0x4B) se o mensageiro estiver ligado |
| 1 PLAYERS | `u8 n, n × sSlotInfo` | Lobby msg 0x2E; limpa o chat; slots (bReady = bMaster) |
| 2 | `u32` | `Doc()->m_expRate` (padrão 100; GB manda `rate_pang`) |
| 3 RIVALS | `u8 angelicWings, u8 angelicWingsEffect, u8 devilWings`, **`SYSTEMTIME`** (conteúdo 0x51 = CARD_SYSTEM ligado no KR) | `ClearRivalVars`, hora do servidor, cartas, `SetEquipCharCaddieInfoFromMyInfo` — mesmos bytes do 0x74 mass que o C# já manda (`u8 0,0,0 + SYSTEMTIME`) |
| 4 START | `u8 mapa, u8 modo, u8 holeType, u8 buracos, u32 m_roomType, u32 shotTimeLimit, u32 gameTimeLimit, 18 × {u32 semente, u8 buraco}, gimmicks` | limpa a loja Bongdari e o UserInfo; grava tudo; `CGimmickContainer::LoadFromPacket` (u32 semente + 18 × {u8 n, n × 0x14}, como no 0x50); `ChangeTask("CGolfTask")`; Score msg 0x131 e 0x2A |

- O START **não** tem o u8 de mapa por buraco do modo 14 e **não** liga o relógio da partida (`m_approachStartTime`
  só é ligado pelo 0x50 ou pelo 0x8B, 2.3) → mandar 0x8B depois [C].
- `m_roomType` (Doc+0x4568) é o **tid do troféu (Match.iff)**: é o que o HUD procura com `FindMatch` para escrever o
  nome do torneio (`gui` 8108-8110), o que o 0x77 sobrescreve e o que o tipo 7 compara com `sRoomInfo.tidMatch`
  (`IC` 352-367) [C]. Mandar `MatchTid` aqui. (Obs.: o C# manda `GameSeed` nesse campo do 0x50 —
  `RoomPackets.GameInit`; ver risco R6.)

### 1.7 Validações do servidor (sub 0 e de novo no sub 1)
Na ordem, com o código do tipo 6: sala existe e está jogando (senão 5) · modo permitido (4; senão 5) · janela aberta
(decorrido < 5 min; senão 2) · sem senha (senão 3) · `Players.Count < MaxPlayers` (senão 1) · uid nunca jogou
nesta partida (lista de quem já esteve; senão 6) · não foi expulso da sala (C# não tem expulsão de sala hoje; 7
reservado) · já entrou como intruso nesta partida e saiu (4) [R + P]. O jogador não pode estar em outra sala (se
estiver na lista de salas, sair dela como no `EnterRoom`).

### 1.8 Tipo 7 — quem já está na partida [C `IC` 339-391, 516-579]
- Chat "(알림) <nick>님이 게임중에 입장하셨습니다." (0xFFFF7878); grava a sala; se `m_roomType != tidMatch` e o tid
  existe em Match.iff: `m_roomType = tidMatch` e chat "(알림) 대회 등급이 [ nome ]으로 변경 되었습니다.".
- Ordem de leitura: **u8 n antes do u32 bônus** (`IC` 373-374; GB igual: Room.cs:4527-4533).
- `PlayersUpdate(bônus)`: rival que já existe → `quitOrder = bônus`; slot novo → cria `sRivalData` (nick, guilda,
  `order = quitOrder = bônus`, `finishOrder = n`, `hole = holeOrder[0]`, `ballPos = tee` via `GOLFDOC()`, estado 0);
  slot com guid 0xFFFFFFFF → `GetPlayer(0)->state = 3` (não mandar). GB: bônus = nº de sessões na sala [R].
- ⚠ `PlayersUpdate` usa `GOLFDOC()`; quem já terminou está no lobby ("GAMEROOM_EXTRES") e o ponteiro Doc+0x5040 é
  zerado no `~CGolfDoc` (golfdoc.c:21665) → risco de crash [P]. Ver R3.

### 1.9 Tipo 10 — placar de um rival para o intruso [C `IC` 478-514]
`u32 oid, u8 totalStroke, u8 buraco, i32 totalScore, i64 totalPang, sIntrusionScore`. Se `oid` = meu guid, ignora
(é por isso que o GB pode mandar a mesma cópia a quem respondeu). Senão grava totais e os 3 arrays no rival `oid` e
atualiza Score/Spot. **Não mexe no estado** do rival → para quem já terminou, mandar também 0x6A(guid, 2); quem saiu
não está nos slots e não vira rival [C + P].
Campos: `buraco` = número do buraco que ele joga agora (rival.hole; quem terminou: o último + 1 como faz o 0x6B,
ou o último [P]); `holeStroke[h]`, `holeScore[h]` (tacadas − par), `holePang[h]` indexados pelo **número** do buraco
(o 0x6B usa o número, `gt` 5003-5016). Precisa ir **antes** de qualquer 0x6B desse rival ao intruso: o 0x6B grava
`hole = total novo − total guardado` (`gt` 5003-5009) [C].

### 1.10 Tipo 8 — membros para o EXP/medalha
Usado em `LM` 8365-8380 (medalha da lista: com n ≠ 0 e modo 4 usa n em vez do tamanho da lista de rivais),
`awarddlg.cpp:183`, resultdlg/scoreboard [C]. GB manda `u8 nº de jogadores` a todos quando a janela de entrada fecha
(Tourney.cs:461-474) [R]. `CIntrusion::FinishGame()` zera no duplo clique de sala (`LM` 9544) [C].

---------------------------------------------------------------------------------------------------------------------
## Parte 2 — Limite de tempo

### 2.1 Campos e padrões [C]
`sRoomInfo` +0x4D `shotTimeLimit`, +0x51 `gameTimeLimit` (ms). No jogo: `Doc()->m_golfGame.shotTimeLimit/gameTimeLimit`
(Doc+0x4770/+0x4774), vindos do 0x50 ou do 0x111 {4,4}. Padrões do diálogo (`LM` 193-236):
| modo | shotTimeLimit | gameTimeLimit | UI (changeroominfodlg.cpp:313-331) |
|---|---|---|---|
| 0 stroke, 1 team, 3 match, 7 pang battle | 40 000 | 0 | segundos de **tacada** |
| 4 torneio, 5 torneio equipe, 6 guild | 0 | 2 400 000 (diálogo cria com 1 800 000) | **minutos** de partida |
| 9, 10 approach | 40 000 | 40 000 | **segundos** por buraco |

Lista de salas/sala (`LM` 3810-3840): não-mass mostra "%d초" ou "시간제한없음" (0); mass "%d분"; tipo 6 fica vazio;
o tipo 10 cai também no "%d분" (mostra "0분") — cosmético [C].

### 2.2 Stroke / team / match / pang battle (0, 1, 3, 7): só relógio de tacada
- **Não existe relógio de partida**: `gameTimeLimit` = 0 e nada no campo o lê fora dos modos mass (`gui` 8068-8100
  só desenha no ramo mass) → **nada acontece "quando o tempo de jogo acaba"** [C].
- Relógio de tacada (`clock.cpp`): começa com msg 0x1E9 (vez começando) e, online, se `shotTimeLimit ≠ 0`, não sou
  observador e é minha vez, manda **C->S 0x22** (sem dados) (`clock.cpp:137-146`). Ponteiro de 12° a 290°; cor azul
  0xFF147EFF > 20 s, mistura até amarelo 0xFFF014FF (20-15 s), amarelo (15-10 s), mistura até vermelho 0xFFFF1450
  (10-5 s), vermelho < 5 s; som "타임" abaixo de 10 s fora da fase de tacada (`clock.cpp:56-100`). Mouse sobre o
  relógio só com limite ≠ 0 (`mousecursor.cpp:866`).
- Ao zerar, **online o cliente só desliga o relógio** (`clock.cpp:102-113`; o `GolfRule msg 0x4000` é só offline) [C].
  O estouro vale só se o servidor mandar **S->C 0x5A u32 guid** (a todos): `gt` 4605 → msg 0x7C → câmera padrão e
  `ChangeGameMode(0x4000)` = DoTimeOut (+1 tacada, gauge −30) → os clientes mandam 0x1C (SPEC-ingame.md) [C].
- GB: timer de `time_vs` iniciado pelo 0x22 (VersusBase.cs:3041-3058); no fim, se a barra de força não está em uso
  e é a vez dele → 0x5C (KR 0x5A) a todos; se está no meio do swing, espera a barra voltar; 3 estouros = expulsão
  (Versus.cs:299-345, VersusBase.cs:1640-1675) [R]. C# hoje: 0x22 ignorado; 0x5A só para o bot "pass".

### 2.3 Modos mass 4, 5, 6 (e 9): relógio de partida
**Início do relógio** = Doc+0x4650 (`m_approachStartTime`) [C]:
- 0x50 (OnPacketCommon, asm 0x73342A-0x733441): se modo ≠ 10 e `IsMassGame()` → `= g_CurrentTime` (ms).
- **S->C 0x8B `u32 decorrido_ms`** (asm 0x73997F-0x739994): `= g_CurrentTime − decorrido`. Funciona em qualquer
  tarefa (campo ou lobby). O C# **não manda** hoje.
- `CGolfRuleApproach/NewApproach::SetPlayer` zeram (golfruleapproach.cpp:116, golfrulenewapproach.cpp:144;
  asm 0x46F8D0/0x471C20) → no modo 9 o HUD fica em "00:00" [C].

`IsMassGame()` sem argumento (@0x40FCE0, usado no campo) = 4, 5, 6, 9, 10; com argumento (@0x4238C0, sala/lobby) =
4, 5, 6, 9, 10, **14** [C asm].

**HUD no campo** (`gui` 8068-8178): embaixo, no centro, o nome do torneio (`FindMatch(Doc+0x4568)`; modo 5 com sufixo
de equipe; modo 6 sem troféu = texto de guilda) e `MM:SS` restante = `gameTimeLimit + início − agora`; branco com ≥ 180 s,
**vermelho** abaixo; acabou → "00:00" vermelho [C]. **Na sala de espera do resultado** ("GAMEROOM_EXTRES") o mesmo
relógio vai para o título (`LM` 2888-2902) [C].

**O cliente não encerra nada sozinho quando o relógio zera** (nenhum teste de `gameTimeLimit` no campo fora do modo 10)
[C]. Tudo depende do servidor:

**S->C 0x8A (sem dados)** — fim do tempo [C]:
- No campo (`gt` 5302-5352): status do mensageiro 4; para cada rival em estado 0 (jogando; inclui a própria linha nos
  modos mass): `finishOrder` (+0x141) = contador decrescente a partir do nº de rivais, estado = 2, e **do buraco
  atual até o último**: `holeStroke = par + 5`, `holeScore = 5`, `totalScore += 5`, `holePang = 0`. `totalStroke`
  **não** muda. A partida local **continua** (não troca de modo).
- No lobby (`lt` 4781-4832): mesma conta e msg 0x2A → `SortRank` (`lmc` 26630) → `MakeRoomUserList`: sem ninguém em
  estado 0 e com Doc+0x455C ligado → Lobby msg **0x2D** → abre o **FrUniteResultDlg**, que lê o 0x77 nesse momento
  (`lmc` 24240-24283; SPEC-resultado-fim-de-jogo.md §3.1).

**Como tirar do campo quem ainda está jogando** [C]:
- **0x77** no campo (`gt` 5158-5225): grava o resultado e, se `golfdoc+0x794 ≠ 0`, manda GolfRule msg 0x8F(1) →
  `ReturnLobby(false)` (`gr` 31039-31085), que liga Doc+0x455C (`gr` 14101) → "GAMEROOM_EXTRES".
  `golfdoc+0x794` é ligado no fim de `UpdateShotResultStat` (`gr` 13097), chamado a cada bola parada
  (`DoFlyBall_Stopped`, `gr` 25724/26080), e zerado pelo 0x6A(próprio guid, 2) (`gt` 4917).
  → quem **já deu pelo menos uma tacada** volta ao lobby só com o 0x77.
- Quem **não tacou nada** (`+0x794 = 0`): mandar a ele **0x6A(guid dele, 2)** (liga golfdoc+0x14C, `gt` 4916) e **0x63**
  → msg 0x7E → modo 0x2000 → `DoScoreBoard` ramo mass com +0x14C → `ReturnLobby` (`gr` 27539-27549) — o mesmo
  caminho do último buraco que o C# já usa.
- GB no fim do tempo: para cada um ainda jogando `finish_tourney(s, 1)` = fecha o buraco, chat "terminou",
  0x6D(KR 0x6B) com u8 0, **0x8C (KR 0x8A) só para ele**; para quem já terminou, só 0x8C; depois `finish()` com
  0xCE/0x79 (KR 0xCC/0x77) (Tourney.cs:282-335, 482-505) [R]. GuildBattle igual, mais os pontos do par
  (GuildBattle.cs:358-478) [R].

**Ordem recomendada no fim do tempo (torneio 4/5/6)** [C mecanismo / P ordem]:
```
servidor (timer = GameTimeMs desde o 0x50):
1. para cada MassPlayer não terminado e não saído (bots inclusive): buraco atual e seguintes = par+5 tacadas, +5 no
   placar cada, pang 0 nesses buracos (igual ao cliente); Finished = true, TimedOut = true.
   GuildMatch: buraco que só um fechou = 2 pontos para ele (SPEC-guildmatch.md §3) -> 0xC0.
2. resultado (Results()/troféus) e, para TODO humano não saído (inclusive os que estavam no campo): 0xCC + 0x77
   (MassOutput.GameOver). No lobby o diálogo ainda não abre (há rivais em estado 0); no campo, quem já tacou volta ao lobby.
3. 0x8A a todos -> lobby: todos em estado 2 -> FrUniteResultDlg (o 0x77 já chegou).
4. para quem estava no campo sem nenhuma tacada: 0x6A(guid dele, 2) e 0x63 só para ele.
5. RoomManager.FinishGame.
```
O 0x77 precisa chegar **antes** do 0x8A para quem está no lobby (senão a barra de EXP fica −1, SPEC-resultado §3.2).

### 2.4 Approach (10): relógio por buraco [C]
- `gameTimeLimit` = tempo da tacada de cada buraco (40 s). Início = `DoPlayerPreview` depois do 0x8E
  (`gr` 29700-29720: `Doc+0x4650 = g_CurrentTime` só no modo 10); zerado a cada buraco (`gr` 30455, 31350).
- HUD: em cima à direita, `"%d.%03d"` segundos restantes; preto ≥ 20 s, laranja 10-20 s, vermelho < 10 s
  (`gui` 3116-3135); som "타임" nos últimos 10 s e "approach_bell" no fim (`clock.cpp:30-53`).
- Ao zerar sem ter tacado (modo fora de 0x800CC0): `SendShotResult` (0x1B com a bola parada), esconde a mira e
  `ChangeGameMode(0x800)` → 0x1C (`gr` 32240-32270). O bloco do 0x12 leva em +0x1D o tempo restante (`gr` 26720-26725).
- O C# já trata (fecha o buraco em limite + 15 s; sem 0x12 = "Out"). **Não mandar 0x8B no modo 10** (mudaria o início).

### 2.5 Resumo por modo
| modo | relógio que o cliente mostra | quando zera, o cliente… | servidor deve |
|---|---|---|---|
| 0, 1, 3, 7 | tacada (ponteiro) | nada (online) | 0x22 → timer `ShotTimeMs` → 0x5A guid a todos se não houve 0x12 |
| 4, 5, 6 | partida `MM:SS` (HUD e GAMEROOM_EXTRES) | nada | 0x8B em cada início de buraco; no fim: 2.3 |
| 9 | "00:00" (início zerado pelo rule) | nada | não oferecer (fora do diálogo) |
| 10 | tacada do buraco `s.mmm` | manda 0x1B/0x1C sozinho | já implementado |
| 14 | sem relógio no campo (`IsMassGame()` = 0) | — | — |

---------------------------------------------------------------------------------------------------------------------
## 3. O que muda no C#

### 3.1 Intrusão
- `RoomPackets.RoomInfo` (src/Pangya.Protocol.KR645/Game/RoomPackets.cs): `bAvailable = Waiting || janela aberta`,
  `bIntrusion = janela aberta` (`TourneyGame.IntrusionOpen`). Ao fechar a janela: 0x45 sub 3 ao lobby e 0x111 {8, n} à sala.
- `RoomManager.CanJoin` continua recusando 0x09 com `Playing` (o GM entra por 0x09 e deve ser recusado).
- Novo `GameHandler.Intrusion.cs` (partial): `CIntrusion = 0x9A` (C->S) e `SIntrusion = 0x111` (S->C); sub 0 → valida →
  {3}/{6}; sub 1 → valida → `RoomManager.Join` + `TourneyGame.AddLate(rp)` → {4,0..4} ao intruso, {7} aos humanos no
  campo, 0x45 sub 3, `Rooms.Lobby.Remove`, `LobbyUser(update)`, `MessengerPlaying(true)`; sub 2 → log e ignora.
  Registrar 0x9A no despacho (não colide com o **S->C** 0x9A `SShotCommand`).
- `GameHandler.MyRoom.cs` (`CQuickEquipRoom`): tratar `kind == 7` (16 bytes) — hoje cai em `QuickEquipAsync(7, …)` e é
  recusado; aplicar personagem/caddie/taco/bola com as mesmas checagens do 0x0B ou só ignorar [P].
- `MassPlayer` (src/Pangya.Domain/Rooms/MassGame.cs): `Late`, `TimedOut`, `HolePang[18]` (delta de `Pang` em
  `FinishHole`) para montar o {10}.
- `TourneyGame`: `StartedAt` (Stopwatch no construtor = envio do 0x50), `ElapsedMs`, `IntrusionOpen`
  (modo 4, sem senha, ElapsedMs < 300 000, sala não cheia), `PlayedUids` (todos que começaram) e `IntrudedUids`;
  `AddLate(RoomPlayer)` cria o `MassPlayer`; `Loaded(p)` do intruso (primeira vez) chama a sincronização.
- `IMassOutput` / `MassOutput`: `ElapsedTime(MassPlayer)` (0x8B), `TimeOver()` (0x8A a todos), `IntruderJoined(...)` ({7}),
  `RivalSync(to, of)` ({10}), `QuitOrder(to, n)` ({9}), `ExpMembers(n)` ({8}).
- Pacotes {4,*}: montar com `RoomPackets.RoomInfo`, `SlotInfo`, `PlayerStructs.SystemTime`, os mesmos 18 × {semente,
  buraco} e gimmicks do `GameInit` (extrair um método comum; sem o byte de mapa do modo 14) e `MatchTid` no u32.

### 3.2 Tempo
- `MassOutput.HoleStart`: depois do 0x51, **0x8B `ElapsedMs`** quando o modo não é 10 (acerta o relógio de todos a
  cada buraco, como o GB; obrigatório para o intruso).
- `TourneyGame`: `Later(GameTimeMs)` → `TimeIsOver()` com a ordem de 2.3. `EndGame` hoje só recompensa `p.Finished`
  (`MassOutput.GameOver` → `BeginGameEnd(..., p.Finished, ...)`): os `TimedOut` precisam entrar como terminados.
  Ranking: hoje `Score` e depois `Total`; somar +5/buraco em `Score` e par+5 em `Strokes`/`Total` [P].
- `RoomSettings.Normalize` (src/Pangya.Domain/Rooms/Room.cs): modos 4/5 com `GameTimeMs == 0` → 30 min (com 0 o HUD
  mostra "00:00" vermelho o jogo inteiro) e `ShotTimeMs = 0` nos modos 4/5/6.
- Stroke/team/match/pang battle (opcional): `GameHandler.Play` passar o 0x22 (`CTurnClock`) para
  `StrokeGame.TurnClockStarted(me)`; timer `ShotTimeMs` + folga; sem 0x12 da vez → `InGameOutput` 0x5A guid a todos
  (o caminho do bot "pass" já existe, InGame.cs:117).

---------------------------------------------------------------------------------------------------------------------
## 4. Plano de implementação

1. **0x8B no início de cada buraco mass (4/5/6)** — pequeno, sem risco, conserta o relógio de quem carregou devagar e
   prepara a intrusão. Teste: relógio do HUD igual em dois clientes.
2. **Fim do tempo do torneio** (`TourneyGame.TimeIsOver`, 0x8A, ordem de 2.3) + padrão de 30 min. Testes de unidade:
   pênaltis +5/par+5, ranking, troféus; teste TCP: um humano no lobby + um no campo + bot → 0x77 antes do 0x8A,
   o do campo volta ao lobby. Teste real: tempo curto (GameTimeMs pequeno via config de teste).
3. **GuildMatch** no fim do tempo: pontos do par + 0xC0 (SPEC-guildmatch §3) antes do resultado.
4. **Intrusão**: janela na lista (bAvailable/bIntrusion), 0x9A sub 0/1, {3}/{6}, {4,0..4}, {7}, sincronização
   {10}/0x6A/{9} no primeiro 0x11, {8} ao fechar a janela. Teste TCP com cliente falso; teste real com 2 clientes.
5. (Opcional) relógio de tacada 0x22 → 0x5A nos modos por vez.

**Riscos**
- R1 [C] Enquanto o diálogo da intrusão está aberto, o cliente descarta todo 0x111: nunca mandar {4}/{7} a quem está
  com {3}/{5}/{6} aberto (só o próprio intruso recebe {3}, e o {4} só vem depois do "예").
- R2 [C] Ordem: {10} do rival antes de qualquer 0x6B dele ao intruso; 0x77/0xCC antes do 0x8A para quem está no lobby;
  0x8A antes de 0x6A(próprio, 2), senão o cliente não aplica o +5 nesse jogador.
- R3 [P] {7} para quem já está em "GAMEROOM_EXTRES": `PlayersUpdate` lê `GOLFDOC()`, que pode ser nulo no lobby
  (crash). O GB manda a todos. Começar mandando só a quem está no campo e testar o lobby separado; sem o {7} o
  intruso não aparece na lista de resultado de quem já terminou (e um 0x46 de slot novo nesse lobby faria `GetIndex`
  de guid desconhecido em `MakeRoomUserList` → não mandar 0x46 durante a partida).
- R4 [C] 0x77 no campo só tira do campo quem já tacou (`golfdoc+0x794`); para o resto, 0x6A(próprio, 2) + 0x63.
  0x63 que chegue depois do `ReturnLobby` cai no lobby (sem tratamento conhecido) [P].
- R5 [C] Modo 14 e GuildMatch não podem receber intrusão (layout do START / pares fixos).
- R6 [C/P] O u32 do 0x50 que o C# preenche com `GameSeed` é o `m_roomType` (tid do troféu) do cliente: o HUD mostra o
  nome errado/vazio e o primeiro {7} dispararia "대회 등급이 … 변경". Trocar por `MatchTid` nos modos mass (verificar
  antes que nada no servidor usa esse valor como semente).
- R7 [P] Formulários "IntrusionAlarm"/"IntrusionDeny" precisam existir no pak do cliente instalado (não verificado).
- R8 [P] Tempo: o relógio de cada cliente começa quando ele recebe o 0x50 (carregamento não conta); o 0x8B por buraco
  corrige a deriva. O timer do servidor deve partir do envio do 0x50.
