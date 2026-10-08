# KR 645: caixas aleatórias, reciclagem (Caixa Mágica da caddie), troca de tickets e outros "use item → ganhe item"

Tags: **[V]** lido no cliente decompilado (pseudo-C do Ghidra e, onde indicado, desmontagem de
`/root/rebang/tools/original/ProjectG_ReleaseQA.exe`, que é o binário original 645 QA); **[I]** inferido (servidores de
referência JP/GB, ou raciocínio sobre o código); **[G]** palpite. Nada disto foi executado contra o cliente real.

Caminhos: `gh/` = /root/ghidra-out, `src/` = /root/rebang/source/client/ProjectG, `shared/` = /root/rebang/source/shared,
`opc.c` = decompilação completa de `CTask::OnPacketCommon` (scratchpad do emulador; os `case N` de lá são índices da
jump table, o mapa índice→id está em `summ.txt`; aqui sempre cito o **id do pacote** e a linha `L`), `RMT` =
`gh/realmyroomtask.c` (`CRealMyRoomTask::OnPacket`, linha 4382; os `case` dele SÃO ids de pacote e esses pacotes só
são entendidos enquanto a task My Room é a corrente). Strings coreanas foram lidas do .exe original pelo endereço.

Cabeçalho dos pacotes C->S: `WSendPacket::InitPacket` grava 5 bytes de cabeçalho + u16 id; `EncodeData(off, …)` escreve
em `buffer[off + 7]`, ou seja, `off` conta a partir do primeiro byte DEPOIS do id (src/packet.cpp:688, 839). [V]

## 0. Resumo dos ids

| fluxo | C->S | S->C | task onde o S->C é tratado | alcançável no KR 645 com os dados 642? |
|---|---|---|---|---|
| Caixa aleatória (Item.iff `RandomBox`) | **0xF1** u32 boxTid | **0x1A2** | My Room (RMT) | não: nenhum item tem o bit (ver §1.2) |
| Caixa especial (gateway, 0x1A00015B + chave 0x1A00015C) | **0xF1** u32 0x1A00015B | **0x1A2** | My Room (RMT) | não: os dois tids não existem no Item.iff 642 |
| Reciclagem / "Caixa Mágica" (CadieMagicBox.iff) | **0x7E** | **0xED** | qualquer (OnPacketCommon) | **sim** (S3_RECYCLE = 5 ligado no KR) |
| Troca de tickets (FrTicketExchangeDlg) | **0xE1** | **0x18C** | Lobby (CLobbyTask) | **não**: conteúdo local 0x7A desligado no KR |
| Pacotes de cartas (já feito) | 0xC2 | 0x14C | qualquer | sim |
| Dinheiro de Ano Novo 0x1A00003B | 0x90 u32 guid | 0x107 | qualquer | item existe (InStock 0) |
| Caixa de presente de evento 0x1A00003E/48 | 0x91 u32 guid | 0x10B | qualquer | item existe |
| "Caixa S4" 0x1A000054 / 0x1A0000BC (약속의 상자) | 0xAA u32 tid | 0x10B | qualquer | item existe |
| Recarga de bolas (supply pack 0x1A000105/14B) | 0xEE u32 packTid, u32 ballTid | 0x19C | qualquer | 0x1A000105 existe; não é aleatório |
| Caixa de Natal 0x1A000052 | nenhum (o resultado do diálogo é vazio) | — | — | — |
| Tiki Magic Box | nenhum (botões vazios no 645) | — | — | — |

Ids livres hoje no servidor (nenhum dos módulos atuais os trata): 0x7E, 0xF1, 0xE1, 0x90, 0x91, 0xAA, 0xEE.

## 1. Dados (pangya.iff KR 642, `emu/data/pangya.iff`)

### 1.1 Tabelas presentes [V]
Cabeçalho: u16 count, u16 bind, u32 version (12), registros de tamanho fixo. O cliente lê com
`IFF_FILE_HEADER {u16 nRecords; u32 Version}` (alinhado = 8 bytes; shared/itemmanager.cpp:39).

| tabela | registros | tamanho | | tabela | registros | tamanho |
|---|---|---|---|---|---|---|
| Item.iff | 260 | **196** (645 espera 200) | | CadieMagicBox.iff | 820 | 104 |
| CadieMagicBoxRandom.iff | 88 | 16 | | TikiRecipe / TikiPointTable / TikiSpecialTable | 10 / 3 / 2 | 52 / 48 / 60 |
| SpecialPrizeItem.iff | 40 | 12 | | TimeLimitItem.iff | 6 | 100 |
| Card.iff | 120 | 328 | | (demais: Character 372, Part 516, Club 196, ClubSet 180, Ball 764, Caddie 200, CaddieItem 236, SetItem 220, Course 312, Match 332, Enchant 16, Desc 516, Skin 196, HairStyle 148, Mascot 252, AuxPart 176, QuestDrop 244, Furniture 464, OfflineShop 200, FurnitureAbility 60, CutinInfomation 208, OpenTournament 80) | | |

**Ausentes** no 642: `RandomBox.sff`, `NonVisibleItemTable.iff`, `SubscriptionItemTable.iff` (o 645 os carregaria se
existissem: shared/itemmanager.cpp:460-470).

### 1.2 Item.iff e o bit RandomBox [V]
`IFF_STRUCT::sItem` (shared/classdefine.h:220) = IFF_ITEM_COMMON (144) + `u32 RandomBox:1` @0x90 + Data[40] @0x94 +
COM[5] @0xBC + Point @0xC6 = 200 bytes. O cliente testa `(byte)sItem[0x90] & 1`
(`CheckRandomBoxTypeID` gh/realmyroommainui.c:17314, `sItem::IsRandomBox` :5120). O 642 tem 196 bytes (sem o campo) e
o `clientfix/projectg_zzfix.pak` (make_iff_fix.py) insere 4 bytes **zerados** → hoje **nenhum** item é caixa
aleatória. Para habilitar o fluxo é preciso gerar o pak de correção com o bit 1 nos tids escolhidos. Candidatos pelo
nome (todos InStock 0) [G]: 0x1A000138 "2010할로윈랜덤박스", 0x1A000147 "위대한상자", 0x1A00012D "유실물 상자",
0x1A0000D6 "행운의 상자".

### 1.3 RandomBox.sff (`IFF_STRUCT::sRandomBox`, classdefine.h:359) [V]/[G]
Layout (alinhamento natural, 228 bytes): bool active @0, 35 bytes desconhecidos, u32 boxTypeId @0x24, u8 kind @0x28,
u32 typeId @0x2C, u32[2] @0x30, u32 checkTypeId @0x38, u32[42] @0x3C. `MakeRandomBoxMap` (itemmanager.cpp:1306)
agrupa por boxTypeId: kind 1 = cabeçalho da caixa, 2 = item possível, 3 = "check item" ligado a um item kind 2 pelo
(typeId, checkTypeId), 4 = "stuff". **Nenhuma função da UI do 645 consulta esse mapa** (sem referências a
`GetEnumRandomBox*`/`FindRandomBox`/`GetRandomBoxInfo` em gh/) → o conteúdo e as chances das caixas são **definidos
pelo servidor**. Onde ficam peso/raridade dentro dos 228 bytes não se sabe [G].

### 1.4 CadieMagicBox.iff (`sCadieMagicBox`, classdefine.h:409, 104 bytes) [V]
| off | campo | uso |
|---|---|---|
| 0x00 | u32 uiNumber | 1..820, único; índice da receita = uiNumber-1 (`g_ArrayRecycleItem[uiNumber-1]`, itemmanager.cpp:1160) |
| 0x04 | u8 bFinal (+3 pad) | todos 1 |
| 0x08 | u32 uiCategory | aba: 0 baixo (20), 1 médio (3), 2 alto (6), 3 especial (790), 4 evento (1). Aba especial exige conteúdo 0x63 (ligado no KR) |
| 0x0C | i32 iCharacter | -2/-1 = todos; senão índice do personagem (filtro da lista) |
| 0x10 | i32 iLevel | nível mínimo (byte de nível do jogador, Doc+0x539); 810 receitas com 0 |
| 0x14 | u32 uiOutput, 0x18 u32 uiOutputCount | item produzido × quantidade por execução (grupos: 785 parts, 21 itens, 6 aux 0x1C, 6 caddie 7, 2 set 9) |
| 0x1C | u32 uiElem[4], 0x2C u32 uiElemCount[4] | materiais × quantidade por execução (grupos: 2429 itens 6, 772 parts 2, 6 caddie 7, 5 cartas 0x1F) |
| 0x3C | u32 uiRandSeq | ≠0 → saída aleatória da CadieMagicBoxRandom com esse seq (só receitas 3,4,5,6 → seq 1,2,4,5) |
| 0x40 | char szRandName[40] | nome exibido no lugar do item quando aleatório |

### 1.5 CadieMagicBoxRandom.iff (`sRandomRecycle`, classdefine.h:372, 16 bytes) [V]
`u32 uiRandSeq, u32 uiTypeId, u32 uiCount, u32 uiProbs`. Em todo seq a soma de uiProbs é **1000** (peso por mil).
Seqs no 642: 1 (3 itens: 카드추출액 700/250/50), 2/4/5 (10 partes, uma por personagem, 100 cada), 3 (9), 6 (24 bolas/
itens, inclui mascotes 0x40000001/2 ×24), 7 (9), 8 (4 clubes "참치": 750/220/20/10), 9 (9). Seqs 3,6,7,8,9 não são usados
por nenhuma receita do 642. `CheckRandomRecycleItem` (gh/recycledlg.c:6966) trata saída 0x1800000D ("물음표") + seq 8:
bloqueia se o jogador já tem um dos clubes 0x10000022/2F/30.

## 2. Caixa aleatória (0xF1 → 0x1A2)

### 2.1 Como o jogador dispara [V]
1. My Room, lista de itens: clicar no mini-botão do item → `CRealMyRoomMain::ExecuteMiniButtonDown`
   (gh/realmyroommainui.c:25169). Ramo `default`: `FindItem(tid)` com bit RandomBox → `OpenNewRandomBox` (:21631),
   que confere o bit de novo (sem ele: popup "열 수 없는 선물 상자입니다.", 0x9FB770).
2. `OnOpenedBoxDlg` (:10068): "notify_yesno" "선택한 아이템을 열어 보시겠습니까?". Guarda **o tid** em this+0x7BC.
3. Sim → `OnOpenNewRandomBoxDlgResult` (:17333) cria `FrRandomBoxProgress` "newRandomBox", texto
   "%선택하신 아이템을 여는 중입니다." com o nome do item.
4. `FrRandomBoxProgress::OnProc` (src/frrandomboxprogress.cpp:80-104): barra de 4 a 7 s (`rand()%4+4`); no fim posta
   MsgObject 1 no ator principal (trava a UI [I]) e envia **0xF1**. Cancelar antes do fim não envia nada.

Custo: 1 unidade da caixa (só o tid é enviado; o servidor escolhe a pilha). Nenhum pang/cookie.

### 2.2 C->S 0xF1 [V]
`u32 boxTid` (4 bytes). Também usado pela caixa especial com `0x1A00015B` (src/specialboxdlg.cpp:63).

### 2.3 S->C 0x1A2 (RMT:7713; disasm 0x5B91AD-0x5B95B0) [V]
`u32 result, u32 boxTid, u32 rewardTid, u32 count` (16 bytes; a ordem bate com o 0x19D do servidor GB, que é a mesma
mensagem em outra versão [I]). Primeiro posta MsgObject 2 (destrava a UI) → **sempre responder**, inclusive em erro.

| result | comportamento do cliente |
|---|---|
| 0 | `FindCommonItem(rewardTid)`; nulo → "잘못된 아이템 타입ID 입니다." e para. Senão popup msg 0x26 com o ícone de rewardTid e texto: grupo 5 (bola) ou grupo 6 com count ≥ 2 → "아이템을 %d 개 받으셨습니다."; 0x1A000010 (팡 주머니) → "%d 팡을 받으셨습니다."; demais → "아이템을 받으셨습니다.". Depois msg 0xDD (só redesenha a vitrine). Como o conteúdo 0x60 (S4_MAILBOX_FOR_REAL_MYROOM) está **ligado** no KR, limpa a lista de correio e envia **C->S 0x15E** (sem payload; senão limparia presentes e enviaria 0x93). Por fim popup "상품이 우편으로 전달되었습니다. 우편함을 확인하세요." |
| 4 | "상자 열기가 실패했습니다.\n수량이 충분하지 않습니다." |
| 8 | "상자 열기가 실패했습니다.\n아이템 종류 에러입니다." |
| 0x200B3B..0x200B3D | posta msg 0x276 (1, result) no ator Gateway → `CSpecialBoxHandler::OnReqWarningMsg` (src/specialbox.cpp) mostra a mensagem de sistema 200/201/202 (0x200B3C = "sem a chave", o próprio cliente usa esse código quando falta 0x1A00015C) |
| outro ≠ 0 | "상자 열기가 실패했습니다.\nError : %d" |

**O cliente NÃO mexe no inventário** com o 0x1A2: não decrementa a caixa nem adiciona o prêmio (a msg 0xDD só chama
`BuildDisplayStand`, gh/realmyroommainui.c:35587). No KR original o prêmio ia para o **correio** (texto do popup; o GB
faz `MailBoxManager.sendMessageWithItem` e responde 0x19D) [I].

### 2.4 O que o servidor deve fazer
1. Validar: o jogador tem uma pilha com tid = boxTid e quantidade ≥ 1 (senão result 4); o item é do grupo 6 e é uma
   caixa conhecida pelo servidor (senão 8). Para 0x1A00015B: exigir e consumir também a chave 0x1A00015C (sem ela,
   result 0x200B3C) [I].
2. Sortear o prêmio no **pool do servidor** (não há tabela no cliente/dados 642). Sugestão (semântica do BoxSystem GB/JP
   [I]): lista {tid, quantidade, peso, raridade}; sorteio ponderado; pular itens não acumuláveis que o jogador já tem
   (exceto se a caixa tiver prêmio de consolação); 0x1A000010 = pang (quantidade = pang); mascote com tempo → quantidade
   = dias.
3. Atualizar o inventário ANTES do 0x1A2 [I]:
   - caixa: **0xA5** `u8 1, {u32 boxTid, u32 guid, u16 novoTotal}` (§6.1); 0 apaga a pilha.
   - prêmio: ou vai para o correio (fiel ao original; precisa de correio implementado — hoje 0xBB..0xBF/0x15E não
     entregam nada), ou entra direto no inventário (**recomendado enquanto não houver correio**): pilha nova → **0x71**
     com um sItemInfo; pilha existente (grupo 5/6) → **0xA5** com o novo total; pang → **0xC6** (§6). O popup
     continuará dizendo "enviado pelo correio" (cosmético).
4. Enviar 0x1A2 `0, boxTid, rewardTid, count`. Ignorar o 0x15E que o cliente manda em seguida (ou responder com a lista
   de correio, se existir).

**Implementado (2026-10-07):** com o correio ligado, o prêmio vai numa carta do sistema ("@Pangya") gravada na mesma
transação que tira o cubo e a chave; só o 0xA5 dos dois sai antes do 0x1A2, e o 0x15E seguinte devolve a carta nova.

## 3. Reciclagem / Caixa Mágica da caddie (0x7E → 0xED)

### 3.1 Como o jogador dispara [V]
Barra inferior "마법상자" → `CTaskMain::OnUnderBar_MagicBoxUp` (gh/taskmain.c:9438) abre `FrRecycleDlg` "recycledlg" se
`IsLocalContent(5)` (ligado no KR). Bloqueios: `IsControlServerService(0x8000)` → "마법상자 관련 부분 점검중입니다.";
`Doc[0x45F] & 0x40` (u32 que o S->C 0x182 grava, gh/lobbytask.c:5947) → "카드정보 이상으로 마법상자를 이용할 수 없습니다."
→ o servidor não deve ligar esses bits. Funciona no lobby e no My Room (o 0xED é do OnPacketCommon).

Na janela: abas por uiCategory, filtro por personagem, lista de receitas (`g_ArrayRecycleItem`). Ao escolher uma:
`SetDataTotalItem` (gh/recycledlg.c:7725) monta 4 slots de material (this+0x328, stride 0x50 = sRecycleItem) com o tid
do material, o **guid** de uma pilha/peça do jogador e a quantidade possuída; `SetMaxEnableRecycle` (:348) = mínimo de
possuído/necessário; `ControlRecycleNumber` (:293) limita "vezes" a **1** se a saída não for grupo 5, 6 ou 0x1C, ou se a
receita for aleatória (uiRandSeq ≠ 0). Peças (grupo 2) como material são escolhidas em `FrSelectPartDlg`.
Exige nível ≥ iLevel. `OnMixBnUp` (:4071): `ConfirmInsertItem(saída, vezes)` falha → "현재 아이템의 보유개수가 20,000개
이므로 아이템을 조합 할 수 없습니다."; senão animação e, 1,5 s depois, `SendDataRecycleItem` (:9263).

Custo: só materiais (uiElemCount[j] × vezes). Sem pang.

### 3.2 C->S 0x7E [V] (gh/recycledlg.c:9294-9310)
```
u16 recipeIndex      // uiNumber - 1 (sRecycleItem.iIndex @+0xE, InitIndexRecycleItem :1)
u8  times            // vezes (≥1)
u8  n                // nº de slots com tid≠0 (gravado depois via EncodeData(3,…))
n × { u32 materialTid, u32 materialGuid }
```
Só um guid por material: para itens acumuláveis é a pilha; o servidor calcula a quantidade a consumir pela receita.

### 3.3 S->C 0xED (opc.c L9674) [V]
`u32 result`; se 0: `u16 recipeIndex, u8 n, n × sRecycleItem (0x50)`.

sRecycleItem (itemmanager.cpp:10, 80 bytes): u32 dwTid @0, u32 dwItemid (guid) @4, u16 iNum @8, u8 byLevel @0xA,
i16 iChar @0xC, i16 iIndex @0xE, u8 byItemType @0x10, u16 wTime @0x12, SYSTEMTIME date @0x14, u32 dwRandSeq @0x24,
char szRandName[40] @0x28.

Cliente (só se "recycledlg" estiver aberto; `SetResultRecycleItem`, gh/recycledlg.c:8586):
- result 0 → saída da receita é **set** (tid & 0xFC000000 == 0x24000000): SetItem.iff tem de existir ("존재하지 않은
  아이템입니다." senão) e a lista precisa ter **exatamente** os elementos do set (count = byte @0x90 do sSetItem e cada
  tid @0x94+4k presente), senão "조건이 올바르지 않습니다.". Saída não-set: **n deve ser 1**, senão o mesmo erro; o item
  da lista (dwTid/iNum) é o que aparece como resultado — é aqui que o servidor mostra o item **sorteado** de uma receita
  aleatória. Depois msg 0xE1 (redesenha), recalcula materiais pelo inventário local.
- result ≠ 0 → zera o resultado e popup: 2 "존재하지 않은 아이템입니다.", 3 "아이템 보유 개수가 20,000개이므로 조합을 할수가
  없습니다.", 4 "이미 소유한 아이템입니다.", outro (1, …) "조건이 올바르지 않습니다.".

**O cliente não remove os materiais nem adiciona a saída** → o servidor envia as atualizações de inventário ANTES do
0xED (o diálogo recalcula as quantidades no fim do `SetResultRecycleItem`) [I].

### 3.4 O que o servidor deve fazer
1. recipeIndex válido (0..819) e receita bFinal; senão 1. Nível do jogador ≥ iLevel; senão 1 [I].
2. times ≥ 1; forçar/validar times = 1 quando a saída não é grupo 5/6/0x1C ou uiRandSeq ≠ 0.
3. Para cada uiElem[j] ≠ 0: o pacote precisa trazer esse tid com um guid do jogador; quantidade possuída ≥
   uiElemCount[j] × times (peça/caddie: o guid indicado, 1 unidade); senão 2 (ou 1).
4. Saída não acumulável já possuída (sem uiRandSeq) → 4. Pilha resultante > 20000 → 3. (Mesma semântica do
   `RequestCadieCauldronExchange` JP [I].)
5. Saída: uiRandSeq = 0 → uiOutput × uiOutputCount × times. uiRandSeq ≠ 0 → sorteio ponderado por uiProbs (soma 1000)
   entre os registros da CadieMagicBoxRandom com esse seq; quantidade = uiCount. Set (grupo 9) → expandir nos
   elementos (como a loja já faz).
6. Inventário: materiais com **0xA5** (novo total; 0 apaga itens/peças/caddie; ver exceções §6.1); saída com **0x71**
   (pilha/peça nova) ou **0xA5** (pilha existente). Card como material (5 receitas) → remoção de carta [G: 0xA5 tem um
   ramo para cartas no fim do handler, não verificado].
7. 0xED `0, recipeIndex, n, sRecycleItem[]` com dwTid, dwItemid (guid entregue), iNum (quantidade), o resto zero [I].

## 4. Troca de tickets (0xE1 → 0x18C) — desligada no KR

### 4.1 Disparo e bloqueio [V]
Botão da página inicial do lobby (`CLobbyMain::OnToppage_TicketExchangeInit/Down`, src/lobbymain.cpp:6049/6084):
visível só se `IsLocalContent(0x7A)` e o container `CTicketExchange` (src/icontentsdoc.cpp:51) tiver flag. **0x7A não
está em `shared/localize_kor.h`** → o botão fica escondido e o 0x18C é ignorado (gh/lobbytask.c:5952). Só funciona
com um cliente patchado. O que segue é para esse caso.

Diálogo `FrTicketExchangeDlg` "ticket_exchage_dlg". Moeda: ticket **0x1A0000BD** "약속 이벤트 응모 티켓" (grupo 6). Itens
fixos no cliente (`OnInit`, src/frticketexchangedlg.cpp:1680):

| id do slot | tipo | tid | custo (quantity) [I] | nome |
|---|---|---|---|---|
| 0x10001 / 0x10002 | 0x10000 "응모" (sorteio/inscrição) | 0x3B9AC9F7 / 0x3B9AC9F8 | 1 / 1 | não são itens do IFF (prêmios externos) [G] |
| 0x100001..4 | 0x100000 troca com estoque | 0x1A00000E, 0x18000006, 0x1A000011, 0x1A000040 | 5 cada | 기쁨의사탕, 사일런트 윈드, 타임부스터, 오토 캘리퍼스 |
| 0x1000001 / 2 | 0x1000000 troca sem estoque | 0x1A0000BC, 0x1A000030 | 3 / 3 | 약속의 상자, 스크래치카드(증정용) |

### 4.2 C->S 0xE1 [V] (src/frticketexchangedlg.cpp:243)
`u32 type`; se type ∈ {0x10000, 0x100000, 0x1000000}: `u32 tid, u32 quantity`. type **0x11110000** = pedido de
informação (ao abrir). Posta MsgObject 1 no ator "Lobby" (trava).

### 4.3 S->C 0x18C [V] (gh/lobbytask.c:5952)
`u8 result, u32 type`, e então:
- 0x11110000: `u32 ticketCount, u8 state`, depois **6** × `{u32 tid, u32 count, u32 remain}` (o laço usa o tamanho do
  mapa local, que o OnInit preenche com os 6 primeiros tids; ordem livre, casado por tid).
- 0x10000: `u32 ticketCount, u32 tid, u32 count, u32 remain`.
- 0x100000: `u32 ticketCount, u32 tid, u32 remain`.
- 0x1000000: `u32 ticketCount`.

Depois msg 2 (destrava) e msg 0x6D(type, result) → `Reponse` (src/frticketexchangedlg.cpp:477): result 0 → 0x10000
"응모 하였습니다.\n행운을 기원합니다.", 0x100000 (zera `state`) e 0x1000000 "지급 하였습니다.\n우편함을 확인해 보십시오.",
0x11110000 só atualiza; 2 → "해당 아이템의 재고가 없습니다…"; 4 → "교환 가능한 티켓이 적습니다…".
`ApplyInformation` (:956) **ajusta sozinho** a quantidade do ticket 0x1A0000BD no inventário local para ticketCount (apaga
se < 1). count/remain aparecem nos campos; troca com estoque fica "payment.tga"/"soldout.tga" e desabilitada quando
remain = 0 ou state = 0; com ticketCount = 0 tudo desabilita.

### 4.4 Servidor
Tabela de prêmios/estoque é do servidor (nada no IFF). Validar ticket ≥ custo (senão 4), estoque (senão 2), descontar
tickets, entregar o item (o texto diz "correio"; sem correio, inventário via 0x71/0xA5) e responder com o ticketCount
novo. `state` parece ser "ainda pode trocar hoje" [G].

## 5. Outros fluxos "use um item, ganhe item"

| item(ns) | gatilho (My Room, mini-botão; gh/realmyroommainui.c) | C->S | resposta | notas |
|---|---|---|---|---|
| 0x1A00003B 세뱃돈봉투 | `OpenNewYearMoney` :16136, yes/no → :10652 | **0x90** u32 **guid** | **0x107** (opc.c L10299): msg 2; u8 ok; se ≠0 o cliente remove 1× 0x1A00003B sozinho e lê 0x14 bytes {u32 ?, u32 tid @4, u32 ?, i32 valor @0xC, u32 ?} → popup "팡을 받으셨습니다."/item [I] | só com conteúdo 0x40 (ligado) |
| 0x1A00003E, 0x1A000048 (선물상자) | `OpenEventGiftBox` :16152 → :10704 | **0x91** u32 **guid** | **0x10B** (opc.c L10470): msg 2; u8 ok; se ≠0 remove a caixa (tid guardado no container 0x1D) e lê 0x14 bytes {u32 kind @0, u32 tid @4, …, u32 pang @0xC}: kind 3 = pang (grava o total), 4 = cupom, 5/6 = foi para presentes (o cliente pede a lista com 0x93) | ok = 0 → popup de erro (msg 0x23) |
| 0x1A000054, 0x1A0000BC | `OpenS4EventGiftBox` :16178 → :10756 | **0xAA** u32 **tid** | 0x10B (mesmo handler) [I] | |
| 0x1A000052 크리스마스 | `OpenEventXmasGiftBox` :11779 | — | — | `OnOpenEventXmasGiftDlgResult` só retorna true (:2126) |
| 0x1A000105 / 0x1A00014B (recarga) | `FrSupplyPackDlg` (gh/supplypack.c) | **0xEE** u32 packTid, u32 ballTid (bola não-básica acumulável) | **0x19C** (opc.c L14629): u8 0 → fecha o diálogo e mostra aviso (falha [I]); senão u32 packTid, u32 ballTid, u32 novoTotal; cliente decrementa o pacote e grava o total da bola | determinístico, não é sorteio |
| 0x1A00015B caixa especial | case 0x1A00015B → ator Gateway msg 0x276 → `CSpecialBoxHandler::OnReqOpenBox` | 0xF1 u32 0x1A00015B (após 3 s) | 0x1A2 (§2.3) | exige chave 0x1A00015C; conteúdo 0xA0 ligado; tids ausentes no 642 |
| cartas | (já implementado) | 0xC2 | 0x14C | SPEC-myroom §7 |

Respostas no RMT cujo C->S não foi identificado (prováveis caminhos antigos de "abrir caixa"; não implementar sem
achar o gatilho) [V layout / G gatilho]:
- **RMT 0x67** (:5202): msg 2; u8 tipo (5 → uma mensagem, outro → outra); u8 n; n × sBuyItemResult 0x26 (cada um vira
  msg 0xD6..0xDC de inserção conforme o grupo); i64 pang; i64 cookie.
- **RMT 0x121** (:5969): u8 0 → msg 2; u32 boxTid (o cliente decrementa 1 dessa pilha sozinho); u32 n; n × sPouchPrize
  0x14 {u32 tid, …, i32 valor @8}; 0x1A000010 soma pang. u8 1 → u32 erro 0..3 ("정의되지 않은 오류.", "잘못된 아이템
  코드…", "서버의 요청이 많습니다…", "아이템 수량이 부족합니다…").
- Tiki Magic Box: `OnInsertBtnLBUp`/`OnMixBtnLBUp` vazios (src/tikimagicboxdlg.cpp:194-205) → nenhum pacote no 645.

## 6. Primitivas de inventário para as respostas

### 6.1 S->C 0xA5 — quantidade/remoção (opc.c L6367) [V]
`u8 n, n × {u32 tid, u32 guid, u16 count}`, despachado por tid>>26: 2 (peça) e 7 (caddie) e 4 (club set, casa guid+tid)
→ **apaga**; 5 (bola) → grava count, 0 apaga (e volta a bola equipada para 0x14000000); 6 (item) → grava count, 0 apaga
**exceto** 0x1A000020..0x1A000027 (ficam com 0); 0x1C (aux) e cartas têm ramos próprios. No fim posta msg 0xDD.
Count é **absoluto** (novo total), não delta.

### 6.2 Inserção
- **0x71** `u16 total, u16 n, n × sItemInfo 0xA8` (total == n; guid repetido é ignorado) — para pilha/peça nova
  (SPEC-player-shop §2; o depósito/retirada do armário já usa assim).
- Pang: **0xC6** `u64 pang, u64 0` (delta 0 = definir) (SPEC-myroom).
- Caddie/mascote/set: mesmas mensagens da loja (SPEC-myroom §4/§5; set = expandir nos elementos).

## 7. Checklist de implementação
1. Reciclagem é o único fluxo realmente usável com cliente 645 + dados 642: carregar CadieMagicBox.iff (104 B) e
   CadieMagicBoxRandom.iff (16 B) no servidor; tratar 0x7E; responder 0xED depois de 0xA5/0x71.
2. Caixa aleatória: tratar 0xF1 em qualquer task mas responder 0x1A2 só faz efeito no My Room (RMT); pool no servidor;
   exige um pak de correção com o bit RandomBox nos itens escolhidos (§1.2).
3. 0x90/0x91/0xAA: implementar com pool do servidor se esses itens forem distribuídos; layout de 0x107/0x10B com campos
   [I] — validar os offsets no cliente antes.
4. Troca de tickets: só com patch do cliente (conteúdo 0x7A).
5. Correio: todos os textos originais dizem "verifique o correio"; enquanto 0x15E/0xBB..0xBF não entregarem itens,
   entregar direto no inventário.
