# Resultado de fim de jogo (KR 645 QA) — o que o cliente espera

Marcas: **[C]** confirmado no cliente (decompile `/root/ghidra-out/*.c` ou desmontagem `/root/pg645.asm`), **[R]** servidores
de referência (GB `/root/pangya-server/Server/GB`, JP: id KR = id JP − 2 na maior parte desta faixa), **[P]** palpite.
Abreviações: `gt` = golftask.c, `gr` = golfrule.c, `sb` = scoreboard.c, `rd` = resultdlg.c, `lm` = lobbymain.c.
`Doc` = CSharedDoc (singleton em 0xAC7088). Ids de pacote são os do fio (S->C).

`CTask::OnPacketCommon` (@00732640) não decompila no Ghidra (timeout): a tabela de saltos foi lida direto do exe
(índice = id − 0x2D, byte em 0x746118 → dword em 0x745E1C). Ids comuns citados aqui: 0x43 → 0x734735, 0xC6 → 0x73A3C3,
0xCC → 0x73D528, 0xF8 → 0x73D5A4, 0x10D → 0x74342B, 0x12B → 0x743FDC, 0x12C → 0x7440AB [C].

---------------------------------------------------------------------------------------------------------------------
## 1. Duas telas diferentes

| Modos (Doc+0x4668) | Tela | Onde | Dados |
|---|---|---|---|
| 0 stroke, 1 team, 3 match, 7 pang battle (não "mass") | **FrAwardForm** ("award", sb @00527480) sobre o placar, ainda no campo | criada 3 s depois de `ChangeGameMode(0x20000)` (Score msg 0x12E(1), gr 22914; `CScore::OnProcess` sb 21192-21219) | registros do **0x64/0x8F**, lista de itens **0xF8**, nível/EXP do slot da sala |
| 4 torneio, 5 torneio em equipe, 6 guild, 9/10 approach (mass) | **FrUniteResultDlg** ("uniteresult", rd @0081F3F0) | no **lobby**, quando todos acabaram (`CLobbyMain` msg 0x2D, lm 24279/24339 → lm 26688/26723) | **0x77** (EXP, troféu, medalhas), rivais (0x6B/0x6A), itens **0xCC** |

`FrResultDlg`/`FrResultTeamDlg` (resultdlg.c) existem no binário mas **nunca são criados** (nenhum `CreateForm<FrResultDlg>`) [C].

---------------------------------------------------------------------------------------------------------------------
## 2. Stroke / team / match / pang battle (não-mass)

### 2.1 Sequência [C] (+ ordem do GB [R])
```
último 0x1C de todos -> servidor:
  (durante o jogo, opcionais) 0xA9 tabela de prêmios por placar, 0xCA prêmio do buraco
  0xF8  itens ganhos na partida (todos os jogadores)            [antes do 0x64: cAquireItem lê no momento em que a award abre]
  0x64  (0x8F no team) registros de 33 bytes  -> GolfRule msg 0x80 (gr 30480): copia rank/placar/tacadas/pang/bônus,
        golfdoc+0x14C = 1, Doc+0x455C = 1, CTHunter[0x5D] = 1, ChangeGameMode(0x2000) -> DoScoreBoard -> 0x20000
cliente em 0x20000 (gr 22908-23034): Score msg 0x12E(1) (abre a award em 3 s), BGM do placar,
  e (online, não-mass, 1x por partida via Doc[0x90]) **C->S 0x06** = sPangYaUserStatistics 0xEB da partida (gr 23016)
  -> servidor (GB: requestFinishGame): grava, credita EXP/pang, responde
  0x43  stats 0xEB + troféus 0x4E + mapStat (já existe)
  0xC6  u64 pang, u64 0 (já existe)
  0x10D sLevelUpDone se subiu de nível (opcional, ver 2.6)
cliente DoGameOver (gr @0044D500): aos 10 s, GolfRule msg 0x8F(1) -> ReturnLobby -> "GAMEROOM" (nenhum pacote necessário)
```
**Não mandar 0x77 neste grupo** [C]: gt 5158-5227 — depois de ler o 0x77, se `golfdoc+0x794` (`StatUpdated`, ligado a
cada tacada por `UpdateShotResultStat` gr 13097) ≠ 0 o cliente manda GolfRule msg 0x8F(1) = **volta imediata para a
sala**, cortando a tela de resultado. O GB também não manda 0x79 no VS [R].

### 2.2 0x64 / 0x8F — registro de 33 bytes [C]
Handler gt 4818-4878 (0x64, 0x8F e 0xB1 caem no mesmo código, gt 5741): `u8 n`; **tipo 7: u32 vencedor do último
buraco (golfdoc+0x18C), u32 vencedor geral (golfdoc+0x190)**; n × 33 bytes copiados para `golfdoc+0x328 + idx×0x21`
(idx = `GetIndex(guid)`; registro de quem não está na partida é ignorado); 0x8F: + u8 vitórias lado 0, u8 lado 1,
u8 vencedor (TEAM().score, golfdoc+0xC0). Consumo (gr 30480-30551) e award (sb 10431-10782):

| off | tipo | destino | tela |
|---|---|---|---|
| +0x00 | u32 guid | — | — |
| +0x04 | u8 posição | player+0x4E0 **nibble alto** | coluna da tabela de EXP offline; a posição da linha é o nibble baixo (calculado pelo cliente) |
| +0x05 | i8 placar | player+0x4E4 | "placar" (vs par; match = buracos ganhos) |
| +0x06 | u8 tacadas | player+0x4E1 | total |
| **+0x07** | **i16 EXP ganho** | lido direto de `golfdoc+0x32F+idx×0x21` (sb 10550, 16888) | **barra de EXP animada, "player_exp_up", "level_up" e troca do ícone de nível** (match tipo 3: **pontos de ladder** ganhos, somados a Doc[0x147C+i×0xB92], sb 10747) |
| +0x09 | i64 pang (só u32 baixo usado) | player+0x4E8 (WCrypticValue) | "pang" |
| +0x11 | i64 bônus (u32 baixo) | player+0x4F8 | "bônus" (azul) |
| +0x19 | i64 | tipo 7: player+0xB78/+0xB7C (pang ganho/perdido) | coluna do pang battle |

O servidor C# hoje grava **U16(0)** em +0x07 → a award mostra a barra parada, sem som nem subida de nível [C: sb 10558
`if exp*10 == 0 → done`]. Nada trava.

### 2.3 Award (FrAwardForm) — o que aparece e de onde vem [C]
- Por jogador (até 4 linhas, `golfdoc+0xC5`): nick (Doc+0x12E8+i×0xB92), placar, tacadas, pang (+0x4E8), bônus (+0x4F8),
  destaque da minha linha. Ícones à direita (sb 16514-16605): **PC-bang** `pc_icon_bonus_20` (bit7 de um byte do slot
  da sala), **bonus_ma / bonus_ma2** (flags do sSlotInfo da sala), **mascote de bônus** (`bonus_mascot_01/02/09-12`,
  tids 0x40000001/2/9/A/B/C, tid do mascote no user info da sala); nenhum → `icon_zero_vs`. Tudo vem dos pacotes da
  sala, não do fim de jogo.
- EXP (sb 10546-10594, OnProc 17223-17450): base = **nível Doc[0x1429+i×0xB92] e EXP Doc[0x1425+i×0xB92] do slot
  da sala** (o que veio em 0x48/0x74), soma o i16 do registro em passos de 0,1, usa a tabela de EXP por nível
  `Doc+0x4DC0 + nível×8` (CSharedDoc::LoadLevelTable); ao passar do limite toca "level_up", troca `level_%03d`,
  manda Player msg 0x1F8 (efeito) e **atualiza sozinho o nível do slot da sala e da CHeadIcon** (sb 17372-17426).
  Texto "próximo nível / prêmio" (FrEdit) quando EXP do slot + ganho ≥ need[nível] (sb 16886-16916).
- Offline (`IsConnected == 0`) o cliente calcula o EXP sozinho com uma tabela (DAT_009F1F40.., por estrelas do curso,
  posição, nº de birdies/eagles/…); online usa só o i16 do servidor [C sb 10473-10552].
- Itens ganhos (`cAquireItem`, sb 19015, desenhado por `cAquireItem::OnProc`/`DisplayPrizeList`): para cada jogador,
  conta os tids de `Doc+0x44C4 + i×0x10` (vector<u32>) e desenha `itemframe_item_one/two/three` + balão com a
  quantidade sobre o personagem. Esses vetores só são escritos por **0xF8** (todos) e **0xCC** (slot 0) [C].
- Treasure Hunter: se `CTHunter[0x5E]` (modo ligado) a award liga Doc[0x4844] em 2 s e o CScore abre `trscore`
  e `trgift` (sb 21224-21246) — ver 4.4.

### 2.4 0xF8 — itens ganhos na partida (todos) [C asm 0x73D5A4] / JP 0xFA [R]
```
u16 n
n × { u32 guid, u8 dobro, u16 k, k × u32 tid }
```
Limpa os 4 vetores (Doc+0x44C4.., stride 0x10); acha o slot pelo id do jogador na lista da sala
(`Doc+0x132B + i×0xB92`, até 4); empurra os k tids. Se `guid == meu` e `dobro ≠ 0`: `Doc.m_pangRate (Doc+0x4654) ×= 2`
(só exibição). GB: `{oid, 0, u16 n, n × typeid}` de `pgi.drop_list` (drops dos buracos), mandado antes do placar.
Sem 0xF8: a award não mostra itens (vetores vazios). Mandar **o mesmo guid do 0x64** [P: GB usa oid].

### 2.5 Durante o jogo (aparecem no fim do buraco, entram na lista do 0xF8) [C]
- **0xA9** (gt 6247): `u8 n, n × {u32 guid, sWinningPrize 24 bytes = 6 × u32 tid}` → player+0x55D..+0x571 =
  prêmio por placar do buraco: [0] hole-in-one, [1] albatross (−3), [2] eagle (−2), [3] birdie (−1), [4] par, [5] bogey (+1)
  (`CGolfRule::AddPrize` @0044BAD0). Ao terminar o buraco o cliente toca `prize.spr` + fala do caddie e zera a tabela.
  Só visual; o item tem de ser creditado pelo servidor.
- **0xCA** (gt 5471, `CSharedDoc::SetExtPrize` @00414130): `u32 guid, u8 n` e, **se n > 0**, sempre **0x600 bytes** =
  128 × sPrizeInfo 12 B {u32 tid, u8[2], u16 qtd, i32 tipo} → player+0x576 (contador +0x575). No fim do buraco
  (`AddExtPrize` @0044EC10): efeito por tid (moedas 0x1A0000AA/AB/AC, pang 0x1A000010 com qtd, 4 deuses 0x1A000143-146,
  quest 0x74xxxxxx via QuestDrop, senão `xmas_prize.spr`/efeito do CodeTinker) e linha vermelha no chat
  "<nome> 획득" para o dono da vez. JP 0xCC (GB `sendEndShot`, 128 × 16 B) [R].

### 2.6 Subida de nível — 0x10D [C asm 0x74342B]
`sLevelUpDone {u8 bDone, u8 bLevel, u8 bItemType}` (3 bytes) → Score msg 0x12D (no campo; também Lobby/MyRoom) →
`FrLevelupItemForm` ("levelupitem"): mostra o ícone `level_%03d` e o presente do nível da tabela fixa do cliente
`s_levelUpGift` (levelupitemdlg.cpp): nível 1: 0x18000008 + 0x18000007; 2: 0x18000005×10; 3: 0x1A000011×20;
4: 0x18000004×10; 5: 0x1A00000F×5; 6: 0x1A000010 (pang) 3000; 7: 0x18000010×5; 8: 0x70000002×18; 9: 0x1A000028×5;
10: 0x1A000002×5; 11: pang 5000; 12: 0x18000011×10; 13: 0x70000003×18; 14: 0x18000025×10; 15: 0x1A000002×5;
16: pang 10000; 17: 0x7CC00003×1; 18: 0x1A00003D×3; 19: 0x18000025×20; 20: 0x1A000002×5; ≥21: 0x1A000033×1.
Se `bItemType == 0 && bDone` o cliente manda **C->S 0x93** (147) ao abrir. O texto diz que o item vai para o
선물함/쿠폰함 → o servidor entrega o item (caixa de presentes) [P]. bLevel = novo nível (índice da tabela) [C].

### 2.7 Depois (já implementado) [C]
- **0x43** (asm 0x734735): `0xEB → Doc+0x4EF` (meu stats; dwExp +0x46 = Doc+0x535, Level +0x4A = Doc+0x539),
  `0x4E → Doc+0x5DA` (**meus troféus**), mapStat normal/clássico; copia stats e troféus para o cache do meu perfil
  (+0x123/+0x20E). Calcula `ganho = EXP_total_novo − EXP_total_antigo` (com a tabela 0x4DBC) e **soma no caddie
  equipado** (Doc+0x628 → lista Doc+0x1164: +0x1C nível, +0x1D EXP, limites Doc+0x4FF8/0x5000/0x5008) — o EXP do
  caddie é do cliente. ⚠ O C# manda **0x4E zeros** → apaga os troféus mostrados (inofensivo enquanto não houver troféus).
- **0xC6**: u64 pang (+ u64 delta se IsLocalContent(0x87); delta 0 = definir; delta ≠ 0 confere e reporta "Pang Hacker").

---------------------------------------------------------------------------------------------------------------------
## 3. Mass (torneio 4/5/6/9/14, approach 10)

### 3.1 Sequência [C] + GB [R]
```
último buraco do jogador: 0x6B, 0x6A(guid,2) a todos, 0x63 a ele -> placar -> ReturnLobby -> "GAMEROOM_EXTRES"
no lobby, cada 0x6A/0x6B atualiza o ranking; quando ninguém está em estado 0 (lm 24255-24280): Lobby msg 0x2D
  -> FrUniteResultDlg::OnInit (rd @0081F3F0) LÊ Doc+0x415E/0x456C/0x456E NESSE MOMENTO
servidor (GB Tourney.finish: requestMakeMedal, requestMakeTrofel, por jogador sendDropItem 0xCE + sendPlacar 0x79):
  0xCC  meus itens (JP 0xCE)      } os dois têm de chegar ANTES do 0x6A que fecha o torneio
  0x77  EXP/troféu/medalhas (JP 0x79) }
cliente no OnInit (se Doc[0x90]==0 e não observador): C->S 0x06 com os stats 0xEB  -> servidor: 0x43, 0xC6 (GB finish_game)
```
⚠ O FrUniteResultDlg usa como base **meu EXP/nível do Doc (0x535/0x539)** e anima `+0x415E` [C rd 4094-4105]. Se o
0x43 (EXP já somado) chegar antes do diálogo, a barra começa no valor novo e soma o ganho de novo. Hoje o C# manda
0x43 no `GameOver` → mandar 0x43/0xC6 **só depois do C->S 0x06** (como o GB) [C base + R ordem].

### 3.2 0x77 — resultado do mass [C gt 5158 / lobbytask 4271]
Lido só se `Doc+0x415E == −1` (zerado por ClearGameVars, shareddoc 43272). No lobby (lobbytask) o tipo vem de
Doc+0x49E0; no campo (gt) de Doc+0x4668; tipo 0xA (approach) no campo lê só u32 e manda C->S 0x2F.
```
u32 EXP ganho               -> Doc+0x415E   (barra; "próximo nível" se need[nível] < EXP+ganho)
u32 tid do troféu da sala   -> Doc+0x4568   (Match.iff, = sRoomInfo.tidMatch @+0x55)
u8  troféu ganho            -> Doc+0x456C   (0 nenhum, 1 ouro, 2 prata, 3 bronze: ícone sMatch.Icon[n])
u8  equipe vencedora        -> Doc+0x456D   (0 vermelho, 1 azul, 2 nenhum = padrão)
[tipo 6 (guild): u32 Doc+0x5018, u32 Doc+0x501C (pontos/placar das guildas, rd 1682/5160), u32 Doc+0x47B4, u32 Doc+0x47B8]
12 × sAwardItem {u32 guid, u32 tid do prêmio} -> Doc+0x456E (0x60 bytes)
```
`m_awardItem` (shareddoc.h:494, awarddlg.cpp): **[0] sorte (luck), [1] mais rápido (speeder), [2] melhor drive (runner),
[3] chip-in, [4] long putt, [5] recuperação**, [6..11] premiados 1º..6º. Vazio = guid **0xFFFFFFFF** (OnPreLoadInit,
gr 20194; a tela testa ≠ 0xFFFFFFFF). Para cada entrada com o meu guid: ícone da medalha na área de prêmios
(`OnAwardAreaOwnerDraw` rd 5702) e o **item** (tid; grupo 2 "FASHION", 5/6 "ITEMS") na área de itens
(`OnItemAreaOwnerDraw` rd 2943), junto com os itens do 0xCC (contados por tid). Sem nada: `icon_zero_vs`.
GB 0x79 manda mais 24 B (medalhas acumuladas do usuário) que o KR não lê [R].
A coluna de medalha ouro/prata/bronze da lista de ranking é **calculada pelo cliente** (`GetMedalIndex` rd @0081E050:
≥10 jogadores, 9/18 buracos, posição entre quem não saiu) — nada a mandar.
- Sem 0x77: EXP fica −1 (a barra recua 1 e para), sem troféu, sem medalhas (só cosmético) [C].

### 3.3 0xCC — meus itens (mass) [C asm 0x73D528] / JP 0xCE [R]
`u8 dobro, u16 n, n × u32 tid` → limpa e preenche o vetor 0 (Doc+0x44C4); `dobro ≠ 0` → m_pangRate ×2.
Desenhado no FrUniteResultDlg com contagem por tid (SetPrizeResult rd @0081F340) e no CBonusInfo do HUD.

### 3.4 Regras do GB para troféu e medalhas [R] (Tourney.cs 663-1010, pangya_game_st.cs 267)
- Troféu da sala: `tid = (MATCH << 26) | ((ROUND5(soma dos níveis) / nJogadores / 5) << 16)`; ao fim
  `TrophyStatistics[(tid>>16)&0xFF][posTroféu−1]++` (linhas 0-5 AMA, 6-12 PRO; sTrophyStatistics = u16[13][3]).
- Quantos troféus (por posição, excluindo quem saiu/quitter): 18 buracos com ≥10 jogadores: 10-14 → 1 bronze;
  15-18 → prata+bronze; 19-22 → ouro, prata, bronze; 23-26 → ouro, prata, 2 bronze; 27-30 → ouro, 2 prata, 3 bronze.
  9 buracos com ≥15: 15-18 → 1 bronze; 19-26 → prata+bronze; 27-30 → ouro, prata, bronze. Cada premiado [6+i] ganha
  um item sorteado `(ITEM<<26) + 0..14` (peso igual).
- Medalhas só com ≥18 jogadores: sorte = sorteio entre os que não saíram; mais rápido, melhor drive, chip-in,
  long putt; recuperação só em 18 buracos; cada uma com item sorteado do mesmo grupo. Itens de medalha/troféu vão para
  o inventário (Tourney.cs 1209-1250).

---------------------------------------------------------------------------------------------------------------------
## 4. Outros pacotes do fim de jogo

### 4.1 EXP — fórmulas
- Cliente online: nenhuma; mostra o que vier [C]. Offline: tabela DAT_009F1F40.. (não usar online).
- GB VS/Match [R] (Versus.cs 371-405): `exp = nJogadores × buracos_jogados × estrelas_do_curso`
  × taxa do item × taxa da sala; × (1 − 0,1 × posição0); 0 se não acertou o 1º buraco; nível ≥ 70 = 0; credita também
  no caddie e mascote (GB) — no KR o cliente já soma no caddie ao receber 0x43 [C].
- C# hoje: `ExpPerHole (2) × buracos` × geleia branca (Rewards.cs). Pode continuar, desde que o valor vá no i16/0x77.
- Taxas de sala (sRoomInfo pangMultiply +0xA5, expMultiply +0xA9) existem no struct [C SPEC-room]; o servidor manda 0.

### 4.2 Pang e bônus por categoria
O cliente não recebe linhas separadas: pang/bônus do registro + ícones (PC-bang, mascote, bonus_ma, itens 0xCC/0xF8).
A soma "com multiplicador" (`Doc+0x4658 × (pang+bônus) × m_pangRate`) só aparece no HUD (CBonusInfo) — SPEC-cards-efeitos §3.5.

### 4.3 Recordes
"Novo recorde" do curso é decidido pelo cliente a partir do mapStat do 0x43 (best score); nada além do 0x43.

### 4.4 Treasure Hunter (sorteio de presentes) [C, baixa]
- **0x12B** (IsLocalContent(0x58)): `u8 n, n × 11 B {u32 guid, u32 tid, u16 qtd, u8}` → `CTHunter::ReceiveRepository` +
  `SetReceiveGiftPacket(1)` (visual do sorteio, `trgift`). JP 0x133 [R].
- **0x12C** (IsLocalContent(0x67)): `u8 n, n × 21 B` → `ReceiveAllGift` (entrega). **0x12A**: u8 sub + pontos/gauge.
- Só faz sentido com o modo Treasure Hunter ligado (CTHunter[0x5E]); hoje não existe.

### 4.5 Outros
- **0x113** bônus ticket (`CBonusTicket::SetTicketFromServer`, str + 100 B, IsLocalContent(0x32)) [C layout, uso P].
- Mensagem "X terminou" no chat (GB 0x40 tipo 16) [R]; tipo KR não verificado [P].

---------------------------------------------------------------------------------------------------------------------
## 5. O que o servidor C# precisa mudar (prioridade)

1. **EXP no registro do 0x64/0x8F (+0x07, i16)** [C] — calcular a recompensa **antes** de montar o 0x64
   (`Rewards.Compute` é puro: chamar em `InGameOutput.GameEnd`/`StrokeGame.BuildEnd` e pôr `Exp` em `GameEnd.Results`);
   gravar depois como hoje. Quem saiu = 0. Match (tipo 3): o campo é ponto de ladder → 0 até existir ladder.
2. **Mandar 0x43 + 0xC6 depois do C->S 0x06** (stroke: chega logo após o 0x64; mass: quando o FrUniteResultDlg abre), com
   timeout de segurança (ex.: 15 s) se o 0x06 não vier [C base do diálogo + R ordem]. Isso também garante que o
   último 0x06 entre nos stats (`lastGameStats`).
3. **0x77 nos modos mass** (antes do 0x6A que fecha o torneio): EXP, tidMatch da sala (0 se não houver), troféu 0,
   equipe 2 (ou vencedora em 5), [tipo 6: 4 × u32], 12 × {0xFFFFFFFF, 0}. **Nunca** em stroke/team/match/pang battle [C].
4. **0x10D ao subir de nível** + entrega do presente de `s_levelUpGift` (caixa de presentes) e tratar C->S 0x93 [C layout, P entrega].
5. **0xF8 (não-mass) e 0xCC (mass) com os itens ganhos** (drops 0xCA, caixas de campo da Wiz City, prêmios) — mesmo que
   vazios `u16 n` com `k = 0`; manter `dobro = 0` [C]. Hoje as caixas de campo já dão item: listá-las aqui.
6. **Troféus** (só torneio): tidMatch na sala (sRoomInfo +0x55) pela regra do GB, troféu por posição (3.4), contar em
   sTrophyStatistics persistido e mandar os troféus reais no 0x43/0x151 em vez de zeros [R regras, C campos].
   **Feito (2026-10-08):** Match.iff 642 tem as faixas 0x2C000000..0x2C0C0000 (아마 6급..프로 7단); os 0x2D/0x2E/0x2F
   são troféus especiais do mesmo grupo e não contam. Faixa = média de nível / 5 (até 12), fixa ao começar. 0x77 leva
   tidMatch, o meu troféu e os premiados em [6..11] com o item (0x18000000 + 0..14, entregue por carta).
7. **Medalhas do torneio** (≥18 jogadores) e itens de troféu/medalha [R]. Baixa (precisa de 18+ jogadores).
8. Prêmios por placar 0xA9 / drops 0xCA por buraco, Treasure Hunter 0x12B/0x12C, taxas de sala — opcionais [C layout].

O que **não** precisa: linhas de bônus por categoria (o cliente desenha ícones do que já tem), medalha ouro/prata/bronze
do ranking (cliente), EXP do caddie (cliente soma no 0x43), novo recorde (cliente, via mapStat).
