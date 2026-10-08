# Bot: itens de partida (0x17 / 0x58) no KR 645

Pesquisa somente leitura (2026-10-08). Fontes: asm `/root/pg645.asm` (endereços), decompile `/root/ghidra-out/*.c`
(arquivo:linha), rebang `/root/rebang/source/client/ProjectG` (itemwindow.cpp, powergauge.cpp, spin.cpp,
shared/localize_kor.h), dados `data/pangya.iff` (Item.iff, registro de 200 bytes, dump com script Python descartável).

Legenda: **[C]** confirmado no 645 (asm/decompile), **[R]** só rebang, **[I]** inferido.

Complementa `SPEC-ingame.md` ("In-game items", "Gauge") e `SPEC-bot-especiais.md` (bloco de 46 bytes, power shot,
alcance). `CItemWindow` **não está** no ghidra-out; ProcessItem foi conferido no asm (@004fee60) contra o rebang.

---

## 1. Caminho do 0x58 no receptor

```
S->C 0x58  u32 tid, u32 random, u32 guid
golftask.c:4552  case 0x58: Decode4 tid, Decode4 random, Decode4 guid; AreWePlayingTogether(guid) senão ignora
  -> actor "Item" msg 0x19f (415) param1 = guid, param2 = tid, param3 = random
CItemWindow::NetUseItem(guid, tid, random)          [R itemwindow.cpp:765; C via golftask]
  i = índice do jogador com oid == guid e state != 3
  ProcessItem(i, tid, bConsume = (guid == meu guid), random)
```

`CItemWindow::ProcessItem` @004fee60 **[C asm, = rebang itemwindow.cpp:434]**:

1. `player >= doc+0xc5` (nº de jogadores) → false.
2. Falha: KR tem 0x70 desligado e 0x51 (S4_CARD_SYSTEM) ligado (localize_kor.h), logo
   `m_bFail = (short)(random % 100) < Item.COM[1] − CardManager.GetCardSuccesssProbUp(jogador da vez)`
   (asm 0x4feeed-0x4fef7c). Item ausente do Item.iff → falha e return false.
3. **`player != doc+0x175` (jogador da vez) → return false, sem efeito e sem consumir** (asm 0x4fef8e).
4. Consumo:
   - quem usou (bConsume): exige `pSlot[m_selected] == tid`;
   - **os outros (caso do bot)**: procuram `tid` em `GetTidItemSlot(player)` =
     `Doc.m_userInfo[player].userEquip.tidItemSlot` (o sUserInfo do **0x74**), com `itemNum` = sPlayerData+0x4ac
     (`CollapseItemSlot` no AddPlayer, golfrulebase.cpp:215 **[R]**). **Não achou → return false, sem efeito.**
     Achou → remove a 1ª ocorrência, itemNum−1. O slot é gasto **mesmo se o item falhou**.
5. Se falhou, `tid = 0x1800002d` (efeito nenhum).
6. Ação imediata por `tid & 0x1ffffff` (tabela de salto 0x4ff3f0/0x4ff408) **[C]**:

   | id | ação |
   |---|---|
   | 0x04, 0x10, 0x22 | Player msg **0x11d** → `CClub::SetPowerShot(1)` + animação de carga (golfplayer.c:11136) |
   | 0x27 | se IsLocalContent(0x31) (S3_STRENGTH, ligado no KR): Player msg **0x11e** → `SetPowerShot(3)` + animação "2" (golfplayer.c:11156) |
   | 0x25 | gauge do jogador (ou do time se modo time) `+= 33`, preso em [0, GetComboGaugeLimit] (asm 0x4ff13d-0x4ff21d) |
   | 0x05 | GuideLine msg 0x1a2 (linha-guia no green; visual) |
   | 0x09 | PerformPowerBarSetting (calibrador na barra; visual) |
   | demais | nada imediato |

7. **`sPlayerData+0x4b0` ("item ativo") = tid** (ou 0x1800002d). É esse campo que os outros sistemas consultam (§2).
8. Som; se jogador da vez e tid 0x0e: GolfRule msg 0x9c (PM_NET_CANCEL_SHOT, `Cancel()`), item = 0; se 0x28: Screen 0x202.

**Duração** **[C]**: o campo +0x4b0 é zerado em `DoToDefaultCamera` (golfrule.c:28870, fim de **cada tacada**,
antes do 0x1C), no time-out (ChangeGameMode 0x4000, golfrule.c:22632) e no fim do buraco (RecordPlayerData,
golfrule.c:11383). Ou seja, **todo efeito vale só para a próxima tacada daquele jogador**; o power shot armado também
é zerado no DoToDefaultCamera (SPEC-bot-especiais §2.1). Exceção: o +33 de gauge (0x25) é permanente.

---

## 2. Itens 0x18xxxxxx do KR 645 e o efeito de cada um

COM[0] = quantidade por compra (pacote); COM[1] = chance de falha (%). Só 0x07 e 0x08 têm COM[1] = 33; todos os
outros 0 → **nunca falham** (random % 100 < 0 é impossível). "Usável" = `CItemWindow::IsUsable` @004ff430
(tabelas 0x4ff69c/0x4ff6c4) **[C]** — checado **só no cliente de quem usa**, nunca no receptor.

Grupos de IsUsable: **A** = bola fora do green; **B** = fora do green e taco ≠ putter; **G** = só no green;
**S** = Safety (IsLocalContent 0x2f, ligado; terreno ≠ green e ≠ tipo 0 [I: tipo 0 = fairway]); **—** = nunca.

| tid | nome (Item.iff) | COM | usável | efeito (onde) | muda a física nos receptores? |
|---|---|---|---|---|---|
| 0x18000000 | 스핀 마스터리 Spin Mastery | 0,0 | A | elipse de spin do atirador com raio 30 em vez do stat (spin.cpp:89 [R], asm 0x5303e6 [C]) → \|spin\| ≤ 1,0 | não (o receptor só lê o bloco) |
| 0x18000001 | 커브 마스터리 Curve Mastery | 0,0 | A | idem para curva (asm 0x53046b) → \|curva\| ≤ 1,0 | não |
| 0x18000002 | 럭키 팡야 Lucky Pangya | 0,0 | A | área pangya +2 (2→4) no atirador (CPowerGauge::GetAddLuckyPangYaArea [R]; lista de tids [C] golfrule.c:24405) | só gauge: pangya (fase 4) com item lucky **não ganha gauge** (12/13 normais; 3 com anel de propriedade 11) (golfrule.c:24442-24500); pang bônus de especial 5 em vez de 10 (CPangInfo::PreCalc fontinfo.c:12948) |
| 0x18000003 | 신경 안정제 Nerve Stabilizer | 0,0 | A | velocidade da barra ×0,5 no atirador (DriveGauge golfrule.c:28222) | não |
| 0x18000004 | 체력 보조제 Power Assist | 0,0 | B | **power shot simples armado** (0x11d) **sem custo de gauge** (golfrule.c:24597) | **sim**: PS tipo 1 (+10 jd madeira/ferro, PW/SW 30→60) |
| 0x18000005 | 미라클 사인 Miracle Sign | 0,0 | G | linha-guia de putt (GuideLine 0x1a2) | não (visual) |
| 0x18000006 | 사일런트 윈드 Silent Wind | 10,0 | B | **vento = 1 m** na mesma direção (CWind::GetGlobalWind/GetWind/GetGlobalIntensity, wind.c:1524/1586/2039) | **sim** |
| 0x18000007 | 럭키 팡야 보조제 | 0,**33** | A | = Lucky Pangya; falha 33 % | gauge (como 0x02) |
| 0x18000008 | 신경 안정 보조제 | 0,**33** | A | = Nerve Stabilizer; falha 33 % | não |
| 0x18000009 | 파워 캘리퍼스 Power Calipers | 10,0 | sempre | régua de distância na barra (PerformPowerBarSetting, CScreen::DrawHitBarInfo) | não (visual) |
| 0x1800000a | 듀얼사 정품 럭키팡야 | 10,0 | A | = Lucky Pangya | gauge |
| 0x1800000b | 듀얼사 정품 신경안정제 | 10,0 | A | = Nerve Stabilizer | não |
| 0x1800000c | 파워 안정제 | 0,0 | gauge ≥ 33 e B | marcador de queda (CSpot, só modo 3 e na própria vez) | não |
| 0x1800000d | 물음표 | 0,0 | — | placeholder (loteria/escada) | — |
| 0x1800000e | 망각화 Oblivion Flower | 10,0 | bola OB | cancela a tacada OB (GolfRule 0x9c `Cancel`) | **sim, perigoso** (desfaz a tacada) |
| 0x1800000f | 택이 안정제 | 10,0 | A | barra ×0,5 + voz especial | não |
| 0x18000010 | 체력안정제 | 10,0 | B (id 16) | **PS simples sem custo** (0x11d) **+ barra ×0,5** | **sim**: PS tipo 1 |
| 0x18000011 | 스핀안정제 | 10,0 | A | Spin Mastery + barra ×0,5 | não |
| 0x18000012 | 럭키팡야안정제 | 10,0 | A | Lucky Pangya + barra ×0,5 | gauge |
| 0x18000013 | 고스트스펠 Ghost Spell | 10,0 | — | BounceProcess atravessa colisões (quadtree.c:18566) | **sim** — não usar |
| 0x18000014 / 15 | 토마호크 / 코브라 스펠 | 10,0 | — | nenhum código no 645 | não |
| 0x18000016 | 리버스윈드 Reverse Wind | 10,0 | B | **vento com direção invertida** (vetor × −1; intensidade igual) | **sim** |
| 0x18000017..1e | itens de "공격/방어" | 0,0 | — | sem código (modo não existente) | — |
| 0x1800001f | 포크 Fork | 10,0 | A | barra ×0,5 | não |
| 0x18000020 | 휴지 Tissue | 10,0 | B | = Silent Wind (vento 1 m) | **sim** |
| 0x18000021 | Duostar Shamrock Pangya (USA) | 10,0 | A | só o pang bônus de especial (5) | não |
| 0x18000022 | 딸기케이크 | 0,0 | B | = Power Assist (PS simples sem custo) | **sim** |
| 0x18000023 / 24 | Duo Lucky/Nervine (PH) | 15,0 | — | sem código | — |
| 0x18000025 | 체력 보충제 Gauge Refill | 0,0 | B | **gauge +33** imediato | não na tacada; muda o gauge espelhado |
| 0x18000026 | 택이 안정제2 (CN) | 10,0 | — | sem código | — |
| 0x18000027 | 체력 강화제 Power Enhancer | 80,0 | B | **PS tipo 3 armado** (0x11e) **sem custo de gauge** | **sim**: PS tipo 3 = **+15 jd** (GetPowerShotFactor {0,10,20,15} club.c:1701), não +20 |
| 0x18000028 | 세이프티 Safety | 10,0 | S | lie tratado como material 1 (§2.3) | **sim** |

Nomes em inglês são descritivos (não estão no Item.iff KR).

### 2.1 Power shot por item **[C]**

- O próprio **0x58 arma o PS** em todos os clientes (0x11d/0x11e → `CClub::SetPowerShot`, mesma rotina do 0x56 →
  0x11f). **Não precisa de 0x56.** Um 0x56 depois é redundante (re-arma) e um 0x56 tipo 2 transformaria em duplo
  sem custo — não mandar.
- Gauge: o desconto de 33/66 no fim da tacada é pulado quando o item ativo é 0x04/0x10/0x22/0x27
  (golfrule.c:24597-24603). Tacada com PS não ganha gauge em nenhum caso (o ganho exige PS = 0).
- Física: idêntica ao PS normal do tipo (alcance +10 ou +15, `RandomPower` só conta com PS; SPEC-bot-especiais §4.2).
  Tomahawk/Spike/Cobra continuam possíveis (exigem só PS armado) **[I: o teste é PS ≠ 0]**.

### 2.2 Vento **[C]** (wind.c, CWind::GetGlobalWind @00478c00, GetWind @00479610, GetGlobalIntensity @00478ea0, GetGlobalDirection @00479020)

Sempre do **jogador da vez** (doc+0x175), consultado durante a simulação do voo em cada cliente:
- 0x06 / 0x20: vetor do vento **normalizado (1 m)**, direção mantida; GetGlobalIntensity = 1,0. Em "servidor clássico"
  (S5::CLASSICSRV::IsClassicServer) seria 1 + parte fracionária do vento **[I: KR padrão não é clássico]**.
  Vale para toda a tacada (inclusive a rolagem) e só para ela; o vento do buraco não muda (a tacada seguinte, de
  qualquer jogador, usa o vento normal).
- 0x16: vetor × −1 (direção oposta, mesma força).
- Na física o vento entra em ApplyForce (×0,02, ×0,01 no Spike; SPEC-bot-especiais §2.2). O 0x53 **não carrega vento**:
  cada cliente usa o seu CWind + o item ativo → todos simulam igual.

### 2.3 Safety (0x18000028) **[C]** (IsLocalContent 0x2f ligado no KR)

Com o item ativo do jogador da vez:
- `CQuadTree::GetMaxPowerOnGround` / `GetMinPowerOnGround` (quadtree.c:6471 e 14978) usam a propriedade do material
  **1** em vez do material sob a bola → a força do clique (GetBarClickPowerFactor) fica a de terreno bom (sem perda de
  rough/bunker).
- `CClub::SetAccuracy` (club.c:2034): precisão = stat + ground_accuracy[1] (= 8; tabela @0xa7e89c = 8, 8, 0, 1, 5, 5…)
  em vez do terreno (rough +1) → N/W de fairway nas fases 2/3.
- BallSpeedFactor (golfrule.c:10955): com fase 4 + pangya (doc+0xe4) + PS + sem flags 0x70 + Safety **pula** a correção
  de inclinação (Screen 0x1c1).
- DriveGauge: sem o aumento de velocidade da barra em rough (só atirador). Depois da tacada sPlayerData+0x4ca = 0.
- Visual "safetee_Avatar.seq" (Player msg 0x203).

---

## 3. Exigências no receptor para o 0x58 do BOT

| exigência | fonte | consequência se violar |
|---|---|---|
| guid do bot em sala (AreWePlayingTogether) e state ≠ 3 | golftask.c:4573, NetUseItem | ignorado |
| **ser a vez do bot** (doc+0x175 == índice do bot) quando o 0x58 é processado | ProcessItem asm 0x4fef8e | sem efeito, sem consumo |
| **tid presente no tidItemSlot do bot vindo no 0x74** (e itemNum > 0) | ProcessItem (ramo !bConsume) | sem efeito |
| cada uso remove uma ocorrência; slots são do jogo inteiro (AddPlayer no início do jogo) | idem | o N+1-ésimo uso do mesmo tid falha silenciosamente |
| IsUsable (terreno, taco) | só UseItem do próprio cliente | **não checado** no receptor |
| 1 item por tacada | só a UI do próprio cliente (abre a janela se item == 0, itemwindow.cpp:240 [R]) | um 2.º 0x58 sobrescreve +0x4b0 (o efeito anterior some; PS/gauge já aplicados ficam) |

Ordem: depois que os clientes entraram no turno do bot (0x61/0x51, como o 0x56) e **antes do 0x53**. TCP mantém a
ordem 0x58 → 0x53 na mesma fila do CGolfTask. Não mandar enquanto a bola anterior ainda voa (o DoToDefaultCamera da
tacada anterior ainda não rodou e a vez não mudou).

---

## 4. O bloco 0x53 com item

**Nenhum campo do bloco muda por causa do item** **[C]**: o vento não está no bloco; PS por item vem do 0x58;
Safety/vento são lidos do +0x4b0 do jogador da vez. Ajustes só de "legitimidade":
- Spin/Curve Mastery (0x00/0x01/0x11): permite \|+0x0C\| ou \|+0x08\| até 1,0 (raio 30/30) em vez de stat/30.
- Lucky Pangya: permite fase 4 com impacto até ±4 (área 2+2) em vez de ±2 (o receptor não confere a fase).
- Power Curve (+0x11 flags 4/8) já exige curva ±1 → com Curve Mastery fica sempre alcançável.

---

## 5. Recomendação para o servidor

### 5.1 Itens seguros para o bot (todos os clientes simulam igual)

| prioridade | item | quando usar | modelo do servidor |
|---|---|---|---|
| 1 | **0x18000004** Power Assist (ou 0x10 / 0x22) | tacada longa que não alcança sem PS, gauge < 33 (ou para poupar gauge) | alcance +10 jd (ferros também); não descontar gauge; **não** mandar 0x56 |
| 2 | **0x18000027** Power Enhancer | idem quando precisa de mais | alcance **+15** (tipo 3); sem custo de gauge; sem 0x56 |
| 3 | **0x18000006** Silent Wind (ou 0x20) | vento ≥ 5 m em tacada longa (madeira) | vento = 1 m, mesma direção, só nesta tacada |
| 4 | **0x18000028** Safety | sempre que o lie for ruim e conhecido; como o servidor **não sabe o lie**, usar em tacadas de recuperação após resultado ruim (0x1B fora do fairway) | tratar como fairway: lie = 1 (é exatamente o que o ShotModel já assume) |
| 5 | **0x18000025** Gauge Refill | antes de tacada longa com gauge 33..65 para virar duplo depois, ou para acumular | gauge += 33 (preso no limite 99/132) |
| 6 | **0x18000016** Reverse Wind | só se vento forte de frente (a favor depois) | vetor × −1 |
| cosméticos | 0x00/0x01/0x11, 0x02/0x0a/0x12, 0x03/0x0b/0x0f/0x1f, 0x05, 0x09 | opcional para "parecer humano" | sem efeito na física; lucky zera o ganho de gauge da pangya |
| **nunca** | 0x0e (desfaz tacada), 0x13 (atravessa obstáculos), 0x07/0x08 se não quiser falha, ids sem código | | |

### 5.2 Sequência

```
(clientes já no turno do bot: 0x61/0x51)
S->C 0x58  u32 tid, u32 random, u32 guidBot   -- para todos; random % 100 >= COM[1] (0x07/0x08: >= 33) para não falhar
  ~0,5-1,5 s (animação de carga do PS / som)
[S->C 0x56 u32 guidBot, u8 1|2]               -- SÓ se a tacada usa PS de gauge e o item não é de PS
S->C 0x53  u32 guidBot + bloco (+0x26 = 0) + trailer
```

### 5.3 Regras de implementação

1. **Equipar o bot com itens no 0x74**: `Player.Equip.ItemSlots` (10 slots; `PlayerStructs.cs:101` já copia para
   `tidItemSlot`). Ex.: 3× 0x18000004, 2× 0x18000006, 2× 0x18000028, 1× 0x18000027, 1× 0x18000025 (até 10).
2. Espelhar os slots no `BotGolfer` por jogo: lista de tids, remover a 1.ª ocorrência a cada 0x58 (mesmo com falha).
   Não usar tid que acabou (o cliente ignoraria e o modelo do servidor divergiria).
3. No máximo **um** 0x58 por tacada; nunca 0x58 de PS **e** 0x56 na mesma tacada.
4. Gauge espelhado: PS por item não desconta; 0x25 soma 33 na hora; tacada com PS não ganha gauge; pangya com item
   lucky não ganha gauge (good continua +4).
5. ShotModel: `powerShot` 1 → +10, 3 → +15 jd (tabela {0,10,20,15}); vento 1 m com Silent Wind; −vento com Reverse.
6. Não precisa reenviar nada na troca de vez: os clientes zeram o item no DoToDefaultCamera.
7. Sorteio honesto opcional: `random` aleatório; com COM[1] = 0 nunca falha.

---

## 6. Pontos em aberto

- Enumeração exata de GetGroundType (0 = fairway? 1 = tee?) para a regra de uso do Safety **[I]**.
- Se Tomahawk/Spike/Cobra com PS tipo 3 (item 0x27) usam os mesmos números do tipo 2 (velocidades por madeira) **[I]**.
- Servidor clássico (vento 1 + fração com Silent Wind) não verificado; o KR padrão não é clássico **[I]**.
