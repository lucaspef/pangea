# KR 645: correio (우편함) e caixa de presentes (선물함)

Marcas: **[C]** confirmado no cliente (decompile/disasm do ProjectG_ReleaseQA 645); **[R]** visto nas referências
(GB `/root/pangya-server/Server/GB`, S6, emulador Python `emu/`); **[P]** palpite.
Fontes: GH = `/root/ghidra-out` (RMT = realmyroomtask.c, MUI = realmyroommainui.c, PRD = post_recivedlg.c,
PSD = post_senddlg.c), OPC = disasm de `CTask::OnPacketCommon` (@0x732640; `idx = id-0x2D`, bytes @0x746118,
ponteiros @0x745E1C; ids < 0x2D caem no default), SRC = `/root/rebang/source` (fonte reconstruída).

## 0. Resumo: o que o KR 645 usa de verdade

- Conteúdo local **0x60 `S4_MAILBOX_FOR_REAL_MYROOM` está LIGADO** no 645, assim como 0x56 (Real MyRoom) e 0x61. O 0x5B
  (caixa de presentes antiga dentro do My Room) está **desligado**. Conferido em `InitLocalizeSystem` @0x7abee0 [C].
- Por isso **o "presente" do KR é o correio**. Presente da loja (0x1F), prêmio de caixa (0x1A2) e "novo presente" passam
  pelo correio. A caixa antiga (`sGiftInfo`, 0x78/0x92/0x93/0x24/0x4D) existe no binário, mas a sub-aba de presentes
  (0x11) do baú "mt_mybox" **não é criada** quando 0x60 está ligado (MUI 29581-29620, `uVar = IsLocalContent(0x60)+0x11`).
  Assim a UI dela não aparece no KR [C].
- Mesmo assim, o **0x78 do login continua obrigatório**: o modo 1 posta o bit 8 da `m_underBarMask` (SPEC-player-shop §5) [C].
- O botão "presente" da barra inferior (`CTaskMain::OnUnderBar_GiftUp`, taskmain.c 11704) com 0x56+0x61 **entra no
  próprio My Room** (Doc[0x5B80]=3 → 0xAF uid,1). Lá a caixa de correio (móvel/menu) abre o diálogo `postbox` [C].
- Menu do correio bloqueado se `controlServerService` (u32 do 0x42, Doc+0x4D68) tiver o **bit 0x40000**: aparece
  "우편함 기능의 부분 점검중입니다." (MUI `SelectMenu` @0x5af0ba e `SetListMenu`) [C]. O servidor não deve mandar esse bit.

## 1. Estruturas (pack 1) [C]

### 1.1 sMailIncludeItem (0x35 = 53 bytes): anexo de carta (SRC globalgamedefine.h:1158)
| off | tipo | campo | uso |
|---|---|---|---|
| +0x00 | u32 | dwIDX | **guid** do item. No envio é o guid do item do remetente; no recebimento (0x144) é o guid que o item terá no inventário. Grupo 8 (peça de caddie): é o **guid do caddie dono** |
| +0x04 | u32 | dwTID | typeid (0x1A000010 = saco de pang: o cliente não põe no inventário) |
| +0x08 | u8 | btItemType | tipo de tempo (vai para os bits 4-6 de sItemInfo+0x17): 0 normal, 2 minutos, 3 horas, 4 dias, 5 meses (ver 0x144) |
| +0x09 | i32 | iCount | quantidade (no 0x144, para empilháveis, é o **total novo**) |
| +0x0D | i32 | iTimeCount | duração (unidade depende do grupo, ver §2.6) |
| +0x11 | i16[5] | soCom | Common[] (upgrades do taco, grupo 3/4) |
| +0x1B | u16 | CharacterSlotNum | slots de card do personagem (peças, conteúdo 0x51) |
| +0x1D | u16 | CaddieSlotNum | |
| +0x1F | u32 | dwSetTID | set (0xFFFFFFFF = nenhum) |
| +0x23 | i32 | iSetItemCount | |
| +0x27 | char[9] | csUCCIndex | UCC (conteúdo 0x57) |
| +0x30 | u32 | reserved30 | 0 |
| +0x34 | u8 | reserved34 | 0 |

### 1.2 sMailInfoBrief (0xC2 = 194 bytes): linha da lista
SRC só declara `id, sender[22], reserved[0x6E], bRead, itemCount, item`. O GB tem o mesmo tamanho até bRead:
`id, from[30], msg[80], unk[18], visit_count u32` [R].
| off | tipo | campo | tela |
|---|---|---|---|
| +0x00 | u32 | id da carta | usado em 0xBD/0xBE/0xBF [C] |
| +0x04 | char[30] | remetente (o cliente copia 22) | alarme de presente (FrGiftAlarmDlg) [C] |
| +0x22 | char[80] | trecho da mensagem | [R] (no SRC, o alarme usa texto vazio) |
| +0x72 | 18 bytes | ? | zerar [R] |
| +0x84 | u32 | visit_count | [R] |
| **+0x88** | u8 | bRead | ícone lida/não lida (PRD ownerdraw, bitmaps +0x188/+0x18C) [C] |
| **+0x89** | i32 | itemCount | 0 = só carta (o botão vira "apagar"); ≥1 = ícone e nome do 1º item, "+ %d개" se >1; -1 = caso especial (imprime "%d") [C] |
| +0x8D | sMailIncludeItem | 1º anexo | ícone/nome (+0x91 tid), quantidade (+0x96) ou tempo (+0x95 tipo, +0x9A tempo) [C] |

Sem anexo, a linha mostra "편지" ("carta") [C].

### 1.3 sMailInfo (leitura): montado do 0x142, não é cópia direta de buffer
`dwIndex, szFromNick[22], szDate[32], szContent[800], bHaveItem, list<sMailIncludeItem>` [C].

### 1.4 sGiftInfo (0xFB = 251 bytes): caixa antiga
`u32 guid +0, u32 tid +4, u16 Arg0 +8 (quantidade/pang), char sFromID[128] +0xA, char sMsg[80] +0x8A,
char sDate[32] +0xDA, u8 ItemType +0xFA` [C]. Ao receber, o cliente descarta o presente cujo IFF não existe ou não tem
o byte 0 == 1 (`ItemManager` fn 0x765690) [C].

### 1.5 sNoteInfo (0x6D): bilhete do mensageiro, não é correio
`u32 uid, u16 reserved, char nick[22], char note[64], char time[16], bool bReply` [C].

## 2. Correio (RealMyRoom, `CPost_ReciveDlg` "postbox" / `CPost_SendDlg` "write_post")

Todas as respostas S->C do correio são tratadas **só no `CRealMyRoomTask::OnPacket`** (RMT). Fora do My Room elas caem no
default e são ignoradas. As exceções são 0x15E, 0x31 e 0x36, que estão no OnPacketCommon [C].
O pedido trava a UI (`EnableMiniBtns(false)` + msg 1 "aguarde"). Toda resposta manda msg 2 / 0x244(1), que destrava.
**Sempre responder**, senão o diálogo fica preso [C].

### 2.1 Listar: C->S 0xBC → S->C 0x140 / 0x141
- **C->S 0xBC** `u32 page` (1-based). Ao abrir: page = 1 (ctor PRD 4050: `+0x1CC = 1`). Pelo índice:
  `grupo*5 + 1 + i` (índice de 5 páginas) (PRD 1931-2300, 4134) [C].
- **S->C 0x140** `u32 totalPages, u32 curPage, u32 n, n × sMailInfoBrief(0xC2)` (RMT 6466) [C].
  - **20 cartas por página**: a listbox completa até 0x14 linhas vazias (PRD 4134) [C]. O GB também usa 20 [R].
  - Se `totalPages != 0` e a página pedida for maior que `totalPages`, o cliente pede de novo a última (PRD 4293) [C].
  - Depois: msg 0x223 → `SetLoadPostList`.
- **S->C 0x141** `u8 code`: erro. **Usa a tabela de mensagens do 0x13F** (o código posto na msg 0x228 é 0x13F, disasm
  0x5b74f6), e não "우편함의 편지들을 읽어오지 못하였습니다" [C].
- Ordem: mais nova primeiro (GB ordena por id desc) [R].

### 2.2 Ler: C->S 0xBD → S->C 0x142 / 0x143
- **C->S 0xBD** `u32 mailId`. É enviado ao clicar na linha fora do mini-botão (`CPost_ReciveDlg::OnPostListBtnDown`,
  `ToServerSendData(...,0xBD)`) [C].
- **S->C 0x142** (RMT 6571) [C]:
  `u32 mailId, Str fromNick (≤21), Str date (≤31, texto livre), Str content (≤799), u8 hasItems (==1 lê a lista),
  [se 1: u32 n, n × sMailIncludeItem 0x35]`
  → msg 0x224 → `OnPostDescDlgCreate` abre "post_contents" (remetente, data, texto, anexos).
  Ao fechar, o cliente marca a linha como lida **localmente** (`brief+0x88 = 1`, PRD 46). O servidor grava `read_at` no 0xBD.
- **S->C 0x143** `u8 code`: erro, também pela tabela do 0x13F [C].

### 2.3 Pegar anexos: C->S 0xBF → S->C 0x144 / 0x145
- **C->S 0xBF** `u32 mailId`: mini-botão de uma carta com `itemCount ≥ 1` e pergunta "선물을 마이룸으로 옮기시겠습니까?".
  Se o tid do 1º item não existe no IFF: "우편물에 첨부된 아이템은 현재 옮길수 없습니다." e nada é enviado [C].
- **S->C 0x144** (RMT 6699) `u32 n, n × sMailIncludeItem` → o cliente **coloca no inventário** por grupo (tid>>26) [C]:
  | grupo | efeito no cliente |
  |---|---|
  | 2 peça | novo sItemInfo guid=dwIDX; dias = iTimeCount/1440; UCC (0x57) → pede 0xB1 |
  | 3/4 taco | novo item guid=dwIDX; Common[] = soCom |
  | 5 bola | se o guid já existe: **count = max(1,iCount)** (total); senão novo |
  | 6 item | idem bola. Tempo pelo btItemType: 2 → iTimeCount/60, 3 → iTimeCount, 4 → ×24, 5 → ×720. **tid 0x1A000042 novo + conteúdo 0x14 (ligado): lê mais `u16, u16, 16 bytes` logo depois dessa struct** |
  | 7 caddie | novo; tids 0x1C000001/2/3/7: dias = iTimeCount/1440 e flag 2 (aluguel); tids 0x1C00000C-0E: guarda bytes de tempo |
  | 8 peça de caddie | procura o **caddie com guid = dwIDX**: tidPart = tid, Remain_Partdate = iTimeCount/60 |
  | 0xE skin | novo; HourRemain = iTimeCount×24 |
  | 0x10 mascote | novo; tempo = iTimeCount/60 |
  | 0x12 móvel | AddFurniture (posição do Furniture.iff) |
  | 0x1C / 0x1D | lista própria; count total / +1 |
  | 0x1F card | por **tid**: count = iCount (total) |
  | 0x1A000010 | **ignorado**: o pang tem de vir à parte em **0xC6** [C: o case pula esse tid; P: usar 0xC6] |

  Depois: popup "편지에 포함된 아이템을 마이룸으로 이동시켰습니다.", msg 0x226 (recarrega a página com 0xBC) e 0xE1 [C].
- **S->C 0x145** `u8 code`: "우편물에 첨부된 아이템을 옮기지 못하였습니다." (qualquer código) [C].

### 2.4 Apagar: C->S 0xBE → S->C 0x15C / 0x15D
- **C->S 0xBE** `u32 n, n × u32 mailId, u32 curPage` [C]:
  - uma carta sem anexo: mini-botão + "편지를 삭제하시겠습니까?" → n = 1;
  - "apagar todas" (`OnDeleteAllLetterBtnUp`): só as cartas **sem anexo** da página atual (`delete_letter_check`), com
    "아이템이 첨부되지 않은 현재 페이지의 편지(%d개)를 모두 삭제…(읽지 않은 편지도 삭제)". Se não houver nenhuma:
    "삭제할 편지가 없습니다." e nada é enviado.
- **S->C 0x15C** (sem payload) → msg 0x226 → recarrega com 0xBC curPage [C].
- **S->C 0x15D** `u32 code`: **9** = "아이템이 포함되어 있어 지우지 못하였습니다."; outro = "우편물을 지우지 못하였습니다." [C].
  O servidor deve recusar carta com anexo não retirado (código 9) [C: a mensagem existe; P: regra].

### 2.5 Enviar: busca de nick C->S 0x07 → 0x9F; envio C->S 0xBB → S->C 0x13E / 0x13F
**Busca do destinatário** (`CPost_SendDlg::OnPostSearchUserBtnUp`, PSD 1474) [C]:
- Validações no cliente: o nick não pode ser o próprio ("본인에게는 우편물을 보낼 수 없습니다.") e precisa ter 1-21 bytes
  ("받는 이의 닉네임을 넣어주세요.").
- **C->S 0x07** `u8 0, Str nick`. É o mesmo pacote do presente da loja (FrGiftDlg) e de adicionar amigo.
- **S->C 0x9F** (OPC case 0x9F @0x739160): `u8 result`; **0** → `u32 uid, sPangYaUserInfo (0x10D)` → msg 0x50 →
  `CPost_SendDlg::SetReceiverInfo(uid, info)` (nick @+0x16; com conteúdo 0x10 e `info+0x85 == 1` usa o nick digitado).
  ≠0 → "유저를 찾을 수 없습니다." [C]. O GB responde 0xA1 com o mesmo formato, porque os ids mudaram [R].
- O destinatário também pode vir da lista de amigos (`OnFriendListResult`), que usa o uid do buddy.

**Regras do diálogo de envio** (`CPost_SendDlg::OnOK` @0x5d91c0, `CPost_MyItemList`) [C]:
- é preciso ter confirmado o destinatário ("선물할 대상의 이름이 확인되지 않았습니다…");
- é preciso mensagem **ou** item ("메시지나 아이템을 첨부하셔야…");
- mensagem ≤ **800** ("편지 내용은 800자를 초과할 수 없습니다.");
- até **4 itens** ("이미 4개의 아이템을 모두 선택하셨습니다."), sem repetir o tid ("같은 아이템을 두 가지 이상…"; "이미 추가된 아이템입니다.");
- ≤ 99 unidades por item, e o guid tem de bater com o tid do inventário (`VerifySendItemList`) → "첨부한 아이템들 중에 데이터가 잘못된…";
- **card (grupo 0x1F) não pode** ("카드는 현재 선물 할 수 없습니다."), nem roupa com card ("카드가 인챈트 된 의상은…");
- itens permitidos (`CTransferableItem`, SRC transferableitem.cpp): IFF **IsSalable ∈ {1,3}**; fora a bola, o caddie e o
  taco equipados e as peças em uso; quantidade = Common[0] − em troca − equipado;
- **taxa** `GetSendServiceCharge` = **100** pang sem item, **500 × nItens** com item; "수수료가 부족합니다." se faltar;
  confirma com "수수료가 %d 입니다. 우편물을 보내시겠습니까?". **Não existe anexo de pang**: `DrawPang` só mostra o pang do jogador.

**C->S 0xBB** (`CRealMyRoomMain::OnPostSendDlgResult` @0x5a4a30) [C]:
`u32 myUid (Doc+0x4EB), u32 receiverUid, u8 nItems, i64 taxa, Str "" (sempre vazio; assunto/destinatário no GB),
Str mensagem, nItems × sMailIncludeItem`.
Cada item vem como `{dwIDX = guid, dwTID, btItemType, iCount, iTimeCount = wTime}` e o resto zerado (`GetSendList`).
O servidor **refaz tudo**: taxa, dono, quantidade, IsSalable, máximo 4, sem repetir, sem card, não equipado.

**S->C 0x13E** (sucesso, RMT 6234) [C]: `u32 n, n × sMailIncludeItem`. São os itens que **saem do inventário do remetente**:
por guid+tid, a quantidade cai em iCount ou o item é apagado quando sobra 0; card por tid. Se o mensageiro estiver
conectado, o cliente manda 0x27 (myUid, Doc+0x4DB8) para ele. Popup "우편물을 보냈습니다. 편지 쓰기를 계속 하시겠습니까?".
O pang da taxa **não** é descontado aqui, então mande **0xC6** com o pang novo [C: não mexe no pang; P: 0xC6].

**S->C 0x13F** `u8 code` (msg 0x228/0x13F, textos @0x5b1a68) [C]:
| código | mensagem (resumo) |
|---|---|
| 1 | o destinatário recusa cartas |
| 2 | destinatário não encontrado |
| 3 | o destinatário já tem o item |
| 4 | mais itens do que possui |
| 5 | item que não possui |
| 6 | o destinatário já recebeu esse item por carta (está na caixa dele) |
| 7 | o item não pode ser anexado |
| 8 | não é possível enviar carta |
| 9 | a caixa do destinatário está cheia |
| 10 | correio em manutenção parcial |
| 0x16 | passou do máximo de anexos |
| outro | "우편물을 보내지 못하였습니다." |

### 2.6 Unidades de tempo dos anexos
São as conversões do 0x144 [C]. A semântica certa por grupo é [P]: o servidor deve gravar a duração em uma unidade só
(minutos) e converter ao montar o pacote:
peça e caddie de aluguel: minutos (/1440 = dias); item grupo 6: depende do btItemType; skin: dias (×24);
mascote e peça de caddie: minutos (/60).

### 2.7 Aviso de carta nova e contador
- **Não há contador de não lidas no login** (0x42/0x44). O aviso é a lista **S->C 0x15E** [C, dentro do que foi procurado].
- **S->C 0x15E** (OPC case 0x15E @0x742bae): `u32 n, n × sMailInfoBrief` → substitui `Doc.m_mailList` (+0x11E8). Se
  n > 0: Doc+0x4DBC = 1 e msg **0xAA(1)** → o botão de alarme de presente pisca (CTaskMain +0x110; no lobby e no jogo,
  ignorado no My Room). Clicar abre "gift_alarm" (`FrGiftAlarmDlg`), que com 0x60 ligado lista **m_mailList**
  (remetente, ícone e nome do 1º item, quantidade/tempo) [C].
- Quem dispara: **S->C 0x31** (sem payload; com 0x60 limpa m_mailList e manda **C->S 0xE6**) e **S->C 0x36** (sem
  payload; manda 0xE6). **C->S 0xE6** e **C->S 0x15E** (este do RMT depois do 0x1A2, caixa aleatória) não têm payload e
  pedem o 0x15E [C: envio; P: a resposta 0x15E, mas é o único pacote que preenche m_mailList].
- `m_mailList` é limpa no ctor do Doc e ao trocar de servidor (`FrServerDlg::OnServerListBtnDown`). Então, ao entrar
  no game server, mande **0x15E** espontâneo com as não lidas [P].
- Carta nova para quem está online: mandar **0x31** (ou 0x36) para a sessão do destinatário [P]. No original, o
  cliente do remetente avisava o servidor de mensageiro (0x27 myUid, uid alvo) e ele repassava [C: o 0x27].
- Limite da lista do alarme: o GB usa 300 não lidas [R]. Sugestão: as 20 mais novas não lidas [P].

### 2.8 Bilhetes (쪽지, 0x3C sub 0x111): não fazem parte do correio
- **C->S 0x3C** `u16 0x111, u32 uid, Str nota (≤63), u8 flag` (0 = resposta pela `FrMailBoxDlg` do lobby,
  1 = My Room com uid 0xFFFFFFFF). 0x3C é o "relay do mensageiro pelo game server", usado quando o servidor de
  mensageiro não está conectado. Responder custa 10 pang no cliente ("쪽지를 보내기 위해서는 10팡이 필요합니다").
  Nick com '@' (operador) não recebe [C].
- Entrega: **S->C 0xB0** (lobbytask 5106) `u8 n, n × sNoteInfo (0x6D)` → `Doc.m_noteList` (+0x4D08) [C].
  Prioridade baixa: são bilhetes de texto, sem item.

## 3. Caixa de presentes antiga (`sGiftInfo`): legado no KR

| pacote | layout | quem usa | |
|---|---|---|---|
| C->S **0x92** | `u32 page` | aba de presentes do My Room (escondida com 0x60), OnUnderBar_GiftUp (só com 0x5B) e o próprio cliente depois de um 0x78 modo≠1 (page = ceil(total/perPage)) | [C] |
| C->S **0x93** | vazio | `_ReFreshMyRoomGift`, diálogo de item de nível (levelupitemdlg), 0x1A2 com 0x60 desligado | [C] |
| S->C **0x78** | `u8 mode, u16 perPage, u16 n, u16 total, n × sGiftInfo` | modo 1: grava em Doc+0x5044/48/4C, preenche m_giftList (+0x11D0), posta o **bit 8** de prontidão (0x2C) e, se a lista não estiver vazia, liga o alarme 0xAA. Modo ≠1: o cliente pede `0x92 ceil(total/perPage)` (OPC @0x738c41; RMT 5295) | [C] |
| S->C **0x108** | `u16 n, n × sGiftInfo` → m_sendGiftList (+0x11DC) | se Doc+0x5050 == 1, antes limpa e manda 0x92(1) | [C] |
| S->C 0x13D | `u8,u8,u16 n, n×sGiftInfo` | só com 0x5B (desligado) | [C] |
| C->S **0x24** | `u32 giftGuid, u32 tid` | "선물을 옮기시겠습니까?" (`CRealMyRoomMain::MoveGift`); saco de pang 0x1A000010 → `FrPangGiftDlg` mostra "pang atual + Arg0" | [C] |
| S->C **0xA4** | `i64 pangDelta, i64 cookieDelta, u32 giftGuid, u32 0` (0x18 bytes) | presente de pang: **soma** ao Doc e msg 0xD1 (tira da lista) (RMT 5675) | [C] |
| S->C **0x79** | `u32 code`; 0 → `u32 giftGuid, u32 newGuid, u32 tid, u16 n, u8 type`; 5 → `u32, u8 n, n × sBuyItemResult 0x26` (set) | erros: 1 "선물 옮기기가 실패", 3 "잘못된 상품 코드", 4 "이미 가지고 있는 아이템", 6 "선물이 존재하지 않습니다", 0xC "해당 캐디가 없습니다", 0xD "휴가중인 캐디…", 0xE "해당 캐릭터가 없습니다", 0x10 "기간제 아이템을 사용중", 0x11 "갯수제 아이템을 사용중" (tabela @0x5ba5f8) | [C] |
| C->S **0x4D** | `u32 giftGuid, u32 tid` (FrReturnGiftDlg +0x110/+0x114) | oferecido quando o item é **IsCash** (IFF+0x68 bit 0) e o jogador já tem um: devolve em vez de pegar | [C] |
| S->C **0xDA** | `u32 giftGuid` | 2× msg 0xD1 (remove) + destrava (RMT 5799) | [C] layout; o par 0x4D→0xDA é [R] (emu) |

Antes do 0x24 o cliente checa `ConfirmInsertItem`: "현재 아이템의 보유개수가 20,000개이므로…" [C]. Caixa de peça de
caddie com caddie de férias: "휴가중인 캐디입니다…" [C].
`CPost_ReciveDlg::OnMoveGiftDlgResult`/`OnReturnGiftDlgResult` (0x24/0x4D dentro do postbox) são **código morto**:
o ponteiro +0x1A0 nunca é preenchido e não existe `CreateForm<FrReturnGiftDlg>` no PRD [C].

**Recomendação:** continuar mandando o 0x78 vazio (modo 1) no login e como resposta a 0x92/0x93. Não guardar nada
em sGiftInfo: tudo que for "presente" vira carta.

## 4. Presentes que viram carta

| origem | fluxo no cliente | o que o servidor faz |
|---|---|---|
| **Loja, 0x1F** (`FrGiftDlg::GiftProgress`, giftdlg.c 2750) | `Str nickDestino, u32 uidDestino (vem do 0x9F), Str mensagem, u8 modo (0 carrinho / 1 item avulso do grupo 6), u16 n, n × sBuyItem 0x10`. Resposta **0x68** `u32 code`; 0 → `i64 pang, i64 cookie` (totais) + popup (shoptask 1321/2020). Com 0x60 ligado o cliente avisa o mensageiro com **0x27** (com 0x60 desligado seria 0x21) | debita do comprador, cria uma carta para o destinatário (remetente = nick do comprador, texto = mensagem, anexos = itens comprados com guid novo), 0x68 code 0 e 0x31 para o destinatário se estiver online. Hoje GameHandler.Shop responde 0x68 Fail [C fluxo; P conteúdo da carta] |
| **Caixa aleatória / especial, 0xF1 → 0x1A2** | o cliente diz "상품이 우편으로 전달되었습니다. 우편함을 확인하세요.", limpa m_mailList e manda C->S 0x15E (SPEC-caixas-reciclagem §2.3) | criar a carta com o prêmio **antes** do 0x1A2 e responder o 0x15E com a lista [C fluxo] |
| **Nível** (GameEnd) | o cliente manda 0x93 depois do diálogo | hoje vai direto para o inventário (GameHandler.GameEnd `LevelUpGiftsAsync`). Pode ir para o correio (fiel ao original, [P]) ou continuar direto |
| Eventos, GM, recompensas | o GB usa `MailBoxManager.sendMessageWithItem(0, uid, msg, item)` com remetente "@ADM"/sistema [R] | API interna `SendSystemMail(uid, texto, itens)` [P]. Nick começando com '@' = operador (no bilhete o cliente bloqueia a resposta) |

## 5. Comparação com as referências
- **GB** (temporada nova): o correio está em outros ids (C->S 0x143-0x147, S->C 0x210-0x215; 0x146-0x14B e 0x164-0x167
  nas listas). Só o **layout do brief** (`MailBox`: id, from[30], msg[80], unk[18], visit_count, lida_yn, item_num,
  item) bate com o 0xC2 do 645 até +0x88 [R]. O item do GB tem 55 bytes (com pang/cookie u64); o do 645 tem 53 (0x35),
  então use o do cliente. Regras úteis [R]: 20 por página, 300 não lidas no máximo, `lida_yn`/`visit_count`, "@ADM"
  para carta do sistema, e `putCommandNewMail` → `authCmdNewMailArrivedMailBox` avisa o jogador online (0x210 no GB = 0x31/0x15E aqui).
  Busca de nick: `Channel.requestCheckNick` responde `u8 err, u32 uid, MemberInfo`; no 645 o mesmo formato vai no 0x9F.
- **S6**: `PLAYER_OPEN_MAILBOX` etc. são stubs. Só o modelo de banco é útil: `Pangya_Mail` (Mail_Index, UID,
  Sender, Sender_UID, Receiver_UID, Msg, ReadDate, ReceiveDate, DeleteDate, RegDate) e `Pangya_Mail_Item` (Mail_Index,
  TYPEID, SETTYPEID, QTY, DAY, UCC_UNIQUE, ITEM_GRP, TO_UID, IN_DATE, RELEASE_DATE). Também confirma o bit de manutenção
  `MAINTENANCE_FLAG_MAILBOX = 1<<18` (= 0x40000) [R].
- **Emulador Python**: myroom.py responde vazio (0x140 1,1,0 / 0x143 1 / 0x15C / 0x145 1 / 0x13F 1); ext_lobby trata
  0x07→0x9F e 0xE6→0x15E vazio; SPEC-coverage já tinha 0x24→0xA4/0x79 e 0x4D→0xDA. Os layouts batem com este documento.
  Uma correção: no 0xBB o 1º Str é **vazio**, não o destinatário (quem vai é o `receiverUid`) [C].

## 6. Banco (PostgreSQL) sugerido [P]
```sql
create table mails (
    id              int primary key default nextval('object_id_seq'),  -- = sMailInfoBrief.id
    account_id      bigint not null references accounts(id) on delete cascade,   -- destinatário
    sender_id       bigint references accounts(id) on delete set null,           -- null = sistema
    sender_nick     text not null,                 -- congelado no envio ('@...' = sistema/GM)
    message         text not null default '' check (length(message) <= 800),
    created_at      timestamptz not null default now(),
    read_at         timestamptz,                   -- não lida = null
    visit_count     int not null default 0
);
create index mails_box_ix on mails (account_id, id desc);

create table mail_items (
    id              int primary key default nextval('object_id_seq'),
    mail_id         int not null references mails(id) on delete cascade,
    type_id         int not null,
    quantity        int not null default 1 check (quantity >= 0),   -- pang em 0x1A000010
    time_minutes    int not null default 0,        -- duração normalizada; convertida por grupo no pacote
    item_type       smallint not null default 0,   -- btItemType
    attrs           jsonb not null default '{}',   -- cópia dos attrs do item (upgrades, ucc, cores...)
    taken_at        timestamptz                    -- null = ainda na carta
);
create index mail_items_mail_ix on mail_items (mail_id);
```
- **Enviar** (uma transação): trava a linha do remetente; confere taxa e pang; para cada anexo confere dono, guid+tid,
  quantidade, IsSalable, não equipado e não card. Empilháveis: subtrai a quantidade. Únicos: copia os `attrs` para
  mail_items e apaga a linha de `items`. Debita a taxa. Insere a carta e os anexos. Depois: 0x13E (os itens que saíram) +
  0xC6 para o remetente e 0x31 para o destinatário online.
- **Pegar** (uma transação): para cada anexo com `taken_at` nulo: pang → `players.pang`; empilhável → soma na pilha
  existente (o guid da pilha vai em dwIDX e iCount = total); único → novo `items` (id novo = dwIDX); peça de caddie →
  dwIDX = guid do caddie dono. Se faltar o dono, ou se for item único que o jogador já tem (personagem/caddie), falha
  tudo com **0x145**. Marca `taken_at`. Envia **0x144** (+ os 20 bytes extras do 0x1A000042) e 0xC6 se houve pang.
- **Apagar**: só se todos os anexos tiverem `taken_at`, senão 0x15D(9). Apagar de verdade (ou `deleted_at`, se quiser
  histórico). Os ids de objeto saem da `object_id_seq`, a mesma dos itens, então o id da carta não colide com nenhum guid.
- Limites [P]: até 4 anexos por carta (o cliente não deixa mais; carta do sistema pode ter mais? o 0x142/0x144 aceitam n
  livre, mas mantenha ≤ 4 ou teste); caixa cheia → 0x13F(9) a partir de N cartas (sugestão 300, igual ao GB).
  Expiração: o cliente não tem campo de validade, então qualquer expiração (ex. 30 dias) é só do servidor.

## 7. Prioridades de implementação
1. **[C] Tabelas + listar/ler/apagar** (0xBC→0x140, 0xBD→0x142, 0xBE→0x15C/0x15D(9)), com 20 por página e brief 0xC2
   preenchido (id, remetente, bRead, itemCount, 1º anexo). O erro de qualquer um volta com u8 (0x141/0x143).
2. **[C] Pegar anexos** 0xBF→0x144 (conversões por grupo, total em empilháveis, guid do caddie no grupo 8, pang à parte
   com 0xC6), erro 0x145.
3. **[C] Aviso** 0xE6/0x15E(C->S) → S->C 0x15E com as não lidas; 0x15E espontâneo depois do login; 0x31 para o
   destinatário online ao entregar carta.
4. **[C] Envio entre jogadores** 0x07→0x9F (u8 0 + uid + sPangYaUserInfo, que já existe em Structs.g.cs) e
   0xBB→0x13E/0x13F, com todas as validações do §2.5 refeitas no servidor, taxa 100/500×n, e 0xC6.
5. **[C fluxo/P conteúdo] Presente da loja 0x1F** → carta para o destinatário + 0x68 code 0 (pang/cookie totais).
6. **[C] Caixa aleatória 0xF1/0x1A2**: entregar o prêmio por carta (o texto do cliente já manda olhar o correio).
7. **[P] `SendSystemMail`** para eventos/GM/nível (remetente "@...").
8. **[C] Legado**: manter 0x78 modo 1 vazio no login e para 0x92/0x93. Os handlers 0x24 (→0x79/0xA4) e 0x4D (→0xDA) só
   precisam existir se algum dia o 0x5B for ligado. Hoje basta responder 0x79 code 1 ou 0xDA com o guid, para não travar.
9. **[C] Bilhete 0x3C/0x111 → 0xB0** (sem item; prioridade baixa).
10. Nunca mandar o bit 0x40000 em `controlServerService` (0x42).
