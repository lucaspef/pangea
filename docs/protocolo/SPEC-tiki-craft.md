# KR 645: crafting/troca por materiais (Caixa Mágica da Caddie, Tiki Magic Box, FurnitureAbility e afins)

Tags: **[V]** lido no cliente decompilado (pseudo-C do Ghidra e/ou disassembly do `ProjectG_ReleaseQA.exe` com `objdump`);
**[D]** conferido nos dados (`/root/pangya-server-work/emu/data/pangya.iff`, dump com Python);
**[I]** inferido (servidores de referência de outras versões, ou raciocínio); **[?]** não verificado.
Nada aqui foi testado contra o cliente real.

Caminhos: `gh/` = /root/ghidra-out, `src/` = /root/rebang/source/client/ProjectG, `shared/` = /root/rebang/source/shared,
`GB` = /root/pangya-server/Server/GB/GameServer (servidor de referência, outra versão: só semântica, ids diferentes).
`OPC` = `CTask::OnPacketCommon` @0x732640 (o Ghidra não conseguiu decompilar: timeout; li o disassembly). O despacho é
`id-0x2D` → tabela de bytes 0x746118 → tabela de ponteiros 0x745E1C (verifiquei com script que o id 0xED cai em 0x741266).

## 0. Resumo: o que o servidor precisa fazer

| Sistema | Situação no cliente 645 KR | Trabalho do servidor |
|---|---|---|
| **Caixa Mágica da Caddie** ("마법상자", diálogo `recycledlg`, tabelas `CadieMagicBox.iff` + `CadieMagicBoxRandom.iff`) | Ativa (conteúdo local 5 `S3_RECYCLE` ligado; aba especial 0x63 ligada) | **Sim**: tratar C->S **0x7E**, responder S->C **0xED**, e mandar as atualizações de inventário (0xA5 / 0x71) |
| **Tiki Magic Box** (`tikimagicbox`, `TikiRecipe/TikiPointTable/TikiSpecialTable.iff`) | Não funciona: o botão exige o conteúdo 0x79 (desligado no KR) e todos os handlers do diálogo são `ret` vazios | Nenhum (não existe pacote) |
| **FurnitureAbility** (`FurnitureAbility.iff`, fogão "난로") | Não funciona: o cliente só mostra "난로 기능은 현재 사용하실 수 없습니다." | Nenhum |
| Troca de tickets (`ticket_exchage_dlg`) | Exige conteúdo 0x7A (desligado no KR) | Nenhum (layout no §4) |
| Upgrade de caddie (`upgradecaddie`) | Ativo pela loja, mas é compra de item, não crafting por materiais | Opcional: C->S 0xEC (§4) |
| "Tiki Report" | É o relatório de caddie (`S3_CADDIE_REPORT`, item 0x1A000041), não crafting | Fora deste documento |

## 1. Caixa Mágica da Caddie (Recycle / "Cadie Cauldron" no GB)

### 1.1 Como o jogador abre [V]
- Botão "MagicBox" da barra inferior → `CTaskMain::OnUnderBar_MagicBoxUp` (gh/taskmain.c:9439, 0x42B920):
  1. Se `CSharedDoc::IsControlServerService(0x8000)` (Doc+0x4D68 & 0x8000; é o u32 `controlServerService` do pacote
     0x42, ver SPEC-game.md) ≠ 0 → aviso "마법상자 관련 부분 점검중입니다." (em manutenção) e não abre.
  2. Senão, se o byte Doc+0x45F tem o bit 0x40 (= sUserInfo+0x7F, Doc guarda sUserInfo a partir de +0x3E0 [I]) →
     "카드정보 이상으로 마법상자를 이용할 수 없습니다." e não abre.
  3. Senão, se `IsLocalContent(5)` (ligado em `shared/localize_kor.h:21`) → cria `FrRecycleDlg` ("recycledlg").
- **Servidor:** mandar `controlServerService` sem o bit 0x8000 e o bit 0x40 de sUserInfo+0x7F zerado.
- Ao criar/fechar o diálogo, como `IsLocalContent(0x59)` está ligado, o cliente manda **0xB9 u8 1** (abrir) e
  **0xB9 u8 2** (fechar) (gh/recycledlg.c:7283, 7554). Já conhecido: aviso de localização, sem resposta.

### 1.2 Dados: `CadieMagicBox.iff` [V][D]
Cabeçalho IFF padrão (u16 count, u16 bind, u32 versão). 820 registros de **104 (0x68) bytes**, alinhamento natural
(struct `IFF_STRUCT::sCadieMagicBox`, `shared/classdefine.h:409`):

| off | tipo | campo | significado |
|---|---|---|---|
| 0x00 | u32 | uiNumber | 1..820, contíguo; **índice da receita = uiNumber-1** |
| 0x04 | u8 (+3 pad) | bFinal | sempre 1 |
| 0x08 | u32 | uiCategory | aba: 0 baixo, 1 médio, 2 alto, 3 especial, 4 evento |
| 0x0C | i32 | iCharacter | -2 = comum, -1 = (6 receitas de caddie), 0..9 = índice do personagem (filtro das abas 3/4) |
| 0x10 | i32 | iLevel | nível mínimo do jogador (0 na maioria) |
| 0x14 | u32 | uiOutput | typeid do resultado |
| 0x18 | u32 | uiOutputCount | quantidade por troca (1; 50 em 6 receitas; 10 em 1) |
| 0x1C | u32[4] | uiElem | typeids dos materiais (sem buracos: zeros só no fim) |
| 0x2C | u32[4] | uiElemCount | quantidade de cada material |
| 0x3C | u32 | uiRandSeq | ≠0: resultado sorteado em `CadieMagicBoxRandom.iff` (grupo uiRandSeq) |
| 0x40 | char[40] | szRandName | nome mostrado no lugar do item quando é sorteio (cp949) |

Números do arquivo [D]: abas 0/1/2/3/4 = 20/3/6/790/1 receitas; o arquivo já vem ordenado por uiNumber e as categorias
são monótonas (o cliente **depende disso**, §1.3). Grupos (`tid>>26`) dos resultados: 2 (part) 785, 6 (item) 21,
0x1C 6, 7 (caddie) 6, 9 (set) 2. Materiais: 786 receitas com 4, 10 com 2, 12 com 3, 12 com 1.

Exemplos decodificados [D]:
- #1 (índice 0), aba 0: 1A000133 "크로노스의 계절" ×1 ← 1A00005B..5E "시간의 파편 (봄/여름/가을/겨울)" ×1 cada.
- #2: 1A00003D "스크래치카드보조권" ×1 ← 1A0000F8 "스페셜 셔플 코인" ×3.
- #3: sorteio grupo 1 ("카드추출액") ← 1A0000CB "회귀초" ×1 + 1A0000CC "초급 조합 시험관" ×1.
- #4: sorteio grupo 2 ("광속의 신발", 10 personagens) ← 1A0000F0 "스피더 메달" ×5.
- #24 (aba 2, nível 18): 70000000 ×50 ← 18000009 ×6, 18000007 ×30, 18000004 ×25, 18000008 ×30.
- #201 (aba 3, personagem 5): 0814E052 "펌프킨위치슈즈(쿠)" ← 0814E051 "바이올렛위치슈즈(쿠)" ×1 + 18000006 ×1 +
  18000008 ×20 + 18000007 ×20 (troca de cor de part + consumíveis).
- #820 (aba 4 evento): 1A000154 "마력의 결정" ← 1A000150..153 "마력의 조각" ×2 cada.

`CadieMagicBoxRandom.iff` [V][D]: 88 registros de 16 bytes (`sRandomRecycle`, classdefine.h:372):
`u32 uiRandSeq, u32 uiTypeId, u32 uiCount, u32 uiProbs`. 9 grupos; **a soma de uiProbs de cada grupo é 1000**
(peso por mil). Ex.: grupo 1 = 1A0000CD ×1 (700) / 1A0000CE ×1 (250) / 1A0000CF ×1 (50). O cliente só usa essa
tabela para mostrar a lista de possíveis resultados (`OpenInformation`, `CItemManager::GetRecycleRandomMixOutItems`);
**o sorteio é do servidor**.

### 1.3 Como o cliente monta a lista [V]
- `CItemManager::MakeCadieMagicBoxMap` (`shared/itemmanager.cpp:1083`) copia cada registro para
  `g_ArrayRecycleItem[uiNumber-1]` (struct `sRecycleItem`, 0x50 bytes, `shared/recycletable.h:3`) e os materiais para
  `g_ArrayTidMixItem[uiNumber-1][0..3]`; `InitIndexRecycleItem` grava `iIndex = posição`.
- `sRecycleItem` (0x50): `u32 dwTid @0, u32 dwItemid(guid) @4, u16 iNum @8, u8 byLevel @0xA, i16 iChar @0xC,
  i16 iIndex @0xE, u8 byItemType @0x10, u16 wTime @0x12, SYSTEMTIME date @0x14, u32 dwRandSeq @0x24,
  char szRandName[40] @0x28`. É também o registro que o servidor devolve no 0xED.
- Abas (`SetListRecycleItem`, gh/recycledlg.c:7620): a aba k mostra o intervalo de posições acumulado pelas contagens
  g_Num{Low,Mid,High,Special,Event}RecycleItem → por isso a ordem por categoria é obrigatória. Abas 3/4 filtram
  `iChar == personagem selecionado || -1 || -2`. A aba especial só aparece com `IsLocalContent(99)` (ligado); a de
  evento só se houver receitas de categoria 4.
- Estado de cada linha (`AddHaveItemMap`): 1 = bloqueada por nível (`byLevel > Doc+0x539`, o Level de sUserInfo
  @0x159), 2 = resultado já possuído (`IsHaveItem`), 0 = disponível.

### 1.4 Validações do lado do cliente [V]
- Selecionar receita (`ClickRecycleItem` 0x67B750): só se nível ok e `!IsHaveItem(resultado)`.
  `IsHaveItem` (0x679430) considera possuído: part (grupo 2) com o mesmo tid **e** flag bit0 de IFF_COMMON+0x68,
  clubset (4), caddie (7), skin (0xE). Itens/bolas (5/6) nunca contam como possuídos.
- Para cada material (`SetDataTotalItem` 0x67A6A0) o cliente procura no inventário do Doc e preenche o "slot de envio"
  (this+0x328 + i*0x50) com **{tid, guid}** e a quantidade possuída:
  - grupo 2 (part): conta as cópias com o tid que **não estão equipadas** no personagem selecionado
    (`IsUsableParts` → `CSharedDoc::IsEquipParts`); o guid enviado é o de uma dessas cópias (ou o escolhido em
    `FrSelectPartDlg` quando há várias) [V parcial];
  - grupo 4 (clubset): o primeiro que não é o equipado (Doc+0x630); 5/6 (bola/item): guid da pilha, quantidade =
    Common[0]; 7 (caddie): guid do caddie.
- Quantidade (`SetMaxEnableRecycle` / `ControlRecycleNumber`): máximo = min(possuído_i / necessário_i). Se o grupo do
  resultado **não** for 5, 6 ou 0x1C, ou se a receita for sorteio (dwRandSeq ≠ 0), a quantidade é forçada a 1.
- Botão "mix" (`OnMixBnUp` 0x676D30): `CSharedDoc::ConfirmInsertItem(tidResultado, qtd)`; se estourar o limite mostra
  "현재 아이템의 보유개수가 20,000개이므로 아이템을 조합 할 수 없습니다." e não envia. Senão desliga os controles,
  toca uma animação da caddie e, **1,5 s depois** (`OnProc`, timer this+0x530 = 1.5f), chama `SendDataRecycleItem`.

### 1.5 C->S 0x7E — pedido de troca [V] (`FrRecycleDlg::SendDataRecycleItem` 0x67B990)
```
u16 index      // posição da receita = uiNumber-1 (sRecycleItem.iIndex)
u8  qty        // multiplicador (int do diálogo truncado para 1 byte)
u8  n          // nº de materiais enviados (o cliente grava 0 e depois corrige: EncodeData(offset 3) = payload[3])
n × { u32 typeid, u32 guid }   // slots i=0..3 com tid ≠ 0, na ordem de uiElem
```
- Se `index < 0` ou `qty < 1` o cliente não envia nada e fecha o diálogo.
- `n` é o número de uiElem ≠ 0 da receita (os slots seguem a ordem de uiElem). A quantidade consumida de cada
  material **não vai no pacote**: é `uiElemCount[i] × qty`, calculada pelo servidor.
- Exemplo (receita #1, qty 1): `7E 00 | 00 00 | 01 | 04 | 5B 00 00 1A gg gg gg gg | 5C 00 00 1A … | 5D … | 5E …`.
- No GB (0x158 lá) o layout é `u16 seq, u32 qty, u8 count, count × {u32 typeid, i32 id}` — mesma ideia, qty de 4 bytes.

### 1.6 O que o servidor deve validar e fazer [I] (regras derivadas do cliente + GB `Channel.requestCadieCauldronExchange`)
1. `index` < número de receitas; receita = registro com uiNumber = index+1. Senão → código 2 ou 1.
2. `qty` ≥ 1; se o grupo do resultado ∉ {5, 6, 0x1C} ou `uiRandSeq ≠ 0` → exigir qty = 1 (senão código 1).
3. `n` = nº de uiElem ≠ 0 e, para cada i, `typeid_i == uiElem[i]` (senão código 1).
4. Nível do jogador (sUserInfo Level) ≥ iLevel (código 1). Opcional: checar iCharacter para abas 3/4.
5. Cada material pertence ao jogador (guid + tid), não está à venda na loja pessoal, part não está equipada, e a
   quantidade possuída ≥ `uiElemCount[i] × qty` (código 2 se não existir; 1 se faltar quantidade).
6. Resultado não sorteado e não empilhável que o jogador já possui (part com flag bit0 de +0x68, clubset, caddie,
   skin) → código 4.
7. Limite de 20.000 unidades do item resultado → código 3.
8. Resultado:
   - `uiRandSeq == 0`: tid = uiOutput, quantidade = uiOutputCount × qty.
   - `uiRandSeq > 0`: sorteio ponderado por `uiProbs` (soma 1000) entre as linhas do grupo uiRandSeq de
     CadieMagicBoxRandom.iff → tid = uiTypeId, quantidade = uiCount (qty é sempre 1).
   - Set item (grupo 9, tid 0x24xxxxxx): entregar as peças do SetItem (como na loja; GB faz o mesmo).
9. Remover os materiais, criar/somar o resultado, salvar a conta, e só então responder (ordem no §1.8).
- As odds/sorteios **não vêm do cliente**: estão só nos IFF e o servidor decide. A animação e a mensagem "아이템획득(레어)"
  são decoração local.

### 1.7 Atualizações de inventário que o servidor precisa mandar [V] para o lado do cliente, [I] para a ordem
O handler do 0xED **não mexe no inventário** do Doc; ele só mostra o resultado e recalcula os contadores do diálogo
a partir do inventário que já está no Doc (`SetDataTotalItem`, `IsHaveItem`). Portanto, **antes** do 0xED:
- **Materiais consumidos → S->C 0xA5** `u8 n, n × {u32 tid, u32 guid, u16 count}` (OPC 0x739B9A; já usado em
  SPEC-myroom.md §10b). O handler escolhe pelo grupo do tid (tabela 0x746580/0x7465A4): tem casos para 2, 4, 5, 6,
  7, 0x1C, 0x1D e 0x1F; os demais grupos são ignorados. Verificados [V]: **2 (part)** procura o guid na lista de parts
  (Doc+0x117C) e o apaga (count ignorado; mandar 0); **6 (item)** grava `count` em Common[0] e apaga se `count < 1`,
  exceto os tids 0x1A000020..0x1A000027 (ficam com 0). Os outros casos (bola 5 em Doc+0x1194, clubset, caddie, card)
  não li em detalhe [?].
- **Resultado novo → S->C 0x71** `u16 total, u16 n, n × sItemInfo (0xA8)` com guid novo (guid duplicado é ignorado
  pelo cliente; ver SPEC-player-shop.md §2). Resultado empilhável que já existe (item grupo 6 com mesmo tid) →
  **0xA5** com o novo total no guid da pilha existente.
- Caddie (grupo 7) como resultado: 0x6F com o novo sCaddieInfo [I]. Set item: as peças via 0x71 [I].

### 1.8 S->C 0xED — resultado [V] (OPC 0x741266..0x741402)
```
u32 code
se code == 0:
  u16 index           // repetido; o cliente usa para reler a receita (byLevel, iNum, nome)
  u8  n
  n × sRecycleItem (0x50 bytes crus)   // o(s) item(ns) recebido(s)
```
Depois de decodificar, o cliente procura a janela "recycledlg"; se não existir, só limpa um estado (TLS+0xB190) e sai.
Se existir: `FrRecycleDlg::SetResultRecycleItem(code == 0, lista, index)` (0x67AE40) e
- **code 0**: posta MsgObject **0xE1** para a task atual (na My Room = `BuildDisplayStand`, redesenha o inventário;
  `realmyroommainui.c:34885`);
- **code ≠ 0**: posta MsgObject 0x23 (aviso) com o texto:

| code | texto (cp949) | sentido |
|---|---|---|
| 1 e > 4 | 조건이 올바르지 않습니다. | condição inválida (padrão) |
| 2 | 존재하지 않은 아이템입니다. | item inexistente |
| 3 | 아이템 보유 개수가 20,000개이므로 조합을 할수가 없습니다. | limite de 20.000 |
| 4 | 이미 소유한 아이템입니다. | já possui |

Como `SetResultRecycleItem` usa a lista [V]:
- Resultado **não-set**: exige `n == 1` (senão aviso de condição [I: mesmo texto do código 1]); copia o registro para a
  área de exibição, mas **sobrescreve iNum por `qty × iNum da receita`** (this+0x478) — o iNum enviado é ignorado na
  tela. O que importa no registro é **dwTid** (para sorteio, o tid sorteado) e, por coerência, dwItemid = guid do
  item criado. (Na seleção da receita o cliente mostra szRandName no lugar do nome do item quando dwRandSeq ≠ 0 [V];
  se a tela de resultado usa o nome do tid sorteado não verifiquei [?].)
- Resultado **set** (grupo 9): exige `n == nº de peças do SetItem` (byte +0x90) e que cada dwTid da lista bata com as
  peças (+0x94…); set ausente no IFF → "존재하지 않은 아이템입니다.", peças diferentes → "조건이 올바르지 않습니다."
  Nesse caso a tela mostra o registro da própria receita.
- Em seguida (sucesso ou falha): reabilita os controles, recalcula materiais/máximo pelo inventário do Doc e só deixa
  "mix" ligado se o resultado ainda não for possuído; 3 s depois apaga a exibição do resultado.
- Falha: mandar só `u32 code` (o cliente não lê mais nada; a lista fica vazia e index = 0).

Exemplo de sucesso (receita #1, guid novo 0x00001234):
`ED 00 | 00 00 00 00 | 00 00 | 01 | 33 01 00 1A  34 12 00 00  01 00  00 … (até 0x50 bytes)`.

Sequência recomendada [I]: `0xA5` (materiais) → `0x71` ou `0xA5` (resultado) → `0xED code 0`.
GB faz o mesmo em outra numeração: 0x216 (atualiza itens) e depois 0x22F (resultado da caixa).

### 1.9 Diferenças em relação ao GB [I]
- GB usa `findCadieMagicBox(seq+1)`, `item_trade.Qty[i] * qty`, sorteio por `Lottery` com `item_random.Rate`; regras
  iguais às do §1.6, mais data de validade da receita (o IFF do 645 não tem campo de data) e achievement 0x6C400082
  (não existe no 645).
- GB dá 10 dias a alguns sorteios temporários (Hermes/Jester/Twilight); no 645 os grupos de sorteio são parts/itens
  permanentes [D].

## 2. Tiki Magic Box (`TikiRecipe.iff`, `TikiPointTable.iff`, `TikiSpecialTable.iff`)

### 2.1 Por que não há nada a fazer no servidor [V]
- `CTaskMain::OnUnderBar_TikiMagicBoxUp` (gh/taskmain.c:9517) só abre `FrTikiMagicBoxDlg` ("tikimagicbox") se
  `IsLocalContent(0x79)`; 0x79 **não** está na lista de `shared/localize_kor.h` → o botão não faz nada.
  `OnUnderBar_TikiMagicBoxInit` é vazio.
- Mesmo forçando: todos os handlers do diálogo (`OnMixBtnLBUp`, `OnInsertBtnLBUp`, listas…) são funções vazias no
  binário original (map: 0x829890..0x8299D0, uma a cada 0x10 bytes; `src/tikimagicboxdlg.cpp:171-291`), e
  `InsertMaterial`/`DeleteMaterial` retornam 0. Nenhum `WSendPacket` existe no módulo.
- `tikireportopener.cpp` é só um inicializador estático (1 linha); o "Tiki/Caddie Report" é outro sistema (§4).
- No GB o "Tiki Shop" (C->S 0x18D, pontos Tiki por milhagem) é de temporadas posteriores; não há nada equivalente no 645.

### 2.2 Tabelas e o algoritmo local (só referência) [V][D]
Structs com alinhamento natural (`shared/classdefine.h:548-583`):
- `TikiRecipe.iff` = `sTikiOutputTable` 52 bytes: `u32 uiIndex, u8 bFinal(+3), char strCategory[32] @5, u32 uiTypeID @40,
  u32 uiCount @44, u32 uiRate @48`.
- `TikiPointTable.iff` = `sTikiPointTable` 48 bytes: `u32 uiIndex, u8 bFinal, char strCategory[32] @5, u32 uiMin @40, u32 uiMax @44`.
- `TikiSpecialTable.iff` = `sTikiSpecialRecipe` 60 bytes: `u32 uiIndex, u8 bFinal, char strCategory[32] @5,
  u32 uiElemCount @40, u32 uiElem[4] @44`.

Conteúdo no pangya.iff [D] — **dados de teste**, typeids 1/2/3/5 que não existem:
- Recipe: 시간의파편 → tid 5 ×5 (2 linhas); 일반_1 → tid 1 ×1 (3); 일반_2 → tid 2 ×2 (3); 고급 → tid 3 ×3 (2); uiRate = uiCount.
- PointTable: 일반_1 = 1..10 pontos; 일반_2 = 20..30; 고급 = 50..100.
- Special: 시간의파편 = 4× 0x000015B3, ou 2× 0x00000037.

Algoritmo `CTikiMagicBoxDoc::GetOutput` (`shared/tikimagicboxtable.cpp:101`, 0x7AFDD0), caso um servidor futuro queira
imitar: (1) se o multiconjunto de typeids dos materiais (até 4, `sTikiMagicBoxMtr {tid, guid, count}`) for igual ao
uiElem de alguma receita especial com uiElemCount = nº de materiais, a categoria é a dela; (2) senão soma
`Point × count` dos materiais (sPart.Point ou sItem.Point) e acha a faixa [uiMin, uiMax] da PointTable; (3) sorteia
`rand() % tamanho` dentro da categoria, mas usa o valor como **chave** do map (uiIndex), não como posição, e ignora
uiRate. A função sempre retorna `false` (código inacabado). Nada disso é chamado em lugar nenhum do cliente.

## 3. FurnitureAbility (`FurnitureAbility.iff`) [V][D]
- Registro de 60 bytes (`sFurnitureAbility`, classdefine.h:492, alinhamento natural): `u8 bFinal @0, u8 btAbilityType @1
  (0 item, 1 buff), u32 dwFurnitureTID @4, u8 btSuccessType @8 (0 stay, 1 set-in, 2 put-out), i32 iStayTime @12,
  u8 btEffectType @16 (0 eu, 1 amigo, 2 guilda, 3 todos), u32 dwSetInTID @20, i32 iMaxCountAtFurniture @24,
  SYSTEMTIME stStart @28, u32 dwDuringTime @44 (horas), u32 dwPutOutTID @48, i32 iProbability @52 (por mil),
  i32 iMaxCountAtUser @56`.
- Dados [D]: 4 linhas, todas do fogão 48006823 "난로", put-out, início 2011-02-17 08:00, 168 h:
  1A0000B3 "군고구마" 900‰ / 1A0000B4 "황금 군고구마" 100‰, para efeito 0 (máx 1 no móvel) e efeito 3 (máx 2); máx 3 por usuário.
- Cliente: `CRealMyRoomMain::FurnitureRun` (gh/realmyroommainui.c:21214) exige `IsLocalContent(0x76)` (ligado) e abre
  `CFurniture_AbilityDlg` ("myitem_choice"); no resultado (`OnFurniture_AbilityDlgResult`, :17000), para um móvel
  0x48xxxxxx com código 0x6800 (o fogão) só mostra "난로 기능은 현재 사용하실 수 없습니다."; nos outros casos
  "가구 사용 및 구동 메뉴 열기가 불가능한 가구입니다." ou "가구에 아이템 추가하기를 중단하였습니다.".
  `CFurniture_AbilityDlg::GetItemInfo` é vazio. **Nenhum pacote** → nada a fazer no servidor.

## 4. Outros fluxos parecidos (fora do crafting por materiais)
- **Upgrade de caddie** [V]: na loja, comprar um item de caddie (grupo 8) com `(tid & 0x1FE000) == 0x6000` chama
  `CShopMain::UpgradeCaddie` (gh/shopmain.c:7478), que usa um mapa fixo (`InitalizeUpgradeCaddieInfo`, :9404)
  0x1C000001→0x1C000010, 0x1C000002→0x1C000011, … (caddie antigo → versão nova), mostra a diferença de stats/preço e,
  no "Yes", manda **C->S 0xEC u32 caddieId** (`src/upgradecaddiedlg.cpp:46`). Resposta não identificada [?].
- **Troca de tickets** [V]: `FrTicketExchangeDlg::Request` manda **C->S 0xE1 u32 tipo** (+ `u32 tid, u32 qtd` quando
  tipo ∈ {0x10000, 0x100000, 0x1000000}) e `Reponse(u32 tipo, u8 erro)` trata erros 0/2/4. Só abre com
  `IsLocalContent(0x7A)`, desligado no KR → ignorar.
- **Evento de Valentine** (`NtValentineEventDlg`): botões "combine" mandam C->S 0x6F u32 0/1, 0x6E u32 0x1A000016,
  0xDD u32 … [V]; diálogo de evento sazonal criado por layout, não analisado [?].
- **Remoção de card (0xE2) e enchant de stats (0x4B/0xA3)**: já especificados em SPEC-myroom.md §7/§8.
- **"Tiki Report"**: `CRealMyRoomMain::OpenTikiReport` usa `IsLocalContent(0x14)` = `S3_CADDIE_REPORT`; manda
  C->S 0xA3 u32, u32 ao confirmar; é o relatório de caddie (item 0x1A000041), não troca de itens.

## 5. Checklist de implementação (Caixa Mágica)
1. Carregar `CadieMagicBox.iff` (104 B/registro) e `CadieMagicBoxRandom.iff` (16 B) do mesmo pangya.iff do cliente.
2. Handler C->S 0x7E (layout §1.5), validações §1.6, códigos 1..4 do §1.8.
3. Persistir: tirar `uiElemCount[i]×qty` de cada material (part: apagar o guid), criar/somar o resultado com guid novo.
4. Enviar 0xA5 (materiais), 0x71/0xA5 (resultado), depois 0xED code 0 com `index` e 1 sRecycleItem
   {dwTid, dwItemid = guid, iNum = quantidade, resto 0} (ou as peças, se set).
5. Em erro: só `0xED u32 code`, sem mexer no inventário.
6. Não mandar o bit 0x8000 em `controlServerService` (0x42), senão o botão diz "em manutenção".
