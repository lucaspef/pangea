# Treasure Hunter (트레저 헌터) — KR 645

Marcas: **[C]** confirmado no cliente (decompile `/root/ghidra-out/*.c` ou desmontagem `/root/pg645.asm`),
**[R]** só rebang (`/root/rebang/source`), **[GB]** servidor de referência GB (`/root/pangya-server/Server/GB`; ids JP =
KR + 8 nesta faixa: JP 0x131..0x134 = KR 0x129..0x12C), **[I]** inferido.
Abreviações: `tr` = treasure.c, `td` = treasuredlg.c, `sb` = scoreboard.c, `gr` = golfrule.c, `lm` = lobbymain.c,
`rd` = resultdlg.c. `CTHunter` = singleton em `ds:0xAF1A88`; `Doc` = CSharedDoc (0xAC7088).

Handlers (tabela de saltos de `CTask::OnPacketCommon`, índice = id − 0x2D, ver SPEC-resultado-fim-de-jogo):
**0x129 → 0x743E9F, 0x12A → 0x743F5B, 0x12B → 0x743FDC, 0x12C → 0x7440AB** [C]. Os dois conteúdos locais
`S4_TREASURE_HUNTER` (0x58) e `S4_TREASURE_HUNTER_AD` (0x67) estão ligados no KR [R `shared/localize_kor.h`].

---------------------------------------------------------------------------------------------------------------------
## 1. Os dois valores que o cliente guarda

`CTHunter` (`Initialize(20)`, projectg.c 8286) [C]:

| campo | off | o que é | escrito por |
|---|---|---|---|
| `m_vecTMap` (20 × CTMapInfo {u8 mapa, u8 TMark, u32 gauge}) | +0x2C | **gauge de cada curso** (global, aparece na escolha de mapa) | 0x129 (`SetTHunterGauge`) |
| `m_vecRepository` (vector<sTreasureHunt 11 B>) | +0x3C | **caixas sorteadas** da partida (tela `trgift`) | 0x12B |
| `m_vecGift` (vector<sTreasureGift 21 B>) | +0x4C | itens **entregues** (vão para o inventário do cliente) | 0x12C |
| `m_treasurePoint` u32 | +0x58 | **pontos da partida** (barra in-game e `trscore`), `SetTreasurePoint` limita a **1000** | 0x12A; zerado no 0x50 |
| `m_bReturnLobby` | +0x5D | libera o `DoGameOver` | 0x64 (msg 0x80), ReturnLobby; a caixa zera e religa |
| `m_bReceiveGiftPacket` | +0x5E | **"há sorteio"**: liga as telas `trscore`/`trgift` | 0x12B → 1; zerado no início do buraco (gr 23133) |
| `m_bUsedTikiReport` | +0x5F | mostra o botão "treasure alarm" no lobby | 0x122 sub 0 (ver 6) |

Não há tabela de Treasure Hunter no IFF: `pangya.iff` não tem `Treasure*.iff`, e `Course.iff` não tem campo de TH (o
cliente só usa o nome `map_xx_yy` para as imagens) [C]. Tudo vem do servidor.

### 1.1 Índices de curso (Course.iff 645, `CMapTypeAdapter::GetMapIndex` tr 2140) [C]
0 Blue Lagoon, 1 Blue Water, 2 Sepia Wind, 3 Wind Hill, 4 Wiz Wiz, 5 West Wiz, 6 Blue Moon, 7 Silvia Cannon,
8 Ice Cannon, 9 White Wiz, 10 Shining Sand, 11 Pink Wind, 12 "NEW MAP", 13 Deep Inferno, 14 Ice Spa, 15 Lost Seaway,
16 Eastern Valley, 17 Chronicle 1 Chaos, 18 Ice Inferno, 19 Wiz City; 0x7F = aleatório.
`GetTHunterGauge` aceita 0..16 e 19 (17, 18 e o resto devolvem **9999** = "sem gauge"; 0x7F idem). A janela de cursos
(`FrTreasureCourse`) não desenha a barra de 12 e 17 (td 1872) [C].

---------------------------------------------------------------------------------------------------------------------
## 2. Gauge de cada curso (escolha de mapa) — **0x129**

### 2.1 Onde aparece [C]
- `FrTreasureCourse` (janela de escolha de mapa, botões por curso + "aleatório", nuvens): criada na criação de sala
  (makeroomdlg.c 2171), na troca de info da sala (changeroominfodlg.c 2061), na sala do lobby (lm 16305) e no modo
  família (familymain.c 2226). `SetMapGauge` (td 2137) lê os 20 gauges; `DecideTMark` (td 8859) marca com TMark=1 os
  cursos de gauge máximo (destaque) [C; uso exato do TMark no desenho: I].
- Mini-barra do mapa na criação de sala (makeroomdlg.c 2100) e no painel da sala (`lm` 12996 / 26423, curso
  Doc+0x49E4).
- **Escala**: todas as barras usam `nsTHunter::CalcGauge` (td 41): `g < 700 → 0; senão g − 700`, numa barra de
  **0..300**. Ou seja, só a faixa **700..1000** é visível; < 700 = vazia, ≥ 1000 = cheia [C].

### 2.2 Layout [C asm 0x743E9F]
Só se `IsLocalContent(0x58)`.
```
u8 sub
sub 0: sTreasureItem               -> SetTHunterGauge(byMapIndex, dwGauge)   (um curso)
sub 1: u8 n, n × sTreasureItem      -> idem para cada um
outro: ignorado
sTreasureItem (5 B, pack 1) = { u8 byMapIndex, u32 dwGauge }
```
`SetTHunterGauge` ignora índice ≥ 20. Não há pedido do cliente: o servidor manda quando quiser [C].

### 2.3 Quando mandar
- **Login** (junto das listas iniciais), sub 1 com os 20 cursos [GB `pacote131(option 1)` no `sendCompleteData`].
- Opcional: de novo ao entrar no lobby/sala ou quando o gauge mudar (sub 0 com um curso) [I]. O cliente só relê o valor
  ao abrir a janela/painel.
- GB manda `MS_NUM_MAPS` entradas e força `point < 1000 → 1000` (todas cheias) [GB]; o valor real fica no banco.

---------------------------------------------------------------------------------------------------------------------
## 3. Pontos da partida (barra in-game) — **0x12A**

### 3.1 Onde aparece [C]
- `FrTreasureGauge` ("tPointdlg", criada pelo CScore em todos os modos menos pang battle 7, sb 20881), barra **0..1000**
  com a seta `th_gage_arrow`. Escondida durante a tacada (Score msg 0x12F(0) → estado 1); ao mostrar o placar do buraco
  (0x12F(1)) vai para o estado 3 e anima até `GetTreasurePoint()` (td 6866).
- `FrTreasureScore` ("trscore", fim de jogo): fundo `th_result_<curso>.tga`, barra 0..1000 com o mesmo valor, ícone
  `th_team_red/blue` no modo team (tipo 1) [C td 4847/6109].

### 3.2 Layout [C asm 0x743F5B]
```
u32 pontos   -> só se IsLocalContent(0x58) e GetGameType() (Doc+0x4668) ∈ {0 stroke, 1 team, 4 torneio, 5 torneio em equipe}
                SetTreasurePoint(min(pontos, 1000)); envia Score msg 0x12F(4) -> FrTreasureGauge::SetTPoint (anima)
```
Nos tipos 2, 3 (match), 6 (guild), 7 (pang battle), 9/10 (approach) e outros o pacote é **ignorado** [C].
O 0x50 (início de jogo) zera os pontos (asm 0x733271) e o reset do buraco também (gr 20271) — o valor é **cumulativo
da partida**: mandar sempre o total, não o delta [C].

### 3.3 Como os pontos são ganhos — servidor [GB] (o cliente não calcula nem manda nada)
Nenhum C->S de Treasure Hunter: o cliente não informa pontos; o servidor calcula a partir das tacadas/par do buraco que
ele já conhece (0x1C/0x1B, SPEC-ingame) [C ausência de envio; GB].
Por jogador, ao terminar cada buraco (`VersusBase.updateTreasureHunterPoint` / `TourneyBase`) [GB]:

| score | HIO (1 tacada) | −3 | −2 | −1 | par | +1 | +2 | +3 | +4 | pior |
|---|---|---|---|---|---|---|---|---|---|---|
| normal (`calcPointNormal`) | 100 | 100 | 50 | 30 | 15 | 10 | 7 | 4 | 1 | 0 |
| SSC (`calcPointSSC`, invertida) | 30 | 1 | 4 | 7 | 10 | 15 | 30 | 50 | 100 | 0 |

\+ bônus fixo por buraco (`stTreasureHunterInfo.getPoint`; no HIO só o "todo score") [GB]:
- **todo score**: card de caddie Avility 8 (티키, 3/5 — SPEC-cards-efeitos 3.2) + card especial Avility 18 (트레저 헌터,
  +5, 120 min) + asa de anjo equipada +30 + mascote com `drop_rate > 100` +15 (GB também +15 por item de drop rate);
- **par**: Avility 9; **birdie**: Avility 10 (스포티 피핀, 8); **eagle**: Avility 14 [GB; Avility 14 no KR: I].
- Cards de caddie só valem se encaixados no personagem equipado [GB].

Stroke/team (VS): o GB **soma os pontos de todos os jogadores num total da sala** e manda o mesmo total a todos
(broadcast antes do pacote de fim de buraco); torneio: total por jogador [GB]. Limite **1000** (o cliente corta) [C].

---------------------------------------------------------------------------------------------------------------------
## 4. Fim de jogo — sorteio (**0x12B**) e entrega (**0x12C**)

### 4.1 0x12B — caixas para a tela `trgift` [C asm 0x743FDC] (JP 0x133)
Sem teste de IsLocalContent.
```
u8 n
n × sTreasureHunt (11 B, pack 1) = { u32 dwUID (UID da conta do dono), u32 dwTid, u16 wCount, u8 byItemType (0) }
-> ReceiveRepository; SetReceiveGiftPacket(1) (CTHunter+0x5E = 1); Doc.SetAppGiftDownloadState(2) (Doc+0x5D08)
```
- `dwUID` é comparado com **Doc+0x4EB (MyUID)**: a caixa do dono aparece marcada (`th_item_mark`) [C td CBoxOpener::OnInit].
  O GB manda o `uid` da conta [GB].
- Grade de 8 / 16 / 24 caixas conforme n (1-8 / 9-16 / 17-24); **n > 24 cai na grade de 8** e corta → **máx. 24** [C].
- `wCount = 0` deixa a caixa fechada (só abre com quantidade ≠ 0) [C ProcCreate].
- Arte da caixa por curso (`DecideTRBox`, td 3912): grupo 1 = cursos 2, 3, 11; 2 = 0, 1, 6, 7; 3 = 10, 13, 15, 4, 5;
  4 = 8, 9, 14; outros = 1 (só visual) [C].
- Modo approach (`SetApproachMode`): `ReAdjustRepository` junta entradas iguais somando a quantidade [C tr 8115].

### 4.2 Quando as telas abrem [C]
- **Stroke/team/match/pang battle (award no campo)**: com CTHunter+0x5E = 1, a award liga Doc[0x4844] 2 s depois de
  abrir (sb 17445); 2 s depois o `CScore` cria `trscore` e `trgift` (sb 21224). `CBoxOpener::OnInit` zera CTHunter+0x5D,
  o que **segura o DoGameOver** (gr 15558, tipos 0/1 online) até a animação terminar (`ChangeSequence(7)` religa); a
  animação só depende de tempo (≈ 1 s + 250 ms/caixa + 1,5 s), nada do servidor → não trava.
  ⇒ mandar o **0x12B antes do 0x64/0x8F** (ordem GB: 0x12B, 0xF8, 0x64 em `requestFinishData`); chegar depois dos ~10 s
  do DoGameOver perde a tela.
- **Torneio/mass (FrUniteResultDlg no lobby)**: se CTHunter+0x5E == 1 e Doc[0x4843] == 0, abre `trscore` + `trgift`
  (rd 2255); senão segue normal. Mandar junto do 0xCC/0x77 (antes do 0x6A que fecha) [C + I ordem].
- **Approach (tipo 0xA)**: no lobby, `Doc+0x5D08 == 2` abre `trgift` em modo approach, dimensionado pelo nº de itens do
  **0x12C** (lm 17681) → o 0x12C tem de ter chegado antes de voltar ao lobby [C].
- O `trscore` mostra o último 0x12A; com 0x12B mas sem 0x12A a barra fica em 0 [C].

### 4.3 0x12C — entrega [C asm 0x7440AB] (JP 0x134)
Só se `IsLocalContent(0x67)`.
```
u8 n
n × sTreasureGift (21 B, pack 1) = { u32 dwUID, u32 dwTid, u32 dwItemGuid, u16 wCount, u8 byItemType,
                                     i32 iHourRemain, u16 reserved }
-> ReceiveAllGift; CTHunter::UpdateItemList (tr 6437): o CLIENTE soma no próprio inventário:
   tid>>26 == 6 (0x18..0x1B, itens):  tid 0x1A000010 (pang) -> Doc pang (+0x53A, u64) += wCount;
                                      senão procura o item de id == dwItemGuid e soma wCount, ou cria
                                      sItemInfo{id = dwItemGuid, tid, qtd = max(wCount,1), flag = (byItemType&7)<<4, +8 = iHourRemain}
   tid>>26 == 0x1C (0x70.., AuxPart/anel): mesma regra na lista de AuxPart (Doc+0x1218)
   tid>>26 == 0x1F (0x7C.., card):          CSharedDoc::AddCard(tid, dwItemGuid, wCount)
   outros grupos: ignorados (só aparecem após relogar)
```
- O GB escreve `{uid, tid, id, i32 qtd, u8 0, u16, u16}` — mesmo tamanho, outra divisão; no KR vale o layout acima [C].
- `dwItemGuid` tem de ser o **id real** do item já criado no banco; para consumível que o jogador já tem, mandar o id
  existente e `wCount` = **quantidade ganha** (o cliente soma). Não mandar também 0x71/0xAA para os mesmos itens
  (somaria duas vezes no caso de item novo). Pang: o cliente soma; o 0xC6 seguinte (absoluto) corrige [C + I].
- GB manda depois do 0x43 (`sendUpdateInfoAndMapStatistics` → `requestSendTreasureHunterItem`) [GB].

---------------------------------------------------------------------------------------------------------------------
## 5. O que acontece hoje (servidor não manda nada) e o mínimo

- Gauges ficam 0 (`Initialize(20)` com 0) → barras vazias na escolha de mapa; pontos 0 → barra in-game vazia;
  CTHunter+0x5E = 0 → nenhuma tela de sorteio; o fim de jogo segue normal. **Nada trava** [C].
- Mínimo cosmético: **0x129 sub 1** no login com 20 × {mapa, 1000} (barras cheias, como o GB).
- Mínimo funcional: + 0x12A por buraco + 0x12B antes do placar + 0x12C na entrega.
- Não mandar 0x12B com n = 0 (abriria a grade vazia); sem itens, simplesmente não mandar [C/I].

---------------------------------------------------------------------------------------------------------------------
## 6. Fora do escopo / baixa prioridade
- **0x122 sub 0** (`S3_TIKI_REPORT`): `SetUsedTikiReport(1)` → botão "treasure alarm" no lobby (taskmain 4337/7171),
  que abre `CTreasureAlarmDlg` listando o repositório (0x12B) [C flag/botão; R conteúdo `giftalarmdlg.cpp`].
- `FrAppTreasureGiftDlg` (approachresultdlg) e o ícone "Treasure Island" do topo do lobby (lm `OnToppage_TreasureIsland`) [R].

---------------------------------------------------------------------------------------------------------------------
## 7. Plano de implementação no servidor C#

1. **Gauge por curso — global do servidor** (é o mesmo para todos: o pacote é por curso, não por jogador) [GB + I]:
   tabela `treasure_course(course u8 PK, gauge int)` com 20 linhas (padrão 1000). Regras configuráveis
   (`Config.TreasureHunter`): `-1` por partida iniciada no curso, `+50` a cada 30 min para todos, teto 1000, piso 0 [GB].
   Mandar 0x129 sub 1 no login (e sub 0 quando mudar, opcional). Pode mandar o valor real (não forçar 1000) para a barra
   ter sentido: lembrar que só 700..1000 é visível — talvez mapear `700 + gauge×300/1000` [I].
2. **Pontos da partida** (`Domain`, puro, testável): `TreasurePoints.ForHole(strokes, par, ssc, bonus)` com as tabelas
   de 3.3; bônus do jogador calculado no início (cards ativos via `CardService`, asa de anjo, mascote). Acumular por sala
   (stroke/team) ou por jogador (torneio); teto 1000. A cada fim de buraco, nos tipos 0/1/4/5, mandar **0x12A u32 total**
   (stroke/team: broadcast; torneio: só ao jogador) antes do pacote de fim de buraco.
3. **Sorteio** ao terminar (só tipos 0/1/4/5 e approach, e só quem terminou): `n = BoxCount(pontos)` (GB: ≤100 → 1-2,
   ≤200 → 2-4, ≤300 → 3-5, ≤400 → 3-7, ≤500 → 4-8, ≤600 → 4-10, ≤700 → 5-11, ≤800 → 5-13, ≤900 → 6-14, ≤999 → 6-18,
   1000 → 12-24) × taxa do curso (`gauge/1000`) × taxa do servidor (`TreasureRate`%) — o GB calcula a taxa mas não a usa
   (bug); aqui aplicar, mínimo 1 se pontos > 0, máximo 24. Cada caixa sorteia por peso de uma lista configurável
   (`treasure_item(typeid, qtd_max, peso, ativo)` ou JSON em config), quantidade `1..qtd_max` (pang: valor fixo).
   Validar `ctx.Data.Exists(tid)` e **só grupos que o cliente sabe somar**: 0x18-0x1B (itens, pang 0x1A000010),
   0x70 (AuxPart), 0x7C (cards). Stroke/team: distribuir as caixas em rodízio entre os jogadores (GB) e mandar a **mesma
   lista** (com o UID de cada dono) a todos.
4. **Pacotes** (em `InGame.cs`/`GameHandler.GameEnd.cs`, sem LINQ): 0x12B (u8 n, n × sTreasureHunt) a cada jogador
   **antes do 0xF8/0x64** (mass: com o 0xCC/0x77); na fase 2 (`FinishAsync`), depois do 0x43, criar os itens do jogador
   (`Shop.GiveAsync`, pang direto) e mandar **0x12C** (u8 n, n × sTreasureGift com o id real e a quantidade ganha). No
   approach mandar 0x12C antes do retorno ao lobby. Quem saiu não recebe nada.
5. Entrega alternativa: se não quiser usar a soma do cliente, mandar 0x12C com n = 0 e entregar por carta
   (`ctx.Mail.SendSystemAsync`) como os presentes de nível — a tela de caixas (0x12B) continua igual [I].
6. Testes: tabela de pontos (HIO, −3..+4, SSC), teto 1000, BoxCount nas faixas, serialização dos 3 structs (11/21/5 B)
   já gerados em `Structs.g.cs`.
