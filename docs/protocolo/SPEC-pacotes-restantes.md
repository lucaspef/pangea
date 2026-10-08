# SPEC — pacotes C->S restantes (letreiro, Tiki Report, mascote PC-bang, partida rápida, ticket, 0x21/0x1E/0x89)

Cliente KR 645 QA. Pacotes que `tools/coverage.py` lista como não tratados e que podem importar para o jogador.
Marcas: **[C]** confirmado no cliente (decompilação Ghidra `gh/` = `/root/ghidra-out`, fonte reconstruída
`src/` = `/root/rebang/source/client/ProjectG`, `asm` = `/root/pg645.asm`, strings lidas do `.exe`);
**[R]** referência (GB = `/root/pangya-server/Server/GB`, emulador = `/root/pangya-server-work/emu`);
**[P]** palpite. "Doc" = `CSharedDoc`. Strings coreanas decodificadas de CP949.

Resumo rápido:

| C->S | O quê | Ligado no KR 645? | Prioridade |
|---|---|---|---|
| 0x67 / 0x66 | letreiro de uma linha (전광판), 1 cookie | **sim** (só bits de serviço do servidor) | **alta** |
| 0xA2 | sair do torneio usando "papel de Tiki Report" (0x1A000041) | **sim** (`S3_CADDIE_REPORT` 0x14); o papel já sai no Papel Shop do C# | média-alta |
| 0xA3 | abrir o Tiki Report gravado (0x1A000042) na My Room | **sim** (0x14) | média (depende do 0xA2) |
| 0x97 | mensagem do mascote PC-bang | só se o cliente for PC-bang **e** o mascote tiver `PCBangMascot` | baixa |
| 0xB7/0xB8/0xB6 | partida rápida / matching | conteúdo 0x59 ligado, **mas a janela não tem como abrir** | baixa |
| 0xE1 | troca de tickets (FrTicketExchangeDlg) | **não** (conteúdo 0x7A desligado) | nenhuma |
| 0x21 (game) | aviso "nick, uid" | **não** (único envio pelo game está atrás de 0x66 = loja offline, desligada) | nenhuma |
| 0x1E | "repay" na loja | **não** (`CShopMain::Repay` não tem chamador) | nenhuma |
| 0x89 | fechar navegador | **não** (conteúdo 0x15 desligado) | nenhuma |

Conteúdos KR (`/root/rebang/source/shared/localize_kor.h`): ligados 0x14 `S3_CADDIE_REPORT`, 0x3D `S3_TIKI_REPORT`,
0x59 `S4_MATCHING_SYSTEM`, 0x6C `S4_MASCOT_NO_LIMIT`, 0x9D; **não** ligados 0x15, 0x21, 0x66 `S4_OFFLINE_SHOP`, 0x7A [C].

---

## (a) Letreiro de uma linha — "전광판" (C->S 0x67 e 0x66)

### Como o jogador dispara [C]
Botão do letreiro em dois lugares:
- **Lobby/telas do CTaskMain** (barra superior): `CTaskMain::OnOverBar_OneLineReqBtnUp` (gh/taskmain.c:10481-10578).
- **Dentro da partida** (HUD): `CGolfRule::OnOnelineBtnUp` (gh/golfrule.c:7632-7680).

Pré-checagens no cliente:
1. `IsControlServerService(0x20000)` (bit do u32 `controlServerService` do **0x42**, Doc+0x4D68, gh/shareddoc.c:386,
   gravado em asm 0x734127) → caixa "전광판 관련 부분 점검중입니다." (letreiro em manutenção) e não abre [C].
2. Só no lobby: pergunta ao ator **Gateway** (msg 0x26D sub 0x10; mesmo teste do "trocar nick", gh/optiondlg.c:5795);
   se negar, manda sub 0x11 (aviso de restrição: a string do exe 0x610990 lista 전광판 entre as funções bloqueadas
   10 dias sem 본인인증) [C fluxo; P significado]. Como compra/presente/troca de nick já passam no C#, deve passar.
3. Só no lobby: `Doc[0x45F] & 0x10` → "현재 전광판 이용 제재중입니다. 전광판을 사용하실 수 없습니다." (sancionado).
   Esse u32 é gravado pelo **S->C 0x182 u32** (gh/lobbytask.c:5947-5951) [C]. **Bit 0x10 do 0x182 = punição do letreiro.**

Diálogo `oneline_req` (FrOnelineReqDlg, src/onelinereqdlg.cpp:135-171): "게임 내에서 유저간 소식을 전할 수 있는
광고입니다", "전광판에 글 등록 시 **1쿠키**가 소비됩니다", "한글 기본으로 최대 **20자**까지 (이모티콘 지원)",
"신청이 완료된 글은 수정, 취소가 불가능", "음란글, 욕설, 상업적 광고 → 제재", "청약철회 불가" [C].

### Passo 1: C->S 0x67 (pedir vaga) [C]
`OnOnelineReqDlgResult` (gh/taskmain.c:7488-7555; gh/golfrule.c:7561-7626): ao OK, o cliente
- passa o texto pelo filtro local (`ChatManager::Filtering`): se tiver palavra proibida → "적당하지않은 단어가
  포함되었습니다." e para; vazio → "보낼 내용이 없습니다." e para;
- guarda o texto em `COneLineBoard+0x5C` e manda **0x67 sem payload**.

**Resposta S->C 0xC8**: `u16 waiting, u32 waitMs` [C asm 0x73D4FB-0x73D51B: Decode2 + Decode4 → msg 0x5D].
`CTaskMain` msg 0x5D (gh/taskmain.c:13457-13490): grava os dois em `COneLineBoard+0x78/+0x7C`;
se `waiting > 0x3FF` → caixa de erro (fila cheia [R emulador]) e para; senão abre `oneline_chkout`
(FrOnelineReqChkOutDlg, src/onelinereqdlg.cpp:200-284): "전광판 신청을 하시면 **1쿠키**가 소비되며, 현재 %d명의 대기자가
있어 약 %d분뒤에 표시됩니다. 계속 진행하시겠습니까?" com `%d분 = waitMs / 60000`, "결제할 쿠키 1 / 보유한 쿠키 N /
남는 쿠키 N-1". **O botão "yes" fica desabilitado se `Doc.m_cookie < 1`** (src/onelinereqdlg.cpp:193-198).
O msg 0x5D só existe no CTaskMain; o CGolfRule chama métodos do CTaskMain (gh/golfrule.c:857-859), então dentro da
partida o mesmo diálogo aparece [P].

### Passo 2: C->S 0x66 (confirmar e pagar) [C]
`OnOnelineReqChkOutDlgResult` (gh/taskmain.c:4566-4592; gh/golfrule.c:4691-4718): resultado 1 → **0x66 `str texto`**
(o texto guardado no passo 1). Cancelar não manda nada.

### Resposta esperada
- **S->C 0x94 `u64 cookie`** (saldo novo; o cliente não desconta sozinho) [R emulador ext_game.py:294; C# já tem
  `SCookie = 0x94`].
- **S->C 0xC7 `str nick, str texto`** para **todos** os jogadores conectados (lobby, salas, partidas):
  `COneLineBoard::AddOnelineMsg` (src/onelineboard.cpp:142-166) monta `"texto  <nick>"` e o põe na fila de rolagem
  do letreiro [C; SPEC-chat-gm.md já documenta 0xC7]. O próprio cliente enfileira (cada mensagem entra 50 px depois
  da anterior) e rola 0,5 px a cada 25 ms ≈ 20 px/s (src/onelineboard.cpp:41-94), aparece no lobby e no HUD da
  partida (gh/golfrule.c:4607-4650).
- Não existe código de erro próprio no 0x66 [C]: recusa = não difundir e mandar só 0x94 com o saldo atual.

### Se o servidor ignorar
0x67 sem resposta: o diálogo de confirmação nunca abre (o jogador clica e nada acontece). 0x66 ignorado: nada
aparece e o cookie não é cobrado. Sem travamento.

### O que o servidor deve validar/gravar
- Jogador logado, `Cookie >= 1`; texto não vazio, limite de bytes (20 caracteres coreanos ≈ 40 bytes; aceitar até
  ~64 bytes por causa das tags de emoticon) e **refiltrar no servidor** (o filtro do cliente é burlável).
- Sanção: flag da conta (o mesmo bit 0x10 que vai no 0x182) → recusar.
- Debitar 1 cookie de forma atômica, gravar log/auditoria (quem, texto, hora) — o painel de administração (fase 9)
  pode listar e punir.
- Fila global [P]: guardar `(nick, texto, horaDeExibição)`; no 0x67 responder `waiting = mensagens ainda não
  exibidas`, `waitMs = tempo até a vaga` (ex.: 30 s por mensagem); no 0x66 agendar a difusão do 0xC7 na vaga.
  Versão mínima aceitável: responder `0xC8 0,0` e difundir na hora (o cliente já enfileira) — é o que o emulador faz.
- Configurável: custo (o texto do cliente diz 1 fixo — não mudar), tamanho da fila (≤ 0x3FF), segundos por mensagem.
- Manutenção: para desligar o letreiro, ligar o bit 0x20000 do `controlServerService` no 0x42.

---

## (b) Tiki Report — abrir o relatório (C->S 0xA3)

### O que é [C + R]
"Tiki Report" (티키 리포트) é o **registro de um torneio de até 30 jogadores**. Dois itens:
- **0x1A000041 = papel (용지)**: quem tem o papel pode **sair do torneio antes do fim** (depois de terminar os
  próprios buracos) sem perder o resultado → C->S **0xA2** (item (c)).
- **0x1A000042 = relatório gravado**: entregue quando o torneio acaba; abrindo-o na My Room mostra o placar final
  (FrUniteResultDlg) e, no GB, **credita a EXP** que o jogador ganhou [R GB ItemManager.openTicketReportScroll].

O C# já sorteia 0x1A000041 no Papel Shop (`src/Pangya.Core/Config/PangyaConfig.cs:230`), então jogadores podem tê-lo.

### Como o jogador dispara [C]
My Room → clicar no item 0x1A000042 (gh/realmyroommainui.c:33115-33119) → `OpenTikiReport` (gh/realmyroommainui.c:16091-16133),
só com `IsLocalContent(0x14)`:
- se o mesmo item já está pendente → "유효하지 않은 티키 리포트 입니다." (local);
- senão guarda `itemGuid = sItemInfo+0`, `reportId = short@0x0E * 0x8000 + short@0x10` e pergunta
  "30인 대회 기록인 티키 리포트를 열람 하시겠습니까?".
- Sim → `OnOpenTikiReport` (gh/realmyroommainui.c:10557-10610): **C->S 0xA3 `u32 itemGuid, u32 reportId`** e trava a
  tela (msg 1) até a resposta.

### Resposta S->C 0x116 [C gh/realmyroomtask.c:5879-5966]
`u32 n, SYSTEMTIME data (16 B), n × sCaddieReportData (0x7F B)`; destrava a tela (msg 2) e limpa a lista.
- `n == 0` → "티키 리포트 열람에 실패 하였습니다."; `n == 0xFFFFFFFE (-2)` → "데이타가 유효하지 않은 티키 리포트
  입니다."; `n == 0xFFFFFFFF (-1)` → "날짜가 유효하지 않은 티키 리포트 입니다." [C strings; ordem -2/-1 R emulador].
- `n > 0`: o cliente **desconta o item 0x1A000042 localmente** (apaga quando chega a 0, msg 0xDD), lê as n entradas
  para `Doc+0x4514`, `BuildTikiReportList` e abre o resultado (msg 0x204/0x205; `FrUniteResultDlg::SetTikiReportState`
  usa a data, gh/resultdlg.c:2605-2637).

`sCaddieReportData` (0x7F, empacotado; src/shared/globalgamedefine.h:1667) [C]:
`+00 u32 uid, +04 i64 pang, +0C i64 bonusPang, +14 u32 roomType, +18 u32 exp, +1C u32 mascotTypeId, +20 u8 premium,
+21 i8 pangItem, +22 u16 level, +24 2B, +26 i8 score, +27 u8 awardFlag (troféu), +28 u8 eventType, +29 22B,
+3F char nick[22], +55 4B, +59 u32 guildUID, +5D char guildMark[12], +69 u8 gameType, +6A 3B, +6D u8 team,
+6E u8 eventFlag, +6F 16B`.

### Se o servidor ignorar
A My Room fica travada esperando o 0x116 (tela bloqueada pelo msg 1) [P: até sair da My Room]. Hoje o emulador
responde `n = 0` (falha, item mantido).

### O que validar/gravar
- O item existe, é 0x1A000042, é do jogador, e `reportId` bate com o do item (GB usa `c[1]*0x800|c[2]` — bug; o
  cliente usa **0x8000**).
- Validade: GB cria o item com **24 horas**; vencido → `-1` (data inválida) e, no GB, ainda dá a EXP [R].
- Relatório inexistente → `-2`.
- OK: apagar o item no servidor (o cliente já apaga), mandar o 0x116 e **depois** creditar a EXP guardada
  (GB credita depois do pacote para o visual não somar duas vezes) [R].

---

## (c) "Relatório de uso de sala" = sair do torneio com papel de Tiki Report (C->S 0xA2)

### Como o jogador dispara [C]
`CLobbyMain::CheckTikiReport` (src/lobbymain.cpp:15189-15236) ao abrir o layout **GAMEROOM_EXTRES** (jogador terminou
o torneio e espera os outros). Liga `m_bUseReport` se: tem 0x1A000041, **não** é GM/identidade 2/4/8, modo ≠ guild
match, `nUserLimit ≤ 30`, canal sem flag 0x800, nível > 5 (≥ Beginner E). Ao clicar em sair
(`GameExtResQuit`, src/lobbymain.cpp:12013-12040; `realGameType != 14`):
- com papel: "티키 리포트 용지를 사용하시겠습니까?";
  - **Sim** → `OnRoomUseReportDlgResult` (src/lobbymain.cpp:6513-6550): desconta 1 papel localmente, manda
    **C->S 0xA2 `sPangYaUserStatistics` (0xEB = 235 B, `Doc.m_holeStatistics[0]`)**, desabilita OK,
    `m_bGameOver = false`, msg 1 (trava);
  - **Não** → diálogo normal de desistência.
- sem papel: "게임이 완전히 종료되기 전에 게임을 포기하면..." → **C->S 0x0F** `u8 0, u16 0xFFFF, i64 totalPang,
  i64 totalBonusPang` (src/lobbymain.cpp:6553-6600).

Ou seja: **0xA2 substitui o 0x0F** quando o jogador usa o papel.

### Resposta esperada [R GB + C]
GB (`Tourney.requestUseTicketReport` + `Channel.requestUseTicketReport` → `leaveRoom(_, 10)`):
1. Confere: jogador terminou o torneio (flag FINISH), nível ≥ Beginner E, tem o papel.
2. Tira 1 papel e manda a atualização do item (GB 0xAA; no KR o equivalente é **0xA5** contagem / 0xA8) [R/P].
3. Marca o jogador como "saiu com Tiki Report", guarda os dados dele no placar, **tira-o da sala** como numa
   saída normal (no KR: `LeaveRoom` → 0x4A para ele).
4. Para os que continuam: GB 0x61 (sai do placar) + GB 0x11B = **KR S->C 0x117 `u32 uid`** — tratado em
   `CLobbyTask::OnPacket` (gh/lobbytask.c:5504-5530): marca o rival como estado 2 e atualiza a lista (msg 0x2A) [C].
5. Fim do torneio: grava um relatório (id, data, todos os jogadores: uid, exp, pang, bonus, mascote, flags, medalha,
   premium, score, estado, troféu, tempo) e, para cada um marcado, cria **0x1A000042** com `c[1] = id / 0x8000`,
   `c[2] = id % 0x8000`, validade 24 h (online: item + aviso; offline: só grava) [R GB requestSaveTicketReport /
   requestSendTicketReport]. A EXP desse jogador é paga ao abrir o relatório (item (b)).

### Se o servidor ignorar
O cliente já descontou o papel e travou a tela (OK desabilitado): o jogador fica no EXTRES até o torneio acabar
[P]; no próximo login o papel "volta" (não foi apagado no servidor). Não perde nada, mas é confuso.

### O que validar/gravar
- **Não confiar** no `sPangYaUserStatistics` do cliente para recompensas (usar o placar do servidor); no máximo
  guardar para o relatório.
- Se qualquer checagem falhar: tratar como 0x0F (sair normal) e reenviar a contagem do papel (0xA5) para o cliente
  corrigir o desconto local — senão ele fica preso.
- Persistir: tabela de relatórios (id, data, sala/curso) e linhas por jogador (estrutura acima); item 0x1A000042
  com o id nos campos `c[1]/c[2]` e validade.

---

## (d) Mensagem do mascote PC-bang (C->S 0x97)

### Como o jogador dispara [C]
1. **Manual**: My Room → info do mascote → mudar mensagem → confirmar ("마스코트 메세지를 변경 하시겠습니까?").
   `FrMascotInfoDlg::OnChangeMsgConfirmDlgResult` (src/mascotinfodlg.cpp:116-141): se
   `CLoginInfo::IsPcBang()` **e** `sMascotInfo.PCBangMascot` → **0x97 `u8 1, u32 mascotGuid, str msg`**; senão o
   normal **0x73 `u32 guid, str msg`** (o C# já trata, GameHandler.MyRoom.cs:244).
2. **Automático**: ao receber a lista de mascotes (S->C 0xDF), para cada `sMascotInfo` com `PCBangMascot` (byte +0x3E)
   o cliente põe uma mensagem padrão e manda **0x97 `u8 2, u32 guid, str msg`** (asm 0x736D46-0x736E3C) [C layout;
   P origem do texto padrão].

`IsPcBang()` vem do login do lançador; o web do C# manda `PCBangNo=0` (src/Pangya.Web/WebServer.cs:132) e o C#
manda `PCBangMascot = 0` (Structs.g.cs:236) → **hoje o 0x97 nunca é enviado** [C/P].

### Resposta esperada
Não há resposta própria conhecida [P]. Para `kind 1` responder igual ao 0x73: **S->C 0xE0 `u8 4, u32 guid, str msg,
u64 pang`** (erro: `u8 1, u32 guid`, como o C# já faz); GB 0xE2 é o mesmo pacote [R GB requestChangeMascotMessage].
`kind 2`: só gravar (silencioso).

### Se ignorar
Mensagem não muda no servidor; nada trava (o diálogo já fechou).

### Validar
Mascote do jogador, texto 1..30 bytes (GB), filtro; PC-bang não cobra pang [P].

---

## (e) Partida rápida / matching (C->S 0xB6, 0xB7, 0xB8)

### Fluxo real no cliente [C]
`S4_MATCHING_SYSTEM` (0x59) está ligado, e todo o protocolo existe:
1. **Interesses**: `FrQuickStartSettingDlg::UpdateData` → **0xB6 `u8 n, n × u8 código, u8 1`**
   (gh/dlgquickstartsetting.c:2922). Servidor guarda e devolve **S->C 0x137 `u8 n, n × u8`** (lista "meus
   interesses", gh/lobbytask.c:5890-5905; pode ir no login).
2. **Pedir**: `FrQuickStartDlg` botões Stroke/Chat/Torneio/Batalha → `SendRequestMatching(tipo)`
   (gh/dlgquickstartsetting.c:1535-1560, 1704-1770): **0xB7 `u8 tipo`** com tipo 0 = stroke, 2 = chat, 4 = torneio,
   10 = pang battle; marca `Doc+0x5CC0 = 3` (ocupado em matching).
3. **Convite ao alvo**: **S->C 0x132 `u32 uidPedinte, str nickPedinte, u8 tipo`** (gh/lobbytask.c:5689-5712) → msg
   0x21C (src/lobbymain.cpp:4920-4950): se o alvo está livre (`m_userInfoMod == 0` e sem diálogo), mostra
   "(%s)님께서 %s모드 매칭 요청을하셨습니다. 매칭에 응하시겠습니까?" por **5 s**; senão responde recusa na hora.
4. **Resposta do alvo**: `CLobbyMain::OnMatchingDlgResult` (gh/lobbymain.c:13917-13990): **0xB8 `u32 uidPedinte,
   u8 aceita`** (1 sim, 0 não/tempo esgotado; tempo esgotado ainda mostra "매칭이 취소되었습니다.").
5. **Resultado ao pedinte**: **S->C 0x133 `u8 código`** (gh/lobbytask.c:5713-5815) → msg 0x21D: código 0 =
   sucesso (param 1, o botão OK continua desabilitado e o texto não muda); 1 "매칭 대상이 없습니다. 잠시후 다시
   시도해 주시기 바랍니다.", 2 "대상이 매칭을 거절하였습니다.", 3 "모든 대상이 매칭을 거절하여...", 4
   "환상의 팡야섬에서는 매칭이 불가능합니다.", 5 "매칭을 실패하였습니다.", 6 "매칭을 요청한 상대방이 매칭 가능한
   위치에 없습니다." [C strings; ordem R emulador]. Todos zeram `Doc+0x5CC0`. **S->C 0x136** (sem payload) = erro com
   texto fixo ("매칭이 취소되었습니다. 잠시후 다시..." [P]). **S->C 0x134 `u8, u8`** é lido e ignorado.
6. **Levar à sala**: **S->C 0x135 `u32 serverUid, u8 canal, u16 sala`** (gh/lobbytask.c:5816-5870): mesmo servidor →
   o cliente manda sozinho **0xAC `u8 canal, u16 sala`** (que o C# já trata = entrar na sala); outro servidor →
   reconecta (0x43 + `SetDirectMoveRoomInfo`).

### Problema: a janela não abre no 645 [C]
- `CLobbyMain::OnQuickStartDlgResult` (que abriria "quick_result" com "매칭 대상을 검색 중입니다.") **não tem
  nenhuma referência** no executável; não existe o nome de formulário "quick_start" nas strings (só
  "quick_start_setting", aberto pelo próprio FrQuickStartDlg, gh/dlgquickstartsetting.c:3153); `FrQuickStartDlg` só é
  registrado na fábrica de classes. Não achei caminho de UI que mande 0xB7 no 645.
- O "partida rápida" que o jogador vê é outro: a sala criada automaticamente após o erro **12** do **0x47**
  ("nenhuma sala adequada", src/lobbymain.cpp:4610-4690 → 0x08 de stroke/torneio/etc.). No 645 `MakeRoom` só é
  chamado com `quick = false` (gh/lobbymain.c:14703), então o servidor não precisa gerar o código 12.

### O que o servidor deve fazer
- Manter o atual (0xB7 → `0x133 u8 1` "sem alvo"; 0xB6/0xB8 engolidos) é **correto e suficiente** para o 645.
- Se um dia a janela for alcançável (outro cliente): alvo = jogador online no lobby do mesmo servidor, livre
  (sem sala, sem diálogo), com interesse compatível, diferente do pedinte e fora do canal "환상의 팡야섬" (código 4);
  mandar 0x132 ao alvo e esperar 0xB8 por ~6 s; recusa → tentar o próximo (todos recusaram → 3; nenhum → 1);
  aceite → criar a sala do tipo no servidor, mandar 0x133 0 ao pedinte e **0x135** (mesmo servidor, canal, sala)
  aos dois, que entram via 0xAC [P].

---

## (f) Troca de tickets (C->S 0xE1, FrTicketExchangeDlg)

[C] Botão do topo `CLobbyMain::OnToppage_TicketExchangeDown` (gh/lobbymain.c:5279-5300) só abre com
`IsLocalContent(0x7A)` — **desligado no KR** → o 0xE1 nunca é enviado. Para referência:
- **0xE1 `u32 tipo [, u32 tid, u32 quantidade]`** (src/frticketexchangedlg.cpp:243-257): 0x11110000 = pedir estado
  (ao abrir); 0x10000 = sorteio (응모); 0x100000 / 0x1000000 = troca direta. Itens fixos no cliente
  (src/frticketexchangedlg.cpp:1680-1711): tids 0x3B9AC9F7/F8 (sorteio, 1 ticket), 0x1A00000E, 0x18000006, 0x1A000011,
  0x1A000040 (5 tickets), 0x1A0000BC, 0x1A000030 (3 tickets).
- **S->C 0x18C `u8 resultado, u32 tipo, ...`** (gh/lobbytask.c:5952-6040): 0x11110000 → `u32 tickets, u8 estado,
  n × {u32 tid, u32 count, u32 remain}`; 0x10000 → `u32 tickets, u32 tid, u32 count, u32 remain`; 0x100000 →
  `u32 tickets, u32 tid, u32 remain`; 0x1000000 → `u32 tickets`. Textos: 0 "응모 하였습니다." / "지급 하였습니다.
  우편함을 확인해 보십시오."; 2 "해당 아이템의 재고가 없습니다."; 4 "교환 가능한 티켓이 적습니다." (src/frticketexchangedlg.cpp:477-523).
- Servidor: engolir com log.

## (g) C->S 0x21 no game

[C] O aviso `str meuNick, u32 meuUid` vai para o **mensageiro** (`WSendPacket::Send(1)`) em
`CTask::_ReFreshMyRoomGift` (gh/taskmanager.c) e nos casos do OnPacketCommon (asm 0x743301, 0x7438BF; o de 0x743BBA
monta e não envia). `WSendPacket::Send` (asm 0x77C7A0) **não tem fallback** para o game. O único envio para o
**game** (`Send(0)`, asm 0x73E2E8-0x73E3A7) fica atrás de `IsLocalContent(0x66)` (loja offline, **desligada**): ao
receber o aviso de venda (u32 1, u64) o cliente avisaria o game. → No 645 o game nunca recebe 0x21. A descrição
antiga "quando o MSN está fora" não se confirma.
Servidor: engolir com log (se chegar, não há resposta esperada).

## (h) C->S 0x1E (shopmain) e 0x89 (CBrowser::Close)

- **0x1E** `u8 grupo (0 se tid>>26 == 1, 1 se == 7, senão 2), u8 1, u32 tid, u32 guid` — `CShopMain::OnSellDlgResult`
  (gh/shopmain.c:3055-3105), resposta do diálogo `repay` aberto só por `CShopMain::Repay` (gh/shopmain.c:5317-5338),
  que **não tem nenhuma chamada** no executável (0 xrefs em asm) → nunca enviado [C]. O diálogo mostra `UsedPrice`
  como custo (falta de pang = "부족"), então seria "pagar de novo/renovar", não vender [P]. Engolir.
- **0x89** sem payload — `CBrowser::Close` (src/browser.cpp:157-175) manda 0x3D (sempre, já tratado), 0x72 (com
  `S3_SCRATCH`, ligado, já tratado) e 0x89 só com conteúdo **0x15** (desligado no KR) → nunca enviado [C]. Engolir.

---

## Plano de implementação (priorizado pelo valor para o jogador)

1. **Letreiro (0x67/0x66)** — visível para todos, barato.
   - 0x67 → `0xC8 u16 fila, u32 msAtéAVaga` (fila global simples; mínimo `0,0`).
   - 0x66 → validar/filtrar/limitar, debitar 1 cookie (atômico, `PlayerActions`), `0x94 u64 cookie`, difundir
     `0xC7 nick, texto` para todas as sessões do game (`ctx.World.Online`), gravar auditoria.
   - Opcional: flag de punição → bit 0x10 no `0x182`; manutenção → bit 0x20000 no 0x42.
   - Testes: sem cookie (só 0x94), texto vazio/longo/proibido, difusão para lobby e partida.
2. **Saída de torneio com Tiki Report (0xA2) + relatório (0xA3)** — o papel 0x1A000041 já é prêmio do Papel Shop.
   - Passo mínimo (evita jogador preso e item fantasma): tratar 0xA2 como o 0x0F do EXTRES e reenviar a contagem do
     papel (0xA5) sem descontar; 0xA3 → `0x116 u32 0 + 16 zeros` (falha, item mantido).
   - Completo: descontar o papel, marcar o jogador, `0x117 u32 uid` aos que ficam, gravar relatório no fim do
     torneio (tabela nova), dar 0x1A000042 (24 h, id em c[1]/c[2]) a quem saiu com papel, 0xA3 → 0x116 com as
     linhas e EXP creditada ao abrir.
3. **Mascote PC-bang (0x97)** — só se o projeto ligar PC-bang; tratar `kind 1` como o 0x73 e `kind 2` silencioso.
4. **Engolir com log** (para a cobertura ficar limpa, sem mudar comportamento): 0xE1, 0x21, 0x1E, 0x89.
5. **Partida rápida**: manter "sem alvo"; reavaliar só com um cliente onde o FrQuickStartDlg abra.

## Riscos
- **Abuso do letreiro**: spam/ofensa visível para todo o servidor. Refiltrar no servidor, limitar por tempo por
  conta, logar e permitir punição (bit 0x10 do 0x182). O custo de 1 cookie é fixo no texto do cliente.
- **Fila/tempo**: `waiting > 0x3FF` dá erro no cliente; manter a fila pequena. Difundir 0xC7 para sessões ainda
  carregando (antes do lobby) pode ser ignorado pelo cliente [P].
- **0xA2 confia no cliente**: o `sPangYaUserStatistics` vem do cliente; usar só o placar do servidor para EXP/pang.
  O cliente desconta o papel antes da resposta: toda recusa precisa reenviar a contagem.
- **0x117/0x116**: o 0x117 só é lido no `CLobbyTask` (quem ainda está jogando pode não ver) [C/P]; o 0x116 com
  `n > 0` faz o cliente apagar o 0x1A000042 — apagar também no servidor na mesma operação.
- **Ordem -1/-2 do 0x116** vem do emulador (strings confirmadas, mapeamento não verificado no asm).
- **Gateway 0x26D sub 0x10** (lobby) não foi decompilado: se em algum cenário negar, o letreiro no lobby mostra o
  aviso de "본인인증" e não abre — testar no cliente.
- Partida rápida: conclusão "UI inalcançável" vem da ausência de referências no executável; layouts nos `.pak`
  podem, em teoria, instanciar `FrQuickStartDlg` pelo nome da classe [P baixo].
