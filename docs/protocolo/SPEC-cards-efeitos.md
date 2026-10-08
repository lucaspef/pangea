# SPEC — efeitos dos cards (KR 645 QA)

Complementa SPEC-myroom.md §7 (abrir/usar/encaixar/remover). Marcação: **[C]** confirmado no cliente (disasm
`/root/pg645.asm`, decompile `/root/ghidra-out`, ou fonte *matching* do rebang — `cardsystem.cpp` está marcado como
casado byte a byte em `rebang/docs/progress.md:96`), **[R]** servidor de referência (GB = `/root/pangya-server/Server/GB`),
**[P]** palpite. `src/` = `/root/rebang/source/client/ProjectG`, `shared/` = `/root/rebang/source/shared`.

## 1. Onde o cliente guarda e calcula

- `CSharedDoc::m_cardAbilityList[4]` (std::list de sSCardAvilityPeriodInfo 0x41) em **Doc+0x1248**, 0xC por lista
  (`shareddoc.h:471`) [C]. Lista 0 = os meus cards; listas 1..3 = cards dos outros jogadores da partida.
- Pilhas (sCards) em Doc+0x123C — só UI.
- `CCardManager` (singleton @0xAC70BC, `src/cardsystem.cpp`) [C]. Layout: m_playerSlot[5] +0x28, m_charInfo[5] (sCharacterInfo
  0x1BC) +0x2D, m_periodStatus[5][5] +0x8DC, m_periodSlot +0x940, m_periodComboGauge +0x954, m_periodPangRate +0x968,
  m_periodPangyaZone +0x97C, m_specialClearBonusProb +0x990, course +0x9A4, m_cardStatus[5][5] +0x9A8, m_powerRangeDown
  +0xA0C, **m_windColor +0xA20**, m_caddieCard[5][13]{effect,value,type} +0xA24, m_playerIndex +0xD34, buff dlg +0xD44,
  SP alarm +0xD48.
- `CalcCardPeriodAndStatus` (chamado em `CGolfRule::ChangeGameMode` @00456280, gr 23191, no lobby/equip dlg etc.) [C]:
  para i = 0..3 percorre `m_cardAbilityList[i]` e despacha por **`info.cardType`** (campo do pacote!):
  0 + partsTid≠0 → SetCharacterCardAvility; 1 + partsTid≠0 → SetCaddieCardAvility; 2 → SetSpecialCardAvility
  (se vencido, `useEndTime <= Doc()->GetServerTime()`, o cliente **apaga** da lista). Os valores vêm sempre do **Card.iff**
  (`pCard->COM/Avility/AvilityValue`), não dos campos Avility do pacote.
- Card de personagem/caddie só conta se `partsTid == charInfo.tidParts[k] && partsUid == charInfo.ItemIdList[k]` para algum
  k<24 do personagem (slot 0: `Doc()->m_charMap[m_myInfo.userEquip.guidChar]`; outros: `m_charInfo[slot]`) [C]. Ou seja,
  **cards são encaixados em PEÇAS** (o 0xC0 é montado em `CCardSlot::OnItemAttachCardDlgResult` @005bb930 após
  `CItemManager::FindPart(partTid)` e `IsPartAttachCard(cardTid, partTid, slot)`, ccardbook.c:1433) e só valem com a peça
  vestida no personagem equipado.
- `SetPlayerIndex(i)` (@golfdoc/club/wind etc.): `m_playerIndex = m_playerSlot[i]` (i = índice do jogador na partida =
  ordem do 0x74); 0xFF → 0 (eu).

## 2. Pacotes que alimentam as listas [C] (jump table OnPacketCommon: id-0x2D → 0x746118 → 0x745E1C)

| id | handler | efeito |
|---|---|---|
| 0x12D | 0x737A86 | limpa pilhas (Doc+0x123C) |
| 0x130 | 0x737AA1 | u32, u16 n, n × sCards 0x3A |
| 0x12E | 0x737C59 | **limpa as 4 listas** (0x1248..0x1278) |
| 0x12F | 0x737C82 | u16 n, n × 0x41 → lista 0. O cliente **reescreve** Avility/AvilityValue (Card.iff +0xC4/+0xC6) e cardType = (tid>>22)&0xF |
| 0x131 | 0x73826C | u16 n, n × 0x41: substitui na lista 0 o nó de mesmo uid (atualização de prazo) — não usado hoje |
| 0x15A | 0x73834F | u16 n, n × 0x41: push_back na lista 0 sem tocar em nada — não usado hoje |
| 0x158 (=0x21D/0x269) | 0x737DAD | resultado de usar/encaixar (SPEC-myroom §7). Especial com Avility 1/4/17 não entra na lista (Avility 1: cliente soma o valor em myInfo.dwExp, Doc+0x535). Demais especiais: com IsLocalContent(0x6F) (ligado no KR) substitui o especial de **mesma Avility** ou faz push_back; liga o alarme do buff (0x422AC0). Encaixe: substitui o nó de mesma peça+slot |

### 0x74 (lista de jogadores da partida) — handler 0x732E31 [C]
Layout por jogador (modo não-massa): **sUserInfo 0xB92, SYSTEMTIME 16, u8 n, n × sSCardAvilityPeriodInfo 0x41**.
- Antes do laço: se !IsLocalContent(0x70) && IsLocalContent(0x51) (KR: 0x70 off, 0x51 on — `shared/localize_kor.h`)
  limpa as listas 1..3 (0x732E62).
- SYSTEMTIME → `Doc::SetServerTime` (0x410C90): é a hora contra a qual o prazo dos especiais é comparado.
- "Sou eu" = `strcmp(userInfo+0x18, Doc+0x3F8)` (mesmo campo de myInfo) → m_playerSlot[i] = 0, m_charInfo[0] = charInfo do
  sUserInfo (+0x96C) e **os n cards lidos são descartados** (vale a lista 0 vinda do 0x12F/0x158).
- Outros: cards vão **crus** (sem reescrever cardType/Avility!) para m_cardAbilityList[k] (k = 1,2,3 na ordem),
  m_charInfo[k] = sCharacterInfo do sUserInfo (+0x96C), m_playerSlot[i] = k.
- Hoje o servidor manda `u8 0` → os outros clientes não aplicam nenhum card de ninguém.

## 3. Efeitos — quem aplica

### 3.1 Cards de personagem (subtipo 0) [C]
- `COM[5]` (power, control, accuracy, spin, curve) somam em `m_cardStatus[slot][j]`, que entra **só na capacidade (teto)**
  do stat: `CItemManager::GetCapacity/GetLevel/...` (`shared/itemmanager.cpp:4196, 4282, 4369`):
  `level = PCL + Attr peças + upgrades; level = min(level, capacidade)` com capacidade += GetCardStatusSlot. Na prática
  o card libera +N upgrades (o limite é imposto pelo cliente, SPEC-myroom §8).
- `Avility == 1` (só 누리 SR 0x7C000003, valor 2): `m_powerRangeDown += AvilityValue` → `GetTotalRange` (CClub::GetRange
  @00476600 / GetPower @00476950) diminui o alcance. Avility 0,2..10 dos cards de personagem são só rótulo.
- Tudo no cliente; nada no servidor além de mandar as listas.

### 3.2 Cards de caddie (subtipo 1) — índice = Avility−1, guarda o maior valor [C]
| Avility | card (KR) | valores | efeito | onde |
|---|---|---|---|---|
| 1 | 봉다리 | 2/5/10 | −% de falha ao usar item (`itemwindow.cpp:459`) | cliente |
| 2 | 피핀 | 1–4 | +alcance (GetCardRangeUp → GetTotalRange) | cliente (física) |
| 3 | 띠땅뿌 R/SR | 1/2 | −força do vento (GetWindPowerDown, `wind.cpp:1684/1804`) **só com m_gameMode==1 (offline/família)** | online: servidor [R]/[P], ver 3.4 |
| 4 | 돌피니 | 1–4 | pang por quique (`CPangInfo::AztecBonusWater` @004907b0, `PreCalc` @00498070: bounceCount × valor) | cliente, já vem no pang informado |
| 5 | 로로 | 2–8 | +alcance só em power shot (GetCardPowerRangeUp) | cliente |
| 6 | 큐마 | 1–4 | +gauge de combo (GetCardComboGaugeUp) | cliente |
| 7 | 카디에 | 1–3 | +zona PangYa × 0,5 (`powergauge.cpp:184` GetTotalPangyaZone) | cliente |
| 8 | 티키 | 3/5 | pontos de Treasure Hunter (todo score) [R GB efeito 8] | servidor (sem sistema hoje) |
| 9 / 10 | 스포티 피핀 | 8 | TH pontos par / birdie [R GB 9/10] | servidor (sem sistema) |
| 11 | 카디에 R | 1 | +zona PangYa × 0,5 quando vento = 1 m | cliente |
| 12 | 띠땅뿌 N | 1 | vento: só 9 m → −1 [R GB 12] | servidor |
| 13 | 띠땅뿌 secreto | 2 | vento: forte (≥6 m) −2, fraco −1 [R GB 13] | servidor |

### 3.3 Cards especiais com prazo (subtipo 2, `SetSpecialCardAvility`) [C] salvo indicação
| Avility | card (KR) | valor | UseTime | efeito | quem |
|---|---|---|---|---|---|
| 1 | 기초/스윙/합숙/중급 훈련 | 10/20/50/25 | 0 | EXP instantâneo | servidor (já faz) |
| 2 | 까망제리 (geleia preta) | 10/20/50 | 120 / 1 | `m_periodPangRate = v` (atribuição: vale **um**) → Doc.m_pangRate (Doc+0x4654) ×= 1 + v×0,01 (`CGolfRule::OnPreLoadInit` @00452f70, gr 20360) | **servidor deve multiplicar** (3.5) |
| 3 | 하양제리 (geleia branca) | 10/20/40 | 120 / 1 | EXP +v% — **o cliente não trata** | servidor [R GB efeito 3 = exp rate] |
| 4 | 팡주머니 | 2000/1e4/5e4 | 0 | pang instantâneo | servidor (já faz) |
| 5–9 | 피핀/카디에/티키/돌피니/큐마 | 1–2 | 120 / 1 | +v em power/control/accuracy/spin/curve (soma no stat **e** no teto; `GetLevel` devolve +periodStatus) | cliente |
| 10 | 캐디의 조력 / 체력보충제 | 20/32 | 120 / 1 | +gauge de combo inicial (`golfrulebase.cpp:178`, `golfruleteam.cpp:150`) | cliente |
| 11 | 빌리의 가방 | 1 | 120 / 1 | +1 slot de item (8 + mascote + card, máx 10; `equipdlg.cpp:122/443`) | cliente (servidor já aceita 10) |
| 12 | 무지개 깃털 / 요정의 귀 | 1/2 | 60/30 | +zona PangYa × 0,5 | cliente |
| 13–16 | 세피아 윈드 / 윈드힐 / 핑크 윈드 / 블루문 | 10 | 120 | bônus de "clear" ×10% só no curso 2/3/11/6 (`CPangInfo::PreCalc`, GetSpecialClearBonusProb) | cliente, já vem no pang |
| 17 | 랜덤 팡주머니 | 3000 | 0 | pang aleatório instantâneo | servidor (já faz) |
| 18 | 트레저 헌터 | 5 | 120 | TH pontos +5 [R GB 18] | servidor (sem sistema) |
| 19 | 돌피니의 우산 | 10 | 60 | chance de chuva +10 [R GB 19] | servidor (sem sistema) |

- Com m_gameMode==5 ou PlayingMode==2 o cliente considera o especial sempre válido.
- **UseTime**: o cliente nunca lê o campo (só usa `useEndTime`); a unidade é do servidor. Valores 120/60/30 parecem
  minutos; `1` nas versões "2"/"진심/영혼" é suspeito (talvez 1 dia) [P].

### 3.4 Vento (0x59) [C]+[R]
`0x59` = u8 vento, **u8 cardFlag** → `CCardManager::m_windColor` (golftask.c:4587, case 0x59 de `CGolfTask::OnPacket`),
u8, u8, u8. `CWindInfo::Display` @004926e0 pinta o texto do vento de `0xFF59D3FF` quando cardFlag==1. O cliente **não**
reduz o vento online (GetWindPowerDown só com m_gameMode==1). GB (VersusBase.cs:260): no início de cada vez,
`wind += delta(jogador da vez)`, `flag = delta<0 ? 1 : 0`, broadcast. Regra GB: normal(12) só 9 m −1; rare(3) qualquer
vento >1 m −1; super rare (GB 17 ≈ KR Avility 3 valor 2) ≥6 m −2; secreto(13) ≥6 m −2, senão −1.

### 3.5 Pang/EXP do fim de jogo [C]
- O pang de cada tacada que o cliente informa (player+0x4E8 total, +0x4F8 bônus) **já inclui** os efeitos de cliente
  (quique, clear bonus, zona etc.) — `CPangInfo::PreCalc` soma em `this+0x30`.
- O multiplicador `Doc.m_pangRate` (+0x4654, float, recalculado em OnPreLoadInit: 1,0; itens 0x1A000001/2 ×2 (×2,5 PC-bang),
  só PC-bang ×1,2; **card Avility 2: ×(1+v/100)**; 0x1A000005 ×4; 0x1A000009/0C ×1,2; mascote (>100%); evento do mapa)
  **não** entra no pang informado: só na exibição — `CBonusInfo::Process` @0049b9f0 e `FrResultDlg::OnBonusInit` @00820210:
  `mostrado = ROUND(Doc+0x4658 × (pang + bônus) × m_pangRate × 0,01)`; linha "bônus" = mostrado − pang. Doc+0x4658 é int,
  padrão 100 (shareddoc.c:43048), só alterado por um pacote com sRoomInfo 0xB2 + u32 (lobbytask case 0xBA / golftask)
  que o servidor não usa [P].
- Logo o servidor (`Rewards.Compute`) credita hoje `pang + bônus` cru: com geleia preta o cliente mostra mais do que é
  creditado. EXP é 100% servidor (o cliente não tem taxa de EXP de card).

## 4. O que o servidor deve mudar (prioridade)

1. **0x74 com os cards de cada jogador** (`RoomPackets.GamePlayers`): trocar o `U8(0)` por `u8 n + n × 0x41` com os
   mesmos itens do 0x12F do jogador (encaixados + especiais não vencidos), preenchendo **cardType = (tid>>22)&0xF**
   (obrigatório: o cliente não recalcula aqui), Avility/AvilityValue do Card.iff, partsTid/partsUid, slot, start/end em
   hora local (mesmo relógio do SYSTEMTIME do pacote), valid = 1. Extrair um `PlayerStructs.ActiveCards(Player)` usado
   por SendCards e GamePlayers. O sUserInfo já leva charInfo com tidParts/ItemIdList — é contra ele que o card casa.
2. **Multiplicador de pang no fim de jogo** (`Rewards`): `pang = ROUND((relatado + bônus) × (1 + v/100))` com v = Avility 2
   do especial ativo (um só; o cliente usa `=`). Aplicar o teto `MaxPangPerHole` antes de multiplicar. Idealmente o mesmo
   ponto passa a calcular os outros fatores de m_pangRate (itens 0x1A0000xx, PC-bang, mascote) para bater com a tela.
3. **EXP**: `exp × (1 + v/100)` com v = Avility 3 (geleia branca) ativo.
4. **Cards de vento** (caddie Avility 3, 12, 13 encaixados numa peça vestida): enviar 0x59 a cada vez com vento ajustado
   ao jogador da vez e `cardFlag = 1` quando reduzido (hoje `InGameOutput.Wind` faz broadcast com flag 0 e o mesmo vento para todos).
5. Especiais vencidos: o cliente remove sozinho; o servidor deve ignorá-los em 1–3 e pode apagar as linhas vencidas.
   Opcional: usar 0x131/0x15A para atualizar a lista 0 sem relogar.
6. Baixa: Treasure Hunter (caddie 8–10, especial 18) e chuva (especial 19) ficam inertes até existirem esses sistemas;
   rever a unidade de UseTime (valor 1) quando houver captura real; preencher Avility/AvilityValue também no 0x12F
   (inofensivo — o cliente reescreve).
