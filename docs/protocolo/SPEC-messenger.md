# KR 645 — Servidor de mensageiro (메신저: amigos, online/offline, bate-papo, bilhetes, guilda)

Pesquisa somente leitura no cliente KR 645 QA (`ProjectG_ReleaseQA.exe`) para implementar o servidor de mensageiro
(MSN) em C#.

Marcas: **[C]** confirmado no cliente (fonte rebang, que reconstrói o exe byte a byte, decompile ghidra ou `pg645.asm`);
**[R]** tirado de uma referência (servidor GB `Server/GB/MessengerServer`, servidor JP
`Server/Modern/JP/Pangya_MessengerServer`, notas do emulador Python `coverage-notes/notes_msn_rank.md`); **[P]** palpite.

Fontes (abreviações):
- `src/` = `/root/rebang/source/client/ProjectG/`, `GD` = `/root/rebang/source/shared/globalgamedefine.h`
- `MD` = `/root/ghidra-out/messengerdlg.c` (FrMessengerDlg / CMessengerInfo), `LT` = `/root/ghidra-out/lobbytask.c`
- `OPC` = `CTask::OnPacketCommon` @0x732640 (o ghidra-out não tem o decompile; endereços abaixo são do `pg645.asm`,
  com os textos coreanos resolvidos no `.rdata` do exe). Tabela principal: índice = id-0x2D, bytes @0x746118,
  ponteiros @0x745E1C. Sub-tabela do 0x2E: índice = sub-0x102, bytes @0x746748, ponteiros @0x7466F0.
- `GB` = `Server/GB/MessengerServer/MessengerServerTcp/MessengerServer.cs`, `JP` = `.../Flags/PacketMessengerFlags.cs`
  e `.../Handles/*.cs`. GB e JP são de versões mais novas: **os ids S->C do JP/GB são os do KR + 2** (hello 0x2E, login
  0x2F, sub-pacotes 0x30, guilda 0x3B...), mas os ids C->S e os sub-ids (0x102..0x123) são os mesmos. [R]

---

## 0. Resumo

- O cliente tem **4 sockets**: GAME, MSN, LOGIN, RANK (`src/networksystem.cpp:9-18`). `Send(TO_MSN)` = `Send(1)`
  (`src/packet.cpp:744-772`). [C]
- **O cliente não sabe de qual socket veio um pacote.** `MSNUnit::OnLine` entrega tudo para `CProjectG::OnPacket`
  (`src/msnunit.cpp:33-43`), igual ao GameUnit; `CProjectG::OnMsnPacket` é vazio (`src/projectg.cpp:1676`,
  ghidra `projectg.c` @0x796970). Os pacotes do MSN são tratados no `OnPacketCommon` comum, num espaço de ids que não
  colide com o game: **MSN S->C = 0x2C (hello), 0x2D, 0x2E (com sub-id), 0x30-0x3C**. [C] Consequência: o game server
  já pode mandar 0x31/0x36/0x39/0x3A/0x3B/0x3C (o C# já manda 0x31, 0x3B e 0x3C pelo game).
- Conexão: **só quando o jogador abre o mensageiro** (tecla/ botão MESSENGER). Endereço = lista do login **0x09**
  (ou game **0x88 -> 0xFA**). Hello `0x2C` com chave u32 (mesma cifra do game), cliente manda **0x12 u32 uid, Str nick**
  (sem chave de autenticação!), servidor responde **0x2D u8 0, u32 uid**; o cliente manda 0x23 (posição) e 0x14 (pedir
  lista); servidor responde **0x2E sub 0x102** (lista de amigos) e o cliente fica "online" (estado 4). [C]
- Sem mensageiro nada trava: o cliente mostra textos na janela do mensageiro e cai em alternativas do game (0x07, 0x3C).
  Mas com um mensageiro "meio vivo" (conecta e não manda 0x102 em 60 s) o cliente **derruba e reconecta a cada 60 s**. [C]
- Bilhetes (쪽지) **sempre vão pelo game** (C->S 0x3C sub 0x111, resposta S->C **0x93** sub 0x111). O MSN só entrega a
  lista de bilhetes (0x2E/0x103) e tem uma resposta antiga 0x2E/0x112. [C]
- Guilda: o mensageiro mostra uma aba "길드" com os membros (entradas `sFriend` com o bit GuildFriend); chat de guilda é
  C->S **0x25** e volta como 0x2E/0x113 com flag 1. [C]+[R]

---

## 1. Structs (pack 1, cp949)

### 1.1 sGameServerInfo (0x5C = 92) — `GD:1039` [C]
`char name[40] +0, u32 id +0x28, i32 maxUser +0x2C, i32 curUser +0x30, char addr[18] +0x34, i32 port +0x46,
u32 property +0x4A, i32 angelicWingsNum +0x4E, u32 eventFlags +0x52, u32 eventValue +0x56, u16 iconIndex +0x5A`.
Usada pelo login 0x09 e pelo game 0xFA (mesmo struct que a lista de game servers).

### 1.2 sUserPosition (0x4B = 75) — `GD:372` [C]
| off | tipo | campo | valor |
|---|---|---|---|
| 0x00 | u16 | iRoomIdx | número da sala, 0xFFFF = fora de sala |
| 0x02 | i32 | iRoomType | tipo de jogo da sala (Doc+0x49E0), -1 = nenhum |
| 0x06 | i32 | iServerGUID | uid do game server (Doc+0x84 = `m_gameServerUID`), -1 = offline |
| 0x0A | u8 | iChannelUid | canal, 0xFF = nenhum |
| 0x0B | char[64] | csChannelName | nome do canal |

O `sIntrusionEnterInfo` (`src/intrusion.cpp:109-116`) tem o mesmo layout. [C]

### 1.3 sFriend (0x8D = 141) — `GD:1062` [C]
| off | tipo | campo | uso no cliente |
|---|---|---|---|
| 0x00 | char[22] | NickName | nick exibido (coluna da direita) |
| 0x16 | char[11] | szAlias | "apelido"/memo (coluna da esquerda); o cliente escreve `Friend`, `(대기중)`, `(요청중)` |
| 0x21 | u32 | Uid | **chave do mapa** do BuddyManager |
| 0x25 | u32 | Guid | o 0x2E/0x115 grava o uid aqui; GB manda -1 |
| 0x29 | u32 | Server | GB manda 0; o 0x10A zera |
| 0x2D | u32 | dwGuildId | ≠0 -> desenha o emblema `szEmblemName`; -1/0 = sem guilda |
| 0x31 | char[12] | szEmblemName | nome da marca (`NetResourceManager::GetEmblemByName`) |
| 0x3D | sUserPosition | userPosition | onde o amigo está (para "따라가기" e convite) |
| 0x88 | u8 | State | GB: estado do amigo (0 jogo, 1 ausente, 3 ocupado, 4 online, 5 offline); o cliente não lê [P] |
| 0x89 | u8 | Channel | GB: 0xFF |
| 0x8A | u8 | GameLevel | GB: nível (ou 1/0 = mestre/membro para guilda) [R] |
| 0x8B | bits | b0 Gender, b1 **IsLogOn**, b2 **IsAccept**, b3 **IsAgree**, b4 **IsBlock**, b5 IsPlay, b6 IsDive, b7 IsBusy | |
| 0x8C | bits | b0 **PangyaFriend**, b1 **GuildFriend**, b2 **IsBlocked**, b3-7 reservado | |

Significado dos bits (`MD` `FrMessengerDlg::UpdateFriendList` @0x695EF0, `GetBuddyStatus` @0x6911F0) [C]:
- Aba Amigos, **online**: PangyaFriend && IsAccept && IsLogOn. Os demais com PangyaFriend vão para **offline**; ali, se
  IsAccept=0 e IsAgree=0, o alias vira `(대기중)` ("aguardando" = ele me pediu, falta eu aceitar); se IsAgree=1 e
  IsAccept=0, vira `(요청중)` ("pedido feito" = eu pedi). IsBlocked (b2 do 0x8C) apaga IsLogOn.
- Aba Guilda: GuildFriend && IsLogOn = online; GuildFriend && !IsLogOn = offline. Para os online o cliente copia a
  minha guilda/marca para dwGuildId/szEmblemName.
- Ícone de status: IsBlock -> 2 (`block_icon`); IsBusy -> 3 (`busy_icon`); IsPlay && IsLogOn -> 0 (`play_icon`);
  IsDive && IsLogOn -> 1 (`dive_icon`, ausente); senão sem ícone. Ícone de sexo pelo bit Gender.

### 1.4 sNoteInfo (0x6D = 109) — `GD:1134` [C]
`u32 uid +0, u16 reserved +4, char sNick[22] +6, char sNote[64] +0x1C, char sTime[16] +0x5C, bool bReply +0x6C`.

### 1.5 GUILD_USER_MSN_LIST (0x37 = 55) — `GD:576` [C]
`u32 userUID +0, u32 guildUID +4, u8 sex +8, char userID[22] +9, char nickname[22] +0x1F, u8 unknown35 +0x35,
u8 online +0x36`.

---

## 2. Conexão

### 2.1 De onde vem o endereço [C]
1. **Login S->C 0x09** `u8 n, n × sGameServerInfo(92)` -> limpa e preenche `CMessengerInfo::m_serverList`
   (`LT` L3089-3114). O C# já manda (`LoginHandler.cs:128`, registro tipo `"messenger"`).
2. **Game C->S 0x88** (sem payload) -> **S->C 0xFA** `u8 n, n × sGameServerInfo(92)` (OPC idx 100: limpa a lista,
   preenche e chama `SetServerListWaitingState(false)`). O cliente manda 0x88 (`src/msnunit.cpp:216-231`) quando vai
   conectar e a lista está vazia, no máximo a cada 30 s e só se não estiver esperando uma resposta anterior.
   **A lista é apagada em todo `MSNUnit::ShutDown`** (`src/msnunit.cpp:26-31`: logout, FD_CLOSE, ida ao LOGIN, falha
   do game), então depois da primeira queda só o 0xFA traz o endereço de volta. Sem resposta ao 0x88 o flag de espera
   fica preso e o mensageiro nunca mais reconecta naquela sessão.
3. Escolha (`GetConnectServerInfo`, `src/msnunit.cpp:172-206`): ignora entradas inválidas; se
   `curUser >= maxUser - 150` marca a entrada como inválida com chance de 2/3 (`rand() % 3`); entre as restantes pega a
   de **menor curUser**. **Use maxUser ≥ curUser + 151** (ex.: 3000) senão o cliente descarta o servidor. `addr` passa
   por `gethostbyname` (aceita nome ou IP). Falha de conexão (erro WSA ou 10 s sem hello) -> `ResetConnect` ->
   `InvalidateCurrentServer` (próximo da lista).

### 2.2 Quando conecta [C]
- Só por `CMessengerInfo::Open` (`MD` @0x6976F0), chamado pela tecla/ botão do mensageiro (`src/projectg.cpp:1000`,
  `taskmain.c:7150`, `golfrule.c:5188`, `avatar_mainui.c:1140`). Se já conectado, só mostra/esconde a janela.
  Se não, mostra "메신저서버에 접속중...." (conectando ao servidor de mensageiro) e inicia; mais de 5 tentativas em
  ~90 s -> "잠시 후에 이용해 주십시오." (tente mais tarde).
- `MSNUnit::OffLine` reconecta a cada 60 s só com `CProjectG::m_bMsnOffline == true`, que nunca é ligado
  (`src/logindlg.cpp:856` zera; nenhum setter). [C]
- Fica conectado ao fechar a janela; cai em: logout (manda 0x16 antes, `src/logoutdlg.cpp:127-133`), volta ao LOGIN
  (`src/lobbymain.cpp:6338-6341`), falha ao conectar no game (`src/gameunit.cpp:73`), FD_CLOSE/erro
  (`src/projectg.cpp:1352-1398`).
- `CMessengerInfo::Process` (`MD` @0x697B80): se conectado (MSN e GAME) e o estado não chega a **4 em 60 s**
  (float 60.0), derruba e reconecta. Também faz o "ausente automático": 120 s sem mouse/teclado -> `SetMyStatus(1)`;
  ao mexer -> `SetMyStatus(4)` (só se o meu status não for 3).

### 2.3 Hello e cifra [C]
- Primeiro pacote do servidor, **cru** (sem cifra), igual ao do game: `u16 id = 0x2C, u8 ?, u8 ?, u32 parseKey`
  (`src/msnunit.cpp:112-125`; asm @0x7812CE-0x78130D: `Decode1`, `Decode2 == 0x2C`, estado = 1, `Decode1`, `Decode1`,
  `Decode4` -> chave). No C#: `conn.SendRaw(new PacketWriter(0x2C).U8(0).U8(0).U32((uint)key))` com `key` em 0..15
  (índice das mesmas tabelas `PublicKeyTable/PrivateKeyTable`; compare: game `0x3D u8 u8 u8 key`, login
  `0x00 u32 key u32 serverUid`).
- Depois disso os dois lados usam a cifra normal (`PacketCipher.SealServer` / `OpenClient`), igual ao game.
- O contador de pacotes do cliente (byte 3 do cabeçalho) **não anda** para pacotes que não são do game
  (`WSendPacket::Send`: `if (to != TO_GAME) ms_nPacketCount--`, `src/packet.cpp:744-747`); o C# não confere o contador.
- O cliente não manda heartbeat ao MSN (o `SendTTL` 0x01 é só para o game, `src/networksystem.cpp:78-87`). **A conexão
  do MSN tem de ter `IdleTimeoutSeconds = 0`** (como o login), senão o servidor derruba o jogador parado.

### 2.4 Login no mensageiro [C]
- **C->S 0x12** `u32 myUID, Str myNick` (`src/msnunit.cpp:130-133`). O cliente lê id e senha mas não manda nada disso.
  O servidor deve validar: uid existe, nick bate com o banco, o jogador está logado em algum game server (de preferência
  do mesmo IP) — GB faz isso via auth server [R]. Derrubar a sessão antiga do mesmo uid.
- **S->C 0x2D** (OPC @0x73B8C9):
  - `u8 0, u32 uid` com **uid == MyUID** -> estado 2 ("로그인에 성공했습니다"), o cliente manda **0x23**
    (sUserPosition atual) e **0x14** (vazio) e passa ao estado 3 ("친구리스트 받아오는 중", recebendo lista).
    uid diferente: nada acontece (fica no estado 1 e reconecta em 60 s).
  - `u8 2` -> Notify "메신저서버에 접속하기 위한 로그인정보가 잘못되어 로그인하지 못하였습니다." (dados de login
    inválidos) e o cliente fecha o MSN.
  - outro código: ignorado (GB manda `u8 1` no erro [R]; no KR prefira 2).
- **S->C 0x2E sub 0x102** (lista) -> estado 4 = online. Só no estado 4 o cliente manda 0x1D, 0x22, 0x23 (fora o do
  login), 0x25 e mostra a lista.
- Estados (`m_complete`, texto no topo da lista, `MD` `DrawFriendGroup` @0x691A70): 0 "접속하지 않음" (desconectado),
  1 "로그인을 요청했습니다" (login pedido), 2 "로그인에 성공했습니다", 3 "친구리스트 받아오는 중", 4 lista
  (vazia: "친구 없음" / "길드원 없음"). [C textos; ordem exata texto↔estado P para 2/3]

---

## 3. Pacotes C->S (cliente -> MSN)

Todos verificados nos pontos de envio [C]; a semântica da resposta vem do handler no cliente [C] e do GB [R].

| id | payload | quando (fonte) | o servidor deve |
|---|---|---|---|
| 0x12 | `u32 uid, Str nick` | após o hello (`src/msnunit.cpp:130`) | validar e responder 0x2D (§2.4) |
| 0x13 | `u16 0x11F` | lista de amigos para correio/presente quando o BuddyManager está vazio e o MSN conectado (`src/post_senddlg.cpp:1000-1008`, `src/giftdlg.cpp:3800-3818`) | responder 0x2E/0x102 [P] (GB só confere a sessão [R]) |
| 0x14 | vazio | logo após 0x2D ok (OPC @0x73B8C9) | 0x2E/0x102 (lista, pode paginar) + opcional 0x2E/0x103 (bilhetes) e 0x38 (guilda); avisar amigos online com 0x2E/0x10E |
| 0x16 | vazio | logout (`src/logoutdlg.cpp:127-129`) e resposta ao 0x2E/0x122 | nada; o cliente fecha. Avisar amigos com 0x2E/0x10F |
| 0x17 | `Str nick` (1..21 chars, ≠ o meu) | botão "확인" da janela Adicionar amigo (`src/addfrienddlg.cpp:160-180`) | 0x2E/0x117 |
| 0x18 | `u32 uidAlvo, Str nick` | OK da janela Adicionar amigo (`src/addfrienddlg.cpp:219-227`) | 0x2E/0x104 ao pedinte; 0x2E/0x106 ao alvo online |
| 0x19 | `u32 uid` | menu "친구허가" (aceitar) (`MD` `OnAcceptFriend` @0x6919F0) | 0x2E/0x109 a quem aceitou; 0x2E/0x10A ao outro |
| 0x1A | `u32 uid` | "차단하기" (bloquear), após confirmação (`MD` @0x6924F0) | 0x2E/0x10C |
| 0x1B | `u32 uid` | "차단해제" (desbloquear) (`MD` @0x692590) | 0x2E/0x10D |
| 0x1C | `u32 uid, Str nick` | "대화상대삭제" (apagar) (`MD` @0x692430) | 0x2E/0x10B a quem apagou; GB também manda 0x10B ao outro [R] |
| 0x1D | `u8 status` | `SetMyStatus` (`MD` @0x692670), só no estado 4: 4 = "온라인", 3 = "다른용무중" (ocupado), 1 = ausente automático | guardar e mandar 0x2E/0x115 aos amigos online |
| 0x1E | `u32 uidAlvo, Str msg` | janela de conversa (`src/messengerchatdlg.cpp:133-170`), só com o alvo IsLogOn e não bloqueado; também a resposta automática "상대방이 응답할 수 없습니다." (não pode responder) quando as janelas de conversa estão cheias (`MD` `RecvChat` @0x698580) | alvo online: 0x2E/0x113 (flag 0) ao alvo; offline: 0x2E/0x114 `u8 3, u32 uid` ao remetente |
| 0x1F | `u32 uid, Str apelido` | "별명입력" (`MD` `OnMemoDlgResult` @0x692350) | gravar e responder 0x2E/0x119 |
| 0x21 | `Str nick, u32 myUID` ou `u32 myUID, u32 uidAlvo` | avisos de presente/loja/evento de pontos (`taskmanager.c` `_ReFreshMyRoomGift` @0x72E930, `shoptask.c:2051`, `pointeventdlg.c:3638/3664`, `src/shoptask.cpp:281-288`, só com 0x60 desligado) | avisar o alvo (0x31) [P] |
| 0x22 | `Str nick` | após respostas 0x4F/0x69 do game, só no estado 4 [R notas] | nada / avisar [P] |
| 0x23 | `sUserPosition (0x4B)` | após o login MSN, troca de canal (`serverdlg.c:1960-1976`, sala -1), entrar em sala (`LT` ~L3744-3757), invasão (`src/intrusion.cpp:138-153`), após 0x42 | guardar e mandar 0x2E/0x123 aos amigos online |
| 0x24 | `u32` | aceitou convite de sala de outro servidor (`LT` L2655-2673, depois de `SetDirectMoveRoomInfo`) | nada [P] (JP só registra [R]) |
| 0x25 | `Str msg` | chat de guilda: sussurro para o nick "길드에게" (para a guilda) na caixa de chat, só no estado 4 (`src/hatmanager.cpp:2905-2935`, idem 3110, 5400, 5560); senão "지금은 길드 대화를 하실 수 없습니다." | 0x2E/0x113 flag 1 a todos os membros online da guilda (inclusive o remetente) [R GB/JP] |
| 0x26 | `u32 serverUid, u8 canal, u16 sala, u32 uidConvidado(=eu), Str nick, u32 uidConvidante` | repasse de convite de sala quando o convite é de outro game server (`LT` L5590-5622; mesmo servidor vai pelo game 0x29) | repassar ao outro game server / cliente [P] |
| 0x27 | `u32 myUID, u32 uidAlvo` (ou `Str, Str` na MyRoom) | presente comprado com 0x60 ligado (`src/shoptask.cpp:272-279`, `shoptask.c:2072`, `realmyroomtask.c:6245/7537`) | avisar o alvo com **0x31** (carta nova) [P]; o C# já manda 0x31 pelo game |
| 0x2A | `u32 guildUID` | GuildActor msg 24 = marca da guilda enviada (`src/guildactor.cpp:369-374`) | mandar 0x3B (GUILD_INFO) aos membros online [P]; o C# já faz pelo game |
| 0x2B | `u32 guildUID` | GuildActor msg 25 = nome da guilda trocado (`src/guildactor.cpp:377-382`, `guildactor.c:1629`) | mandar 0x3C aos membros online [P]; o C# já faz pelo game |

Não há 0x15, 0x20, 0x28, 0x29 no KR 645 (GB tem 0x28/0x29 de versões novas) [C pela varredura dos `Send(1)`].
Bilhete NÃO é pacote do MSN: o menu "쪽지보내기" manda **game 0x3C** (§5.3).

---

## 4. Pacotes S->C (MSN -> cliente)

Todos tratados no `OnPacketCommon` (qualquer task: lobby, sala, jogo, loja, MyRoom). [C]

### 4.1 Ids de primeiro nível
| id | payload | efeito no cliente | OPC |
|---|---|---|---|
| 0x2C | hello cru `u8 ?, u8 ?, u32 key` (§2.3) | | — |
| 0x2D | `u8 code [, u32 uid]` | login (§2.4) | @0x73B8C9 |
| 0x2E | `u16 sub, ...` | §4.2 | @0x73C13D |
| 0x30 | (qualquer) | consumido sem ler (return 1) | @0x745DF6 |
| 0x31 | vazio | correio: limpa a lista e manda game 0xE6 (com 0x60) = "carta nova" (SPEC-correio §2.1) | @0x73BA51 |
| 0x32 | `u32 uid, Str nick` | troca o NickName do amigo (troca de nick) | @0x73BECD |
| 0x36 | vazio | manda game 0xE6 (atualizar correio) | @0x73BAFB |
| 0x37 | `Str ?, Str nick, Str msg` | linha de chat de guilda "길드:nick> msg" (cor 0xFF5BFFB0) se o nick não estiver na lista de ignorados do chat (Doc+0x4CBC) | @0x73BF63 |
| 0x38 | `u32 n, n × GUILD_USER_MSN_LIST(0x37)` | adiciona cada membro como sFriend com GuildFriend=1, Gender=sex&1, IsLogOn=online, Uid/dwGuildId/NickName; não atualiza a tela (mande 0x2E/0x102 depois ou junto) | @0x73BB3B |
| 0x39 | `GUILD_USER_MSN_LIST(0x37)` | idem para 1 membro + "%s 님이 길드에 가입하였습니다." (entrou na guilda) | @0x73BC05 |
| 0x3A | `u32 uid` | apaga o amigo, troca a marca dele na lista de usuários do canal para "guildmark" (padrão) e mostra "%s 님이 길드를 탈퇴하였습니다." (saiu) | @0x73BD69 |
| 0x3B | `GUILD_INFO(0x129)` | marca da guilda mudou (SPEC-guilda §3.5) | @0x73BDF9 |
| 0x3C | `GUILD_INFO(0x129)` | nome da guilda mudou | @0x73BE63 |

Os 0x33-0x35 e 0x2F caem no default (ignorados). [C tabela]

### 4.2 Sub-pacotes do 0x2E (`u16 sub` logo após o id)

Depois de qualquer sub válido o cliente manda a msg 0x48 à task (atualizar tela). Subs 0x107, 0x108, 0x111, 0x116,
0x11A-0x121 são ignorados. Os textos aparecem em `Notify1` (linha de aviso na janela do mensageiro) ou `Notify2`
(balão de aviso). [C, endereços = OPC]

| sub | payload | efeito | GB [R] |
|---|---|---|---|
| **0x102** @0x73CC0D | `u8 página, u16 total, u16 n, n × sFriend(0x8D)` | página 1 -> `ClearBuddy`; adiciona todos (pula o meu próprio uid); **estado 4** + Refresh. O `u16 total` é ignorado | paginado (30 por página): `u8 pagina, u16 restante, u16 nestapágina`; mistura amigos e membros da guilda |
| 0x103 @0x73CDC1 | `u8 n, n × sNoteInfo(0x6D)` | substitui a lista de bilhetes (Doc+0x4D08) + "쪽지가 도착했습니다" (chegou bilhete) | — |
| **0x104** @0x73C2A0 | `u32 code [, sFriend]` | resposta ao 0x18. 0: lê sFriend, adiciona (ou, se já era só da guilda, copia PangyaFriend/IsAccept/IsAgree) + "친구등록을 요청하였습니다." (pedido enviado). Erros: 2 "더 이상 친구등록을 할 수 없습니다." (lista cheia), 3 "상대방 친구슬롯이 모두 차 있습니다." (lista do outro cheia), 4 "친구 허가 대기 상태입니다." (aguardando aprovação), 5 "친구추가 후 승인 거절 되었습니다." (recusado), 6 "자기 자신을 친구로 등록할 수 없습니다." (você mesmo), 7 "대화명이 존재하지 않습니다." (nick não existe), 8 idem com "..", 0xB "이미 친구로 등록되어 있습니다." (já é amigo), 1/9/10/≥12 "친구 등록을 실패하였습니다." | code 0 + FriendInfo + posição + bytes |
| 0x105, 0x110 @0x73C163 | `u8` | ignorado | — |
| **0x106** @0x73C429 | `sFriend(0x8D)` | alguém me pediu amizade: adiciona/atualiza + Notify2 "알림 : <nick> 님이 친구요청을 했습니다." | manda ao alvo do 0x18 |
| **0x109** @0x73CCAB | `u32 code [, u32 uid]` | resposta ao 0x19. 0: IsAccept+IsAgree, alias "Friend", Notify2 "%s님이 친구가 되었습니다" (virou amigo); 9 "친구 등록되어있지 않습니다." (não está na lista); outro "친구 허용에 실패하였습니다." | |
| **0x10A** @0x73CD20 | `u32 code, u32 uid` | o outro aceitou meu pedido: code 0 -> IsLogOn+IsAccept, Server=0, alias "Friend", Notify2 "%s님이 친구가 되었습니다" | manda ao pedinte |
| 0x10B @0x73C840 | `u32 code [, u32 uid]` | 0: apaga o amigo (se também for da guilda, só limpa PangyaFriend); senão "친구삭제에 실패하였습니다." | |
| 0x10C @0x73C8BD | `u32 code [, u32 uid]` | 0: IsBlock=1; senão "친구블럭 설정을 실패하였습니다." | GB também manda 0x10F (logout) do bloqueador ao bloqueado |
| 0x10D @0x73C90F | `u32 code [, u32 uid]` | 0: IsBlock=0, IsBlocked=0; senão "블럭 해지를 실패하였습니다." | GB manda 0x115 do desbloqueador ao outro |
| **0x10E** @0x73CA30 | `u32 uid` (KR: 0x7D ligado, `localize_kor.h:76`) | amigo entrou: liga IsLogOn (se aceito e não bloqueado) + Notify2 "%s님이 로그인하셨습니다" (fez login). Sem 0x7D seria um sFriend inteiro | |
| **0x10F** @0x73CBAA | `u32 uid` | amigo saiu: IsLogOn=0 | |
| 0x112 @0x73CE47 | `u8 code [, u8]` | resultado antigo de bilhete pelo MSN: 0 -> lê u8, desconta 10 pang local + "쪽지를 남겼습니다. 상대방이 다음 로그인할 때 전달됩니다."; 1 "쪽지 보내기에 실패했습니다."; 3 "상대방의 쪽지함이 가득차서 쪽지를 보낼 수 없습니다." | (não usado: o KR manda bilhete pelo game) |
| **0x113** @0x73C535 | `u32 uidRemetente, Str nick, Str msg, u8 isGuild` | isGuild=1: linha "길드:nick> msg" no chat (cor verde), se o nick não estiver ignorado. isGuild=0: busca o amigo pelo uid; se não estava IsLogOn, liga; abre/atualiza a janela de conversa (`RecvChat`). Uid que não é amigo: descartado | igual (GB: 0 amigo, 1 guilda) |
| 0x114 @0x73C7DB | `u8 code [, u32 uid]` | code 3: escreve "메시지를 전달할 수 없습니다." (não foi possível entregar) na conversa e apaga IsLogOn | |
| **0x115** @0x73CF95 | `u32 status, u32 uid` | status do amigo (grava uid em Guid): 0 IsLogOn+IsPlay (jogando), 1 IsLogOn+IsDive (ausente), 3 IsLogOn+IsBusy (ocupado), 4 limpa play/dive/busy, liga IsLogOn e, se estava offline, "%s님이 로그인하셨습니다"; 5 limpa play/dive; 2 nada. **Ordem: status primeiro** (o GB escreve uid, state, u8, posição — layout de versão nova, não usar) | |
| 0x117 @0x73C16F | `u32 code [, Str nick, u32 uid]` | resposta ao 0x17. 0: no lobby/avatar manda msg 0x51 (nick, uid) e `SetOfflineUserNick` (preenche a janela com o uid para o 0x18); 0xD "회원탈퇴한 아이디입니다." (conta apagada); outro "대화명이 존재하지 않습니다." | igual |
| 0x118 @0x73CEBB | vazio | outro login com a mesma conta: fecha o MSN e abre "notify" "다른 사용자가 현재 계정으로 접속하였습니다." | |
| 0x119 @0x73C968 | `u32 code [, u32 uid, Str apelido]` | resposta ao 0x1F. 0: copia até 10 chars para szAlias; 1 "별명변경에 실패했습니다."; 9 "친구 등록되어있지 않습니다." | igual |
| 0x122 @0x73D06D | vazio | expulsão: o cliente manda 0x16, fecha o MSN e mostra "메신저서버에 잘못된 유저 정보가 전달되었습니다. 재 접속해주시기 바랍니다" (dados inválidos, reconecte) | |
| **0x123** @0x73CF3E | `u32 ?, u32 uid, sUserPosition(0x4B)` | copia a posição do amigo para sFriend+0x3D | |

### 4.3 Fluxo de amizade (montado dos handlers) [C cliente; ordem do servidor R/P]
1. A: 0x17 "nickB" -> A recebe 0x2E/0x117 `0, "nickB", uidB`.
2. A: 0x18 `uidB, "nickB"` -> servidor grava (A->B: pedido; B->A: pendente) e manda
   A: 0x2E/0x104 `0` + sFriend(B: PangyaFriend=1, IsAgree=1, IsAccept=0) -> A vê "(요청중)";
   B (se online): 0x2E/0x106 sFriend(A: PangyaFriend=1, IsAgree=0, IsAccept=0, IsLogOn=1) -> B vê "(대기중)" e o
   menu "친구허가".
3. B: 0x19 `uidA` -> B: 0x2E/0x109 `0, uidA`; A (online): 0x2E/0x10A `0, uidB`. Os dois ficam IsAccept=1.
   Mandar também 0x2E/0x123 e 0x2E/0x115 de cada um para o outro, para ícone e posição. [P]
4. Quem estava offline recebe o estado certo na próxima lista 0x102.
Recusar não tem pacote próprio no KR: B usa "대화상대삭제" (0x1C) ou "차단하기" (0x1A). [C menus]

---

## 5. Bate-papo, bilhetes, presença

### 5.1 Conversa com amigo [C]
- Envio 0x1E `u32 uid, Str msg` (filtrado por `ChatManager::Filtering`). Entrega: 0x2E/0x113 `uidRemetente,
  nickRemetente, msg, 0`. Destino offline/inexistente: 0x2E/0x114 `3, uidDestino` ao remetente.
- Loop: com as janelas de conversa cheias o cliente responde sozinho com 0x1E "상대방이 응답할 수 없습니다.". Se os dois
  estiverem cheios isso vira pingue-pongue; o servidor deve limitar (ex.: não repassar essa frase a quem a mandou nos
  últimos segundos) [P].
- Bloqueio: o cliente já não envia para bloqueados ("차단한 상대와는 대화할 수 없습니다..."); o servidor deve também
  descartar mensagens de quem o destino bloqueou (IsBlocked do lado dele) [P].

### 5.2 Chat de guilda [C envio, R resposta]
- Envio 0x25 `Str msg`. Resposta GB/JP: 0x2E/0x113 `uid, nick, msg, 1` para todos os membros online (inclusive o
  remetente, que só vê a própria linha por esse eco). Alternativa sem o eco no remetente: 0x37 `"", nick, msg`.
- Sem MSN no estado 4 o cliente mostra "지금은 길드 대화를 하실 수 없습니다." e não envia.

### 5.3 Bilhetes (쪽지) — sempre pelo GAME [C]
- Envio: **game C->S 0x3C** `u16 0x111, u32 uidAlvo, Str nota (≤63), u8 flag` (menu do mensageiro `MD`
  `OnNoteDlgResult` @0x692250: flag 0; lobby/avatar também). O cliente exige ≥ 10 pang: "쪽지를 보내기 위해서는 %d팡이
  필요합니다." (`MD` @0x6936F0).
- Resposta: **game S->C 0x93** `u16 0x111, u32 code [, u64 pangTotal]` (OPC @0x73B5DA, tabela @0x7466CC/0x7466E0):
  0 -> grava o pang total (u64) + "쪽지를 남겼습니다. 상대방이 다음 로그인할 때 전달됩니다." (fica para o próximo login);
  3 "상대방의 쪽지함이 가득차서 쪽지를 보낼 수 없습니다."; 7 e 0xA "자신에게 쪽지를 보낼수 없습니다."; 0xC
  "보유팡이 부족하여 쪽지를 보낼수 없습니다."; outro "쪽지 보내기에 실패했습니다.". Outros sub do 0x93 são ignorados.
  (A SPEC-correio §2.8 cita só a entrega 0xB0; a resposta é 0x93.)
- Entrega: **game S->C 0xB0** `u8 n, n × sNoteInfo` (SPEC-correio §2.8) ou **MSN 0x2E/0x103** com o mesmo corpo
  (este também mostra "쪽지가 도착했습니다"). Sugestão: no login do MSN (após 0x102) mandar 0x2E/0x103 com os não lidos;
  ao chegar bilhete para alguém online, 0x2E/0x103 pelo MSN (ou 0xB0 pelo game). [P]

### 5.4 Presença [C cliente, R/P servidor]
- Ao logar no MSN: mandar aos amigos online (que me têm aceito) 0x2E/0x10E `myUid` e 0x2E/0x123 `0, myUid, posição`.
- 0x23 do cliente -> guardar posição e mandar 0x2E/0x123 aos amigos online.
- 0x1D do cliente -> 0x2E/0x115 `status, myUid` aos amigos online. Status "jogando" (0) quando a partida começa não vem
  do cliente: o mensageiro precisa saber pelo game (§7) [P]; GB usa o estado salvo (0 jogo, 1 ausente, 3 ocupado,
  4 online) [R].
- Ao cair (0x16 ou socket): 0x2E/0x10F `myUid` aos amigos online.
- Na lista 0x102: para cada amigo online preencher userPosition, IsLogOn e o bit de status; offline: posição -1,
  State 5 (GB) [R].

---

## 6. Guilda no mensageiro

- A aba "길드" lista entradas com GuildFriend=1 (0x8C b1). Formas de preencher: incluir os membros da guilda na
  própria 0x2E/0x102 (GB) com dwGuildId e szEmblemName, ou 0x38 (sem emblema). [C/R]
- Membro entrou: **0x39** GUILD_USER_MSN_LIST a todos os membros online (mostra "%s 님이 길드에 가입하였습니다.").
  Membro saiu/expulso: **0x3A** `u32 uid`. Marca/nome: **0x3B/0x3C** GUILD_INFO (o C# já manda pelo game). [C layout]
- Como o cliente aceita 0x39/0x3A pelo socket do game, o **game server pode mandar** direto ao aprovar/expulsar/sair
  (C->S 0x1xx da SPEC-guilda), sem depender do MSN. Mesmo assim, se o MSN estiver no ar, a lista dele (0x102) tem de
  refletir a guilda nova no próximo envio. [C dispatch; P divisão]
- Membros online da guilda no 0x102: IsLogOn + posição, como amigos.

---

## 7. O que o game server precisa fazer junto

1. **Login 0x09**: lista de mensageiros (já existe). Garantir maxUser ≥ curUser + 151 (§2.1).
2. **C->S 0x88 -> S->C 0xFA** `u8 n, n × sGameServerInfo` com a mesma lista (necessário para reconectar). Sem
   mensageiro: responder `0xFA u8 0` (libera o flag de espera; o cliente fica offline sem erro).
3. **C->S 0x3C sub 0x111** (bilhete) -> 0x93 sub 0x111 (§5.3) e entrega ao destino.
   **C->S 0x3C sub 0x11F** (lista de amigos com o MSN desconectado, para correio/presente) -> responder com o formato do
   MSN **0x2E/0x102** pelo socket do game [P: é o único handler que preenche o BuddyManager]. Efeito colateral: o
   cliente põe o estado do MSN em 4; inofensivo, pois `Open` e os envios conferem o socket.
4. Avisar o mensageiro (se estiver em outro processo; mesmo processo = chamada direta) de: entrou no game server
   (para validar o 0x12), saiu, começou/terminou partida (status 0/4 via 0x115), troca de nick (0x32), mudanças de
   guilda (0x39/0x3A/0x102).
5. Convites entre servidores (0x26/0x24) e "seguir amigo" (game 0xAC u8 canal, u16 sala quando o amigo está no mesmo
   servidor, `MD` `OnGoWithFriend` @0x695190; senão o cliente pede a lista 0x43 e troca de servidor) dependem de
   `userPosition.iServerGUID` = uid do game server do amigo. O menu "따라가기" mostra "친구 따라가기 기능은
   준비중입니다." (em preparação) em alguns casos (`MD` @0x698A3D). [C]
6. Pacotes que o game já manda e o MSN não precisa duplicar: 0x31 (carta nova), 0x3B/0x3C (guilda).

Recomendação de arquitetura [P]: um `MessengerHandler` no mesmo processo do `Pangya.Server`, porta própria (ex.:
30303 como no REPORT; nunca 80/10101/10102/20201), registrado como servidor tipo "messenger" no registro. Presença em
memória (uid -> sessão MSN + posição + status), amizades no banco (tabela `friends(owner_uid, friend_uid, alias,
state: pedido/pendente/aceito, blocked)`), bilhetes no banco.

---

## 8. Plano de implementação mínima

### Fase A — conectar e não dar erro
- Listener MSN (cifra igual ao game, `IdleTimeoutSeconds = 0`), hello **0x2C** `u8 0, u8 0, u32 key`.
- **0x12** -> validar uid/nick contra o banco (e, se possível, sessão de game ativa) -> **0x2D `0, uid`**
  (falha: `0x2D 2`).
- **0x14** -> **0x2E/0x102 `u8 1, u16 0, u16 0`** (lista vazia => estado 4, "친구 없음").
- Engolir sem erro 0x13, 0x16, 0x1D, 0x22, 0x23, 0x24, 0x26, 0x27, 0x2A, 0x2B (só log).
- Registro "messenger" com maxUser alto; game **0x88 -> 0xFA**.
- Teste: abrir o mensageiro no lobby, ver "친구 없음", esperar > 60 s sem reconexão, logout limpo.

### Fase B — amigos e online
- Banco de amizades + presença em memória.
- 0x102 com amigos reais (bits do §1.3, posição, status); paginar se > ~30 (página 1 limpa).
- 0x17 -> 0x117; 0x18 -> 0x104 + 0x106; 0x19 -> 0x109 + 0x10A; 0x1C -> 0x10B; 0x1A/0x1B -> 0x10C/0x10D;
  0x1F -> 0x119.
- Presença: 0x10E/0x10F, 0x23 -> 0x123, 0x1D -> 0x115; derrubar sessão duplicada (0x118 para a antiga [P]).
- Game: 0x3C/0x11F -> 0x2E/0x102 pelo game.

### Fase C — chat e bilhetes
- 0x1E -> 0x113 (flag 0) / 0x114 `3` ; proteção contra o eco automático.
- Bilhetes no game: 0x3C/0x111 -> 0x93/0x111 (cobrar 10 pang, devolver u64 total), guardar; entregar com 0x2E/0x103
  no login do MSN e na hora se o destino estiver online (ou 0xB0 pelo game).

### Fase D — guilda
- Membros da guilda na 0x102 (GuildFriend, dwGuildId, emblema) ou 0x38.
- 0x25 -> 0x113 flag 1 para os membros online.
- 0x39/0x3A quando o game aprovar/expulsar/sair; 0x2A/0x2B -> 0x3B/0x3C (se o game ainda não tiver mandado).

### Riscos e dúvidas
- **Autenticação fraca**: o 0x12 só tem uid e nick. Sem conferir a sessão de game (e o IP), qualquer um entra como
  outro jogador. [C]
- **Reconexão a cada 60 s** se o 0x102 não chegar ou o uid do 0x2D não bater. [C]
- **Lista some no ShutDown** -> 0x88/0xFA é obrigatório para a 2ª conexão. [C]
- **maxUser - 150**: servidor com maxUser pequeno é descartado 2 em 3 vezes. [C]
- Semântica exata de: `u16 total` do 0x102 (ignorado), primeiro u32 do 0x123, primeiro Str do 0x37, campos State/
  Channel/GameLevel do sFriend (o cliente não lê) — usar os valores do GB. [R]
- 0x115 do GB tem outra ordem/layout; no KR é `u32 status, u32 uid` e nada mais. [C]
- Quem manda o status "jogando" (0x115 status 0): o cliente nunca manda 0x1D 0; tem de vir do game -> MSN. [P]
- 0x21/0x22/0x24/0x26/0x27: só os pontos de envio estão confirmados; o que o servidor original fazia é palpite.
- Resposta ao 0x3C/0x11F pelo game com 0x2E/0x102 não foi vista em referência; é dedução pelo dispatcher comum. [P]
- `IsLocalContent(0x7D)` (S4NT_VIEW_OFFLINE_GUILD) está ligado no KR (`localize_kor.h:76`): muda o 0x10E (só uid), o
  0x10F e o 0x113 (lê o u8 final). Não mandar o formato "sem 0x7D". [C]
