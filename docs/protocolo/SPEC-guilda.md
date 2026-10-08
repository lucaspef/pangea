# KR 645 — Guilda (길드)

Pesquisa somente leitura no cliente KR 645 QA (`ProjectG_ReleaseQA.exe`) para implementar o sistema de guilda no
servidor C#.

Marcas: **[C]** confirmado no cliente (decompile ghidra, `pg645.asm` ou fonte rebang, que é uma reconstrução byte a byte
do mesmo exe); **[R]** tirado de uma referência (emulador Python `emu/ext_guild.py` + `coverage-notes/notes_guild.md`,
servidores GB/JP e S6); **[P]** palpite.

Fontes (abreviações):
- `GA` = `rebang/source/client/ProjectG/guildactor.cpp` (CGuildActor::ExecGuildMessage, msg 0x263)
- `GAC` = `rebang/.../guildauthcontroller.cpp` (menus/permissões), `CD` = `rebang/.../contentsdoc.cpp:551-860`
  (tabela de mensagens/códigos, CGuildInfo)
- `GD` = `rebang/source/shared/globalgamedefine.h:457-593` (structs, pack 1)
- `LT` = `ghidra-out/lobbytask.c` L6420-6856 (respostas 0x1B6-0x1C6), `RMT` = `ghidra-out/realmyroomtask.c` L7942-8056
  (0x1B3-0x1B5, 0x1BD, 0x1C7, 0x1C8)
- `LD` = `ghidra-out/ingameguildlobbydlg.c` (CGuildMainForm, FrGuildJoin), `MD` = `ghidra-out/ingameguildmyroomdlg.c`
  (FrCreateGuildKit, FrRegisterGuildMark, FrChangeGuildName)
- `NRM` = `rebang/.../netresourcemanager.cpp` (upload/download HTTP), `RSS` = `rebang/.../rssmanager.cpp`
- `OPC` = `CTask::OnPacketCommon` @0x732640 no `pg645.asm` (jump table: idx = id-0x2D, bytes @0x746118, ponteiros
  @0x745E1C; o decompile dessa função não existe no ghidra-out)

---

## 0. Resumo

- Toda a gestão de guilda é feita **no game server** (C->S 0xFE..0x113, S->C 0x1B3..0x1C8). Regra: resposta = pedido
  + 0xB5 (0xFE->0x1B3 ... 0x113->0x1C8); toda resposta começa com `u32 result`, **1 = sucesso**; qualquer outro valor
  vira uma caixa de mensagem com o texto do código (tabela §6). [C]
- Janela principal da guilda: só no **lobby** (respostas 0x1B6-0x1C6 tratadas só por `CLobbyTask`). Criar guilda,
  trocar nome e registrar marca: só na **MyRoom** (0x1B3-0x1B5, 0x1C7, 0x1C8 só em `CRealMyRoomTask`), abertos ao usar o
  item no inventário (`realmyroommainui.c` L25263: typeid 0x1A00012F/130/131 -> GuildActor msg 27). 0x1BD (push do meu
  estado) é tratado nos dois; **não** no jogo (golftask). [C]
- A guilda "de verdade" do jogador vem do **GUILD_USER_INFO (0x119) no fim do 0x42** (`OPC` @0x73415e: decodifica 0x119
  bytes -> GuildActor msg 6 = `SetMyGuildUserInfo` + copia uid/nome/marca para o `sUserInfo`). [C]
- O RSS disparado por guildId != 0 vem do **`sUserInfo.dwGuildId` (@0x6B do 0x42)**, não do GUILD_USER_INFO; o callback
  é vazio (`RecvRSS0` @0x729aa0 = `ret`) e a falha de rede é silenciosa. É seguro mandar guildId != 0. [C]
- Emblema: upload HTTP multipart para `http://qa.contents.pangya.gametree.co.kr:50006/Guild/upload.asp`, download de
  `http://qa.contents.pangya.gametree.co.kr:50006/_Files/GuildMark/<marca>.png`. As duas URLs ficam numa string só
  cada uma no exe e podem ser trocadas pelo `tools/client/make_client.py` para apontar ao `Pangya.Web` (porta 30080). [C]
- Chat de guilda, avisos "fulano entrou/saiu da guilda" e os C->S 0x2A/0x2B são do **messenger** (Send(1) = TO_MSN),
  que o servidor C# ainda não tem. Os S->C 0x39/0x3A/0x3B/0x3C (lista de membros do messenger, emblema e nome mudaram)
  chegam pelo dispatcher comum (`OnPacketCommon`), então o game server também pode mandá-los. [C] (origem MSN = [P])

---

## 1. Structs (GD, pack 1, cp949) [C]

Todas já estão geradas em `src/Pangya.Protocol.KR645/Structs.g.cs`. Tamanhos conferidos nos `DecodeBuffer` do cliente
(0x119, 0x129, 0xC4, 0x51, 0x31) e no `OPC` (0x37).

### GUILD_USER_INFO (0x119 = 281) — 0x42, 0x1BD
| off | tipo | campo | notas |
|---|---|---|---|
| 0x000 | u32 | guildUID | 0 = sem guilda |
| 0x004 | char[21] | guildName | |
| 0x019 | i32 | guildPang | mostrado na janela; o cliente também copia este valor para `sUserInfo+0x6F` (`dwEmblemVer` no rebang; ninguém mais lê, só zera) [C]. Nome "pang" vem do rebang/S6 [R] |
| 0x01D | i32 | guildPoint | mostrado na janela |
| 0x021 | i32 | memberCount | mostrado na janela |
| 0x025 | char[12] | guildMark | nome do arquivo do emblema sem ".png" (máx. 11 + NUL) |
| 0x031 | char[101] | notice | 공지 |
| 0x096 | char[101] | introduce | 소개 |
| 0x0FB | i32 | classIdx | **cargo de quem recebe** (§2) |
| 0x0FF | u32 | masterUID | |
| 0x103 | char[22] | masterNickname | |

### GUILD_INFO (0x129 = 297) — 0x1B6, 0x3B, 0x3C
`GUILD_USER_INFO` + `SYSTEMTIME createTime` @0x119.

### GUILD_LIST (0xC4 = 196) — 0x1BA/0x1BB
| off | campo |
|---|---|
| 0x00 | u32 guildUID |
| 0x04 | char[21] guildName |
| 0x19 | i32 guildPang |
| 0x1D | i32 guildPoint |
| 0x21 | i32 memberCount |
| 0x25 | SYSTEMTIME createTime |
| 0x35 | char[101] introduce (mostrado ao clicar na guilda: `LD` L3749 `this+0x221` = +0x35) |
| 0x9A | u32 desconhecido (`unknown9a`; nunca lido na janela) — mandar 0 [P] |
| 0x9E | u32 masterUID |
| 0xA2 | char[22] masterNickname |
| 0xB8 | char[12] guildMark |

A janela de detalhe (`LD` OnGuildDetail_OwnerDraw L3366-3400) desenha emblema, nome, mestre, memberCount, +0x19, +0x1D.

### GUILD_USER_LIST (0x51 = 81) — 0x1C4
| off | campo |
|---|---|
| 0x00 | u32 guildUID |
| 0x04 | u32 userUID |
| 0x08 | i32 classIdx (inclui 9 = aguardando: o gestor vê os pedidos na mesma lista e aprova/recusa por ela, `GAC` L97-166) |
| 0x0C | char[25] message (mensagem pessoal; o cliente limita a 20 bytes) |
| 0x25 | char[21] guildName |
| 0x3A | char[22] nickname |
| 0x50 | u8 online |

### GUILD_HISTORY (0x31 = 49) — 0x1BC
| off | campo |
|---|---|
| 0x00 | i32 index |
| 0x04 | u32 guildUID |
| 0x08 | char[21] guildName (não é mostrado) |
| 0x1D | i32 state (§2.3) |
| 0x21 | SYSTEMTIME time (mostra só ano/mês/dia: formato `" - %d년 %d월 %d일 %s\n"`, `guilddefine.h`) |

### GUILD_USER_MSN_LIST (0x37 = 55) — S->C 0x39
`u32 userUID, u32 guildUID, u8 sex, char userID[22], char nickname[22], u8 ?, u8 online`.

### Onde a guilda aparece em outros structs [C]
| struct | campos |
|---|---|
| `sUserInfo` (0x42) | **sGuild[21] @0x2E**, **szEmblemName[12] @0x43**, **dwGuildId @0x6B**, dwEmblemVer @0x6F (offsets a partir do início do `sUserInfo` com o u16 inicial, como no SPEC-game; no doc do cliente: doc+0x3E0 + off — sGuild doc+0x40E, emblema doc+0x423, guildId doc+0x44B, +0x6F doc+0x44F, `guildactor.c` L854-857) |
| `sBriefUserInfo` (lista do lobby) | `m_GuildId`, `szEmblemName[12]` (GD L852-853) |
| `sSlotInfo` (sala, 0x152) | `sGuild[21]` @0x1A, `GuildId` @0x59, `szEmblemName[12]` @0x5D (SPEC-room) |
| `sRoomInfo` | `sGuildRoomInfo GuildInfo` @0x5B (74 B): `u32 nGuildID[2]`, `char szName[2][21]`, `char szEmblemName[2][12]` |
| `sFriend` (messenger) | `dwGuildId`, `szEmblemName[12]`, bit `GuildFriend` |
| perfil (0x15x) | troféus de guilda 0x153, guildPang/guildPoint no registro de estatísticas (SPEC-perfil-mapas) |

Hoje `PlayerStructs.UserInfo`, `RoomPackets.SlotInfo` e `RoomPackets.BriefUser` deixam tudo isso zerado.

---

## 2. Cargos, permissões e histórico

### 2.1 classIdx (GUILD_CLASS_IDX) [C]
| valor | nome (msgs 1077-1080) | controlador na janela (`LD` ConvertGuildClass / `guildauthcontroller.h`) |
|---|---|---|
| 0 | sem guilda | `CGuildNull` (só "pedir para entrar") |
| 1 | 길드장 (mestre) | `CGuildMaster` : `CGuildManager` |
| 2 | 부길드장 (submestre) | `CGuildSubMaster` : `CGuildManager` |
| 3 | 길드원 (membro) | `CGuildMember` |
| 9 | 가입대기자 (aguardando aprovação) | `CGuildWaitMember` |

`IsGuildMember` = 1..3; `IsManager` = 1..2 (`CD` L446-472). Um jogador com pedido pendente tem `guildUID` = guilda pedida
e `classIdx` = 9 (a janela manda 0x101 com esse uid e mostra a guilda; `RequestMember_Page` não roda para 0 e 9).

### 2.2 O que o cliente deixa fazer (o servidor tem de checar tudo de novo) [C]
| ação | quem (cliente) | C->S | regra no cliente |
|---|---|---|---|
| criar guilda | qualquer um que use o kit 0x1A00012F na MyRoom | 0xFE | nome conferido antes (0xFF) e igual ao campo; intro não vazia |
| trocar nome | gestor (1,2) com kit 0x1A000131 | 0x100 | não gestor: msg 0xD4E9 = 54505 "길드장이 아닙니다"; sem guilda: 1040; nome novo ≠ atual (0xD2F3 = 54003), conferido por 0xFF |
| registrar marca | gestor (1,2) com kit 0x1A000130 | 0x112 | não gestor: msg 0xD4EB = 54507 "관리권한이 없습니다"; PNG 32 bits, largura ≤ 22, altura ≤ 20 |
| notícia / apresentação | gestor (`CGuildManager`) | 0x102/0x103 | texto mudou (senão 1060), filtro 2..100 bytes |
| menu sobre membro | mestre: `승인(승급)|강등|길드원 추방|길드장 위임|정보보기` (1008); submestre: `승인(승급)|길드원 추방|정보보기` (1009); membro: `정보보기` (1010) | | nunca sobre si mesmo |
| 승인(승급) em classe 9 | gestor | 0x10B (aprovar) | |
| 승인(승급) em classe 3 | gestor | 0x10D class=2 | em 1 ou 2: msg 1011 "não dá para promover mais" |
| 강등 em classe 2 | mestre (submestre não tem o item no menu, mas o código é o mesmo) | 0x10D class=3 | em 1/3: msg 1012 |
| 강등 em classe 9 | gestor | 0x10C (recusar) | |
| 길드장 위임 | mestre | 0x10D class=1 | só sobre submestre (classe 2), com confirmação 1032; senão 1036 |
| 길드원 추방 | gestor | 0x111 | **sem checagem de alvo** (o cliente deixa o submestre tentar expulsar o mestre) |
| sair | submestre, membro (confirmação 1047) | 0x110 | mestre e aguardando: msg 1022 "cargo não pode sair" |
| encerrar guilda | mestre (confirmação 1024) | 0x104 | |
| desistir do pedido | aguardando (9) | 0x10A | confirmação 1015 |
| pedir para entrar | sem guilda (0) | 0x109 | já membro: 1013; texto não vazio (1014) |
| mensagem pessoal | membro: só a própria (o ícone de edição só aparece na própria linha); gestor: ícone em todas as linhas | 0x10E | filtro 0..20 bytes; só envia se mudou |
| ver perfil | todos | (CUserInfo, não é pacote de guilda) | |
| botão Guilda no lobby | todos, se `controlServerService & 0x1000000` == 0 (0x42) | | com o bit ligado mostra mensagem e não abre (`lobbymain.c` L12403) |

Regras do servidor sugeridas [P], usando os códigos que o cliente já tem: só o mestre promove para 2, rebaixa e delega
(54505); submestres limitados (54012); gestor não expulsa gestor de cargo igual ou maior (54008/54504); mestre não sai
(54021/1022) nem encerra com membros (54014); 24 h de espera depois de criar/encerrar/sair (54006, texto 1024 confirma);
guilda cheia (54009); jogador já em guilda ou com pedido (54005/54017/54510); nível mínimo (54004).

### 2.3 Estados do histórico (GUILD_STATE_IDX) [C]
`ConvertGuildHistory` @0x85B2E0 (tabela @0x85B3A0): state -> mensagem.
| state | texto | state | texto |
|---|---|---|---|
| 1 | 가입신청 (pediu para entrar) | 8 | 길드개설 (criou) |
| 2 | 가입취소 (desistiu do pedido) | 9 | 길드폐쇄 (encerrou) |
| 3 | 가입승인 (foi aprovado) | 10 | 길드장 위임 (delegou o mestre) |
| 4 | 가입거절 (foi recusado) | 11 | 길드장 임명 (virou mestre) |
| 5 | 길드원등급변경 (cargo mudou) | 12 | 부길드장 임명 (virou submestre) |
| 6 | 길드원 추방 (expulso) | 13 | 길드원 승인 (aprovou alguém) |
| 7 | 길드탈퇴 (saiu) | 51/52 | 길드마크블럭 / 해제 (marca bloqueada/liberada) |
| | | 53 | GM에의한추방 (expulso por GM) |
Outros valores -> texto vazio. O 0x107 não leva parâmetro: é o **histórico do próprio jogador** (todas as guildas por
onde passou; por isso o struct tem guildUID/guildName) [P pela forma].

---

## 3. Pacotes C->S e respostas S->C

Strings = `u16 len + bytes cp949` (EncodeStr). "espera" = o cliente abre a caixa "aguarde" (msg 1) e a resposta a fecha
(msg 2); sem resposta a caixa fica aberta. Falha = `u32 código` -> caixa com o texto de §6 (GuildActor msg 0x1A),
salvo onde indicado.

### 3.1 Lista e busca (lobby)
| C->S | layout | quando | resposta |
|---|---|---|---|
| **0x105** | `u32 page` (0 vira 1) | ao abrir a janela (sempre `page=1`, `LD` L10497) e nas setas/páginas (grupos de 5 páginas: `(p-1)/5*5` e `+6`) ; espera | **0x1BA** |
| **0x106** | `u32 page, str nome` | busca por nome: ≥ 4 bytes (senão msg 1041/1042), sem `%%` (`LD` L5443-5523); espera | **0x1BB** |

0x1BA / 0x1BB (`LT` L6509-6547) [C]: `u32 1, u32 page, u32 totalGuildas, u16 n, n × GUILD_LIST(0xC4)` -> msg 8.
`totalGuildas` = número total de guildas que casam (o cliente calcula páginas = ceil(total/15)); **15 por página**
(`CD` L484-492). `n == 0` -> caixa 1035 "검색된 길드가 없습니다" e volta ao modo lista (acontece sempre que não há
guildas; é o comportamento original). O 0x1BA atual (`U32(1).U32(1).U32(1).U16(0)`) está correto para "nenhuma guilda".

### 3.2 Minha guilda (lobby)
| C->S | layout | resposta |
|---|---|---|
| **0x101** | `u32 guildUID` — só se o meu guildUID != 0 (inclui classe 9), ao abrir a janela; sem espera | **0x1B6** `u32 1, GUILD_INFO(0x129)` -> msg 4: limpa e grava a info (**classIdx = cargo do requisitante**), atualiza `sUserInfo` (uid, nome, marca, +0x6F), manda **0x107** e pede a página de membros (0x10F). Falha: só caixa (sem msg 2). [C] |
| **0x107** | vazio | **0x1BC** `u32 1, u16 n, n × GUILD_HISTORY(0x31)` -> msg 9 (lista o histórico). Falha: caixa. [C] |
| **0x10F** | `u32 guildUID, u32 page` (page 0 = página atual) — só classes 1..3 | **0x1C4** `u32 1, u32 page, u32 totalMembros, u16 n, n × GUILD_USER_LIST(0x51)` -> msg 7; **n == 0 é ignorado** (nem atualiza). 15 por página. Espera. [C] |

### 3.3 Criação e itens (MyRoom)
| C->S | layout | resposta |
|---|---|---|
| **0xFF** | `str nome` (filtro 4..20 bytes, sem espaço: `FilteringGuildString(nome,4,20,true)`); usado pelo kit de criação e pelo de troca de nome; espera | **0x1B4** `u32 1, str nome` -> msg 1029 "사용 가능한 길드명입니다" + msg 3 (o form guarda o nome confirmado). Falha (ex. 54003 nome em uso): caixa. [C] |
| **0xFE** | `str nomeConfirmado, str apresentação` (linhas da apresentação unidas pelo separador de linha do cliente); **não manda o id do kit** -> o servidor procura o 0x1A00012F no inventário (54502 se não tiver). O form fecha; espera | **0x1B3** `u32 result`; 1 -> msg 1031 "길드가 생성되었습니다". **O 0x1B3 não traz a guilda**: em seguida mandar **0x1BD** com o novo GUILD_USER_INFO (classIdx 1) — `CheckMyGuildState(novo 1, atual 0)` aceita [C]. Consumir o kit e avisar o inventário [P]. |
| **0x100** | `u32 guildUID, str nomeNovo` (nome tem de ser o confirmado por 0xFF e diferente do atual); espera | **0x1B5** `u32 1` -> msg 1039 "길드명이 변경되었습니다" + msg 25 (manda MSN 0x2B guildUID). Para atualizar o nome na tela: S->C **0x3C** GUILD_INFO (msg 23 `ChangeGuildName`) a todos os membros online [C layout / P quem manda]. Consome 0x1A000131. |
| **0x112** | `u32 guildUID`; espera (fica aberta durante o upload) | **0x1C7** `u32 1, u32 emblemIdx, str emblema` -> msg 21 `RequestUpload(emblemIdx, emblema)` = upload HTTP (§4). Falha: fecha a espera + caixa. [C] |
| **0x113** | vazio — enviado pelo cliente depois que o POST respondeu `PANGYA_UPDATE_OK` (`NRM` L4268-4281) | **0x1C8** `u32 1` -> msg 0x1A(1053) "길드 마크 업로드에 성공하였습니다"; falha: fecha espera + caixa. Depois: S->C **0x3B** GUILD_INFO (msg 22 `ChangeGuildEmblem`) / 0x1BD para os membros online [P]. Consome 0x1A000130 [P: aqui ou no 0x112]. |

### 3.4 Gestão (lobby) — todas com espera, falha = caixa
| C->S | layout | resposta (sucesso) |
|---|---|---|
| **0x102** notícia | `u32 guildUID, u32 meuUID, str texto` (filtro 2..100; `'` trocado por `` ` `` antes de enviar; cada linha termina com o separador de linha) | **0x1B7** `u32 1` -> 1027 "길드 공지를 변경하였습니다" |
| **0x103** apresentação | idem (linhas unidas pelo separador, sem separador no fim) | **0x1B8** `u32 1` -> 1026 |
| **0x104** encerrar | `u32 guildUID` (mestre, após 1024) | **0x1B9** `u32 1` -> msg 11 = 1025 "길드를 폐쇄하였습니다". Mandar 0x1BD classIdx 0 (CheckMyGuildState 0 com 1/2/3 = limpa tudo) [C] |
| **0x109** pedir entrada | `u32 guildUID, str texto` (FrGuildJoin; texto não vazio) | **0x1BE** `u32 1` -> msg 14 (desabilita o botão, form mostra sucesso). **Falha é diferente**: msg 17(código) fecha o form de pedido e mostra o texto (`LT` L6595-6617). Depois mandar 0x1BD (guildUID, classIdx 9) para a janela virar "aguardando" [C: 9 com atual 0 é aceito] |
| **0x10A** desistir | `u32 guildUID` | **0x1BF** `u32 1` -> msg 10 = 1016 "가입 신청이 취소되었습니다"; depois 0x1BD classIdx 0 (0 com atual 9 = atualiza) |
| **0x10B** aprovar | `u32 guildUID, u32 userUID` (alvo classe 9) | **0x1C0** `u32 1` -> msg 15 = 1019 + recarrega membros. Ao aprovado (se online): 0x1BD classIdx 3 (3 com atual 9 = atualiza + emblema) |
| **0x10C** recusar | `u32 guildUID, u32 userUID` | **0x1C1** `u32 1` -> msg 16 = 1018. Ao recusado: 0x1BD classIdx 0 |
| **0x10D** cargo | `u32 guildUID, u32 userUID, u32 novoCargo` (1 delegar, 2 promover, 3 rebaixar) | **0x1C2** `u32 1, u32 X` -> msg 19(X) e recarrega membros: X=1 -> "길드장 권한을 위임하였습니다.\n길드원이 되었습니다." (1033+1077+1079), X=2 -> "부길드장 권한으로 변경하였습니다" (1034+1078), X=3 -> "길드원 권한으로 변경하였습니다" (1034+1079); outro X: sem texto. Mandar 0x1BD ao alvo (2<-3, 3<-2, 1<-2) e, na delegação, ao antigo mestre (3<-1). **Mestre -> submestre não é aceito pelo cliente** (CheckMyGuildState ignora 2 com atual 1): ao delegar, o antigo mestre vira membro (3). [C] |
| **0x10E** mensagem pessoal | `u32 guildUID, u32 userUID, str msg` (≤ 20 bytes, `'`->`` ` ``) | **0x1C3** `u32 1` -> msg 18 = 1030 + recarrega membros |
| **0x110** sair | `u32 guildUID` | **0x1C5** `u32 1` -> msg 12 = 1021 "길드를 탈퇴하였습니다"; 0x1BD classIdx 0 |
| **0x111** expulsar | `u32 guildUID, u32 userUID` | **0x1C6** `u32 1` -> msg 13 = 1017 + recarrega membros; ao expulso (online) 0x1BD classIdx 0 |

### 3.5 Pushes do servidor
| S->C | layout | efeito |
|---|---|---|
| **0x1BD** | `u32 1, GUILD_USER_INFO(0x119)` | msg 5 `CheckMyGuildState` (lobby e MyRoom; ignorado no jogo). Transições aceitas (novo<-atual): 0<-1/2/3 limpa tudo; 0<-9, 9<-0, 3<-9 (+emblema), 3<-2, 3<-1, 2<-3, 1<-2, 1<-0 atualizam; o resto é ignorado. Atualiza `sUserInfo` e recarrega membros se a janela estiver aberta. `u32 != 1` = caixa com o código. [C] |
| **0x3B** | `GUILD_INFO(0x129)` (sem result) | `OPC` @0x73BDF9 -> msg 22 `ChangeGuildEmblem`: se guildUID != 0, troca a marca na minha info, no `sUserInfo` e no meu `sBriefUserInfo` [C] |
| **0x3C** | `GUILD_INFO(0x129)` | `OPC` @0x73BE63 -> msg 23 `ChangeGuildName`: troca o nome na minha info e no `sUserInfo` [C] |
| **0x39** | `GUILD_USER_MSN_LIST(0x37)` | `OPC` @0x73BC05: adiciona o membro à lista de amigos do messenger com a marca da minha guilda e mostra "%s 님이 길드에 가입하였습니다." [C]; normalmente vem do messenger [P] |
| **0x3A** | `u32 uid` | `OPC` @0x73BD69: marca o membro como fora da guilda e mostra "%s 님이 길드를 탈퇴하였습니다." [C]; messenger [P] |
| 0x42 | ... `GUILD_USER_INFO` no fim | msg 6: `SetMyGuildUserInfo` + `sUserInfo` [C] |

### 3.6 Para o messenger (Send(1) = TO_MSN, servidor C# ainda não tem)
| C->S (MSN) | layout | quando |
|---|---|---|
| 0x25 | `str texto` | chat de guilda/amigos na caixa de chat, só com o messenger online (SPEC-chat-gm) |
| 0x2A | `u32 guildUID` | msg 24: depois do upload da marca |
| 0x2B | `u32 guildUID` | msg 25: depois de 0x1B5 (nome trocado) |
Sem messenger o envio se perde sem erro [P]. (O C->S **0x2A no game server é o cochicho**, outra coisa.)

S->C 0x1CD/0x1CE (que o emulador cita como "guild-battle gateway") vão para o ator **Gateway** (msg 0x26D), não para
o GuildActor: não são de guilda [C].

---

## 4. Emblema (marca da guilda)

### 4.1 Arquivo no cliente [C]
`FrRegisterGuildMark::OnMarkSearch` (`MD` L1541-1841): escolhe um arquivo local; precisa ser PNG (1052), 32 bits por pixel
(1059), largura < 0x17 (≤ 22) e altura < 0x15 (≤ 20) (1051); erros de leitura 1056/1058. Texto do cliente: "가로 22, 세로 20".

### 4.2 Upload [C]
1. C->S 0x112 `u32 guildUID` -> S->C 0x1C7 `u32 1, u32 emblemIdx, str emblema`.
2. `RequestUpload` monta `sResource{type=2 (GUILD_EMBLEM), filename=<caminho local>, id=emblemIdx, arg=emblema (char[64])}`
   e põe na fila de upload (`MD` L710-775, URL passada literal).
3. `WorkUploadThread` (`NRM` L4150-4167, L4187): `POST http://qa.contents.pangya.gametree.co.kr:50006/Guild/upload.asp`,
   HTTP/1.0, `User-Agent: MERONG(0.9/;p)`, `multipart/form-data; boundary=--MULTI-PARTS-FORM-DATA-BOUNDARY` (mesmo formato
   do login web e do Ghost). Campos, nesta ordem:
   - `EMBLEM_IDX` = emblemIdx (decimal)
   - `GUILD_IDX` = guildUID (pego do GuildActor, msg 0x21)
   - `UID` = meu UID (doc+0x4EB)
   - `EMBLEM` = string recebida no 0x1C7
   - `FILENAME` = arquivo (binário, `filename="<caminho local completo>"`)
4. Resposta: o corpo tem de **começar com `PANGYA_UPDATE_OK`**. Senão: fecha a espera (SendGlobalMsg) e o GuildActor
   recebe um aviso (o cliente procura `PANGYA_UPDATE_BLOCK` mas não usa o resultado). Com OK: C->S **0x113** (vazio) e
   msg 24 (MSN 0x2A). Em qualquer caso o laço termina com `DeleteFileA(res.filename)` (`NRM` L4289), que para o tipo
   guilda é o caminho do PNG escolhido pelo jogador [C].
5. S->C 0x1C8 `u32 1`.

Sugestão [P]: no 0x112 o servidor gera `emblemIdx` = id do upload pendente (tabela `guild_emblem_uploads`) e
`emblema` = nome novo da marca (≤ 11 chars, ex. `g{guildId}_{versão}` em hex), para cada versão ter outro nome (o
cliente guarda cache em `<pasta do jogo>\emblem\<nome>.png`, então reaproveitar o nome não atualiza a imagem). O
`Pangya.Web` aceita o POST se `EMBLEM_IDX`/`GUILD_IDX`/`UID`/`EMBLEM` batem com o pendente, confere PNG 32 bits ≤ 22×20
e tamanho pequeno, grava `<dados>/GuildMark/<emblema>.png` e responde `PANGYA_UPDATE_OK`. No 0x113 grava
`guilds.mark = emblema`, consome o kit, responde 0x1C8 e avisa os membros online (0x3B ou 0x1BD).

### 4.3 Download [C]
`NetResourceManager::GetEmblemByName(nome)` (`NRM` L6352-6364): se o nome não é vazio, baixa
`http://qa.contents.pangya.gametree.co.kr:50006/_Files/GuildMark/<nome até 12 chars>.png` para a pasta `emblem`.
Usado na janela de guilda (marca da minha guilda e da guilda selecionada), na lista de usuários do lobby, nos slots da
sala, na lista de amigos, no jogo (rival.guildMark) — todo lugar que tem `szEmblemName`.

### 4.4 URLs no exe (`ProjectG_ReleaseQA.exe`, VA = offset + 0x400000) [C]
| uso | texto | offset | tamanho (espaço até a próxima string) |
|---|---|---|---|
| upload | `http://qa.contents.pangya.gametree.co.kr:50006/Guild/upload.asp` | 0x613AE0 | 63 (+NUL = 64) |
| download | `http://qa.contents.pangya.gametree.co.kr:50006/_Files/GuildMark/` | 0x613B20 | 64 (68 com NULs) |
| RSS base | `http://qa.club.pangya.gametree.co.kr/InGame` | 0x613BA8 | 43 (44) |
| RSS formato | `%s/NoticeRSS.aspx?gIdx=%d\n` | 0x613B8C | 26 (28) |
Cada uma aparece **uma vez** no exe, então `replace_slot` do `tools/client/make_client.py` redireciona tudo, ex.
`http://127.0.0.1:30080/Guild/upload.asp` e `http://127.0.0.1:30080/_Files/GuildMark/` (cabem). Alternativa: hosts
apontando `qa.contents.pangya.gametree.co.kr` para 127.0.0.1 e o `Pangya.Web` ouvindo também na 50006 [P].

### 4.5 RSS de notícias da guilda [C]
No 0x42 (`OPC` @0x73427B): se `sUserInfo.dwGuildId` (@0x6B) != 0 chama `RequestGuildRSS(guildId, RecvRSS0)`: thread que
faz `InternetOpenUrl("http://qa.club.pangya.gametree.co.kr/InGame/NoticeRSS.aspx?gIdx=<id>\n")` (porta 80, `\n` no fim
da URL) e grava `guildlast.xml`; compara `<rss><ver>` com `guildnews.xml`. `RecvRSS0` é só `ret`; falhas só zeram flags.
O messenger consulta `IsRSSDownLoadComplete` (aviso de notícia nova). **Não precisa implementar**; se um dia quiser,
servir `<rss><ver>N</ver><notice>...</notice></rss>` (porta 80 é do emulador — não usar; redirecionar a URL pelo exe).

---

## 5. Itens consumidos [C dados do `data/pangya.iff`, interpretação P]
| typeid | nome (Item.iff) | descrição (Desc.iff) | uso | preço no Item.iff |
|---|---|---|---|---|
| 0x1A00012F | 길드생성키트 | 길드를 생성할 때 필요한 아이템 | abre FrCreateGuildKit (`create_guild`) | 50 000 pang |
| 0x1A000130 | 길드마크변경키트 | 길드 마크를 변경할 때 필요한 아이템 | abre FrRegisterGuildMark (só gestor) | 30 000 pang |
| 0x1A000131 | 길드이름변경키트 | 길드 명을 변경할 때 필요한 아이템 | abre FrChangeGuildName (só gestor e com guilda; sem guilda msg 1040) | 49 (bit IsCash ligado -> cookies) |
Os três têm `Level = 11` no registro (Junior E; nível para comprar) [P]. O cliente **não** checa nível para criar a
guilda; o código 54004 "레벨이 부족합니다" existe para o servidor recusar [C]. O kit é usado direto do inventário da
MyRoom (`realmyroommainui.c` L25263) sem pacote; só o 0xFE/0x100/0x112 vão ao servidor. O cliente não tira o kit do
inventário sozinho [P] — usar o mesmo caminho de "item gasto" de `GameHandler.Boxes.cs`.

Custo em pang para criar além do kit: nenhum indício no cliente [P: não cobrar].

---

## 6. Códigos e mensagens (`CD` L579-792) [C]
Códigos do servidor (o `u32 result`):
| código | texto | uso sugerido |
|---|---|---|
| 1 | 성공하였습니다 | sucesso |
| 0 | 정의되지 않은 오류입니다 | — |
| 54001 | 길드 정보 처리 실패입니다 | erro genérico |
| 54002 | 존재하지 않는 계정입니다 | alvo não existe |
| 54003 | 이미 동일한 이름의 길드가 있습니다 | nome em uso (0xFF/0xFE/0x100) |
| 54004 | 레벨이 부족합니다 | nível baixo para criar |
| 54005 | 이미 길드에 가입되어 있거나 가입 신청 중입니다.. | criar/pedir já estando em guilda |
| 54006 | 길드 개설/폐쇄/탈퇴 후 24시간이 지나지 않았습니다 | cooldown 24 h |
| 54008 | 명령을 실행할 권한이 없습니다 | sem permissão |
| 54009 | 신청한 길드의 인원이 모두 차서 더는 가입할 수 없습니다 | guilda cheia |
| 54010 | 요청한 유저가 길드원이 아닙니다 | alvo não é membro |
| 54011 | 부길드장이 되었습니다 | (informativo) |
| 54012 | 부길드장 제한 인원 초과입니다 | limite de submestres |
| 54013 | 길드장 추방 | — |
| 54014 | 남아있는 길드원이 있기 때문에 길드를 폐쇄할 수 없습니다 | encerrar com membros |
| 54015 | 길드가 존재하지 않습니다 | guildUID inválido |
| 54016 | 길드원이 남아 있는 상태에서 길드를 탈퇴할 수 없습니다 | mestre saindo |
| 54017 / 54510 | 이미 길드에 가입되어 있습니다 | já membro |
| 54018 | 가입 대기 중인 길드원이 아닙니다 | aprovar/recusar quem não pediu |
| 54019 | 가입 신청 상태가 아닙니다 (o mesmo id aparece duas vezes no mapa; vale o 1º: "오류가 발생하였습니다.(길드 생성 번호)") | — |
| 54020 | 가입 대기자가 아니므로 길드 가입 철회를 요청할 수 없습니다 | 0x10A sem pedido |
| 54021 | 가입 대기자는 길드를 탈퇴할 수 없습니다 | 0x110 de classe 9 |
| 54022 / 54030 | marca bloqueada / erro ao salvar guild battle | |
| 54500 | 데이터 베이스 오류 | erro de banco |
| 54501 | 시스템 시간 변환 실패 | |
| 54502 | 아이템을 소유하고 있지 않습니다 | sem o kit |
| 54503 | 길드 생성은 성공하였으나, 아이템 삭제에 실패하였습니다 | |
| 54504 | 현재 등급에서는 사용할 수 없는 기능입니다 | cargo não permite |
| 54505 | 길드장이 아닙니다 | precisa ser mestre |
| 54506 | 부길드장이 아닙니다 | |
| 54507 | 관리권한이 없습니다 | precisa ser gestor |
| 54508 | 길드에 가입되어 있지 않습니다 | sem guilda |
| 54509 | 가입 대기자가 아닙니다 | |
| 54511 | 유효하지 않은 길드상태입니다. 재접속 후 다시 시도해 주세요 | estado do cliente desatualizado |

Mensagens locais 1000-1084 (textos de interface): 1000 digite o nome, 1001 apresente a guilda, 1005-1007 títulos dos
kits, 1008-1010 menus, 1011/1012 limites de promoção, 1013-1022 pedidos/saída, 1024/1025 encerrar, 1026/1027
intro/notícia ok, 1028/1029 nome, 1030 msg pessoal ok, 1031 criada, 1032-1034 delegação/cargo, 1035 nenhuma guilda
encontrada, 1036-1038, 1039 nome trocado, 1040 sem guilda, 1041-1046 filtro de texto, 1047 confirmar saída,
1048/1049 entrou/saiu, 1050-1059 marca/arquivo, 1060 texto não mudou, 1061-1076 histórico, 1077-1080 cargos.

---

## 7. Guilda no resto do jogo

| onde | o que o servidor precisa preencher | marca |
|---|---|---|
| 0x42 | `sUserInfo.sGuild`, `szEmblemName`, `dwGuildId` (@0x6B), +0x6F = guildPang; `GUILD_USER_INFO` completo com o cargo | [C] |
| lista do lobby | `sBriefUserInfo.m_GuildId`, `szEmblemName` | [C] campos |
| sala | `sSlotInfo.sGuild`, `GuildId`, `szEmblemName`; em GuildMatch também `sRoomInfo.GuildInfo` | [C] campos |
| convite (0xB2) | código 0x15 = "não está na guilda" no 0x127 (SPEC-coverage) | [R] |
| chat de guilda | messenger 0x25 | [C] destino; sem servidor |
| perfil | 0x153 troféus de guilda (`u8 season, u32 uid, u16 n, n × u32 TypeID`), guildPang/guildPoint no perfil | [C] (SPEC-perfil-mapas) |
| GuildMatch (modo 6) | ver abaixo | |

### GuildMatch (gameType 6) [C salvo indicação]
- Entrar na sala (`lobbymain.cpp` L9586-9615): o cliente exige `sUserInfo.dwGuildId != 0` ("길드에 가입하셔야
  길드대전이 가능합니다.") e que a guilda seja uma das duas da sala ou que haja vaga (`nGuildID[k] == 0`), senão
  "타길드의 대전입니다.". `DetailedRoomInfoDlg` repete a regra.
- Times: vermelho = jogadores com `GuildId == GuildInfo.nGuildID[0]`, azul = `nGuildID[1]` (`RemakeTeam` L7939-7975);
  se um lado fica vazio o cliente zera aquele `GuildInfo` (Reset(k)). A lista da sala mostra "VS" com nome/emblema das
  duas guildas (`sGuildRoomInfo`). Auto-start: 6 s e **não** inicia sozinho em GuildMatch.
- No jogo: `CGolfRuleGuildMatch::SetPlayer` copia `sGuild`, `GuildId`, `szEmblemName` do slot para cada rival; o placar
  (`scoredlg`) soma por guilda.
- Resultado: FrUniteResultDlg com 4 × u32 extras (Doc+0x5018/0x501C/0x47B4/0x47B8 = pontos/placar das guildas; já
  zerados em `SendMassResult(..., guild: true)`) — SPEC-resultado-fim-de-jogo.
- Pontos/pang de guilda por vitória: o cliente não define. GB (`GuildRoomManager.calcGuildWin`) decide por pontos e
  desempate por pang, pang de prêmio `(duplas+quits)×50` para o vencedor [R, versão bem mais nova, com "duplas"].

---

## 8. Comparação com as referências

| fonte | o que tem | serve para |
|---|---|---|
| emulador `ext_guild.py` + `notes_guild.md` | todos os C->S 0xFE-0x113 com layout certo; responde só falhas (54001/54015/54508/54020), 0xFF diz "nome livre", 0x107/0x10F vazios; 0x105/0x106 em `player.py` | base de layout já testada pelo cliente scriptado [R]; corrige-se aqui: 3º u32 do 0x1BA/0x1C4 = **total** (não páginas); 0x1C7 = `emblemIdx` + nome da marca; 0x1CD/0x1CE não são de guilda |
| GB (`Server/GB`) | ids deslocados (+3: 0x101 criar, 0x102 checar nome, ...), handlers de gestão todos **comentados**; só GuildBattle (duplas) e leitura de `ProcGetGuildInfo` (uid, nome, marca, index_mark_emblem, point, pang) | regras de guild battle e campos de banco [R] |
| S6 (`ref/Pangya-Server-Source-master/S6`) | só enum de ids (também deslocados: 0x101 create, 0x102 avaiable, 0x104 data, 0x108 list, 0x109 search, 0x10A log, 0x10C join, 0x10D cancel, 0x10E accept, 0x110 promote, 0x112 players, 0x113 leave, 0x114 kick, 0x115/0x116 upload) e modelo EF: `Pangya_Guild_Info` (INDEX, NAME, INTRODUCING, NOTICE, LEADER_UID, POINT, PANG, IMAGE, IMAGE_KEY_UPLOAD, CREATE_DATE, VALID), `Pangya_Guild_Member` (GUILD_ID, MEMBER_UID, POSITION, MESSAGE, ENTERED_TIME, MEMBER_STATUS), `Pangya_Guild_Log` (UID, GUILD_ID, GUILD_NAME, ACTION, ACTION_DATE), `Pangya_Guild_Emblem` (EMBLEM_IDX, GUILD_ID, MARK_IMG, ISVALID) | desenho das tabelas [R]; confirma que o histórico é por usuário (UID) e o fluxo de upload com chave (`IMAGE_KEY_UPLOAD` ≈ `emblemIdx`) |

---

## 9. Banco (PostgreSQL) — sugestão [P]

```sql
-- 005_guilds.sql
create table guilds (
    id           int generated always as identity primary key,      -- guildUID (u32 no protocolo, nunca 0)
    name         text not null,                                      -- até 20 bytes cp949
    name_key     text not null unique,                               -- nome normalizado (lower/trim) para unicidade
    master_id    bigint not null references accounts(id),
    notice       text not null default '',                           -- até 100 bytes cp949
    introduce    text not null default '',                           -- até 100 bytes cp949
    mark         text not null default '',                           -- nome do arquivo do emblema (<= 11 chars)
    pang         int not null default 0,
    point        int not null default 0,
    mark_blocked boolean not null default false,                     -- 54022 / histórico 51-52
    created_at   timestamptz not null default now(),
    closed_at    timestamptz                                         -- encerrada = não aparece nas listas
);

create table guild_members (
    account_id   bigint primary key references accounts(id) on delete cascade,  -- 1 guilda (ou pedido) por jogador
    guild_id     int not null references guilds(id) on delete cascade,
    class        smallint not null check (class in (1, 2, 3, 9)),   -- 9 = pedido pendente (guild_requests embutido)
    message      text not null default '',                           -- msg pessoal (<= 20 bytes) ou texto do pedido
    joined_at    timestamptz not null default now()
);
create index guild_members_guild_ix on guild_members (guild_id, class);

create table guild_history (                                         -- histórico POR JOGADOR (0x107)
    id           bigint generated always as identity primary key,
    account_id   bigint not null references accounts(id) on delete cascade,
    guild_id     int not null,
    guild_name   text not null,
    state        smallint not null,                                  -- GUILD_STATE_IDX (§2.3)
    at           timestamptz not null default now()
);
create index guild_history_account_ix on guild_history (account_id, id desc);

create table guild_emblem_uploads (                                  -- ticket do 0x1C7 até o 0x113
    id           int generated always as identity primary key,       -- emblemIdx
    guild_id     int not null references guilds(id) on delete cascade,
    account_id   bigint not null,
    mark         text not null,                                      -- nome novo
    uploaded     boolean not null default false,
    created_at   timestamptz not null default now()
);

-- cooldown de 24 h (54006): última criação/encerramento/saída do jogador
alter table players add column guild_cooldown_until timestamptz;
```
Pedido pendente como `class = 9` em `guild_members` (é o que o protocolo usa: o jogador aguardando "está" na guilda
com classe 9, e o gestor o vê na lista de membros); por isso não precisa de `guild_requests` separado. Regra 13 (sem
LINQ em src/): cache em memória `Dictionary<int, Guild>` + `Dictionary<long, GuildMember>`.

---

## 10. Lista priorizada do que implementar

**Fase A — guilda existe e aparece (base)**
1. [C] Migração 005 + repositório (`GuildRepository`) e cache em memória.
2. [C] 0x42: `GUILD_USER_INFO` real (com o cargo) + `sUserInfo.sGuild/szEmblemName/dwGuildId(@0x6B)/+0x6F`.
   guildId != 0 é seguro (RSS sem efeito).
3. [C] `sBriefUserInfo.m_GuildId/szEmblemName` e `sSlotInfo.sGuild/GuildId/szEmblemName`.
4. [C] 0x105/0x106 -> 0x1BA/0x1BB com páginas de 15 e total de guildas (busca por nome com ≥ 4 bytes).
5. [C] 0xFF -> 0x1B4 (nome livre? filtro 4..20 bytes, sem espaço) e 0xFE -> 0x1B3 + **0x1BD** (kit 0x1A00012F no
   inventário, consumir; 54005/54006/54004/54003); histórico estado 8.
6. [C] 0x101 -> 0x1B6 (GUILD_INFO com o cargo de quem pede), 0x107 -> 0x1BC (histórico do jogador), 0x10F -> 0x1C4
   (membros + pendentes, online pelo `World`).

**Fase B — gestão**
7. [C] 0x109/0x10A/0x10B/0x10C (pedido, desistência, aprovar, recusar) com 0x1BD para quem estiver online; histórico 1/2/3/4/13.
8. [C] 0x110 sair, 0x111 expulsar, 0x10D cargos (incl. delegação: antigo mestre vira 3), 0x10E msg pessoal,
   0x102/0x103 notícia/apresentação (`` ` `` -> `'` ao gravar [P]), 0x104 encerrar (só sem membros) — todas
   revalidando cargo no servidor (§2.2) e mandando 0x1BD aos afetados; histórico 5/6/7/9/10/11/12.
9. [P] Limites: membros por guilda (54009) e submestres (54012) em `PangyaConfig`.

**Fase C — emblema e nome**
10. [C] 0x112 -> 0x1C7 (ticket + nome versionado), `Pangya.Web`: `POST /Guild/upload.asp` (multipart, responde
    `PANGYA_UPDATE_OK`) e `GET /_Files/GuildMark/{nome}.png`; 0x113 -> 0x1C8, consumir 0x1A000130, avisar membros (0x3B).
11. [C] Redirecionar as 2 URLs no `make_client.py` (offsets em §4.4).
12. [C] 0x100 -> 0x1B5 (kit 0x1A000131) + 0x3C para os membros online; 0xFF já serve.

**Fase D — GuildMatch e extras**
13. [C/P] Modo 6: `sRoomInfo.GuildInfo` (1ª guilda = criador, 2ª = primeiro de outra guilda), validação de entrada,
    times por GuildId, 4 × u32 no resultado, pontos/pang de guilda [R GB].
14. [C] Troféus de guilda (0x153) e guildPang/guildPoint no perfil.
15. [P] Convite de guilda (0xB2 código 0x15).

**Fase E — depende do messenger (fora do escopo atual)**
16. Chat de guilda (MSN 0x25), 0x2A/0x2B, pushes 0x39/0x3A na lista de amigos, RSS de notícias.
