# SPEC — Papel Shop (봉다리샵 / "Bongdari") e Raspadinha (스크래치 / "Scratchy") — cliente KR 645 QA

Fontes (somente leitura). GH = `\\wsl.localhost\Ubuntu\root\ghidra-out`. SRC = `\\wsl.localhost\Ubuntu\root\rebang\source`.
`opc.c Lnnnn` = decompile completo de `CTask::OnPacketCommon` @0x732640 (scratchpad da sessão rebang `.../keen-nobel-8c039a/.../scratchpad/opc.c`,
mapa id→case em `summ.txt`; os `case N` do opc.c são índices da jump table). "@0x…" = endereço no `ProjectG_ReleaseQA.exe`
(confirmado por hexdump/jump table quando o decompile era ambíguo). Strings coreanas lidas direto do .exe (cp949).
Legenda: **[C]** confirmado no cliente; **[R]** vem dos servidores de referência (GB/JP C# em `/root/pangya-server/Server/GB`,
S6 em `ref/Pangya-Server-Source-master/S6`); **[P]** palpite/recomendação.

Os ids KR seguem a regra "KR = GB − 2" nesta faixa, e os nomes do enum GB (`Server/GB/GameServer/PangyaEnums/PacketGame.cs`) batem
exatamente com o que o cliente KR faz:

| KR C->S | nome GB | KR S->C | nome GB (id GB) |
|---|---|---|---|
| 0x95 | (abrir papel / point event) | 0x109 | SERVER_BONGDARI_BONUS_TIMES (0x10B) |
| 0x6D | CLIENT_REQUEST_BONGDARISHOP_ITEM | 0xD4 | SERVER_RESPONSE_BONGDARISHOP_ITEM (0xD6) |
| — | — | 0xD3 | SERVER_USE_COUPON (0xD5) |
| — | — | 0xF9 | SERVER_BS_USABLE_TIMES (0xFB) |
| 0x70 | CLIENT_SCRATCH_ITEM | 0xDB | SERVER_SCRATCH_ITEM (0xDD) |
| 0x71 | CLIENT_SCRATCH_SERIAL_NUMBER | 0xDC | SERVER_SCRATCH_SERIAL_NUMBER (0xDE) |
| 0x72 | CLIENT_SCRATCH_CARD_NUMBER | 0xDD | SERVER_SCRATCH_CARD_NUMBER (0xDF) |
| 0xB9 u8 | (posição/UI, já em SPEC-myroom) | — | — |

Flags de conteúdo KR (`SRC/shared/localize_kor.h`, `localize.h`): `S3_BONGDARISHOP`=1, `S3_SCRATCH`=2, `S3_BS_USABLE_TIMES`=6,
`S3_BS_4TH_RARE_ITEM`=7, `S4_MATCHING_SYSTEM`=0x59 — todas **ligadas**.

---

## 0. Estruturas comuns

### sBonusBall (0x14 = 20 bytes) [C]
Usada por 0xD4 (bolas do봉다리) e 0xDB (itens da raspadinha). Layout deduzido de `FrBongdariShopDlg::UpdateListMyItem`
(GH/bongdarishopdlg.c:8335), `ShowBonusBall` (:2308), `SetRandomDataItemSilhouette` (:5359), `SetOpeningBonusBallInfo` (:5330),
`AddListTotalItem` (:7615) e `FrScratchDlg::UpdateListMyItem` (GH/scratchdlg.c:3225).

| off | tipo | campo | uso no cliente |
|---|---|---|---|
| +0x00 | i32 | cor da bola | 0/1/2 → modelo `B_dol01/02/03` (azul/verde/vermelha no GB). <0 = "já aberta" (cliente marca −1 ao abrir). Outro valor = bola sem modelo. Raspadinha ignora. |
| +0x04 | u32 | typeid | ícone/nome via IFF; grupo = typeid>>26 decide a lista do inventário |
| +0x08 | u32 | guid do item (id no DB) | vira o `sItemInfo.id` local; **tem que ser o id real** que o servidor gravou |
| +0x0C | u32 | quantidade | lida como int (texto "%s %d개 획득", número sobre a silhueta) e como u16 ao somar no inventário |
| +0x10 | u8 | classe | 0 = normal ("item de pang"), 1 = "item de cookie", 2 = raro (mostra silhueta genérica `ITEMS/item_rare`) |
| +0x11 | u8 | tipo de tempo | 0 = item comum (qtd = +0x0C). 1/2 = qtd = +0x12; 3 = horas? (+0x12); 4 = dias (+0x12×24 h); 5 = meses (+0x12×720 h). **Usar 0.** |
| +0x12 | u16 | valor de tempo | ver acima |

O GB escreve `u32 color, u32 typeid, u32 id, u32 qntd, u32 tipo` (`Channel.cs:17863-17870`), i.e. +0x11..0x13 = 0 — compatível.

### Como o cliente coloca o prêmio no inventário (importante) [C]
O cliente **adiciona sozinho** cada sBonusBall ao inventário local, já ao receber o pacote (0xD4: em `Clear()`/abertura da bola;
0xDB: imediatamente em `SetScratchItem` → `UpdateListMyItem` para cada item, scratchdlg.c:3635-3641):
- grupos 2 (parts) → `AddMyPartsList`; 5/6/8/9/0xE/0x1C/0x1D → listas do doc. Se já existe item **com o mesmo typeid**, não
  temporizado (`flags & 0x70 == 0`) e +0x11 == 0, **soma** +0x0C à quantidade do existente (não importa o guid); senão cria um novo
  `sItemInfo{id=+8, tid=+4, Common[0]=qtd}`.
- grupo 7 (caddie) → `m_caddieMap[guid]`; grupo 4 (club) → mapa de clubs; grupo 0x12 (mobília, só raspadinha) → `AddFurniture`.
- Se o typeid já existe no mapa de caddies / o guid já existe no mapa de clubs, não adiciona nada.
- **Raspadinha + grupo 9 (SetItem)**: não adiciona nada localmente (scratchdlg.c:3296-3298) — o servidor teria de enviar as peças.
  **Não colocar SetItems no prêmio da raspadinha.**

Consequência para o servidor: gravar o item no DB **antes** de responder e mandar o guid real; **não** reenviar o mesmo item
via 0xA8/0x71 (duplicaria; 0x71 só pula guid repetido, 0xA8 soma). Opcional e seguro: 0xA5 {tid, guid, count} com a contagem
**absoluta** depois (SPEC-myroom: "0xA5 sets the count").

### Consumo de cupom/cartão: S->C 0xD3 [C] (opc.c L8015, case 0x44)
`u32 itemGuid`. Se 0, ignorado. Senão o cliente posta msg 0xAD → `CTaskMain::HandleMsgCommon` (GH/taskmain.c:13602-13694):
procura o guid na lista de itens usáveis (doc+0x11A0); se não temporizado: quantidade −1 e remove se <1; se temporizado e
`GetCouponKind`==3: −1 e remove se ≤0; kind 0 com dias: mantém. Se `GetCouponKind(tid)==2` e a janela `bongdari_shop` está
aberta → `InitCouponBongdariShop` (recontagem) + `SetDataFee(1)` (anima "−1 cupom"); e manda msg 0xE1 ao actor RealMyRoom.

### CItemManager::GetCouponKind @0x746C40 [C] (decompilado agora, read-only)
Só typeids com bit 0x2000000 (grupo 6, faixa 0x1A……):

| kind | low 25 bits | typeids KR (nome no pangya.iff) |
|---|---|---|
| 1 | 0x15-17,0x1D,0x1E,0x2B-2E,0x3A,0x3C,0x4E | cupons de item grátis etc. |
| **2 (봉다리)** | 0x28,0x29,0x2A | **0x1A000028** 봉다리샵이용권(이벤트), **0x1A000029** (증정용), **0x1A00002A** (GM) |
| **3 (raspadinha)** | 0x30,0x33,0x34,0xA3 | **0x1A000030** 스크래치카드(증정용), **0x1A000033** (이벤트), **0x1A000034** (GM), 0x1A0000A3 (não existe no IFF KR; é cupom GB) |
| 4 | 0x36, 0x63-0x75, 0xD2 | 문화상품권쿠폰, … |
| 5 | 0x3D,0x3F,0x53 | 0x1A00003D 스크래치카드보조권, 클로버티켓, … |

O GB usa exatamente os mesmos cartões de raspadinha (`ScratchCardSystem.cs:32`: 436207664=0x1A000030, 0x1A000033, 0x1A000034, 0x1A0000A3).

### Bloqueios globais [C]
- `controlServerService` (u32 do 0x42, SPEC-game): bit **0x10** desliga o 봉다리 (`IsControlServerService(0x10)`, taskmain.c:9692 →
  aviso "봉다리샵 관련 부분 점검중입니다." @0x9E7A24); bit **0x4000** desliga a raspadinha (taskmain.c:9591 → "스크래치 관련 부분
  점검중입니다." @0x9E79F0). Batem com `MAINTENANCE_FLAG_PAPELSHOP=1<<4` / `MAINTENANCE_FLAG_SCRATCHY=1<<14` do S6 [R].
  Nesses casos o cliente nem envia nada.
- doc+0x5064 == 2 (algum item com >20000 unidades, opc.c ~L5074-5083 no 0x71): ambos mostram "현재 한 종류의 아이템 보유 개수가
  20,000개이므로 해당 컨텐츠를 사용할 수 없습니다." e não abrem.

---

## 1. Papel Shop / 봉다리샵

### 1.1 Estado do jogador (doc) [C]
Três `short` no doc, carregados pelo **0x42** (login, opc.c L2619: `D2 SetBsUsableTimes, D2 SetBsUsableBonusTimes,
D2 SetBsRemainedBonusTimes`; também `sUserInfo.iBongdariShop*`, SRC/shared/globalgamedefine.h:161-163):

| doc | getter | significado (cliente) |
|---|---|---|
| +0x455 | GetBsUsableTimes | jogadas do dia. **−1 = ilimitado** (mostra "-"). O painel mostra `max(v−1,0)` (bongdarishopdlg.c:5063-5072) |
| +0x457 | GetBsUsableBonusTimes | jogadas bônus disponíveis. −1 = sem bônus (sem animação) |
| +0x459 | GetBsRemainedBonusTimes | número mostrado no aviso "bônus" (`warning_back_bonus`, provável "faltam N jogos p/ ganhar bônus") [P] |

`ShowDiscountUsableTimes` (bongdarishopdlg.c:4832), chamado **a cada 0xF9**, usa os valores **antigos** da janela:
se `usable<2 && usable!=−1`: se `bonus<1` → abre aviso: `bonus==0 && remained==0` → `warning_back_2` (acabaram as jogadas);
senão `warning_back_bonus` com o número `remained`. Ou seja: **o servidor atual manda 0/0/0 no 0x42** (`GameHandler.cs:115`)
e 0/0/0 no 0x109 (`GameHandler.MyRoom.cs:57`), o que faria o aviso "acabaram as jogadas" aparecer **depois de toda jogada**.
**Recomendado: 0x42 com usable=0xFFFF, bonus=0xFFFF, remained=0; idem no 0x109/0xF9 (−1)** enquanto não houver limite diário.

### 1.2 Abrir [C]
1. Botão `*.utab.bongdarishop` (TOPPAGE, ROOMLIST, GAMEROOM, GAMEROOM_EXT, GAMEROOM_EXTRES — SRC/client/ProjectG/lobbymain.cpp:545…)
   → `CTaskMain::OnUnderBar_BongdariShopUp` (taskmain.c:9662): bloqueios acima; se a janela não existe envia
   **C->S 0x95 (vazio)** e põe doc+0x505C=1 (taskmain.c:9717-9720). (0x95 também é enviado pelo botão de point event com
   doc+0x505C=2, lobbymain.c:11255 — aí o 0x109 vira msg 0x201 ao Lobby.)
2. **S->C 0x109** (opc.c L10427, case 0x6C): `u32 usableBonusTimes, u32 remainedBonusTimes, u32 (ignorado)` → `SetBsUsableBonusTimes`,
   `SetBsRemainedBonusTimes` (truncados p/ short). **Correção da nota antiga**: o 1º campo é *bonus* (doc+0x457), não *usable*;
   *usable* só vem no 0x42 e no 0xF9. Se doc+0x505C==1 → msg **0x200** → `HandleMsgCommon` cria o form `bongdari_shop`
   (taskmain.c:13763-13788); doc+0x505C=0. Sem resposta = a janela nunca abre (sem timeout).
3. Construtor (bongdarishopdlg.c:7388): copia os 3 shorts do doc, conta cupons kind 2 no inventário, sorteia silhuetas
   decorativas, envia **C->S 0xB9 u8 2** (:7572); ao fechar, o destrutor envia **0xB9 u8 1** (:9115). 0xB9 não tem resposta
   (SPEC-myroom). Mensagens internas 0xAF/0xB0/0xB1 a actors Lobby/Shop/RealMyRoom/GolfRule são só locais (sem pacote).

### 1.3 Jogar [C]
Clique no saco (`OnLButtonDown/OnLButtonUp`, bongdarishopdlg.c:4521/6702), só no estado 0 e sem jogada em curso:
- Se `doc+0x539 == 0` (= `sUserStatistics.Level`, rookie): abre `bongdarishop_warning` com `warning_back` e **não envia nada**.
  (GB também exige level≥1 [R], `Channel.cs:17697`.)
- Senão: flag "ocupado"=1 (this+0x282), som, **desabilita o botão Sair**, envia **C->S 0x6D (sem payload)**.
  **O cliente não escolhe pang × cupom nem verifica saldo/limite** — o servidor decide.

**Resposta obrigatória** (senão o botão Sair fica desabilitado para sempre). Ordem recomendada:

1. *(se pagou com cupom)* **S->C 0xD3** `u32 guidDoCupom` (§0). Pode vir antes ou depois do 0xD4 (`SetDataFee` só sobe o índice;
   cupom=1 vence pang=0).
2. **S->C 0xD4** (opc.c L8027, case 0x45):
   ```
   u32 result
   se result == 0:
     u32 n                 // 1..5; fora disso o pacote é ignorado (sem animação; a flag "ocupado" fica presa)
     n × sBonusBall (20 B)
     u64 pang              // valor ABSOLUTO -> doc+0x53A (sUserStatistics.i64Pang)
     u64 cookie            // valor ABSOLUTO -> doc+0x1298, RefreshAllCookie
   ```
   Sempre chama `EnableExitButton(true)` primeiro. Em sucesso: `InitBongdariShop` + `SetBonusBall(n, balls)` +
   `SetBongdariShop` (silhuetas: os prêmios aparecem entre 9 silhuetas, as de classe 2 como `item_rare`) + `SetDataFee(0)`
   (anima "−pang") e msg 0xE1 a RealMyRoom. **Não precisa 0xC6/0x94** — pang e cookie já vêm aqui.
   Erros (confirmados por hexdump @0x73B6D9): **2 → "금액이 부족합니다"** (@0x9FE538, dinheiro insuficiente);
   **3 → "잘못된 아이템 코드입니다"** (@0xA150DC, item inválido); outro ≠0 → silencioso.
   Em erro a flag "ocupado" **não** é limpa: o saco não responde mais até reabrir a janela.
3. **S->C 0xF9** (opc.c L10080, case 99): `u32 usableTimes, u32 usableBonusTimes` → doc+0x455/0x457, depois
   `EnableExitButton(true)`, `ShowDiscountUsableTimes()` (avisos, ver 1.1), `UpdateUsableTimes()`. O GB manda o equivalente (0xFB)
   após toda jogada (`Channel.cs:17839-17852`, −1/−3 quando sem limite) [R]. Recomendado: sempre mandar, com −1 se ilimitado.

Depois da animação o jogador clica em cada bola (estado 4) e o cliente mostra "%s %d개 획득" (@0xA0A628); ao fechar a janela,
bolas não abertas vão para o diálogo `closed_bonusball` — **nada disso gera pacote**; os itens já foram entregues (§0).

### 1.4 Custo [C]+[R]+[P]
- **Cupom**: qualquer item com `GetCouponKind==2` (0x1A000028/29/2A). O painel mostra a soma das quantidades.
  Prioridade cupom > pang, como no GB (`Channel.cs:17708-17794`) [R]. Consome 1 e avisa com 0xD3.
- **Pang**: o preço não está no cliente (há só a imagem `text_fee_pang`). GB lê `price_normal` do DB [R].
  **[P] usar config, padrão 900 pang** (valor conhecido das versões JP/GB; não confirmado para KR).
- Limite diário: opcional. GB: `limitted_per_day`, 50/100/30 por dia [R]. [P] começar ilimitado (−1).

### 1.5 Prêmios e odds [C]+[R]+[P]
O cliente **não** define prêmios nem odds (não há tabela de papel no pangya.iff KR: só Item/Ball/… sem PapelShop*).
Mas o cliente tem listas fixas usadas só para as **silhuetas decorativas** (@0xA97C9C…0xA97DDC, `InitRandomDataItemSilhouette`
bongdarishopdlg.c:266) — são a melhor pista do pool KR original:

*Itens de pang* (`g_arrdwPercentSelectPangItem`, peso%): 0x18000008 신경 안정 보조제 11 · 0x18000007 럭키 팡야 보조제 11 ·
0x18000001 커브 마스터리 11 · 0x18000000 스핀 마스터리 13 · 0x18000004 체력 보조제 12 · 0x18000005 미라클 사인 11 ·
**0x1A000028 봉다리샵이용권(이벤트) 12** · 0x1A00003D 스크래치카드보조권 9 · 0x1A000041 티키리포트스크롤 10.

*Itens de cookie* (`g_arrdwPercentSelectCookieItem`): 0x1800000E 망각화 7 · 0x1800000B 듀얼사 정품 신경안정제 7 ·
0x1800000A 듀얼사 정품 럭키팡야 7 · 0x18000009 파워 캘리퍼스 7 · 0x18000006 사일런트 윈드 6 · 0x1A00004F 리플레이 테잎 7 ·
0x1A000002 팡 마스터리 5 · 0x1A000011 타임부스터 5 · 0x14000005 워터아즈텍 5 · 0x14000003 러브러브아즈텍 5 ·
0x14000002 블루스타아즈텍 5 · 0x14000001 폭탄아즈텍 5 · 0x14000020 레인보우아즈텍 4 · 0x18000010 체력안정제 5 ·
0x18000011 스핀안정제 5 · 0x18000012 럭키팡야안정제 5 · 0x1A000040 오토 캘리퍼스 4 · 0x18000028 세이프티 3 · 0x18000027 체력 강화제 3.

*Quantas silhuetas de cookie aparecem* (dado n prêmios de classe 1 = 0/1/2): {0:70%,1:15%,2:8%,3:5%,4:2%} /
{1:60%,2:25%,3:10%,4:5%} / {2:70%,3:20%,4:10%} — sugere que itens de cookie são minoria.

Algoritmo GB [R] (`PapelSystem.cs:451-522`): nº de bolas aleatório 1..4 (o código pretende 1..5; o cliente aceita 1..5);
cada bola por roleta ponderada sobre a tabela (com multiplicadores de rate para cookie/raro); cor aleatória 0..2;
raro → qtd 1, demais → 1..3; re-sorteia se o item não empilha (ou é CadItem) e o jogador já tem.

**Recomendação [P]** (configurável): n = 1..5 com pesos {1:35,2:30,3:20,4:10,5:5}; cada bola: classe 0 (pool pang acima) 85%,
classe 1 (pool cookie acima) 14%, classe 2 (raro: parts/caddie items escolhidos pelo admin) 1%; qtd 1..3 (raro 1);
cor = classe (0/1/2) para o jogador "ler" a raridade (o GB usa cor aleatória). Restringir a grupos 2/5/6/8 (evitar sets,
chars, caddies). Para itens não empilháveis já possuídos, re-sortear (como o GB).

### 1.6 O que o servidor grava/atualiza
Pang/cookie (débito), cupom (−1), itens (insert ou soma na pilha existente do mesmo typeid — o cliente soma na pilha local),
contadores de jogadas (se houver limite), log de raros [P]. Pacotes: 0xD3 (se cupom), 0xD4, 0xF9. Nada de 0xC6/0x94/0xA8/0x71.

---

## 2. Raspadinha / 스크래치 (Scratchy)

### 2.1 Abrir [C]
Botão `*.utab.scratch` (mesmos layouts) → `CTaskMain::OnUnderBar_ScratchUp` (taskmain.c:9567): bloqueios (§0) e
`IsLocalContent(2)`; cria o form `scratchdlg` **localmente, sem pedir nada ao servidor**. Construtor (scratchdlg.c:600)
envia **0xB9 u8 2**; destrutor (:2873) **0xB9 u8 1**. `OnInit` → `InitScratchCard` (:2812): conta os cartões
(`GetCouponKind==3`, soma das quantidades) e mostra no campo.

### 2.2 Custo
1 cartão kind 3 (0x1A000030/33/34) por raspadinha. Sem pang. O cliente só envia o pedido se a contagem local >0.

### 2.3 Raspar [C]
Mouse down dentro do cartão e arrastar (`OnLButtonDown` :929 → `OnMouseMove` :2381) → `RequestScratchItem` (:1099):
se cartões>0 e flag "pode pedir" (this+0x2A8) → envia **C->S 0x70 (sem payload)** uma vez, zera a flag e desabilita o campo
de serial. O "pó" continua sendo raspado localmente.

Resposta, ordem recomendada:
1. **S->C 0xD3** `u32 guidDoCartão` (§0) — decrementa 1 cartão. Mandar **antes** do 0xDB, porque `SetScratchItem` recalcula a
   contagem (`InitScratchCard`) ao processar o 0xDB. (GB faz 0xD5 → 0x216 → 0xDD, `Channel.cs:18845-18882` [R].)
2. **S->C 0xDB** (opc.c L8137, case 0x49):
   ```
   u32 result
   se result == 0:
     u32 n                // 0 = não ganhou ("다음 기회에..." @0xA0D88C); recomendado 0..2 (buffer do cliente = 11, UI = 2 tampas)
     n × sBonusBall       // +0 (cor) ignorado; +0x10 classe usada só pelo GB
   ```
   Sucesso: `EnableNextPageButton(true)`, `SetScratchItem(n, items)` → adiciona cada item ao inventário local (§0),
   recontagem, ícones (`scratch_text_nothing` se n=0 ou typeid=0; se aparecer um SetItem (grupo 9) o cliente força n=1),
   `InitChangeScratchCardNumber(0)` (anima "−1"); em RealMyRoom posta msg 0xE1.
   Erros (hexdump @0x73D8B4): **0x1B → "스크래치를 이용하시는데 불편을 드려 죄송합니다. 재로그인하시면 정상적으로 이용하실 수
   있습니다."** (@0xA14CC0); outro ≠0 → **"스크래치 이용 오류가 있습니다."** (@0xA14D20). Em erro a flag "pode pedir" fica 0
   até reabrir a janela.
3. Quando ≥3 dos 4 retângulos de cada item foram raspados (`ProcessCheckScratchItemRect` :3013) ou no botão "próxima"
   (:3115), abre `scratchresultdlg` (ícone, nome, stats de parts/caddie/caddie-item) ou "다음 기회에...". Ao fechar
   (`OnConfirmResult` :2561) a flag "pode pedir" volta a 1 → próximo cartão. Nada disso gera pacote.

Sem resposta ao 0x70 o cartão fica "travado" (sem timeout), mas a janela pode ser fechada.

### 2.4 Serial (cartão físico/evento) [C]
Campo de 13 caracteres + botão (`SendScratchSerialNumber` :3152): se o texto não tem exatamente 13 chars, só mostra
"시리얼 번호가 올바르지 않습니다" localmente. Senão envia **C->S 0x71**: `u32 13, 13 bytes ASCII` (sem NUL) e desabilita o campo.

**S->C 0xDC** (opc.c L8198, case 0x4A; jump table @0x7467B4 confirmada):
```
u32 status
  0 -> u32 code, u32 itemGuid        // ver tabela "code"
  1 -> "이미 사용된 시리얼 번호입니다."      (@0xA14C9C, já usado)
  2 -> "존재하지 않는 시리얼 번호 입니다."   (@0xA14C78, não existe)
  3 -> "유효기간이 지난 시리얼 번호입니다."  (@0xA14C54, expirado)
  4 -> "최대 지급 횟수를 초과하였습니다."    (@0xA14C30, limite de entregas)
  >4 -> "올바른 쿠폰 번호가 아닙니다."       (@0xA14C10)
```
`code` (`ResponseScratchSerialNumber` scratchdlg.c:3544, jump table @0x6813B8): **0 = sucesso** → o cliente cria localmente
1× **0x1A000030** 스크래치카드(증정용) com `id=itemGuid` (soma na pilha se já houver), recontagem; 1 = "이미 사용한 시리얼 번호입니다.";
3 = "유효 기간이 지난 카드의 시리얼 번호입니다."; 4 = "이벤트로 받으신 쿠폰은 … 한 계정당 최대 5개까지만 …";
2/outro = "시리얼 번호가 올바르지 않습니다.". O campo é reabilitado em qualquer resposta.
[P] Servidor: tabela de seriais (serial, usado_por, validade); sucesso = inserir/somar 1 cartão 0x1A000030 e mandar o guid.
Sem tabela, responder `status=2`. (O handler GB está comentado/"faz nada" [R].)

### 2.5 0x72 → 0xDD (recarregar cartões comprados na web) [C]+[R]
`CBrowser` ao fechar a página normal envia 0x3D (→0x94 cookie) e, com `S3_SCRATCH`, **C->S 0x72 (vazio)**
(SRC/client/ProjectG/browser.cpp:163). Nome GB: CLIENT_SCRATCH_CARD_NUMBER (o GB ignora). Resposta provável **S->C 0xDD**
(opc.c L8260, case 0x4B; GB SERVER_SCRATCH_CARD_NUMBER): `sItemInfo` (0xA8). Se guid≠0, tid≠0 e Common[0]>0: na lista de itens
usáveis, se já existe item com o mesmo guid **e** tid → **define** a quantidade; senão adiciona. [P] Responder com um 0xDD por
pilha de cartão kind 3 que o jogador tem (contagem absoluta), ou não responder.

### 2.6 Prêmios e odds [R]+[P]
O cliente não tem tabela. GB [R] (`ScratchCardSystem.cs:107-208`): 1 item por cartão (1..2 em evento x2), roleta ponderada por
tipo (normal/cookie/raro) com rates do servidor; raro qtd 1; re-sorteia não empilháveis já possuídos.
**Recomendação [P]**: n=0 (perdeu) 30%, n=1 69%, n=2 1%; cada item: normal 80% / cookie 18% / raro 2%, reaproveitando os pools
do §1.5 (+ parts/caddie items para raro); qtd 1..3 (raro 1). Proibir grupo 9 (SetItem) e grupos que o cliente não adiciona.

### 2.7 O que o servidor grava/atualiza
Cartão −1 (0xD3), itens no DB antes do 0xDB, log de raros [P]. Seriais: marcar usado, inserir cartão (0xDC). Nada de 0xA8/0x71.

---

## 3. Resumo de pacotes (layouts)

| dir | id | payload | quando |
|---|---|---|---|
| C->S | 0x95 | — | botão 봉다리 (doc+0x505C=1) / point event (=2) |
| S->C | 0x109 | u32 bonusTimes, u32 remainedBonus, u32 ignorado | resposta ao 0x95; abre a janela |
| C->S | 0xB9 | u8 2 (abriu) / u8 1 (fechou) | 봉다리 e raspadinha; sem resposta |
| C->S | 0x6D | — | jogar 봉다리 |
| S->C | 0xD3 | u32 itemGuid | cupom/cartão consumido (−1) |
| S->C | 0xD4 | u32 res; [u32 n(1..5), n×sBonusBall, u64 pang, u64 cookie] | resposta ao 0x6D (2=sem dinheiro, 3=item inválido) |
| S->C | 0xF9 | u32 usableTimes, u32 bonusTimes | após cada jogada (−1 = ilimitado) |
| C->S | 0x70 | — | raspar (1×/cartão) |
| S->C | 0xDB | u32 res; [u32 n, n×sBonusBall] | resposta ao 0x70 (0x1B=relogar, outro=erro) |
| C->S | 0x71 | u32 13, char[13] | serial |
| S->C | 0xDC | u32 status; [u32 code, u32 itemGuid] | resposta ao 0x71 |
| C->S | 0x72 | — | browser fechado |
| S->C | 0xDD | sItemInfo 0xA8 | cartão comprado fora (define qtd) [P quanto ao gatilho] |

Também: 0x42 (login) `u16 usable, u16 bonus, u16 remained` → recomendado 0xFFFF, 0xFFFF, 0; bits 0x10/0x4000 de
`controlServerService` desligam as features.

## 4. Pendências / a validar no cliente real
- Preço KR em pang (900 é palpite). Semântica exata de *remainedBonus* (só aparece como número no aviso).
- Se o cliente aceita 0xD3 depois do 0xDB sem ficar com contagem errada (deveria: a próxima recontagem corrige).
- 0x72→0xDD: gatilho inferido pelos nomes GB; nunca observado.
- `S3_BS_4TH_RARE_ITEM`: `OpenBongdarishopRareItemDlg` existe mas não tem chamador no 645 (código morto).
