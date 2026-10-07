# Modo Ghost (KR 645 QA) — o que o servidor precisa fazer

Escopo: `eGameType GAME_TYPE_OFFLINE_GHOST = 13` (`shared/globalgamedefine.h:18`) e as unidades do cliente
`ghostmain`, `ghosttask`, `ghostgamedlg`, `ghostplayer`, `ghosthandler`, `ghostdocument`, `ghostmanager`.

Fontes: `rb/` = `/root/rebang/source/client/ProjectG/` (C++ reconstruído), `gh/` = `/root/ghidra-out/` (pseudo-C),
`map` = `/root/rebang/build/projectg/ProjectG_ReleaseQA.map`, `exe` = `/root/rebang/tools/original/ProjectG_ReleaseQA.exe`
(byte-a-byte igual a `E:\dev\pangya-test\KR642\ProjectG_ReleaseQA.unpatched.exe`; as cópias `LocalServer*_ReleaseQA.exe`
diferem só em ~100 bytes de patch de IP/URL/shader). Endereços são VA (base 0x400000).
[V] = lido no código/disassembly, [I] = inferido, [P] = palpite.

## Conclusão (TL;DR)

**No 645 QA o modo Ghost é código morto: não existe caminho de UI nem de pacote que o alcance.** [V]

- Nenhuma instrução do exe chama (call/jmp rel32, nem ponteiro imediato) os pontos de entrada:
  `CGhostHandler::RequestGhostGameList` @005f1f50, `OnGhostPacket` @005f2540, `OpenEventGhostGameDlg` @005f2480,
  `IsAvailableGhostSystem` @005f1750, `IsExistEventGhost` @005f2b20, `RecordComplete` @005f2ba0,
  `UploadComplete` @005f1a90, `ExceptionFinishGame` @005f1e90 — varredura de todo rel32 de todas as seções do exe
  apontando para esses endereços: 0 ocorrências. (Controle: `SendErrorReport` @005f1510 tem 3 chamadores, então a varredura funciona.)
- O singleton `WSingleton<CGhostHandler>::m_pInstance` (@00af659c, map:67627) só é lido por código das próprias
  unidades ghost (`CGhostMain::HandleMsg`, `CGhostMain::OnDownStart`, `CGhostPlayer::OnProcess`), por construtor/destrutor
  e por `DeleteSingletonClasses`. Nenhuma task de lobby/sala/golfe despacha pacote para `OnGhostPacket`.
- O único botão "ghost" da UI fora das unidades ghost é o da janela de ranking: `FrRankingDlg::OnGhostBtnInit` @00857910
  esconde e desabilita o botão (`gh/rankingdlg.c:3069`), e `OnGhostBtnUp` @00858f00 só abre um aviso
  "서비스 준비 중입니다." ("serviço em preparação", string @00a09c2c) (`gh/rankingdlg.c:4430`).
- A `CGhostTask` só é aberta por: `CGhostHandler::LoadingComplete` @005f1910 (chamada só por
  `CGhostManager::LoadDocumentFromLocalFile` @005f86c0 ← `OnResultEventGhostGameDlg` ← `OpenEventGhostGameDlg`, sem chamador),
  `ExceptionFinishGame` (sem chamador), e por caminhos de "voltar" que exigem já estar no modo ghost
  (`CTaskMain::OnUnderBar_BackUp` se a task anterior era `CGhostTask`, `gh/taskmain.c:8665/8836`; `CGolfRule::ReturnLobby`/`DoGameOver`
  e `FrQuitForm::OnToLobbyConfirmResult` se `doc+0x4560 == 5`, `gh/golfrule.c:14130/15600`, `rb/quitwindow.cpp:429`).
  `doc+0x4560 = 5` só é escrito em `CGhostMain::Initialize` (`gh/ghostmain.c:4830`), i.e. dentro da própria `CGhostTask`.
- Os atores de jogo `CGhostRecorder` / `CGhostPlayer` só são registrados na fábrica de objetos (strings @00a04598 / @00a05180
  referenciadas apenas pelos inicializadores estáticos @009a69e6 / @009a8ee6); nenhuma task faz `AddActor` deles.
- Mesmo o download HTTP está quebrado: no `_RealDownLoad` @0071bc60, caso `RESOURCE_GHOST`, os bytes baixados são
  descartados (`delete[] data`, @0071bd51) e nada chama `LoadingComplete` (`rb/netresourcemanager.cpp:5912-5927`).

**Implementação mínima do servidor: nada.** O cliente 645 nunca envia os pacotes ghost nem faz as requisições HTTP ghost,
e nenhuma resposta do servidor consegue abrir o modo. Recomendação: não implementar; no máximo ignorar silenciosamente
um C→S `0xBA` (nunca chega na prática) e manter `serverProperty` bit 0x800 = 0 (já é 0). O resto deste documento registra
o protocolo "como projetado" caso alguém algum dia faça patch de **código** (não só de dados) no exe para ligar o modo.

## Condições de habilitação (gate) [V]

- `IsLocalContent(0x5d)` = `S4_GHOST` (`shared/localize.h:59`) — **ligado no KR** (`shared/localize_kor.h`, linha `S4_GHOST`).
- `serverProperty` (doc+0x4afd, u32; vem do pacote 0x44 de login, ver SPEC-game.md:13) bit 0x800 tem de ser 0:
  `IsAvailableGhostSystem` = `S4_GHOST && !(prop & 0x800)` (`gh/ghosthandler.c:680-690`); `RequestGhostGameList` e
  `OnGhostPacket` testam `doc[0x4afe] & 8` (= mesmo bit) (`gh/ghosthandler.c:1151, 1567`).
- `IsLocalContent(0x59)` = `S4_MATCHING_SYSTEM` (ligado no KR) só decide se o cliente manda `0xB9` antes/depois.

## Fluxo projetado (UI) [V nas funções, I na ligação entre elas]

1. Algum botão (inexistente no 645) chamaria `RequestGhostGameList(uid)`: envia `0xB9 {u8 2}` (se S4_MATCHING_SYSTEM) e
   `0xBA {u8 0, u32 uid}`; põe a task em modo "aguardando" (`gh/ghosthandler.c:1123-1201`).
2. O servidor responderia com o sub-op ghost `0x0C` (lista de replays) → `OpenGhostGameDlg` abre o form `GHOST_GAMEMAP`
   (`FrGhostGameMapDlg`, mapa de campos com os replays disponíveis). Lista vazia (`count == 0`) → aviso
   `NOTIFY_MSG_GHOST_NOSAVE` (`gh/ghosthandler.c:1589-1594`).
   Alternativa local: `OpenEventGhostGameDlg` lê `event_ghost.xml` (`<active uid="..."><element uid nick><param file="X.GST"/>…`)
   e carrega o `.GST` local sem HTTP (`gh/ghosthandler.c:1249-1426, 1464`).
3. Escolhido um item: `OnResultGhostGameDlg` monta o nome `"%d_%d_%d_%d_%d_%d.GST"` com os 6 primeiros u32 do item e chama
   `CGhostManager::LoadDocument` → download HTTP (abaixo) (`gh/ghosthandler.c:914-1021`, `rb/ghostmanager.cpp:63-81`).
4. Download concluído → (projetado) `CGhostLoader::LoadFromMemory` + `LoadingComplete(true)` → `ChangeTask("CGhostTask")`,
   tela `CGhostMain` (fundo `ghost_main_bg.jpg`, layout `GHOST`: equipamento do jogador vs. do ghost, escolha de pet, "start").
   Ao entrar: `0xB9 {u8 2}` (`gh/ghostmain.c:4833`) e `0xBA {u8 1, …}` (`OnInitGhostFinish`).
5. "Start" (`CGhostMain::OnDownStart` @005f7d00): monta localmente 2 slots (eu + ghost convertido de `GHOST_USER_DATA`),
   `SetCurMap(header.m_map)`, game type 0x12 em doc+0x476d, copia par/seeds dos 18 buracos do arquivo, `ChangeTask("CGolfTask")`
   e envia `0xBA {u8 2}`. **O jogo é offline/local** (não há sala no servidor; `IsMyTurn` tem ramo próprio p/ `0x4560 == 5`,
   `gh/golfdoc.c:14810`). O `CGhostPlayer` reproduz as tacadas gravadas.
6. Fim: `CGhostPlayer::SendAwardResult` envia `0xBA {u8 6, …}`; sair da tela ghost envia `0xBA {u8 0x0A, …}` e `0xB9 {u8 1}`,
   volta para `CLobbyTask` (`gh/ghostmain.c:1836-1905`).
7. Gravação (lado de quem joga normal): `CGhostRecorder` grava se `IsSaveAvailable` (doc[0x4668] ∈ {0,4}, doc[0x4564] ≠ 4,
   doc[0x4a49] ≠ 0xE, doc[0x476d] == 0x12) (`gh/ghosthandler.c:616-655`); `RecordComplete` manda `0xBA {u8 4, …}` só se o
   score final < 0 (abaixo do par); o servidor responderia `0x0D {str nome}` → `SaveDocument` exporta e faz upload HTTP.

## Pacotes C→S (todos com id 0xBA + u8 sub-op, exceto onde dito) [V]

Strings = formato padrão do cliente (`EncodeStr`, u16 tamanho + bytes, como nos outros SPEC-*).

| sub | layout depois do sub-op | origem |
|-----|--------------------------|--------|
| 0x00 | u32 uid (dono da lista pedida) | `RequestGhostGameList` @005f1f50 |
| 0x01 | u32 meuUid (doc+0x1f6d), str meuNick (doc+0x1e7a), u8 (CGhostMain+0x40c, [P] índice do pet escolhido) | `OnInitGhostFinish` @005f7ac0 |
| 0x02 | — (início da partida contra o ghost) | `OnDownStart` @005f7d00 |
| 0x03 | u32 typeId do item usado (ex. 0x1a000040, 0x1a000011) | `CGhostPlayer::HandleMsg` @005fb5f0 |
| 0x04 | u8 mapa, u8 doc[myIndex*0xB92+0x1c3c], u32 \|score\|, u32 pang (decodificado `~(a^b)`) | `RecordComplete` @005f2ba0 |
| 0x05 | str nomeArquivo (upload concluído) | `UploadComplete` @005f1a90 |
| 0x06 | u32 ghostUid (GHOST_USER_DATA+0x254 = m_uid), u32 m_index do arquivo carregado, u8 mapa, u32 ×3 e u32(byte) de estado do CGhostPlayer, u32 score jogador0, u32 score jogador1, u32 pang0, u32 pang1, u8 doc[0x1fbb], u8 doc[0x1429], u8 buracos jogados (1..18, 0x13 = todos) | `SendAwardResult` @005faff0 |
| 0x08 | str nomeArquivo, str textoErro ("GHOST FILE LOAD FAILED", "GHOST FILE UPLOAD FAILED.") | `SendErrorReport` @005f1510 |
| 0x09 | u8 (dlg+0x18c), u32 item[7] (último u32 do item da lista), u32 uid (dlg+0x13c) — pede histórico | `FrGhostGameMapDlg::OnDownRecentMatch` @005efea0 |
| 0x0A | u32 meuUid, u8 (CGhostMain+0x40c) — saiu da tela ghost | `CGhostMain::OnExit` @005f4460 |

Outros enviados pela tela ghost (pacotes comuns, já existentes no servidor): `0xB9 {u8 1|2}` (estado de tela; também usado por
lobby/ranking/loja), `0x0B {u8 tipo, u32 item}` (troca de equipamento, `SendPacketEquipResult` @005f39f0), `0x20 {u8 2, buffer}`
(`CallBackEquipItemResult` @005f49d0).

## Pacotes S→C (sub-ops lidos por `OnGhostPacket` @005f2540) [V layout, sem id de pacote]

`OnGhostPacket` lê `u8 sub` e despacha; **nenhum `OnPacket` de task a chama**, portanto não há id S→C que a alcance no 645
(nos servidores de referência JP o par é C→S 0xC2 / S→C 0x143 — ids diferentes, handler vazio). Layouts:

- **0x0C lista**: u32 count, u32 ownerUid, count × { u32 a, u32 b, u32 c, u32 d, u32 e, str nick, u32 f }.
  Item na lista = 7 dwords `{ownerUid, a, b, c, d, e, f}` (disassembly @005f263e-005f26db: o 1º dword é o `ownerUid` do
  cabeçalho, igual para todos). O nome de arquivo baixado = `"%d_%d_%d_%d_%d_%d.GST"` dos dwords 0..5, enquanto
  `GHOST_FILE_INFO` interpreta como `date_time_course_score_uid_index` (`rb/ghostdocument.h:133`) — inconsistente [P: bug ou
  convenção do servidor original desconhecida]. O `nick` passado ao diálogo é o do último item. count 0 → aviso "sem gravação".
- **0x0D salvar**: str nomeArquivo → `CGhostManager::SaveDocument(nome)`: exporta o `.GST` e põe na fila de upload.
- **0x0E descartar**: libera o documento gravado e apaga `.\temporary_save\*.GST`.
- **0x10 erro**: u8 código: 0x19 → `NOTIFY_MSG_GHOST_NODATA`; 0x1A, 0x1C → outros avisos; demais ignorados.
- **0x13 item**: u8, u32 typeId, u32 qtd, u8; se `!(typeId & 0x2000000)` manda MsgObject 0x19f ao ator "Item".
- **0x16 histórico** (form `GHOST_HISTORY`): u8 (0xFF = falha) senão u32 n × { u8, str, u32, 16 bytes, u32, u8 }.

`CGhostTask::OnPacket` (`rb/ghosttask.cpp:49-68`) só trata `0x132 {str, u8}` (ignora) e manda o resto para `OnPacketCommon`.

## HTTP [V]

URLs fixas no exe (usadas por `push imm32` em `LoadDocument` @005f8679 e `SaveDocument` @005f8ea5):

| uso | texto exato | tamanho | VA | offset no arquivo | espaço disponível |
|-----|-------------|---------|----|-------------------|-------------------|
| download (base) | `http://qa.contents.pangya.gametree.co.kr:50006/GHOST/GHOST/` | 59 | 0x00A04DE4 | 0x604DE4 | 60 bytes (59 + NUL; a string seguinte começa logo depois) |
| upload | `http://qa.contents.pangya.gametree.co.kr:50006/GHOST/upload.asp` | 63 | 0x00A04E20 | 0x604E20 | 64 bytes (63 + NUL; depois vêm floats) |

Ambas aparecem uma única vez no exe; dá para redirecionar com `replace_slot(data, <texto>, len+1, <novo>)` do
`tools/client/make_client.py` (novo texto ≤ 59 / ≤ 63 chars + NUL). **Redirecionar não ativa nada no 645** (ver TL;DR).
(Mesmo host serve UCC/Guild: `.../UCC/upload_one.asp`, `.../UCC/UCC_ONE/clothes/`, `.../Guild/upload.asp`, `.../_Files/GuildMark/`.)

**Upload** (`NetResourceManager::WorkUploadThread`, `rb/netresourcemanager.cpp:4094-4292`; cliente `GenericHTTPClient`):
- `POST <upload.asp>` HTTP/1.0, `User-Agent: MERONG(0.9/;p)`,
  `Content-Type: multipart/form-data; boundary=--MULTI-PARTS-FORM-DATA-BOUNDARY` (mesmo formato já tratado no login web).
- Partes: `arg1` = uid do jogador em decimal (`sprintf("%d", MyUID())`, `rb/ghostmanager.cpp:56`);
  `arg2` = arquivo, `filename="<cwd>\<nome>.GST"` (caminho completo local), `Content-Type` do registro p/ `.GST` ou
  `application/octet-stream` (`gh/generichttpclient.c:1686, 704-745`).
- Resposta: corpo tem de **começar** com `PANGYA_UPDATE_OK`; para o tipo GHOST nada mais acontece (não manda pacote), e o arquivo
  local é apagado.

**Download** (`_RealDownLoad` caso 3 @0071bcb9, `cHttp::DownLoad<uchar>` @0071a230):
- `GET <base><date[0]>/<date>/<nome>` — `date` = 1º campo do nome (`res.arg = "%d" de m_date`); note que o 1º segmento é só o
  **primeiro caractere** de `date` (`operator+=(char)` @0071bcd7). Ex.: `…/GHOST/GHOST/2/20080115/20080115_…GST`.
- Corpo = bytes do `.GST`, máx. 0x7D000 (512000) bytes. No 645 os bytes são descartados (ver TL;DR).

## Formato do arquivo `.GST` [V] (`rb/ghostdocument.cpp:2595-2658` Export, `3108-3190` Import)

- Nome: `date_time_course_score_uid_index.GST` (6 inteiros decimais; `GHOST_FILE_INFO`, `rb/ghostmanager.cpp:72`).
- Contêiner: ZIP (XZip `CreateZipZ`) com a entrada `ghost_document_data` (+ opcional `ghost_exception_log.txt`).
- `ghost_document_data` (little-endian): u32 0x3E9; u32 tamanhoCabeçalho (0x1C); `GHOST_DATA_HEADER` {0x3E9, 600, 100, 0x140,
  totalTacadas, 0x20, nCartas}; `GHOST_USER_DATA` 600 bytes (visual/equipamento/nick/id/guild/uid, `rb/ghostdocument.h:39`);
  cabeçalho do jogo 100 bytes (par[18], seeds[18], mapa, holeType, tipo, serverProperty; `rb/ghostdocument.h:27`);
  18 × { u32 n, n × `GHOST_SHOT_DATA` 0x140 }; u32 nCartas; nCartas × `GHOST_CARD_DATA` 0x20.
- Arquivo temporário local: `.\temporary_save\` (limpo após upload/erro).

## O que um servidor guardaria (se o modo existisse) [I]

Os `.GST` enviados por `upload.asp` (chave = nome do arquivo; índice por uid e por campo), servidos estaticamente em
`GHOST/GHOST/<d>/<date>/<nome>`, e a lista por uid para o sub-op 0x0C. Ranking não aparece no protocolo do cliente.
Bônus de vitória é calculado no cliente (`shared/ghostshared.h`, `CGhostGameBonusUtil`); o servidor só receberia o 0x06.

## Recomendação para este servidor

1. Não implementar nada para o Ghost no 645 (sem endpoint HTTP, sem pacotes). Nada quebra.
2. Manter o bit 0x800 de `serverProperty` em 0 (irrelevante enquanto o modo estiver morto, mas é a condição projetada).
3. Se um C→S `0xBA` chegar (só com exe modificado), ignorar e logar o sub-op.
4. Ativar exigiria patch de **código** no exe (ligar o botão do ranking a `RequestGhostGameList`, rotear um id S→C para
   `OnGhostPacket`, e fazer o download chamar `LoadingComplete`/`LoadFromMemory`) — fora do escopo de `make_client.py`.
