# KR 645 — Servidor de ranking (랭킹: telas de ranking, fase 8)

Pesquisa somente leitura no cliente KR 645 QA (`ProjectG_ReleaseQA.exe`) para implementar o servidor de ranking em C#.

Marcas: **[C]** confirmado no cliente (fonte rebang, que reconstrói o exe byte a byte, decompile ghidra ou `pg645.asm`);
**[R]** tirado de uma referência (servidor GB `Server/GB/RankingServer`, servidor JP `Server/Modern/JP/Pangya_RankingServer`,
notas do emulador Python `coverage-notes/notes_msn_rank.md`); **[P]** palpite.

Fontes (abreviações):
- `src/` = `/root/rebang/source/client/ProjectG/`, `GD` = `/root/rebang/source/shared/globalgamedefine.h`
- `RD` = `/root/ghidra-out/rankingdlg.c` (FrRankingDlg / CRankingInfo / FrRankingAboutDlg), `TM` = `/root/ghidra-out/taskmain.c`
- `OPC` = `CTask::OnPacketCommon` @0x732640 (sem decompile no ghidra-out; endereços do `pg645.asm`). Tabela principal:
  índice = id-0x2D (vai até 0x1F8), bytes @0x746118, ponteiros @0x745E1C. Os textos coreanos foram lidos do `.rdata` do exe
  (endereço entre parênteses).
- `GB` = `Server/GB/RankingServer/RankingServerTcp/RankingServer.cs` + `UTIL/RankRegistryManager.cs`, `UTIL/RankRegistry.cs`,
  `UTIL/RankCharacter.cs`, `Models/pangya_rank_st.cs`. GB e JP são de versões novas: **os ids S->C do JP/GB são os do KR + 4500**
  (hello 0x1388 = 5000 ↔ KR 0x1F4 = 500; 0x1389 ↔ 0x1F5; 0x138A ↔ 0x1F6; 0x138B ↔ 0x1F7; 0x138C ↔ 0x1F8). Os ids C->S (0x00,
  0x01, 0x02) são os mesmos. [R]

---

## 0. Resumo

- O ranking usa o **4º socket** do cliente (NET_RANK = 3, `src/networksystem.cpp:15`; `Send(TO_RANK)` = `Send(4)`,
  `src/packet.cpp:761-762`). [C]
- Como no mensageiro, **o cliente não sabe de qual socket veio o pacote**: `RankUnit::OnLine` entrega tudo a
  `CProjectG::OnPacket` (`src/rankunit.cpp:25-37`) e os pacotes caem no `OnPacketCommon`. Ids S->C do ranking:
  **0x1F4 (hello), 0x1F5, 0x1F6, 0x1F7, 0x1F8** (OPC @0x741C5C, @0x741F6A, @0x742016, @0x742032). [C]
- Conexão: **só quando o jogador clica no botão "Ranking"** da barra inferior do lobby. O cliente manda **game C->S 0x47**
  (sem payload); o game responde **S->C 0xA0 `Str ip, u32 porta`**; o cliente conecta, recebe o hello **0x1F4** e, ~1,8 s
  depois, abre a janela. Fechar a janela **fecha o socket** (o cliente faz `ForceShutDown`). [C]
- **Não há pacote de login**: cada pedido C->S 0x00 leva `u32 uid, Str id(login)`. O servidor valida em todo pedido (ou no
  primeiro) contra a sessão de jogo. [C envio; R validação]
- 3 pedidos: **0x00** página do ranking (+ "minha posição"), **0x01** ficha de um jogador (personagem 3D + 5 posições do
  ranking geral), **0x02** busca por nick ou por posição. Respostas 0x1F5 / 0x1F6 / 0x1F8; 0x1F7 = aviso "o servidor está
  reordenando o ranking". 12 linhas por página. [C]
- Sem servidor de ranking nada trava: sem 0xA0 o cliente mostra "지금은 랭킹 서버에 접속할 수 없습니다." depois de 15 s e
  libera o botão Cancelar; sem respostas dentro da janela, os controles voltam sozinhos em 1 s. **Mas o servidor nunca deve
  fechar o socket do ranking**: FD_CLOSE/erro faz o cliente trocar a task para o lobby. [C]

---

## 1. Conexão

### 1.1 De onde vem o endereço [C]
1. Botão "Ranking" (`CTaskMain::OnUnderBar_RankingUp` @0x42C100, `TM:9874-9946`):
   - se `IsControlServerService(0x10000)` (campo `controlServerService` do pacote de dados do jogador, game 0x44; o C# manda 0,
     `GameHandler.cs:172`) -> só mostra "랭킹 관련 부분 점검중입니다." (ranking em manutenção, @0x9E7A84) e **não conecta**;
   - senão fecha diálogos abertos (vfunc +0x3C) e:
     - **conectado ao game**: manda **game C->S 0x47** sem payload (`TM:9910`);
     - **sem game**: `WNetworkSystem::Init(3)` + conectar direto no último endereço guardado (`TM:9906-9907`);
   - abre o diálogo de espera `FrLoginRsDlg` ("login_gs", estado 1 = conectando) (`TM:9917-9922`).
2. **Game S->C 0xA0** (OPC @0x73925A, idx 0x73): `Str endereço` -> `Doc+0x98` (`m_rankServerAddr`), `u32 porta` ->
   `Doc+0xB4` (`m_rankServerPort`), depois `WNetworkSystem::Init(3)` + `SendMessage(3, 0)` (= conectar).
   Sem outro conteúdo, sem código de erro. [C asm @0x73925A-0x7392C4]
3. `RankUnit::Connect` usa **`inet_addr(m_rankServerAddr)`** + `htons(porta)` (`src/rankunit.cpp:44-68`): **só IP numérico**
   (sem DNS, ao contrário do mensageiro). Evento de socket `WM_USER+14`.
4. Não existe lista de servidores de ranking no login (o 0x09 é só mensageiro). O game decide sozinho o endereço.

### 1.2 Hello e cifra [C]
- `RankUnit::Connecting` (`src/rankunit.cpp:70-96`): espera o primeiro pacote por **3 s** (`GetTickCount() - m_connectTime >
  3000`); senão fecha e marca falha.
- `RankUnit::OnConnect` (`src/rankunit.cpp:111-130`; asm @0x781CE0-0x781D5C): pacote **cru** (sem cifra), lido como
  `Decode1` (ignorado), `Decode2 == 500 (0x1F4)`, **`Decode4` = parseKey** (`SetParseKey`), **`Decode1` tem de ser 5**,
  `DecodeStr` = versão (lida e descartada). Qualquer diferença -> o cliente não completa (falha em 3 s).
- No C# (mesma forma do hello do MSN, que também lê `Decode1, Decode2`):
  `conn.SendRaw(new PacketWriter(0x1F4).U32((uint)key).U8(5).Str("645"))`, `key` em 0..15 (mesmas tabelas do game).
  GB manda `key, u8 5, Str data` [R]. Depois disso, cifra normal nos dois sentidos (`PacketCipher`), igual ao game/MSN.
- O contador de pacotes do cliente não anda para pacotes que não são do game (`src/packet.cpp:744-747`). [C]
- **Sem heartbeat**: o cliente não manda TTL ao ranking. Use `IdleTimeoutSeconds = 0` (ou bem longo) depois do hello.

### 1.3 Diálogo de espera `FrLoginRsDlg` (`src/loginrsdlg.cpp:37-100`) [C]
- Mostra "랭킹 서버에 접속하고 있습니다. %c" (conectando ao servidor de ranking) com o botão Cancelar desabilitado.
- Conexão terminou (`IsConnectionComplete(NET_RANK)`):
  - falhou -> "지금은 랭킹 서버에 접속할 수 없습니다." (não é possível conectar agora) e Cancelar habilitado;
  - deu certo -> espera **1,8 s**, fecha a ficha de usuário aberta (`CUserInfo::Close`), fecha o diálogo e chama
    **`RANKING()->Open()`** (`src/loginrsdlg.cpp:85-99`).
- **15 s sem conclusão** (ex.: o game não respondeu 0xA0) -> mesmo texto de falha e Cancelar habilitado. Nada trava.

### 1.4 Abrir, fechar, cair [C]
- `CRankingInfo::Open` (`RD:5316-5351`) cria o form "rankingdlg". O construtor (`RD:5005-5094`) zera os filtros
  (`s_iRankType = s_iSubType = s_iTermType = s_iClassType = s_iPageNum = s_iSearchType = 0`) e, com
  `IsLocalContent(0x59 S4_MATCHING_SYSTEM)` (ligado no KR), manda **game C->S 0xB9 `u8 2`**; o destrutor manda **game 0xB9
  `u8 1`** (`RD:5162-5170`). O C# já engole 0xB9 sem resposta (`GameHandler.MyRoom.cs:33`, mesmo id do "place" da MyRoom).
- `FrRankingDlg::OnInit` (`RD:4625-4637`) manda logo **0x00** (flag 1) e **0x01** (meu uid, meu id).
- Fechar a janela (`CRankingInfo::OnRankingDlgResult`, `RD:4497-4531`): **`ForceShutDown(3)`** — o cliente fecha o socket.
  Cada abertura repete 0x47 -> 0xA0 -> conexão nova.
- FD_CLOSE ou erro no socket do ranking (`src/projectg.cpp:1426-1460`): o cliente **troca a task para `CLobbyTask`** e
  manda as mensagens "ROOMLIST"/26 ao lobby. Ou seja: **não feche o socket pelo servidor** (nem por ocioso).

---

## 2. Structs (pack 1, cp949)

### 2.1 sListInfo (linha da lista; 0x4C na memória, nó da `std::list` = 0x54, `RD:4857-4878`) [C]
Formato **no fio** (lido em OPC @0x741D80-0x741E62, igual nas listas do 0x1F5/0x1F8 e na "minha posição"):

| ordem | tipo | campo (offset na memória) | uso no cliente (`SetListItemInfo`, `RD:704-935`) |
|---|---|---|---|
| 1 | u32 | uid (+0x00) | clique na linha -> pedido 0x01 com este uid; linha destacada (amarelo) se for o uid da ficha aberta |
| 2 | u32 | posição atual (+0x04) | número mostrado se 1..10000 **e** valor ≠ 0; senão "-" |
| 3 | u32 | posição anterior (+0x08) | 0 -> ícone "new"; \|atual-anterior\| > 10000 -> "new"; anterior < atual -> seta "dn" + diferença (azul); anterior > atual -> seta "up" + diferença (vermelho); igual -> "-" |
| 4 | i32 | valor (+0x0C) | coluna de valor (ver §3.4) |
| 5 | u16 | nível (+0x10) | byte baixo = nível 0-based (ícone `level_%03d` de nível+1, máx. 71), só desenhado no ranking "레벨"; byte alto = 0 (GB: `u8 level, u8 term`) |
| 6 | u8 | (+0x12) | não lido pelo desenho; GB manda `class_type` [R] |
| 7 | Str | id de login (+0x14) | não mostrado; vai no pedido 0x01; comparado (sem caixa) com o id da ficha aberta |
| 8 | Str | nick (+0x30) | texto mostrado (some com `HidePrivacy`) |

GB escreve exatamente isto: `u32 uid, u32 cur, u32 last, i32 value, u8 level, u8 term(0), u8 class, Str id, Str nick` [R].

### 2.2 sRankUserInfo (0x22C = 556 bytes; `FrRankingDlg+0x16C`, lido por `DecodeBuffer(0x22C)` no 0x1F6) [C]
| off | tipo | campo | uso |
|---|---|---|---|
| 0x000 | u32 | uid | -1 = nenhuma ficha (o painel não é desenhado) |
| 0x004 | char[22] | id de login | comparado com o id das linhas clicadas |
| 0x01A | char[22] | nick | topo da ficha (`OnUpperMiniInfoOwnerDraw`, `RD:4103-4166`) |
| 0x030 | u8 | nível 0-based | ícone `level_%03d` (nível+1, máx. 71) |
| 0x031 | u8 | 0 | (GB: byte alto de `u16 level`) |
| 0x032 | sCharacterInfo (0x1BC, `GD:274`) | personagem | modelo 3D na ficha (`SetPetCharacter`, `RD:3705-3792`: `CPartTidList::SetTids`; inválido -> partes padrão); com `S4_UCC` (0x57, ligado) o cliente pede as roupas UCC pelo **game 0xB1** (`src/ucclibrary.cpp:2531-2560`) |
| 0x1EE | u16 | ? | não lido; mande 0 [P] (GB tem aqui `u8` "tem overall" [R]) |
| 0x1F0 | 5 × {u32 atual, u32 anterior, i32 valor} | posições no ranking geral | `DrawUserInfoLine` (`RD:938-1085`) |

As 5 linhas da ficha (`ShowUserInfo`, `RD:3795-3816`): desenha as entradas 1, 2, 3, 4 em sequência e a **entrada 0 por último,
separada** — e a entrada 0 também vira o texto "%d 위" / "- 위" ao lado da ficha (`UpdateUserInfo`, `RD:666-701`). Ordem
(GB: `getAllOverallInfoFromPlayer` = itens do ranking geral) [R/P]: **0 종합 점수, 1 스코어 점수, 2 트로피 점수, 3 획득팡 점수,
4 플레이홀 점수**. Cada linha: posição "%d위" se 1..9999 e valor ≠ 0 (senão "- "), valor "%d" se < 100.000.000 (senão
"기록없음"), seta/new com as mesmas regras do §2.1. GB zera atual/anterior quando atual > 10000 [R].

`ranktype::sRankUserInfo` no rebang só declara `unknown0[0x32] + sCharacterInfo` (`src/ucclibrary.cpp:140-149`); o resto vem
do construtor (`RD:625-663`, que zera exatamente os campos do sCharacterInfo em +0x32..+0x1EE) e dos desenhos.

---

## 3. Telas e filtros [C]

### 3.1 Estado do diálogo (globais `s_i*`, todos enviados como u8 exceto a página)
| global | endereço | valores |
|---|---|---|
| `s_iRankType` | 0xB7D030 | 0..2 (lista "rank type", `OnRankTypeListLDown`, `RD:4197-4224`, zera sub e página) |
| `s_iSubType` | 0xB7D02C | depende do tipo (abaixo); trocar zera a página |
| `s_iTermType` | 0xB7D028 | só 0 = "시즌5" (botão `OnTermType_S5`, `RD:3922-3935`) |
| `s_iClassType` | 0xB7D024 | combo: 0 전체 (todos), 1 초급, 2 중급, 3 상급 (`OnClassType_ComboDown`, `RD:4169-4194`; `FrComboBox::FindString` é 1-based). **Trocar a classe NÃO zera a página** |
| `s_iPageNum` | 0xB7D020 | página 0-based; o cliente mostra página+1 |
| `s_iPageTotal` | 0xB7D01C | gravado pelo servidor, **nunca mostrado** (o campo "total" só é zerado) |
| `s_iSearchType` | 0xB7D018 | 0 "닉네임" (nick), 1 "순위" (posição) |

### 3.2 Tipos e subtipos (tabelas inicializadas @0x9DF020-0x9DF2E4)
**Tipo 0 — 종합 랭킹 (ranking geral)**, subtipo 0..4 (`RD:4227-4251`):
0 종합 점수 (pontos totais), 1 스코어 점수 (pontos de placar), 2 트로피 점수 (pontos de troféu), 3 획득팡 점수 (pontos de pang
ganho), 4 플레이홀 점수 (pontos de buracos jogados).

**Tipo 1 — 코스별 랭킹 (por curso)**, subtipo 0..16 = **índice na lista** (`RD:4351-4375`). A lista só tem os cursos que
existem no IFF (`FindCourse(0x28000000 | id)`, `RD:4254-4348`) e o id do curso vem de `GetRealCourseType` (`RD:1-24`):

| sub | curso (texto) | id do curso |
|---|---|---|
| 0..11 | 블루라군, 블루워터, 세피아윈드, 윈드힐, 위즈위즈, 웨스트위즈, 블루문, 실비아캐논, 아이스캐논, 화이트위즈, 샤이닝샌드, 핑크윈드 | 0..11 |
| 12 | 딥인페르노 | 13 |
| 13 | 아이스 스파 | 14 |
| 14 | 로스트 시웨이 | 15 |
| 15 | 이스턴 벨리 | 16 |
| 16 | 위즈 시티 | 19 |

Se algum curso faltar no IFF do cliente, os índices seguintes **deslizam** (o servidor não tem como saber) [C]. Com o IFF
padrão do 645 todos existem [P].
**A ordem do GB/JP é outra** (tem Ice Inferno, Abbot Mine etc.): não copiar o enum deles [R].

**Tipo 2 — 기록 랭킹 (recordes)**, subtipo 0..4 (`RD:4378-4402`): 0 알바트로스 (albatross), 1 홀인원 (hole-in-one),
2 "강종율" (texto exato do exe @0xA29F9C; valor mostrado ×0,1 com 1 casa — uma taxa em décimos) [C texto; significado P],
3 레벨 (nível), 4 총타구비거리 (distância total de tacadas). GB/JP só têm 0, 1, 3, 4 (`RR_LEVEL = 3`) [R].

### 3.3 Paginação [C]
- **12 linhas por página**: `UpdateRankList` (`RD:4654-4686`) adiciona no máximo 12 itens da lista recebida. GB:
  `LIMIT_REGISTRY_FOR_PAGE = 12` [R].
- "Anterior" (`RD:3972-3989`): só se página > 0 (senão "더 이상 랭킹 기록이 없습니다." local). "Próxima" (`RD:3992-4002`):
  **sempre** incrementa e pede; o fim da lista é o servidor que avisa (0x1F5 código 2, §4.2).
- O cliente aplica a página/tipos **que o servidor devolve** (`SetRankTypes`, `RD:189-206`), antes mesmo de olhar o código
  de resultado.

### 3.4 Coluna de valor (`SetListItemInfo`, `RD:873-931`; asm @0x854FE0-0x8550B6) [C]
- Tipo 2 sub 3 (레벨): desenha o **ícone do nível** (u16 da linha) no lugar do número.
- Tipo 2 sub 2: `valor × 0,1` com `%.1f`.
- Tipo 2 sub 4: `valor / 10000` com "%d 만y" (dezenas de milhares de jardas).
- Demais: `%d` (i32 com sinal). O ramo "기록없음" (sem recorde) só valeria para valores ≥ 10^10: na prática nunca.
- **Valor 0 esconde a posição** ("-" na coluna de posição): não mande valor 0 para quem está ranqueado.

### 3.5 Busca (`SendRequestSearchUser`, `RD:4718-4822`) [C]
- Texto vazio -> "찾을 대상을 입력해주십시오."; contém `'`, `;` ou `"` -> "특수기호 ' ; " 는 입력할 수 없습니다." (nada é enviado).
- Por posição: `atoi` < 1 -> "숫자가 아니거나 값이 너무 작습니다."; > 100000 -> "찾는 순위의 값이 너무 큽니다.".
- Busca no tipo/subtipo/classe/página atuais. Enter também dispara (`OnProc`, `RD:4897-4947`).

### 3.6 "Minha posição" [C]
- Lista separada de 1 linha (`UpdateMyInfo`, `RD:167-186`) com a minha entrada (`FrRankingDlg+0x120`); se o uid dela for -1
  mostra o painel "não está no ranking" (`+0x8AC`).
- Clicar na minha linha ou numa linha da lista pede a ficha (0x01) se o id for diferente do da ficha aberta
  (`RD:3938-3969`, `RD:4005-4036`).
- O botão "ghost" da janela fica escondido; clicar mostra "서비스 준비 중입니다." (`RD:3069-3094`, `RD:4430-4444`).

---

## 4. Pacotes

### 4.1 C->S (cliente -> ranking) — todos `Send(4)` [C]
Os envios só acontecem com o socket conectado (`IsConnected(3)`); depois de cada envio o cliente desabilita os controles e
liga um temporizador de **1 s** (`+0xADC = 1.0`); ao vencer, reabilita e mostra a ficha (`OnProc`, `RD:4911-4916`).
Não há timeout de resposta: sem resposta a tela só fica como estava.

| id | payload | quando (fonte) |
|---|---|---|
| **0x00** | `u32 meuUID, Str meuId(login), u8 rankType, u8 subType, u8 termType, u8 classType, u32 página, u8 comMinhaPosição` | `SendRequestRank(bool)` (`RD:3819-3871`). Flag **1** = consulta nova (abrir, trocar tipo/subtipo/classe/termo); **0** = só paginação (anterior/próxima). O id é `Doc+0x3E2` (o id de login que o login 0x01 confirmou, `lobbytask.c:2695-2715`), o uid é `Doc+0x4EB` (MyUID) |
| **0x01** | `u32 uidAlvo, Str idAlvo(login), u8 termType` | `SendRequestUserInfo` (`RD:3874-3919`): ao abrir (eu), ao clicar numa linha, após busca (linha encontrada), no botão de termo. Não envia se uid = 0xFFFFFFFF. Esconde a ficha até a resposta (ou 1 s) |
| **0x02** | `u8 searchType`, depois **`Str nick`** (searchType 0) **ou `u32 posição`** (1; 1..100000), depois `u8 rankType, u8 subType, u8 termType, u8 classType, u32 página` | `SendRequestSearchUser` (`RD:4718-4822`) |

GB lê o 0x00 como `uid, id, search_dados{menu, item, term, class, page}, active` e o 0x01 como `uid, id, u8` [R]
(`RankingServer.cs:722-725`, `:358-360`). GB tem handlers 0x03-0x05 que o KR nunca envia [R]. Não existem outros envios
`Send(4)` no 645 (os únicos `IsConnected(3)` estão em `RD`). [C]

### 4.2 S->C (ranking -> cliente)
Todos exigem a janela aberta (`CRankingInfo+0x2C != 0`); sem janela são consumidos sem efeito. [C]

#### 0x1F4 — hello (cru) — §1.2.

#### 0x1F5 — página do ranking (resposta ao 0x00) — OPC @0x741C5C-0x741F65 [C]
```
u8  resultado
u8  rankType, u8 subType, u8 termType, u8 classType    -> SetRankTypes (sempre, antes de olhar o resultado)
u32 página (0-based)                                    -> s_iPageNum
u32 total de páginas                                    -> s_iPageTotal (não mostrado)
-- só se resultado == 0:
u16 n, n × sListInfo (§2.1)                             (a lista é trocada; o cliente mostra até 12)
u8  minhaPosição: 0 -> segue 1 × sListInfo (a minha linha)
                  1 -> não estou no ranking (minha linha = -1, painel "fora do ranking")
                  outro (GB manda 2) -> não lê nada e mantém a minha linha anterior
```
Depois: `UpdateRankList` (lista + página+1) e `UpdateMyInfo`.
Resultado ≠ 0: barra inferior "더 이상 랭킹 기록이 없습니다." (não há mais registros). **2** -> só a mensagem (mantém a lista;
use para "próxima página além do fim"). **Outro (1)** -> limpa a lista, página 0, minha linha = -1, "fora do ranking".
Como `SetRankTypes` roda antes, **o cabeçalho tem de vir sempre** e, no código 2, deve repetir a última página válida
(o cliente já incrementou a página ao pedir). GB manda `resultado + 14 zeros` no erro [R] — no KR isso zeraria o filtro.
Para `comMinhaPosição = 0` mande `u8 2` (GB [R]).

#### 0x1F6 — ficha do jogador (resposta ao 0x01) — OPC @0x741F6A-0x742011 [C]
```
u8 código
0 -> sRankUserInfo (0x22C bytes, §2.2)  -> UpdateUserInfo (+ pedido UCC game 0xB1 com S4_UCC)
1 -> barra inferior "순위내에 정보가 없습니다." (sem informação no ranking)
2 -> aviso (popup) "유저 정보를 가져올 수 없습니다." (não foi possível obter a ficha)
```
GB: `0x138A u8 0, u32 uid, char id[22], char nick[22], u16 level, CharacterInfo, u8 temOverall, N × {cur,last,value}` [R]
(o CharacterInfo do JP é maior; no KR o bloco tem tamanho fixo 0x22C).

#### 0x1F7 — "reordenando" — OPC @0x742016 [C]
Sem payload. Popup "서버에서 랭킹 정보를 다시 정렬하고 있습니다." (o servidor está reordenando o ranking). Use como resposta a
qualquer pedido enquanto o ranking estiver sendo recalculado.

#### 0x1F8 — resultado da busca (resposta ao 0x02) — OPC @0x742032-0x742223 [C]
```
u8 código
≠0 -> minha linha = -1 (sem redesenhar) + popup "순위내에 정보가 없습니다." (não encontrado)
0  -> u8 rankType, u8 subType, u8 termType, u8 classType, u32 página, u32 total -> SetRankTypes
      u16 n, n × sListInfo
      u16 índice da linha encontrada na página (0-based)
```
Sem bloco "minha posição". Depois: `UpdateRankList` e `SelectRankList(índice)`, que seleciona a linha e **manda sozinho um 0x01**
para ela (`RD:4689-4715`). GB: `0x138C` igual, índice = `(posição-1) % 12` [R].

#### Outros ids
0x1F9+ estão fora da tabela do OPC (ignorados). Não há outro pacote do ranking. [C]

### 4.3 Game (o que o game server faz) [C]
| id | dir | payload | nota |
|---|---|---|---|
| 0x47 | C->S | vazio | botão Ranking (`TM:9910`). Único envio de 0x47 C->S no 645 (o S->C 0x47 "entrar na sala" é outra coisa) |
| **0xA0** | S->C | `Str ip, u32 porta` | endereço do ranking (§1.1). **IP numérico** |
| 0xB9 | C->S | `u8 2` ao abrir / `u8 1` ao fechar | S4_MATCHING_SYSTEM; só engolir |
| 0xB1 | C->S | `u8 1, u32 guid, u8 0` | pedido de roupa UCC das partes do personagem da ficha (já existe no fluxo UCC) |
| 0x44 | S->C | `controlServerService` bit **0x10000** | 1 = botão Ranking mostra "manutenção" e não conecta |

---

## 5. Erros e servidor ausente [C]

| situação | o que o cliente faz |
|---|---|
| game ignora o 0x47 | 15 s de "conectando", depois "지금은 랭킹 서버에 접속할 수 없습니다." + Cancelar. Nenhum travamento |
| 0xA0 com endereço errado/porta fechada | falha imediata (ou em 3 s sem hello) -> mesmo texto |
| hello errado (id ≠ 0x1F4 ou byte ≠ 5) | falha em 3 s |
| sem resposta a 0x00/0x01/0x02 | controles voltam em 1 s; lista/ficha ficam como estavam |
| servidor fecha o socket / erro de rede | `ChangeTask("CLobbyTask")` + mensagens ROOMLIST — evitar |
| `controlServerService & 0x10000` | só a mensagem "랭킹 관련 부분 점검중입니다."; nada é enviado |

Recomendação para "sem ranking": deixar `controlServerService` sem o bit (0) e **não** responder 0x47 é seguro, mas a melhor
experiência é ligar o bit 0x10000 quando não houver servidor de ranking registrado (mensagem imediata em vez de 15 s). [P]

---

## 6. De onde vêm os dados e quando recalcular

### 6.1 Referência (GB/JP) [R]
- O ranking é um **retrato** carregado do banco: `pangya.ProcGetRankRegistryInfo` devolve `uid, posição atual, posição anterior,
  valor, menu, item`; `pangya.ProcGetRankRegistryCharacterInfo` devolve `uid, id, nick, nível, personagem equipado` (85 colunas).
  O SQL dos procedimentos (`pangya.GeraRankAll`) não está nas referências.
- Recalculo: `pangya_rank_config(refresh_time_H, reg_date)`; o heartbeat chama `GeraRankAll` quando
  `última + refresh_time_H horas` passou (`RankRefreshTime.isOutDated`), recarrega o retrato e grava um log. A "posição
  anterior" é a do retrato anterior (é o que alimenta as setas up/dn/new).
- Login no ranking: confere uid != 0, id não vazio, IP banido, **id igual ao do banco**, bloqueios, sessão duplicada (derruba a
  antiga) e, via auth server, se o jogador está online num game server com o mesmo IP. Falha -> 0x1389 código 1.
- Busca por nick: exata (case-sensitive no GB). Busca por posição: página = `(posição-1) / 12`, índice = `(posição-1) % 12`.

### 6.2 Proposta para o nosso servidor [P]
Calcular em memória a partir do banco (snapshot), sem LINQ em `src/` (regra 13: dicionários/listas e laços).

| tipo/sub | valor (i32) | ordem | fonte no C# |
|---|---|---|---|
| 0/1 스코어 점수 | pontos de placar: ex. `max(0, -TotalScore)` (total abaixo do par) ou média por buraco ×100 | desc | `PlayerStats.TotalScore`, `Hole` |
| 0/2 트로피 점수 | ex. Σ troféus × peso (ouro 3, prata 2, bronze 1) × (faixa+1) | desc | `PlayerStats.Trophies` (`Trophy.cs`) |
| 0/3 획득팡 점수 | pang ganho em partidas (acumulador novo; o saldo de pang não serve) | desc | novo campo em `PlayerStats` |
| 0/4 플레이홀 점수 | buracos jogados | desc | `PlayerStats.Hole` |
| 0/0 종합 점수 | soma (ou soma ponderada) dos 4 acima | desc | — |
| 1/n curso | melhor resultado em 18 buracos do curso `GetRealCourseType(n)`; **mande as tacadas totais (par + placar)** ou outro valor > 0, porque valor 0 esconde a posição (placar "par" = 0) | asc | `CourseRecord.BestScore` (só partidas de 18 buracos) |
| 2/0 알바트로스 | contagem | desc | `PlayerStats.Albatross` |
| 2/1 홀인원 | contagem | desc | `PlayerStats.HoleInOne` |
| 2/2 "강종율" | taxa em décimos (ex. ‰/10): sugestão `Fairway*1000/Drive` | desc | `PlayerStats.Fairway`, `Drive` |
| 2/3 레벨 | nível+1 ou experiência total (≠ 0); a linha mostra o ícone do nível | desc | `Player` nível/exp |
| 2/4 총타구비거리 | distância total em jardas (cliente mostra /10000) | desc | `PlayerStats.Distance` |

- Classe (`classType`): 0 = todos; 1/2/3 = faixas de nível (ex. 초급 Rookie–Amateur, 중급 Junior–Senior, 상급 Pro em diante)
  — o corte original é desconhecido [P]. Termo: só 0.
- Só entram jogadores com valor ≠ 0 (e, no curso, com recorde). Empate: valor anterior, depois uid. Posições 1-based.
- Recalcular **uma vez por dia** (ex. 05:00, configurável) + comando de admin; guardar a posição anterior do retrato velho.
  Durante o recálculo responder **0x1F7**. Ficha (0x1F6) e linhas usam nick/nível/personagem do momento do retrato.

---

## 7. Plano de implementação

### Fase A — conectar e não dar erro
- Listener `RankingServer` (projeto novo `Pangya.Ranking` ou dentro do processo do game, como o MSN), porta configurável
  (`Ranking.Port`, ex. **4774** como o original no REPORT; nunca 80/10101/10102/20201), registro tipo "ranking".
- `RankingHandler.OnConnectedAsync`: `ParseKey` aleatório 0..15, `SendRaw(0x1F4 U32 key, U8 5, Str "645")`,
  `IdleTimeoutSeconds` curto até o primeiro pedido válido, depois 0. Nunca fechar por iniciativa própria exceto em pacote inválido.
- Game: **C->S 0x47 -> S->C 0xA0 `Str ip, u32 porta`** (IP público numérico do registro/config). Sem servidor de ranking:
  ligar `controlServerService |= 0x10000` no 0x44 (ou não responder).
- 0x00 -> validar (uid na sessão de jogo do mesmo processo, `Login` igual ao id sem diferenciar caixa, mesmo IP) e responder
  **0x1F5 código 1** com o cabeçalho ecoado (lista vazia, "fora do ranking"); 0x01 -> **0x1F6 código 1**; 0x02 -> **0x1F8 1**.
- Teste: clicar em Ranking no lobby, ver a janela abrir sem travar, trocar abas, fechar e abrir de novo.

### Fase B — rankings reais
- Serviço de snapshot: para cada (tipo, sub, classe) uma lista ordenada de `RankEntry(uid, atual, anterior, valor)` +
  dicionário uid -> índice; dicionário uid -> `RankCharacter(id, nick, nível, sCharacterInfo)`.
- 0x00: página `min(pedida, última)`; fora do fim -> código 2 com a última página; `comMinhaPosição` 1 -> minha entrada
  (0) ou 1; 0 -> `u8 2`.
- 0x01: sRankUserInfo de 0x22C com as 5 posições do tipo 0 (ordem do §2.2) — `PlayerStructs.Character(...)` para o
  sCharacterInfo; jogador sem retrato -> código 1.
- Recalculo diário + comando de admin; 0x1F7 enquanto recalcula.

### Fase C — busca
- 0x02 por nick (sem diferenciar caixa, dentro do tipo/sub/classe pedidos) e por posição -> 0x1F8 com a página e o índice;
  não achou -> 0x1F8 1.

### Fase D — acabamento
- Contador novo de pang ganho; faixas de classe; log do retrato; limites (no máx. 1 pedido a cada ~200 ms por conexão).

### Riscos e dúvidas
- **Fórmulas dos pontos** (종합/스코어/트로피/획득팡/플레이홀) e o significado de "강종율" não aparecem no cliente nem nas
  referências (o SQL `GeraRankAll` não está disponível). [P]
- Ordem das 5 linhas da ficha: confirmada só a posição de desenho (1..4 em cima, 0 separado); o mapeamento para os subtipos
  vem do GB. [R/P]
- `u16` em +0x1EE do sRankUserInfo e o `u8` +0x12 da linha: o cliente não lê; mandar 0 / classe. [P]
- Valor 0 esconde a posição: cuidado com recordes de curso "no par" (placar 0). [C]
- **inet_addr**: o 0xA0 precisa de IP numérico; nome de host falha. [C rebang]
- **Fechar o socket pelo servidor** joga o cliente para a task do lobby. Ocioso = 0. [C]
- Autenticação fraca: o cliente só manda uid + id de login; conferir com a sessão de jogo e o IP. [C/R]
- Lista de cursos depende do IFF do cliente (índices deslizam se faltar curso). [C]
- Trocar a classe não zera a página: o servidor deve limitar a página. [C]
- `s_iPageTotal` não aparece na tela; mandar o total certo mesmo assim (GB). [C/R]
