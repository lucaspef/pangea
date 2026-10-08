# KR 645: sistemas de item que o servidor ainda não trata

Upgrade de caddie (C->S 0xEC), buff de item (0xDA), composição de item de quest (0x68), pacote de suprimentos
(0xEE) e troca de nick (0x38). Pesquisa só de leitura, feita no cliente KR 645 QA.

Marcas: **[C]** confirmado no cliente (fonte reconstruída em `/root/rebang/source`, decompilação em
`/root/ghidra-out/*.c` ou disassembly em `/root/pg645.asm`, sempre com arquivo:linha ou endereço), **[R]**
referência (servidores GB/JP/S6 e o emulador `/root/pangya-server-work/emu`), **[P]** palpite.

Abreviações: `Doc` = `CSharedDoc` (singleton em `ds:0xac7088`); `OPC` = `CTask::OnPacketCommon` @0x732640 (não está na
decompilação: o switch é `opcode-0x2D` → tabela de bytes @0x746118 → tabela de saltos @0x745e1c, lida direto do exe);
"msg N" = `MsgObject(NULL, N, …)` mandado a um actor. "msg 1/msg 2" ao actor principal = trava/destrava a tela
("aguarde"). Os textos coreanos foram lidos do `.rdata` do exe (CP949).

Resumo:

| Sistema | C->S | S->C | Alcançável no KR 645? | Prioridade |
|---|---|---|---|---|
| Troca de nick | 0x38 `str nick` | 0x4E `u32 code [str nick, u64 cookie]` | sim (Opções › Jogo; conteúdo 0x82 ligado) | 1 |
| Upgrade de caddie | 0xEC `u32 tid do caddie antigo` | 0x199 (CShopTask) | sim (loja › caddie antigo › item "캐디 업그레이드") | 2 |
| Buff de item | 0xDA `u32 tid` | 0x186 tipo 2 (OPC) | só se o jogador tiver 0x1A0000B3/B4 (fora da loja) | 3 |
| Pacote de suprimentos | 0xEE `u32 tid do pacote, u32 tid da bola` | 0x19C (OPC) | só com 0x1A000105 (fora da loja); 0x1A00014B **não existe** no Item.iff | 4 |
| Composição | 0x68 `u32 tid da quest` | 0xCB (só RealMyRoomTask) | **não**: nenhum código abre o diálogo e não há Quest.iff | — |

Nenhum dos cinco está escondido por `IsLocalContent`, exceto a troca de nick, que **depende** do conteúdo 0x82
`S4_NT_NICKNAME_CHANGE_2ND` (ligado no KR, `shared/localize_kor.h:81`; enum em `shared/localize.h:80`) [C].

---

## 1. Troca de nick (C->S 0x38 → S->C 0x4E)

### 1.1 Como o jogador dispara [C]
- Botão "trocar" ao lado do campo de nick na aba Jogo das Opções. Só existe se
  `FrOptionDlg::sGameTab::IsEnableChangeNickname` (optiondlg.c:3870 @0x666ac0) devolver true:
  `IsLocalContent(0x82)` **e**, se a tarefa atual for o lobby, o layout não pode ser GAMEROOM/GAMEROOM_EXT/
  GAMEROOM_EXTRES (não dá para trocar dentro de uma sala).
- `FrOptionDlg::gOnChangeNickBtnUp` (optiondlg.c:5746 @0x668830):
  1. Pergunta ao actor "Gateway" (msg 0x26D, 0x10); se não liberar, manda msg 0x26D 0x11 e sai.
  2. `ChatManager::FilteringNick` (filtro de palavras do cliente): se achar algo, abre "notify" com o texto do filtro.
  3. O texto não pode ter espaço nem `'` (tokens `" '"`): senão "[']는 대화명에 사용할 수 없습니다.".
  4. Igual ao nick atual (sem diferenciar maiúsculas, `_strcmpi` com `Doc+0x3F8`) → "새로운 대화명을 입력해 주세요"
     (ou fecha as opções).
  5. Senão abre `FrChangeNickDlg` ("changenick") com o nick novo em `+0x114` e desliga os controles de nick.
- `FrChangeNickDlg` (ctor optiondlg.c:5572): `+0x118` = o jogador tem o item **0x1A000003 "대화명 명찰"** na lista
  de itens (`Doc+0x11A0`). `OnInit` (optiondlg.c:4208) mostra:
  - "닉네임을 변경하시기 전에 변경하고자 하는 닉네임을 꼭 확인하여 주십시오." / "이 상품은 청약철회(구매취소)가
    불가능한 상품입니다." / "대화명을 '%s'에서 '%s'(으)로 변경합니다."
  - identity com bit 0x204 (`Doc+0x433`, GM): "이 아이디는 무료로 대화명 변경이 가능합니다."
  - tem o 명찰: "대화명을 변경할 때 대화명 명찰 아이템이 1개 소모됩니다."
  - senão: "대화명을 변경할 때 **49쿠키**가 차감됩니다." (preço = `0x31` = 49, constante no código: OnPriceInit
    optiondlg.c:3979, OnRemainCookieInit :4141). Se os cookies (`Doc+0x1288`, u64) forem < 49, o botão
    "checkout" fica desabilitado (OnCheckoutInit :4195) e o texto mostra o saldo de cash (`S5::ExchangeOwnCookieToCash`).
- Checkout → "notify_okcancel" "대화명을 변경 하시겠습니까?" → `OnChangeNickConfirmDlgResult` (optiondlg.c:4408
  @0x6672c0): manda 0x38 se `cookies >= 49` **ou** tem o 명찰 **ou** identity & 0x204; senão mostra
  "쿠키가 부족합니다. 상점에서 쿠키를 충전해 주세요." e não manda nada.

### 1.2 C->S 0x38 [C]
`str nick` (EncodeStr do texto do campo, CP949; send em optiondlg.c:4441-4454, asm 0x66737d). Logo depois o cliente
manda **msg 1 ao actor "Lobby"** (trava a tela), desliga os controles de nick e fecha o FrChangeNickDlg.
Sem resposta a tela fica presa no "aguarde".

### 1.3 S->C 0x4E [C] (OPC case 0x4E @0x7366d0; tabela de códigos @0x7464A8)
Primeiro o cliente manda **msg 2** à tarefa atual (destrava). Depois `u32 code`:

| code | Layout extra | Efeito / texto |
|---|---|---|
| 0 | `str nick, u64 cookie` | ver abaixo |
| 1, 6 | — | "변경 요청이 실패하였습니다." |
| 2, 8, 10, ≥12 | — | "이미 사용되고 있는 대화명입니다." |
| 3 | — | "대화명은 영문 4 - 16 글자 또는 한글 2 - 8글자의 길이를 가져야 하며, 공백문자를 포함할 수 없습니다." |
| 4 | — | "쿠키가 부족합니다. 상점에서 쿠키를 충전해 주세요." |
| 5 | — | "사용이 금지된 대화명입니다." |
| 7 | — | "대화명 변경이 일시적으로 중단되었습니다." (o que o C# manda hoje) |
| 9 | — | "이전 대화명 변경 후 1주일이 지나지 않았습니다." |
| 11 | — | "대화명 명찰을 가지고 있지 않습니다." |

(As notas do emulador diziam "48 cookies" e "else genérico"; o certo é 49 e "já em uso".)

Sucesso (code 0):
1. Lê `str nick` e `u64 cookie`. Se o nick tiver de 4 a 16 bytes, copia para `Doc+0x3F8` (MyNick).
2. **Só se `!IsLocalContent(0x82)` e `!IsPcBang()`** (0x7abe10(0x82), 0x43e2d0): se o jogador não tem o 명찰,
   grava o u64 em `Doc+0x1298` e chama `RefreshAllCookie`; se tem, tira 1 do 0x1A000003. **No KR 0x82 está
   ligado, então o cliente não mexe em cookie nem no 명찰**: o servidor tem que mandar isso à parte.
3. msg 0x23 com "대화명이 변경 되었습니다. 지금부터 1주일 후 재 변경이 가능합니다." e msg 0xE7 com o nick novo
   (atualiza o nick mostrado [P]).
4. Se o MSN estiver conectado (estado 4), manda MSN 0x22 `str nick`.
Em todos os códigos, no fim, msg **0x40(code)** à tarefa: no lobby (lobbymain.cpp:4735-4754) code 0 fecha as Opções,
outro code religa os controles de nick.

### 1.4 Dados do pangya.iff
- Item.iff 0x1A000003 "대화명 명찰": Final 1, cash, Price 49, InStock 0 (não aparece na loja; só por GM/presente).
- Nenhuma tabela guarda o preço da troca: são os 49 cookies fixos no cliente.

### 1.5 O que o servidor deve validar
1. Fora de sala (o cliente já impede; conferir de novo é barato).
2. `NicknameRules.IsValid` (já existe em `Pangya.Domain/Players/PlayerService.cs:43`: 4..16 bytes CP949, ASCII
   visível ou Hangul, sem `'`, diferente do login) → senão code 3.
3. Palavras proibidas (a lista do servidor, se houver) → code 5.
4. Diferente do nick atual, sem diferenciar maiúsculas (o cliente já confere) → code 1.
5. Livre (`NicknameExistsAsync`, índice único `lower(nickname)` em `002_accounts.sql:18`) → code 2.
6. Última troca há ≥ 7 dias → code 9 (precisa de coluna nova, ex.: `accounts.nickname_changed_at`).
7. Pagamento, nesta ordem (a mesma do diálogo): identity GM (bit 0x204) = grátis; senão, se tiver 0x1A000003
   livre (não à venda), gasta 1; senão precisa de `cookie >= 49` → code 4.
8. Gravar numa transação: nick novo, data da troca, cookie/명찰, e auditoria (`audit_log`, ação tipo `nick`).

### 1.6 Resposta de sucesso sugerida
`0x4E u32 0, str nick, u64 cookie` (cookie depois do débito) e, porque 0x82 está ligado no KR:
`0x94 u64 cookie` (o mesmo de `GameHandler.Shop.cs:23`) e, se gastou o 명찰, `0xA5 u8 1, u32 0x1A000003, u32 id,
u16 total` (o padrão de `GameHandler.Boxes.cs:75`).
Depois atualizar a sessão (`Player.Nickname`) e quem guarda o nick em memória: canal/lobby, messenger (presença e listas
de amigos), guilda, ranking. Correio e recados congelam `sender_nick` no envio (`006_mail.sql:7`, `009_notes.sql:7`), o
que está certo.

### 1.7 Riscos
- O C# usa o nick como chave em vários lugares (comentário em `GameHandler.Social.cs:35`; buscas por nick em
  `FriendRepository`, `GuildRepository`, `MailRepository`, `RankingRepository`, `FindByNicknameAsync` em `Pangya.Domain/Accounts/Account.cs:38`).
  Antes de ligar é preciso confirmar que tudo que é persistido usa o id da conta, e que só a exibição usa o nick.
- O servidor de messenger e o de ranking são outros processos: precisam recarregar o nick (ou só ver o nick novo no
  próximo login) [P].
- Com 0x82 ligado, se esquecer o 0x94/0xA5 a tela continua mostrando os cookies e o 명찰 antigos até relogar.

---

## 2. Upgrade de caddie (C->S 0xEC → S->C 0x199)

### 2.1 Como o jogador dispara [C]
- Na loja, aba de caddie, selecionando um caddie **antigo "(구)"** (`CShopMain+0x528` = tid do caddie selecionado).
  `Build_Caddie` (shopmain.c:11931) lista os itens de caddie (CaddieItem.iff) cujo `(tid>>21)&0x1F` é o caddie e
  `InStock != 0`. Para cada caddie antigo existe um item **"캐디 업그레이드(…)"** (CaddieItem.iff, grupo
  `(tid>>13)&0xFF = 3`, Price 0, InStock 3, New 1):
  0x20206003 피핀, 0x20406004 뿌, 0x20606004 돌피니, 0x20806004 로로, 0x20A06004 큐마, 0x20C06005 티키, 0x20E06004 카디에.
- O botão de compra do item (`CShopMain::OnItemListBtnDown`, shopmain.c:12279-12349): depois da checagem do Gateway
  (msg 0x26D 0x10), se `(tid & 0xFC000000) == 0x20000000` e `(tid & 0x1FE000) == 0x6000`, chama
  `UpgradeCaddie(tid do caddie selecionado)` em vez do checkout.
- `CShopMain::UpgradeCaddie` (shopmain.c:7473 @0x7ba770): procura o caddie no mapa fixo
  `InitalizeUpgradeCaddieInfo` (shopmain.c:9402):

  | antigo | novo |
  |---|---|
  | 0x1C000001 피핀(구) | 0x1C000010 피핀 |
  | 0x1C000002 띠땅 뿌(구) | 0x1C000011 띠땅 뿌 |
  | 0x1C000003 돌피니(구) | 0x1C000012 돌피니 |
  | 0x1C000004 로로(구) | 0x1C000013 로로 |
  | 0x1C000005 큐마(구) | 0x1C000014 큐마 |
  | 0x1C000006 티키(구) | 0x1C000015 티키 |
  | 0x1C000007 카디에(구) | 0x1C000016 카디에 |

  Fora do mapa: "해당 캐디는 업그레이드가 불가능 합니다". Senão abre `FrUpgradeCaddieDlg` ("upgradecaddie") com
  `SetUpgradeInfo(tid antigo, deltas, MonthlyFee antigo, MonthlyFee novo)` (upgradecaddiedlg.cpp:42-69):
  "캐디 업그레이드를 진행합니다. 업그레이 이후 변경사항은 아래와 같습니다." + "재고용 비용 : %d팡 -> %d팡" (ou
  "해당사항 없음") + diferença de 파워/컨트롤/정확도/스핀/커브 (sCaddie.Attr novo − antigo).
- O cliente **não confere** se o jogador tem o caddie antigo: isso é com o servidor.

### 2.2 C->S 0xEC [C]
`u32 tid do caddie antigo` (upgradecaddiedlg.cpp:28-35; no exe o envio está inline em @0x7d80b0, Encode2 0xEC +
Encode4 `[this+0x128]`). Antes de fechar, manda **msg 1 ao actor principal** (trava a tela). É o tid, não o guid.

### 2.3 S->C 0x199 [C] (CShopTask::OnPacket; shoptask.cpp:495-530, decompilação shoptask.c:1891-1993, asm 0x7c69e2)
msg 2 à tarefa atual (destrava), depois `u32 code`:

| code | Layout extra | Efeito |
|---|---|---|
| 0 | `u32 guidAntigo, u32 guidCaddieEquipado, sCaddieInfo (25 B)` | apaga `guidAntigo` do mapa de caddies (`Doc+0x1164`), `AddCaddie(info)`, `Doc+0x628` (caddie equipado) = 2º u32, msg 0x24F ao "Shop" com "업그레이드를 성공하였습니다." e o tid novo |
| 1 | — | "해당 캐디를 가지고 있지 않습니다." |
| 2 | — | "업그레이드를 실패하였습니다." |
| 3 | — | "알수없는 오류가 발생하였습니다." |

Depois do switch o pacote cai também em `OnPacketCommon` (o código original não tem `break`); não faz nada útil, mas
é inofensivo [C]. O 0x199 só é tratado na tarefa da loja, que é de onde o 0xEC sai.
`sCaddieInfo` (globalgamedefine.h:304, 25 bytes pack 1): `u32 guid, u32 tid, u32 tidPart, u8 Level, u32 Exp, u8 flags
(bit0 presente, bit1 aluguel), u16 Remain_Date, u16 Remain_Partdate, u8 Purchase, u8 byCheckCaddieWarning, u8 PCBang`.
O C# já monta isso em `PlayerStructs.Caddie` (PlayerStructs.cs:52).

### 2.4 Dados do pangya.iff
- Caddie.iff (sCaddie: `IFF_ITEM_COMMON` 0x90 B, `MonthlyFee` @0x90, `Data[40]`, `Attr short[5]` @0xBC):

  | tid | Fee | Attr | → | tid | Fee | Attr |
  |---|---|---|---|---|---|---|
  | 0x1C000001 | 4100 | 2,0,0,0,0 | | 0x1C000010 | 30000 | 2,2,0,0,0 |
  | 0x1C000002 | 10000 | 2,2,0,0,0 | | 0x1C000011 | 35000 | 2,2,1,0,0 |
  | 0x1C000003 | 3950 | 0,0,0,2,0 | | 0x1C000012 | 15000 | 1,2,0,0,0 |
  | 0x1C000004 | 0 (cash) | 0,0,0,1,1 | | 0x1C000013 | 0 | 0,2,0,1,1 |
  | 0x1C000005 | 0 (cash) | 0,0,0,0,2 | | 0x1C000014 | 0 | 0,2,0,0,2 |
  | 0x1C000006 | 0 (cash) | 0,0,2,0,0 | | 0x1C000015 | 0 | 0,2,2,0,0 |
  | 0x1C000007 | 12000 | 0,2,2,0,0 | | 0x1C000016 | 40000 | 2,2,0,1,0 |
- CaddieItem.iff: os 7 itens "캐디 업그레이드" (Price 0 pang, não são cash): o upgrade é **grátis**; o item é só o
  botão. As roupas antigas `0x20xx…` "(구)" têm par "(신)" com o índice do caddie novo nos bits 21-25
  (ex.: 0x20200001 블루스타 피핀(구) → 0x22000001 블루스타 피핀(신); 0x20402001 → 0x22202001).

### 2.5 O que validar e gravar
1. `tid` está no mapa acima → senão code 2.
2. O jogador tem um caddie com esse tid, no inventário (não à venda/emprestado) → senão code 1.
3. Não tem ainda o caddie novo → code 2 [P] (o cliente não confere; ter dois do mesmo tipo quebra o mapa por tipo).
4. Gravar: trocar o `TypeId` do item caddie mantendo guid, nível, EXP e validade [P]. Roupa (`tidPart`): converter para
   o par "(신)" se existir no CaddieItem.iff, senão tirar [P]. Auditoria.
5. Resposta: `0x199 u32 0, u32 guid, u32 guid do caddie equipado (o mesmo se era ele), sCaddieInfo do caddie novo`.
   Mantendo o guid não precisa mexer no equipamento; se o caddie estiver equipado na sala/lounge, o próximo 0x49/0x74
   já leva o tid novo.
6. Erro inesperado (banco) → code 3.

### 2.6 Riscos
- As roupas "(구)" que o jogador comprou continuam ligadas ao caddie antigo por tid. Sem conversão, ficam inúteis.
- O cliente não manda guid: se o jogador tiver dois caddies com o mesmo tid (não deveria), escolher o primeiro.

---

## 3. Buff de item (C->S 0xDA → S->C 0x186)

### 3.1 Como o jogador dispara [C]
- No My Room, os itens que são buff mostram um mini-botão (bitmap "trade_item_edit_bn_*", dica 70) desenhado por
  `CItemBuff` quando o RealMyRoom manda msg 0x246 (realmyroommainui.c:23378 e :33011).
  `CItemBuff::UseBuffItem` (itembuffactor.cpp:344): confere compatibilidade com os buffs ativos e com os cards ativos
  (`Doc->m_cardAbilityList[0]`); se não der: "다음 아이템과 함께 사용할 수 없습니다. %s" ou "함께 사용할 수 없는
  아이템이 있습니다." (msg 0x23). Senão abre "notify_item_yesno": "이 아이템을 사용하시겠습니까?" com
  "기간 : %d분" e "효과 : 획득 경험치 +%d%%".
- Os buffs vêm de uma tabela **fixa no cliente** (`CItemManager::MakeItemBuffMap`, shared/itemmanager.cpp:4670,
  conferida no exe @0x770a01):

  | tid | Nome (Item.iff) | Tipo | Valor | Duração | Accum | Exclusivo com |
  |---|---|---|---|---|---|---|
  | 0x1A0000B3 | 군고구마 | 2 = SPECIAL_EXP_REAE_UP_TIME | +5% EXP | 120 min | 1 | 0x1A0000B4 |
  | 0x1A0000B4 | 황금 군고구마 | 2 | +50% EXP | 120 min | 1 | 0x1A0000B3 |

  Os dois estão no Item.iff (Final 1, Price 10.000.000, InStock 0): **não são vendidos**, só por GM/evento/presente.

### 3.2 C->S 0xDA [C]
`u32 tid` (itembuffactor.cpp:406-423; exe @0x5e4065). Antes manda **msg 1 ao "RealMyRoom"** (trava).
Sem resposta a tela fica presa no "aguarde" (hoje o C# só registra "pacote não tratado").

### 3.3 S->C 0x186 [C] (OPC case 0x186 @0x7458e7 → `CItemBuff::ProcessPacket` itembuffactor.cpp:430 @0x5e4ed0)
`u32 tipo`, depois:
- **tipo 0** (lista, ex.: no login): `u8 n, n × sSCardAvilityPeriodInfo` → `AddBuffItem` de cada um.
- **tipo 1** (venceu): `u32 tid` → `RemoveBuffItem`.
- **tipo 2** (resposta do 0xDA): msg 2 ao RealMyRoom (destrava), `u32 result`:
  - `result = 1`: `u32 tid usado, sSCardAvilityPeriodInfo`. O OPC, antes de passar ao CItemBuff, chama
    `CTask::_RemoveWareHouseItem(tid, RealMyRoom, 1)` (taskmanager.c:9979): **o próprio cliente tira 1 unidade** do
    item na lista `Doc+0x11A0` e manda msg 0xDD (atualiza a lista). Depois `AddBuffItem(info)` (exige
    `IsItemBuff(info.tid)`; se o tid já está ativo só troca `useEndTime`), liga o alarme de buff, msg 0x219(tid,1) ao
    RealMyRoom e abre "notify_item": "아이템을 사용하여 다음 효과를 얻었습니다!" + período/efeito.
  - outro `result`: abre o mesmo "notify_item" com item "none" e texto vazio (não há texto de erro).

`sSCardAvilityPeriodInfo` (globalgamedefine.h:1620, 0x41 B pack 1): `u32 uid, u32 tid, u32 partsTid, u32 partsUid,
i32 Avility, u32 AvilityValue, i32 slotNum, SYSTEMTIME useStart, SYSTEMTIME useEnd, i32 cardType, u8 valid`. O C# já
tem a struct (usada em `PlayerStructs.ActiveCards`, PlayerStructs.cs:110). Para buff: `uid` = id do registro do buff,
`tid` = 0x1A0000B3/B4, `parts*` = 0, `Avility = 2`, `AvilityValue = 5/50`, `slotNum = 0`, horas locais, `valid = 1`
[P nos campos que o cliente não lê: ele só usa tid e useEndTime na lista de buffs].

### 3.4 O que validar e gravar
1. `tid` ∈ {0x1A0000B3, 0x1A0000B4} (tabela no servidor igual à do cliente).
2. O jogador tem ≥1 livre (não à venda).
3. Não há buff ativo exclusivo (B3 × B4) → recusar (result ≠ 1).
4. Gravar numa transação: −1 do item e o buff ativo (tid, início, fim). Se o mesmo tid já está ativo: com Accum = 1,
   somar 120 min ao fim [P] (o cliente aceita qualquer `useEndTime` novo).
5. Responder `0x186 u32 2, u32 1, u32 tid, sSCardAvilityPeriodInfo`. **Não** mandar 0xA5 antes (o cliente já tira 1;
   um 0xA5 com total absoluto antes faria descontar duas vezes). Se quiser sincronizar, mandar o 0xA5 depois.
6. No login (depois da lista de cards 0x12F): `0x186 u32 0, u8 n, n × info` com os buffs ainda ativos. Ao vencer:
   `0x186 u32 1, u32 tid`.
7. Efeito: no fim da partida somar `AvilityValue` ao bônus de EXP (`Rewards.Exp`, onde hoje entra
   `CardService.ActiveRate(…AbilityExpRate…)`, GameHandler.GameEnd.cs:38). Se soma ou não com o card de EXP é [P]
   (o cliente só proíbe B3+B4 juntos).

### 3.5 Riscos
- Itens fora da loja: o sistema só serve a GM/eventos. Baixa prioridade.
- A tabela de buffs é do cliente; se mudar de versão de cliente, a tabela do servidor tem que acompanhar.

---

## 4. Pacote de suprimentos (C->S 0xEE → S->C 0x19C)

### 4.1 Como o jogador dispara [C]
- No My Room, usar (mini-botão) o item **0x1A000105** ou **0x1A00014B** (`CRealMyRoomMain::ExecuteMiniButtonDown`,
  realmyroommainui.c:25250) abre `S5::FrSupplyPackDlg` ("FrSupplyPackDlg"), caption "아즈텍 수량 보충 팩", texto
  "선택된 아이템의 수량을 보충 합니다." (`Initialize`, supplypack.c:1876 @0x86e320).
- A lista mostra as bolas do jogador (lista `Doc+0x1194`, com quantidade > 0) que passam em `IsSuppliableItem`
  (supplypack.c:758): não é 0x14000004, `IsCanOverlapped`, e, se for bola (`0x14…`), não é da "família Aztec básica"
  (`IsBasicAztecFamily`: 0x14000000 ou Price 0 com COM[0] = 0, itemmanager.cpp:4784) e existe no Ball.iff.
- Clicar numa bola → "notify_yesno" "선택하신 아이템 '%s'의 수량을 보충 하시겠습니까?" (`OnItemListUp`,
  supplypack.c:1095). Sim → `OnResultConfirm` (supplypack.c:843).
- ESC fica bloqueado enquanto o diálogo está aberto (`BlockKey("ESCAPE")` no ctor).

### 4.2 C->S 0xEE [C]
`u32 tid do pacote, u32 tid da bola` (supplypack.c:924-928; exe @0x86d499). Antes manda **msg 1** ao actor da
tarefa atual (trava); o botão OK fica visível porém desabilitado e o Cancel some. **Sem resposta o diálogo fica
preso** (sem OK, sem Cancel, sem ESC).

### 4.3 S->C 0x19C [C] (OPC case 0x19C @0x732771)
msg 2 à tarefa atual (destrava). Procura o diálogo aberto. `u8 ok`:
- `ok = 0`: fecha o diálogo e abre "notify" "오류가 발생하였습니다. 잠시 후 다시 시도해 주세요.".
- `ok ≠ 0`: `u32 tid do pacote, u32 tid da bola, u32 novoTotal`:
  1. tira 1 do pacote na lista `Doc+0x11A0` (apaga se zerar) e manda msg 0xDD (atualiza a lista);
  2. **só se a bola for tipo 0x14…**: acha a bola na lista `Doc+0x1194`, `delta = novoTotal − quantidade atual`,
     grava `novoTotal` e chama `FrSupplyPackDlg::UpdateData(delta)` (supplypack.c:798).
  3. `UpdateData` exige **delta ≥ 10** (e só na primeira vez); senão fecha o diálogo e mostra
     "업데이트 에니메이션에 문제가 발생하였습니다.". A animação mostra 10 "+N" que somam `delta` (10 × 1 + o resto
     espalhado ao acaso) e só então habilita o OK (`OnProc`, supplypack.c:1933).

(GB/JP chamam isso de "Comet Refill" e respondem 0x197 com `u16` quantidade [R]; no KR 645 é 0x19C com `u32`
[C].)

### 4.4 Dados do pangya.iff
- Item.iff **0x1A000105 "아즈텍수량보충상자(이벤트)"**: Final 1, Price 10.000.000, InStock 0 (fora da loja).
- **0x1A00014B não existe no Item.iff do KR**: o cliente trava ao procurar esse tid (emu `ext_items.py:15`) [R].
  O servidor nunca deve mandar esse tid (nem dar o item).
- A quantidade sorteada não está em nenhuma tabela do cliente. GB/JP sorteiam entre `min` e `max` de uma tabela do
  banco (`CometRefillSystem.cs:69`) [R]. Sugestão: configuração `SupplyPack.Min/Max` com `Min ≥ 10` [P].

### 4.5 O que validar e gravar
1. `tid do pacote` = 0x1A000105 (recusar 0x1A00014B), e o jogador tem ≥1 livre.
2. `tid da bola`: tipo 0x14, existe no Ball.iff, o jogador tem a pilha, passa nas mesmas regras de `IsSuppliableItem`
   (não 0x14000004, não Aztec básica).
3. Sortear `q ≥ 10`; limitar o total ao máximo de pilha (u16 com sinal no cliente: ≤ 32767; usar o limite que o C#
   já usa para pilhas) [P].
4. Transação: −1 pacote, +q na bola, auditoria.
5. Responder `0x19C u8 1, u32 0x1A000105, u32 tid da bola, u32 novoTotal`. Qualquer falha: `0x19C u8 0`.
   Não mandar 0xA5 antes (o cliente já tira 1 do pacote e grava o total da bola).

### 4.6 Riscos
- Se o total não crescer pelo menos 10 em relação ao que o **cliente** tem, a animação falha. O servidor deve calcular
  a partir da quantidade gravada, que precisa estar igual à do cliente (itens à venda na loja pessoal continuam
  contando na pilha do cliente).
- Item fora da loja: só serve para GM/evento.

---

## 5. Composição de item de quest (C->S 0x68 → S->C 0xCB)

### 5.1 O que existe no cliente [C]
- `FrCompoundDlg` (compounddlg.cpp): `SetCompound(sQuest)` mostra "유니크 아이템의 조합에 %s %d개가 소비됩니다." e,
  se `Doc->CanCompound(quest.TypeId)` (shareddoc.c:35044: todos os `DropTid[0..4]` da quest com quantidade ≥
  `DropNum` na lista de itens de quest `Doc+0x1228`), "조합을 하시겠습니까?"; senão "%s가 부족합니다.".
- Sim → barra de progresso + "%s을 조합 중 입니다. 잠시만 기다려 주세요." e **C->S 0x68 `u32 quest.TypeId`**
  (compounddlg.cpp:219; exe @0x7ce4e6). Não trava a tela.
- O resultado do diálogo viria por `SetCompoundRes(u8, sItemInfo)` (0 "조합에 실패했습니다.", 1 "축하합니다! %s를
  획득하셨습니다. '마이룸 > 의상실'에서 확인하세요.").

### 5.2 Por que não é alcançável no KR 645 [C]
- O construtor do `FrCompoundDlg` (@0x7cdbb0) só é chamado pelo `FrCompoundDlgMakeInstance` (@0x7ce0b0, registro na
  fábrica de objetos); não existe `CreateForm<FrCompoundDlg>` no exe.
- `SetCompound` (@0x7ce130, único chamador de `CanCompound` @0x7ce1b9) e `SetCompoundRes` não têm chamador.
- O pangya.iff do KR não tem **Quest.iff** (só QuestDrop.iff, 4 registros): `FindQuest` sempre devolveria nulo.
- Logo o 0x68 nunca sai do cliente.

### 5.3 S->C 0xCB [C] (só `CRealMyRoomTask::OnPacket`, realmyroomtask.c:5729)
msg 2 ao RealMyRoom, `u8 code`: 0 → `u32, u32, u16` → msg 0xDE (ninguém usa); 1-4 → "조합에 실패했습니다.";
5 → "축하합니다. 이벤트에 당첨되셨습니다. 자세한 사항은 홈페이지를 참조하세요."; 6 → "이벤트에 당첨이 안되셨습니다.".
O C# já usa o 0xCB para as caixas de evento (`SEventBox`, GameHandler.Boxes.cs:20).

### 5.4 Recomendação
Não implementar. Se um cliente modificado mandar 0x68, responder `0xCB u8 1` (falhou) e registrar no log.

---

## 6. O que acontece hoje sem resposta

| C->S | Trava enviada pelo cliente | Sem resposta |
|---|---|---|
| 0x38 | msg 1 "Lobby" | o C# responde 0x4E 7 ("suspenso"): destrava e religa os controles |
| 0xEC | msg 1 actor principal | "aguarde" infinito na loja (o C# só registra "pacote não tratado") |
| 0xDA | msg 1 "RealMyRoom" | "aguarde" infinito no My Room |
| 0xEE | msg 1 actor da tarefa | diálogo preso sem OK/Cancel/ESC |
| 0x68 | nenhuma | nunca é mandado |

Até implementar, a resposta mínima que destrava é: 0xEC → `0x199 u32 2`; 0xDA → `0x186 u32 2, u32 0`;
0xEE → `0x19C u8 0`.

---

## 7. Plano de implementação (por prioridade)

1. **Troca de nick** (o único com UI sempre visível).
   - Migração: `accounts.nickname_changed_at timestamptz`.
   - `AccountService.ChangeNicknameAsync(accountId, nick, pagamento)` com as regras do §1.5, transação com cookie/명찰.
   - Handler 0x38 em `GameHandler.Social.cs` → 0x4E (códigos do §1.3) + 0x94 + 0xA5.
   - Propagar o nick: sessão, canal, messenger/ranking (ou aviso de que só muda no próximo login). Auditoria.
   - Testes: nick inválido/igual/em uso/7 dias/sem cookie/com 명찰/GM.
2. **Upgrade de caddie**.
   - Tabela fixa antigo→novo (§2.1) no domínio; troca de TypeId mantendo guid; conversão da roupa "(구)"→"(신)".
   - Handler 0xEC → 0x199. Testes: sem o caddie (1), tid fora da tabela (2), já tem o novo (2), sucesso.
3. **Buff de item**.
   - Guardar buffs ativos (pode seguir o modelo dos cards ativos, com tid/início/fim).
   - Handler 0xDA → 0x186 tipo 2; 0x186 tipo 0 no login; tipo 1 ao vencer (na entrada do lobby ou num timer).
   - Bônus de EXP no fim da partida.
4. **Pacote de suprimentos**.
   - Configuração Min/Max (Min ≥ 10); handler 0xEE → 0x19C; nunca usar 0x1A00014B.
5. **Composição**: só a resposta defensiva `0xCB u8 1`.
6. Respostas mínimas do §6 podem entrar já, antes do resto, para tirar as telas presas.

## 8. Riscos gerais

- Nick como chave em memória e em outros processos (§1.7) é o maior risco: pode quebrar amigos, guilda, correio por
  nick e ranking se algum deles gravar o nick em vez do id.
- Buff e pacote dependem de itens que nenhum jogador tem sem GM; o valor de testar no cliente é baixo até haver
  evento.
- Ordem dos pacotes: 0x186 e 0x19C já descontam o item consumido no cliente; mandar 0xA5 antes deles desconta duas
  vezes.
- 0x1A00014B (pacote de suprimentos) não existe no Item.iff do KR: dar esse item ou citar o tid trava o cliente.
- Upgrade de caddie: o cliente não manda guid nem confere posse; tudo depende do servidor.
