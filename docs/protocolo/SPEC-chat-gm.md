# KR 645: chat, cor de GM, avisos e comandos de GM

Pesquisa somente leitura no cliente KR 645 QA. Legenda: **[C]** confirmado no cliente (decompile/exe), **[R]** servidor de referência
(GB = `/root/pangya-server/Server/GB`, ids S->C dele = KR **+2** nesta faixa), **[P]** palpite.

Fontes: `opc.c` / `opc.lst` / `summ.txt` = `CTask::OnPacketCommon` (scratchpad do emulador, "Lnnn" = linha em opc.c; os `case` de lá são
índices do jump table, o id real está no summ.txt); `gh/` = `/root/ghidra-out`; `src/` = `/root/rebang/source/client/ProjectG`;
strings lidas de `KR642/ProjectG_ReleaseQA.unpatched.exe` (cp949). `doc+0x433` = `m_myInfo.info.dwIdentity` (sPangYaUserInfo começa em
doc+0x3E2: sID 0x3E2, sNick **0x3F8**, sGuild 0x40E, emblema 0x423, school 0x42F, **dwIdentity 0x433**).

## Resposta curta

- **Sim, chat de GM é azul-claro** (`0xFF6EECFB`, ciano/azul). A cor vem **só do bit 7 do u8 `kind` do 0x3E**, não da identidade do
  remetente: o cliente não procura o nick nas listas nem olha `dwIdentity` para colorir. Quem decide é o servidor. [C]
- GM fala → servidor manda `0x3E u8 0x80, str nick, str msg` (em vez de `kind 0`). [C] (GB faz o mesmo: `CHAT_GM = 0x80` quando o
  remetente tem `game_master`/0x04 [R])
- Não há ícone de GM no chat. [C] (nas listas, o GM com identidade `& 0x14` fica **oculto** para não-GM, a menos que esteja "visível",
  e o nick dele na sala sai semitransparente; ver §5)
- Anúncio global: `0x40 str` (letreiro no topo da tela, com "dingdong") e/ou `0x3E kind 7` ("알림 : msg" no chat, rosa/vermelho). [C]

## 1. S->C 0x3E — linha de chat (opc case 0xc, L2236; `switchD_007335d1`, jump table 0x7462E4) [C]

```
u8  kind     bit7 = GM (cor azul), bits 0..6 = tipo
str nick
str msg
[u32 valor]  só se tipo == 10
```

O cliente roda `ChatManager::FilteringHack(msg)` sempre. Cor padrão = branco `0xFFFFFFFF`.

| tipo (`kind & 0x7F`) | texto mostrado | cor | obs. |
|---|---|---|---|
| 0 | `nick + " : \b" + msg` | branco; **amarelo `0xFFFFCC00`** se `nick == meu nick` (doc+0x3F8); **`0xFF6EECFB` se bit7** (sobrepõe o amarelo) | único tipo que olha o bit7. Se o nick está na lista de bloqueados (doc+0x4CBC) e não é o meu, a linha é descartada. Último arg do `AddChatMsg` = 1 → **remove códigos `\c` de cor** do texto; também chama `SetChatFacialUser(nick)` (emoticon/expressão do avatar) |
| 1 | `"알림 : " + nick + " 님은 현재 채널에 없습니다."` | `0xFFFF7878` | sussurro: alvo fora do canal |
| 2 | `"알림 : " + nick + " 님이 퇴장했습니다."` | `0xFFFF7878` | + `CSharedDoc::SetGoStopMode(true)` |
| 3 | `"강퇴에 관한 투표가 부결되었습니다."` | `0xFFFF7878` | voto de expulsão rejeitado; nick ignorado |
| 4 | `"알림 : " + nick + " 님은 현재 귓속말을 수신할 수 없습니다."` | `0xFFFF7878` | sussurro recusado |
| 5 | `"알림 : " + nick + " 님은 현재 서버에 없습니다."` | `0xFFFF7878` | sussurro: alvo fora do servidor |
| 6 | `"알림 : " + nick + " 님은 현재 접속중이 아닙니다."` | `0xFFFF7878` | sussurro: alvo offline |
| **7** | **`"알림 : " + msg`** (nick ignorado) | `0xFFFF7878` | **aviso/notice no chat** |
| 8 | `"알림 : " + nick + " 님이 퇴장해서 스트라이크포인트가 1포인트씩 올라갑니다."` | `0xFFFF7878` | |
| 9, 11..127 | `nick + " : " + msg` | branco (bit7 **não** vale aqui) | sem remover `\c` |
| 10 | `"알림 : " + "%s님께서 %d 팡 주머니를 획득하였습니다."` (nick, u32) | `0xFFFF7878` | lê **u32 extra** depois das strings |

Não mostra nada se `doc+0x4560 == 5`, nem se conteúdo local 0x4F ligado e `doc+0x5B9C == 2`. Strings: " : \b" 0x9E929C, "알림 : "
0x9F58D8, sufixos 0x9F58BC/0x9F588C/0x9F5868/0x9F5840/0x9F58A0/0x9F5820/0xA1587C/0x9F57F8. Os tipos batem com o enum `eChatMsg` do GB
(1 NOT_EXIST_IN_CHANNEL … 7 NOTICE, 0x80 CHAT_GM) [R].

`AddChatMsg` (`src/chatmsg.cpp:912`): no lobby vai para o actor "Lobby" (msg 3, cor); no jogo (`CGolfTask`) vai para "GolfRule" (cor) e
"Screen" (`ConvertToUIColor`), então **o azul de GM aparece também dentro da partida**. [C]

Outros S->C de texto:

| id | layout | efeito | ref |
|---|---|---|---|
| **0x3F** | str | linha de chat crua em `0xFFFF7878` (mensagem de sistema, sem prefixo) | opc case 0xd L2567 [C] |
| **0x40** | str | `CNoticeBoard::AddNotice(text, 1, false)`: **letreiro rolando no topo da tela** + som `notice_dingdong` (se a fila estava vazia). Aceita prefixo de cor `"\cRRGGBB…\c"` (`GetColornText`, `src/noticeboard.cpp:86`; cor ARGB em hex, padrão branco) | opc case 0xe L2578 [C]; GB 0x42 = notice do GM/Auth [R] |
| 0xC7 | str nick, str msg | `COneLineBoard::AddOnelineMsg` (ticker/"megafone" de uma linha) | opc case 0x3d L7861 [C]; GB 0xC9 ticker [R] |
| 0x37 | str, str nick, str msg | `"길드:"`[P] + nick + `"> "` + msg em verde `0xFF5BFFB0`, se nick não bloqueado | opc case 6 L2043 [C], significado [P] |
| 0x82 | u8 f, str nick, str msg | sussurro (abaixo) | `gh/lobbytask.c` L4560 [C] |
| 0x98 | u32 identity | `CSharedDoc::SetIdentity` (doc+0x433); PC-bang soma 0x80; no jogo copia para `m_userInfo[me]` | opc case 0x2c L6171 [C]; GB 0x9A [R] |

## 2. Sussurro

- **C->S 0x2A** `str nickAlvo, str msg` (`src/hatmanager.cpp` ~L2935, `SendChatMessage2`). [C]
- **S->C 0x82** `u8 f, str nick, str msg` (só no `CLobbyTask`, inclui sala de espera) [C]:
  - `f & 0x7F == 0` → eco para quem mandou: `"meuNick>nick : \b'msg'"`;
  - `f & 0x7F != 0` (use 1) → recebido: `"nick>meuNick : \b'msg'"`; respeita a opção "aceitar sussurro" e a lista de bloqueio;
  - **bit7 → cor GM `0xFF6EECFB`**, senão rosa `0xFFD2A2FF`.
  - GB: 0 = FROM (eco, nick = destino), 1 = TO (nick = remetente) [R].
- Falhas: responder ao remetente com `0x3E` tipo 1/4/5/6 + nick do alvo. [C]/[R]
- Outros C->S de chat [C]: **0x03** `str meuNick, str msg` (normal); **0x54** `str msg` (chat de equipe, `bWhisper=true` na assinatura;
  GB `CLIENT_TEAMCHAT` [R]); **0x25** `str msg` (guilda, vai ao *messenger*, `Send(1)`); **0x4F** `u8 1` (cliente avisa que tomou
  punição de flood/palavrão); 0xE0 `str nick` ao receber sussurro em certo estado [P].

## 3. Comandos com "/" (`ChatManager::CheckChatCommand` @00716A60, `gh/hatmanager.c` L1510) [C]

Ordem em `SendChatMessage`: grava histórico → `CheckChatCommand` → se retornou 1, **nada vai para o servidor**.

1. Texto começa com `/`:
   - se `IsAdministrator()` (= `dwIdentity & 0x04`, `CGMToolkit::IsAdministrator` @007A8970) → `CGMToolkit::Command(texto)`: mostra
     `"GM_Command:<texto>"` em `0x6EECFF` e executa; erro → "Syntax error."/"Argument error." (laranja `0xFF7802`);
   - senão (ou se o toolkit não reconheceu) → tabela `ChatCmd[3]` (0xA9E904): `identity`, `notice`, `itemdrop`.
   - **Qualquer outra coisa começando com `/` é engolida** (retorna 1): por isso `/bot` nunca chega ao servidor; só `!bot` funciona.
2. `ChatCmd` (vale também para não-GM):
   - `/identity [n] [nick]` → **C->S 0x41** `u32 n (0xFFFFFFFF se omitido), str (meu nick ou o 3º argumento)`;
   - `/notice` → não faz nada;
   - `/itemdrop [taxa]` → só se `dwIdentity & 0x40`; **C->S 0x4E** `u32 3, u8 0` ou `u32 3, u8 1, u32 taxa (≥100)`.
3. GM (`dwIdentity & 0x04`) também pula o filtro de palavrão/flood do cliente.

**O servidor tem que validar tudo** (0x41, 0x57, 0x8C, 0x4E, 0x60, 0x61): um cliente comum pode mandar esses pacotes.

### 3.1 Toolkit de GM (`gh/gmtoolkit.c`, tabela 0xAA53D8 de 32 × {u8 ativo, u32 tag, char* nome, char* ajuda}, montada em 0x9B9B90) [C]

Ativos: todos menos `loadscript` (23) e `gettid` (25). Pacote padrão: **C->S 0x8C `u16 tag` + dados**. `status` em 3/4/5 =
`u16 flags` (bit0 visible, bit1 whisper, bit2 channel; `eGMStatusFlag`).

| comando | envia | obs. |
|---|---|---|
| `/help [cmd]`, `/command`, `/status`, `/list` | nada | só local |
| `/visible on\|off` | 0x8C `u16 3, u16 flags` | mostra o GM na lista do lobby |
| `/whisper on\|off` | 0x8C `u16 4, u16 flags` | GM recebe todos os sussurros |
| `/channel on\|off` | 0x8C `u16 5, u16 flags` | GM vê sussurros do canal/lista |
| `/open nick`, `/close nick` | 0x8C `u16 8\|9, str nick` | lista de sussurros permitidos |
| `/kick nick [-p]` | 0x8C `u16 10, u32 guid, u8 (1 se -p)` | guid vem de `sBriefUserInfo` (lista do lobby, doc+0x4A50); recusa alvo com identity & 4 |
| `/disconnect nick` | 0x8C `u16 11, u32 guid` **e** C->S 0x61 `u32 guid` | idem |
| `/discon_uid uid` | igual ao anterior (procura pelo uid) | |
| `/destroy n` | C->S 0x60 `u16 sala` **e** 0x8C `u16 13` | só fora de sala (doc+0x49E1 == 0xFFFF) |
| `/wind vel dir` | 0x8C `u16 14, u8 vel-1, u8 dir` | vel 0..18; só em partida de tipos ≠ 4,5,6,9,10,14 |
| `/weather fine\|cloud\|rain\|snow` | 0x8C `u16 15, u8 0..3` | |
| `/identity admin\|user` | **C->S 0x41** `u32 0x484 (admin) ou 0x80 (user), str meuNick` | só no lobby; resposta esperada **S->C 0x98 u32** [R GB 0x9A] |
| **`/notice texto`** | **C->S 0x57 `str texto`** (palavras + " ") | GB 0x57 = `CLIENT_NOTICE` [R] |
| `/giveitem nick tid qtd` | após "/Y": 0x8C `u16 18, u32 guid, u32 tid, u32 qtd` | |
| `/goldenbell tid qtd` | após "/Y": 0x8C `u16 19, u32, u32` | todos da sala |
| `/setprize`, `/unsetprize`, `/showprize`, `/noticeprize` | 0x8C `u16 20`(…)/`25` | prêmios de torneio |
| `/setmission n` | 0x8C `u16 27, u32` | |
| `/finditem`, `/category` | local (IFF) | |
| `/matchmap n`, `/matchhole n` | 0x8C `u16 30\|31, u32` | |

GB trata o mesmo pacote como `CLIENT_GM_COMMAND` (lá id 0x8F), `COMMON_CMD_GM.CCG_*` (`Game/System/GameMasterSystem.cs`). [R]

## 4. O que o servidor deve mandar

**(a) Chat de GM em azul** [C]: no handler de 0x03, se `remetente.IdentityFlags & 0x04` → `RoomPackets.Chat(nick, msg, kind: 0x80)`
(lobby e sala; o próprio GM também recebe, e o bit7 sobrepõe o amarelo do "meu nick"). Para sussurro de GM: `0x82` com `f | 0x80`.

**(b) Anúncio global de GM** — ao receber **0x57** `str` de um GM (validar `& 0x04`, senão ignorar):
- `0x40 str texto` para **todas** as sessões (letreiro no topo; opcional `"\cFF00FFFF\c"` para colorir) [C]; e/ou
- `0x3E u8 7, str nick, str texto` → "알림 : texto" no chat de todos [C] (é o que o GB faz no `/notice` [R]).
- `0x3F str` serve para mensagens de sistema simples no chat (sem prefixo). Ex.: trocar o atual `Chat("Server","sala cheia")` por 0x3F.

## 5. Identidade nas listas (contexto) [C]

- Lista de usuários do lobby (`CLobbyMain::OnRoomList_UserListOwnerDraw` @007E4B30): se `alvo.dwIdentity & 0x14` e eu não sou GM →
  **não desenha** a menos que `sBriefUserInfo.state & 1` (offset 0x3E); se eu sou GM → desenha semitransparente `0x50FFFFFF`. Mesma
  regra em `lobbymain.cpp:3330` e `avataruserlistdlg.cpp:169`. Ou seja, com 0x04 na lista o GM fica invisível para jogadores comuns
  até mandar `/visible on` → servidor deve ligar `state |= 1` e reenviar a entrada (GB: `requestExecCCGVisible` + update [R]).
- Slot da sala (`OnGameRoomExt_RoomUserOwnerDraw` @007E9370): nick com identity & 0x14 sai em `0x50FFFFFF` (semitransparente) e sem
  emblema de guilda. Não há ícone específico de GM. [C]
- `sBriefUserInfo` (pack 1): uid 0, guid 4, roomIndex 8, nick[22] 0xA, level 0x20, dwIdentity 0x21, … state u16 0x3E. [C]

## 6. Recomendação de implementação

1. **Chat 0x03 com `kind 0x80` para GM** (1 linha). Maior ganho, pedido do usuário.
2. **0x57 /notice** → `0x40` (letreiro) + `0x3E kind 7` para todos; só GM.
3. **Sussurro** 0x2A → 0x82 (0 eco / 1 destino, |0x80 se GM) e erros via 0x3E 5/6/4.
4. **0x8C 10/11 + 0x61** (kick/disconnect por guid) e **0x8C 3** (/visible → `state` bit0 + reenviar lista); recusar alvo GM.
5. Opcional: 0x8C 14/15 (vento/clima na partida), 0x41/0x98 (/identity admin↔user), 0x8C 18 (/giveitem).
6. Mover comandos de chat do servidor para `!` (o cliente engole tudo que começa com `/`).
