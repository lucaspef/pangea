# KR 645: lounge (sala de avatar, modo 2) e loja pessoal (개인상점)

Pesquisa só de leitura no cliente KR 645 QA, nos servidores de referência e no emulador Python.

**Marcas usadas no texto:**
- **[C]** confirmado no cliente (decompile, asm ou fonte de referência do cliente).
- **[R]** visto em servidor de referência (GB e S6) ou no emulador.
- **[P]** palpite.

**Abreviações de fonte:**
- **GH** = `/root/ghidra-out` (`avatar_main.c`, `avatar_task.c`, `lobbytask.c`, `taskmain.c`, `projectg.c`…).
- **SRC** = `/root/rebang/source/client/ProjectG` e `/root/rebang/source/shared`.
- **GB** = `/root/pangya-server/Server/GB/GameServer`. É uma versão nova; os ids S→C dele são os do KR **+2**.
- **S6** = `/root/pangya-server-work/ref/Pangya-Server-Source-master/S6/Pangya_GameServer`.
- **EMU** = `/root/pangya-server-work/emu`.
- **PSM** = `GB/Manager/PersonalShopManager.cs`.

**Leitura de pacotes S→C:** o despacho comum é `CTask::OnPacketCommon` @0x732640. O Ghidra não decompila essa função (timeout). O switch faz `id-0x2D` (até 0x1CB), com a tabela de bytes em 0x746118 e a de ponteiros em 0x745E1C. As primitivas de leitura do cliente são:

| Endereço | Primitiva | Observação |
|---|---|---|
| 0x77c3a0 | D1 | |
| 0x77c3b0 | D2 | |
| 0x77c3d0 | D4 | |
| 0x77d2e0 | Str | u16 len + bytes |
| 0x77c510 | Buf(n) | |
| 0x77c410 | "D8" | Lê 8 bytes mas devolve só os 32 bits baixos, então pang/renda acima de 4G se perde [C] |

`sStall` e `sStallSlot` **não são da loja do lounge.** Pertencem à estante de exposição do MyRoom com o Pangya Market (offline trade, conteúdo 0x66), descritos na §6. A loja do lounge usa só `sTradeItem` (0xAA bytes).

---

## 1. Entrar no lounge

### 1.1 Criação e entrada [C]

**Criação.** C→S **0x08** tem o mesmo layout da sala normal (`makeroomdlg.cpp:1296`, ver SPEC-room.md).
- O botão `make_chat` faz `ChangeGameType(2)`.
- `course` = mapa. Com 0x7F (aleatório), o cliente troca por `rand()%11` (`makeroomdlg.cpp:1208`).
- `holes` = `m_chatHole`, de 1 a 18 (`:1266`, `:2893-2925`).
- Ordem do seletor de mapas: tabela 0xa1ce08 = 19,16,15,14,13,11,8,10,0..7,9 (127 = aleatório). Com `bChat`, LastMap = 9 (NextMap @007e2920, LastMap @007e6c30).
- Se o controle de serviço 0x2000 estiver ligado, a criação é bloqueada.

**Entrada.** C→S **0x09** u16 sala, str senha (igual à sala normal).

**Resposta.** S→C **0x47** (`GH/lobbytask.c` L3670-3770), layout igual ao da sala normal. Quando `sRoomInfo.gameType == 2`, o cliente:
1. esconde o cursor e fixa o mínimo de 800×600;
2. chama `ChangeTask("ntAvatarChatTask")`;
3. posta para "AvatarChat" (L3761-3768).

Na entrada do lounge o GB manda, nesta ordem [R]: `sendUpdate`, `sendMake`, 0x48 (lista com posição, opção 7), 0x196, 0x9E (clima), `sendUpdateRoomInfo` (`Channel.cs:1695-1707`, `:1966-1972`).

### 1.2 Mapa do lounge [C]

Fonte: `ntAvatarChatMain::OnLoad` @0053ea60 (`GH/avatar_main.c` L2735) e `LoadBg` @0053caf0.

- `SetCurMap(sRoomInfo.mapType @0x4C)`.
- `LoadBg(<nome do curso>, sRoomInfo.nHole @0x47)` carrega:
  - `"%s_%02d.gbin"`
  - `%s_wave.txt`
  - `%s_river.txt`
  - céu e nuvens
- O cenário é **um buraco de um curso**: `mapType` escolhe o curso e `nHole` escolhe o buraco (1..18). O servidor precisa manter o curso e o número de buracos que o cliente pediu. Não existe outro pacote de tema.
- O clima começa em 0 e só muda com S→C **0x9C** (u8 clima, u8, u8; `SetWeather`).
- A avatar task não trata o 0x48 (configurações). Ele também não está na tabela comum. Tudo vem do 0x47.

### 1.3 Ordem dos pacotes e o problema da troca de task [C]

**Por que os pacotes da mesma rajada se perdem.** `CProjectG::ProcessRecvPacketQueue` @0x79b5d0 (`GH/projectg.c` L7463) só despacha a fila quando a task atual tem `flag 0x1f0 & 2` e não há troca pendente (`TaskManager[0x28]==0`). O 0x47 apenas **agenda** a troca de task. O laço continua entregando o resto da fila (0x48, 0x46) à **task antiga** (lobby). O lobby guarda os slots, mas não cria avatares.

**O 0x46 precisa de u16 = 0xFFFF.** A avatar task descarta qualquer 0x46 cujo u16 não seja **0xFFFF**. O mesmo vale para o **0x7A** (novo dono). Na sala normal o lobbytask ignora esse u16, então 0xFFFF serve nos dois casos.

**O que o servidor deve fazer:**
1. Mandar 0x47 (0x48 é opcional) e depois 0x46 sub 0 com u16 0xFFFF.
2. **Reenviar o 0x46 sub 0 a cada ~1 s** até receber do jogador C→S 0x63 sub 4 (o próprio avatar foi criado).

O reenvio é inofensivo: o sub 0 limpa a lista, e um nick que já tem avatar não é recriado. Só sai de novo um 0xED.

### 1.4 Lista de avatares: 0x46 [C]

Fonte: `ntAvatarChatTask::OnPacket` @00546040 e `ntAvatarManager::CreateAvatar` @0053b640 / construtor `ntAvatar` @0053a330.

**Layout:** `u8 sub, u16 0xFFFF`, depois:

| sub | Dados | Mensagem / efeito |
|---|---|---|
| 0 / 5 | u8 n, n × (sSlotInfo 0x152 + sCharacterInfo 0x1BC). O sub 0 limpa a lista. O cliente **não lê** a lista de convidados do final | msg 0xE4: cria cada avatar. `IsMassGame(2)` é falso, então o sCharacterInfo vem junto |
| 1 | slot + personagem, só se o guid for novo | msg 0xE4: cria |
| 2 | u32 guid | msg 0xE5: remove |
| 3 | u32 guid + slot | msg 0xE6: **não faz nada** no lounge |
| 7 | u8 + slot + personagem | msg 0xE4: cria |

**Limite:** **30 avatares** (0x1E).

**Campos de sSlotInfo que o avatar usa** [C]:

| Offset | Campo | Uso |
|---|---|---|
| 0x00 | dwGuid | |
| 0x30 | dwIdentity | bit 4 = GM, mostra a opção de expulsar |
| 0x38 | tidChar | |
| 0x54 / 0x55 / 0x57 | bits | gênero, manner, asas |
| 0x59 / 0x5D | | guilda, emblema |
| 0x69 | dwUserUID | uid usado na loja (0x77) |
| 0x6D | u32 state | bits de estado (ver §2.2); **0x80 = loja aberta, 0x100 = esgotada** |
| 0x73 | u32 action | pose |
| 0x77 / 0x7B / 0x7F | f32 x, f32 z, f32 ângulo (rad) | posição. O y vem do terreno. O ângulo é normalizado para 0..2π |
| 0x83 | iStateTrade | o construtor do avatar não usa [P] |
| 0x87 | strTradeTitle[64] | título no balão da loja (`ntAvatar`+0x71C) |
| 0xCD / 0xD1 | | displayID (canalização) |

O servidor precisa guardar x/z/ângulo/action/state/título de cada jogador para preencher esses campos para quem entra depois. O GB faz o mesmo no seu 0x48 opção 7 (`PlayerRoomInfo`, `Room.cs:339-350`) [R].

**Lista de usuários (0x44).** O cliente procura o guid do slot em `m_briefUserInfoMap` ao criar ou remover um avatar e na janela de usuários (`avataruserlistdlg.cpp UpdateUserList`), **sem testar o fim do map**. Se a entrada faltar, ele lê lixo (só afeta a mensagem de entrada/saída e o efeito). Por isso: manter `sBriefUserInfo.dwGuid == sSlotInfo.dwGuid` e mandar o 0x44 [C].

### 1.5 Posição inicial [C]

O cliente escolhe a posição sozinho (msg 0xE4, ramo "sou eu", `GH/avatar_main.c` ~L4060-4160):
1. Procura o ponto `"대화방시작"` no `.gbin`; se não achar, usa `"시작점"`.
2. Aplica um pequeno deslocamento aleatório em x/z, pega o ponto no chão e um ângulo aleatório.
3. Chama `Stop()`.
4. Envia **C→S 0x63 sub 4** (x, z, ângulo).
5. Envia **C→S 0xED** u32 guid para cada avatar criado.
6. Se o estado de troca ≠ 0, envia também **0x75**.

### 1.6 Sair [C]

- O botão voltar com o layout `AVATARCHAT_MAIN` (`GH/taskmain.c` ~L8694) troca para `CLobbyTask` e envia **0x0F** (`SendRoomExitPacket`), voltando para ROOMLIST.
- Isso só acontece com doc[0x4b7f] ligado (lista de salas ativa, via 0xF3). Sem ele, troca de task **sem enviar 0x0F**. O servidor deve tratar desconexão ou nova entrada como saída.
- Resposta: 0x4A para o jogador e 0x46 sub 2 para os outros (como na sala normal).
- O ESC abre `CQuitWindow`. Com loja pessoal aberta, o ESC é bloqueado.
- Ao sair, um **visitante** (estado de troca 4) envia **0x75** (`avatar_main.c:4090/4507`). O servidor precisa tratar o 0x75 também como "sair da loja visitada".
- Saída no GB [R]: `m_personal_shop.destroyShop` (`Room.cs:623`, **sem** avisar os visitantes, o que é um bug), zera `state`/`state_lounge` e envia sub 2.

---

## 2. Movimento, ações e chat

### 2.1 C→S 0x63 (ação do avatar) [C]

Layout: `u8 sub` + dados. O GB chama de `CLIENT_SYNC_ACTIVITY` e o S6 de `PLAYER_ACTION` (`Flags/ActionFlag.cs:11-50`, mesma numeração) [R].

| sub | Dados | Origem | O que o servidor faz |
|---|---|---|---|
| 4 | f32 x, f32 z, f32 ângulo (absolutos) | criação do próprio avatar (HandleMsg L4153) | grava; marca "pronto" (para de reenviar 0x46); repassa 0xC2 sub 4 |
| 5 | u32 action, só quando muda. 0 = em pé/andando; 1 e 2 alternam com PageDown/PageUp (sentar/deitar [P]; GB/S6: 0 em pé, 1 sentado, 2 dormindo [R]) | `ntAvatar::SetAction` @00537170 | grava; repassa sub 5 |
| 6 | f32 dx, f32 dz (deltas acumulados), f32 ângulo absoluto | `ntAvatar::SendMove` @00537210. Sai quando o deslocamento passa de velocidade × 16,5, ou forçado por `Stop()` | x += dx, z += dz, ângulo = valor; repassa sub 6 com o delta |
| 7 | str nome do movimento (emote) | `ntAvatar::ChangeMotion` @0053a750, só para tipo 4, limite de 1 por 1000 ms | repassa sub 7 |
| 8 | u32 bits de estado, só quando muda | `ntAvatar::SetState` @00537ba0 | grava; repassa sub 8 |

Na sala normal (fora do lounge) o 0x63 tem os subs 0 (4 bytes), 1 (string), 2 e 3 (sem dados) [R: emu `notes_core.md:46`]. É também o primeiro pacote que o cliente envia de dentro da sala.

### 2.2 Bits de estado (sub 8 e sSlotInfo.state) [C]

Fonte: `ProcessUI` @00544530 L2690-2735 e `OnPreserveBack`.

| Bit | Significado |
|---|---|
| 0x02 | MyRoom |
| 0x04 | Shop |
| 0x08 | loja bongdari |
| 0x10 | raspadinha |
| 0x20 | reciclagem |
| 0x40 | ranking |
| 0x80 | loja pessoal aberta |
| 0x100 | loja pessoal esgotada |

Qualquer bit de 0x7E bloqueia o movimento do avatar.

### 2.3 S→C 0xC2 (ação de outro avatar) [C]

Layout: `u32 guid, u8 sub`, depois os mesmos dados do 0x63. O sub 9 não tem dados (atualiza a lista de usuários).

- Vira a msg 0x5B (HandleMsg ~L3770-3830).
- **Ignorado quando o guid é o próprio jogador ou não tem avatar**, então o servidor pode excluir o remetente do broadcast.
- sub 4: teleporta e toca o efeito `Talk_Room.seq`.
- sub 6: o destino é o último alvo + delta.

Equivale ao 0xC4 do GB/S6 (KR + 2) [R]. O GB manda para a sala toda, inclusive o remetente, e valida só a existência da sala: não confere limite de mapa nem velocidade (`Channel.cs:2131-2292`) [R].

**Anti-trapaça mínima** (o cliente não valida o que recebe):
- valores finitos;
- |dx|, |dz| limitados por pacote (o cliente envia a cada ~16 unidades);
- string do sub 7 curta;
- limitar a taxa de pacotes.

### 2.4 Chat [C]

- C→S **0x03** str nick, str texto (`ChatManager::SendChatMessage` @00717d10).
- S→C **0x3E** u8 tipo, str nick, str texto, para **todos, inclusive o remetente**: online o cliente não mostra o próprio texto localmente (`OnChatInputEnterKey` @00543400).

**Tipos do 0x3E:**

| Tipo | Efeito |
|---|---|
| 0 | balão sobre o avatar, emote ou expressão pelo texto (msg 0x60), linha no log |
| 3 | "votação de expulsão recusada" |
| 7 | "알림 : " + texto |
| 10 | "%s ganhou %d pang" (+ u32) |
| outros (1-7) | avisos de sussurro; qual número é qual é [P] |

O bit 7 do tipo muda a cor e tira o balão.

**Filtros do cliente:**
- no recebimento, se os dois avatares estão a mais de **256 unidades**, a mensagem não aparece;
- lista de bloqueados e `FilteringHack`;
- texto que começa com "!" não vai para o log;
- o envio exige a flag golfdoc+0x151.

### 2.5 Equipamento e efeitos visíveis [C]

**C→S 0x0C sub 4** (u8 4, u32 id do personagem). Enviado em `OnRestorePreserved` @0053f4f0 L3184, na volta do Shop/MyRoom. Resposta: S→C **0x49 sub 4** em broadcast.

**S→C 0x49** (tratado no OnPacketCommon, handler 0x734988, tabela de subs 0x746444). Layout: `u8 sub, u32 guid`, depois:

| sub | Dados | Efeito |
|---|---|---|
| 1 | caddie 0x19 | msg 0x4A |
| 2 | u32 | msg 0x4C |
| 3 | taco 0x12 | msg 0x4B |
| 4 | sCharacterInfo 0x1BC | troca a roupa e recarrega o modelo (msg 0x49) |
| 5 | mascote 0x3F | msg 0x4E |
| 6 | u32 tipo SP, f32 valor | msg 0x24E: liga ou desliga o efeito. O valor provavelmente é duração ou escala [P] |

**Itens SP** (`/comando` → `SpCommand` @0053e760 → `UseSPItem` @005374a0): C→S **0x0C sub 6** = u8 6, u32 meu guid, u32 tipo.

| Tipo | Efeito |
|---|---|
| 0 | 거인 (gigante) |
| 1 | 왕머리 (cabeça grande) |
| 2 | 광속 (andar rápido) |
| 3 | 반짝이 (fogos) |

- O cliente só envia com a peça equipada (`CanUseSPItem` @0053d170 procura em parts[24] e aux[5]). O tipo 3 tem recarga de 1 s.
- Resposta: 0x49 sub 6 em broadcast.
- S→C **0x19B** u32 guid, 5 × u32: valores SP 0..4 desse avatar. Provavelmente é a resposta ao C→S **0xED** u32 guid [P].
- No GB [R]: tipo `TC_ITEM_EFFECT_LOUNGE` = 6. Ele valida a peça Jester/Hermes/Twilight equipada (erros 0x57007/8/9), alterna `scale_head`/`walk_speed` entre 1.0 e 2.0 e responde com 0x4B (= KR 0x49) tipo 6 (`Room.cs:3608-3700`). O GB também tem 0x196 (= KR 0x194?) com `StateCharacterLounge` 4×f32. No KR o 0x196 é consumido sem efeito pela avatar task.

### 2.6 Outros S→C tratados pela avatar task [C]

Fonte: `ntAvatarChatTask::OnPacket` @00546040, `GH/avatar_task.c` L1949-3057.

| Id | Layout | Efeito |
|---|---|---|
| 0x76 | u32 guid, u8 | pronto (handler comum) |
| 0x7A | u32 guid, **u16 0xFFFF** | novo dono (msg 0x5F) |
| 0x7F | str nick, u32 | voto de expulsão; ignorado no lounge |
| 0x82 | u8 flags, str nick, str texto | sussurro |
| 0x8C | u32 guid, u8 (1 = liga) | bit bSleep (ausente) do slot |
| 0x92 | u8 | resultado de denúncia |
| 0x9C | u8 clima, u8, u8 | `SetWeather` |
| 0x3F | str | ignorado |
| 0x196 | — | consumido |
| 0x19B | ver §2.5 | |

Também chegam pelo OnPacketCommon: 0x4A, 0x44, 0x45, 0x40.

### 2.7 Outros C→S do lounge [C]

| Id | Layout | Origem | Prioridade |
|---|---|---|---|
| 0xED | u32 guid | um por avatar criado | aceitar em silêncio; opcional 0x19B |
| 0x26 | u32 uid alvo | expulsão (`OnKickAvatar` @00542a90), só com o meu dwIdentity bit 4 | opcional; validar GM no servidor |
| 0x09 | u16 sala atual, str senha | `OnPasswordDlgResult` @00543f70 | já tratado |
| 0x3C | u16 0x111, u32 uid, str nota, u8 0 | bilhete (`OnNoteDlgResult` @005428f0) | opcional |
| 0xE0 | str nick | sussurro recusado (`avatar_task.c` case 0x82) [P] | opcional |
| 0x74–0x7D, 0x11A | loja pessoal | ver §3 | |
| 0xDB | troca direta | ver §6 | opcional |

### 2.8 Recursos que não precisam de servidor ou não existem [C]

- **Só cliente:** jukebox, NPC (ator CNPC), câmera, log de chat (`avatarchatlogdlg`, usa `m_chatLineList`) e dicas (`avatartipdlg`).
- **Megafone:** não existe pacote de megafone no lounge. Há só os tipos de aviso do 0x3E e o 0x40 comum (o GB também não tem nada) [C/R].
- **Convite e mini-jogos:** nada específico do lounge. Raspadinha, reciclagem, bongdari e ranking são diálogos comuns que só ligam bits de estado.

---

## 3. Loja pessoal (개인상점)

### 3.1 Estado no cliente [C]

| Campo | Offset em `CSharedDoc` | Conteúdo |
|---|---|---|
| eStateTrade | +0x5200 | 0 nenhum, 1 aberta (dono), 2 editando, 3 esgotada, 4 visitando |
| lista de `sTradeItem` | +0x5204/5208 | |
| quantidade de itens | +0x520c | |
| UID da loja atual | +0x521c | |
| título | +0x5220 | |
| renda | +0x523c | |
| Level | +0x539 | |
| i64 Pang | +0x53a | |

Mensagens de resultado: `SetTradeResultCode(code, pktId, item*)` @0x68e160. Textos em `FrTradeWarningDlg::SetWarningMessageType` @0x684a20 (tabela 0x684f58).

### 3.2 sTradeItem (0xAA) [C]

Fonte: `shared/globalnetworkdefine.h`; bate com `Structs.g.cs`.

| Offset | Campo | Observação |
|---|---|---|
| 0x00 | iIndex | posição na loja, 0..n-1 |
| 0x04 | dwTid | |
| 0x08 | dwGuid | id do objeto do dono |
| 0x0C | iNum | quantidade |
| 0x10 | byItemType | |
| 0x11 | wTime | |
| 0x13 | i64Price | **preço unitário**: o diálogo de compra faz `_allmul(qtd, price)` |
| 0x1B | dwUpgradeCost | |
| 0x1F | arrEnchant[5] | |
| 0x29 | wFlagTradeLimit | bit 0 = comprador não pode comprar |
| 0x2B | UccIndex[9] | |
| 0x34 | Seq | |
| 0x36 | status | |
| 0x37 | attachCardTid[12] | |
| 0x67 | CharacterSlotNum | |
| 0x69 | CaddieSlotNum | |
| 0x6B | ItemName[41] | |
| 0x94 | CopierNick[22] | |

O GB usa um TradeItem de 0xA8 com `u32 index` na frente (PersonalShopItem 0xAC). O layout é outro; não copiar [R].

### 3.3 C→S [C]

Os emissores ficam em `CSharedDoc` @0x40c730..0x40cb40.

| Id | Nome | Layout | Pré-condição no cliente |
|---|---|---|---|
| 0x76 | Editar/criar | vazio | menu "store" do próprio avatar com estado 0 (criar) ou 2; botão Edit do `FrTradeDlg` |
| 0x79 | Título | Str (1..31 bytes) | passa por `ChatManager::Filtering`; só envia se mudou; enviado **antes** do 0x7C quando muda |
| 0x7C | Publicar | u32 saleType (1 normal, 2 pacote) + u32 n + n × sTradeItem (iIndex 0..n-1) | `FrTradeFinishEditDlg::OnOkBnUp` @0x68f0f0. **n 1..6**: com 0 ou >6 o cliente dá erro 0 e envia 0x75 |
| 0x74 | Reabrir | vazio | estado 2 com itens ≠ 0 (`FrTradeEditDlg::OnExitBnUp`) |
| 0x75 | Fechar/sair | vazio | estado ≠ 0. **O visitante também envia** ao sair do lounge |
| 0x77 | Entrar na loja | u32 ownerUID (= sSlotInfo.dwUserUID) | `OnEnterTradeShop` @0x543d90. Exige estado 0 e distância ≤ ~19,2 ("더 가까이 가야 합니다") |
| 0x78 | Sair da loja | u32 shopUID | destrutor do `FrTradeDlg` no modo visitante |
| 0x7A | Visitantes | vazio | estado 1 ou 3 |
| 0x7B | Renda | vazio | estado 1 ou 3 |
| 0x7D | Comprar | u32 ownerUID + sTradeItem (iNum = quantidade desejada) | `SendTradeBuyItem` @0x40cb40. Exige estado 4, uid ≠ 0/-1, tid ≠ 0, guid ≠ 0, price ≠ 0, iNum > 0 |
| 0x11A | Comprar pacote | u32 ownerUID + u8 n + n × sTradeItem | `FrTradeBuyDlg::OnConfirmResult` @0x68e920 |

### 3.4 S→C [C]

Erro em qualquer resposta: o mesmo id com `D4 código` ≠ 1 (ver §3.6).

| Id | Resposta a | Layout | Efeito no cliente | Para quem (servidor) |
|---|---|---|---|---|
| 0xE1 | 0x74 / publicar | D4 res; se 1: Str nick | AvatarChat 0xE8(nick, 1) liga o ícone; se o nick é o meu, estado = 1 | lounge todo |
| 0xE2 | 0x75 | D4 res; se 1: Str nick, D4 uid | apaga o ícone. Se uid == loja que eu visito, ou é o meu: InitTradeData e fecha os diálogos. O visitante vê o código 3 ("상점이 닫혔습니다") | lounge todo |
| 0xE3 | 0x76 | D4 res; se 1: Str nick, D4 uid | apaga o ícone. Dono: estado 2 e abre `tradeeditdlg`. Visitante daquela loja: código 4 ("편집중") e fecha | lounge todo |
| 0xE4 | 0x77 | D4 res; se 1: D4 saleType, Buf[22] nick do dono, Str título, D4 ownerUID, D4 n, n × sTradeItem | estado 4, abre `FrTradeDlg` | visitante |
| 0xE5 | 0x78 | D4 res | se 1, InitTradeData | visitante |
| 0xE6 | 0x79 | D4 res; se 1: Str título, D4 uid, Str **ID de login** | o balão usa `FindAvatarByIDName` com a 3ª string (o GB manda o nick [R]) | lounge todo |
| 0xE7 | 0x7A | D4 res, D4 visitantes | | dono |
| 0xE8 | 0x7B | D4 res, D8 renda | | dono |
| 0xE9 | 0x7C | D4 res; se 1: D4 saleType, Buf[22] nick, D4 uid (SetTradeUID), D4 n, n × sTradeItem | estado 1, abre o `FrTradeDlg` do dono | **só o dono**: mandar a terceiros quebra o estado deles. Para os outros, 0xE1(1, nick) [P] |
| 0xEA | 0x7D | ver abaixo | | vendedor e comprador |
| 0xEB | venda | Str ownerNick, D4 ownerUID, sTradeItem (com o que sobrou), D4 state | o D4 state só é lido se ownerUID == loja atual, mas mandar sempre. state 3 = esgotada: "모든 상품이 판매되었습니다"; o visitante fecha e o dono vê `ShowStaticOutOfStock`; o ícone de esgotado (0xE8 estado 3) só é aplicado por quem está na loja | dono e visitantes |
| 0xEC | — | u8 flag | código 15: flag 1 = "개인상점 이용 제재 중" (você está punido); 0 = loja punida | |
| 0x1D6 | venda em pacote | D4 err (0 = OK); se 0: D8 (ignorado), D8 pang (delta), u8 n, n × sTradeItem | `CTradeHandler` tipo 2 | |
| 0x1D7 | compra em pacote | D4 err; se 0: D8 pang (absoluto), u8 n, n × {sTradeItem, u8 countingType 2..3, sItemInfo} | tipo 1, abre `traderesultdlg` | |
| 0x1D8 | redução em pacote | D4 err; se 0: Str nick, D4 shopUID, u8 n, n × sTradeItem, D4 state | tipo 3 | |

Nos três pacotes 0x1D6–0x1D8, err ≠ 0 vai para `OnNotifyTradeErrorCode`, e os códigos 1900001..1900039 aparecem como "묶음판매 오류 [%d]".

**0xEA (resultado da compra), layout:** `D4 res`; se res = 1: `u8 role, D8 pang, sTradeItem`, e então:

**role = 1 (vendedor).** O cliente tira do próprio inventário, conforme `tid>>26`:

| Grupo | Ação |
|---|---|
| 2 (parte/UCC) | remove |
| 4 (taco) | remove |
| 5, 6, 0x1C | decrementa `iNum`; apaga se zerar |
| 7 (caddie) | remove |
| 0x1F (carta) | remove |

Em seguida: `pang += D8` (**delta**), `AddTradeIncome` e a mensagem "%s이(가) %d개 판매되었습니다".

**role = 0 (comprador).** Lê `u8 kind`:

| kind | Dados |
|---|---|
| 1 | sItemInfo(168) de item empilhável (soma na pilha existente) |
| 2 / 3 | sItemInfo(168) de item novo |
| 4 | caddie (25 bytes) |
| 5 | carta (58 bytes) |

Depois **pang = D8 (absoluto)** e abre `FrTradeResultDlg`.

Lembrete: "D8" só lê os 32 bits baixos.

### 3.5 Como os outros veem a loja [C]

- **Não existe pacote com a lista de lojas.** Cada avatar carrega `sSlotInfo.state` (bit 0x80 aberta / 0x100 esgotada) e `strTradeTitle` @0x87.
- O ícone muda em tempo real pela msg AvatarChat 0xE8, gerada a partir de 0xE1/E2/E3/E9/EB:

  | Estado | Ação no ícone |
  |---|---|
  | 0 ou 2 | `SubState(0x180)` |
  | 1 | `AddState(0x80)` |
  | 3 | tira 0x80 e põe 0x100 |

- O 0xC2 sub 8 (u32 state) também acerta o estado.

### 3.6 Códigos de resultado (eResultTrade, tabela 0x684f58) [C]

| Código | Mensagem |
|---|---|
| 0 | erro, "관련된 데이터가 초기화됩니다" (dados reinicializados) |
| 1 | OK |
| 2 | já está aberta |
| 3 | loja fechou |
| 4 | loja em edição |
| 5 | título vazio |
| 6 | título inválido |
| 7 | palavra proibida |
| 8 | nenhum item registrado |
| 9 | item não é seu |
| 10 | quantidade inválida |
| 11 | preço inválido |
| 12 | preço acima de 30 milhões |
| 13 | item já vendido |
| 14 | muitos visitantes |
| 15 | sanção (flag do 0xEC) |
| 16 | manutenção |
| 17 | título só com espaços |
| 18 | confirmar fechar (cancela itens); o OK envia 0x75 |
| 19 | confirmar fechar (esgotada) |
| 20 | confirmar cancelar registro |
| 21 | pang insuficiente |
| 22 | tudo vendido, obrigado |
| 23 | diálogo de limite de level |
| 24 | itens demais |
| 25 | item com carta encantada não pode ser exposto |
| 26 | não está em canal |
| 27 | não está em sala de bate-papo |
| 28 | limite de lojas na sala ("use outra sala") |
| 29 | item não vendável em pacote |
| 30 | pacote contém item não vendável em pacote |

O GB devolve códigos "system error" grandes (5200100…), que **não** servem para o KR [R].

**Outras checagens do cliente:**
- `IsControlServerService(0x20)` mostra "개인상점 관련 부분 점검중" (manutenção).
- A lista de servidores tem a flag "개인 상점 블럭".

### 3.7 Validações do cliente que o servidor precisa refazer

**Feitas pelo cliente [C]:**
- **Level:** Level ≠ 0 (doc+0x539), senão código 23. O GB usa `LIMIT_LEVEL_PERSONAL` (6 no código, 1 no ini) [R].
- **Preço:** 1..30.000.000 (`< 0x1C9C381`); a caixa aceita até 9 dígitos.
- **Quantidade:** 1..disponível, já descontado o que está equipado e o que já está na loja; máximo de 6 itens.
- **Pang:** saldo suficiente para comprar (código 21). `ConfirmInsertItem` trata o limite de 20.000 itens.
- **Título:** não vazio, < 32 bytes, filtrado, só espaços → 17.
- **Venda em pacote:** só partes não-UCC (`tid>>26 == 2`) e tacos (4), senão códigos 29/30.

**Itens vendáveis** (`FrTradeEditDlg::SetListEnableTradingMyItem` @0x68cfa0) [C]:
- No IFF, `((IFF_COMMON[+0x68] >> 1) & 0xF)` precisa ser 1 ou 3: bit 0x02 ligado e bits 0x08/0x10 desligados.
- Ficam de fora:
  - partes equipadas em qualquer personagem (`IsEquipParts`);
  - partes com carta anexada;
  - o taco equipado (+0x630), +0x634 (bola equipada? [P]) e +0x628 (caddie? [P]);
  - a quantidade equipada dos consumíveis.
- Cartas só aparecem com o conteúdo 0x51 ligado.
- O comprador não consegue comprar item com `wFlagTradeLimit` bit 0.
- Itens com tempo e de cookie: o cliente não confere nada além da flag do IFF. O servidor decide [P].

**Taxa:** **não existe no cliente KR.** As strings de "수수료" só aparecem em presente e correio [C]. O GB cobra 5% (`PersonalShop.cs:492-498`) [R]. Como a tela do KR não mostra taxa, não cobrar.

**Regras do GB que valem a pena** [R], de `PSM` e `GB/Game/PersonalShop.cs`:
- só em sala lounge;
- até 6 itens;
- até 15 visitantes;
- lojas por sala ≤ 80% do `max_player`;
- título único na sala;
- **o preço usado na compra é o da loja, nunca o do pacote 0x7D**;
- a loja precisa estar aberta (em edição recusa);
- o comprador precisa estar registrado como visitante;
- conferir `iIndex`, quantidade ≤ estoque e pang;
- quantidade 0 vira 1;
- a transferência confere que o dono ainda tem o item;
- itens à venda continuam no inventário, mas ficam **travados**: outras operações (usar, card, Tiki…) são recusadas enquanto estão na loja (`Room.checkPersonalShopItem`, `Room.cs:1784`).

**Bugs do GB que não devem ser copiados** [R]:
- a primeira abertura sempre falha (`PSM:390-402`);
- `_findShop` cria loja implicitamente;
- o erro de close sai com o id errado;
- a compra não é atômica;
- `destroyShop` não avisa os visitantes.

### 3.8 Fluxo completo (servidor)

1. **Criar.** Dono 0x76. Servidor: cria a loja em edição e envia 0xE3(1, nick, uid) a todos. Os visitantes dessa loja (se reeditando) saem.
2. **Título.** Dono 0x79 título. Servidor: valida (único na sala), grava em `sSlotInfo.strTradeTitle` e envia 0xE6(1, título, uid, login) a todos.
3. **Publicar.** Dono 0x7C (saleType, itens). Servidor: valida cada item contra o inventário e envia:
   - 0xE9 ao dono;
   - 0xE1(1, nick) aos outros;
   - state |= 0x80 com 0xC2 sub 8 aos outros.
4. **Visitar.** Visitante 0x77 uid. Servidor: registra o visitante e envia 0xE4 com os itens.
5. **Comprar.** Visitante 0x7D. Servidor: valida e **reserva sob o lock**, grava as duas contas numa transação e envia:
   - 0xEA role 1 ao dono;
   - 0xEA role 0 ao comprador;
   - 0xEB ao dono e aos visitantes;
   - quando esgotar: state 3 e bit 0x100.
6. **Sair da loja.** Visitante 0x78. Servidor: 0xE5(1).
7. **Fechar.** Dono 0x75 (ou sai da sala ou desconecta). Servidor: 0xE2(1, nick, uid) a todos, limpa título e bits e solta os visitantes.

---

## 4. Emulador Python (o que já existia) [R]

**Lounge**
- O modo 2 é tratado como sala genérica (`room.py:16`, `:451-458`).
- O 0x63 (`room.py:555-564`) só reenvia 0x46 uma vez e **não retransmite 0xC2**.
- O `slot_info` não preenche posição nem loja.

**Loja pessoal** (`ext_trade.py`)
- Só a própria loja, em memória (`sess.state['pshop']`). Handlers 0x74–0x7D em `:71-150`; dispatch em `:204-210`.
- 0x77 e 0x7D sempre falham com código 3.
- 0xEB nunca é enviado.

**Outros**
- Mercado/estante (0xC5–0xCA) e troca 0xDB caem no caminho de falha (`:160-201`).
- SPEC-coverage.md L180-189 marca os layouts 0x74–0x7D como verificados e os pares pedido→resposta como inferidos.

---

## 5. Implementação atual no servidor C# (commits 07eb16a, 0e71aa8): o que está certo e o que falta

Os arquivos são `GameHandler.Lounge.cs`, `GameHandler.Trade.cs`, `Domain/Rooms/PersonalShop.cs` e `RoomPackets.SlotKey`/`SlotInfo`.

**Já de acordo com o cliente:**
- u16 0xFFFF no 0x46/0x7A do lounge;
- reenvio do 0x46 a cada 1 s até o 0x63 sub 4 (15×);
- 0x63 sub 4..8 gravados e repassados como 0xC2 (exceto ao remetente), com checagem de finito e passo ≤ 200;
- sSlotInfo com posição, action, state e título;
- chat também ecoado ao remetente;
- 0xED aceito em silêncio;
- loja: códigos KR, até 6 itens, preço ≤ 30M, 15 visitantes, 80% da sala, título único, preço da loja na compra, reserva sob o lock + transação, 0xEA/0xEB nos formatos da §3.4, 0x75 tratado também como "sair da loja visitada", loja fechada ao sair da sala, sem taxa.

**Lacunas e riscos:**

1. **[C] 0x0C sub 6 (itens SP).** O layout tem dois u32 (guid, tipo) e não um. Falta a resposta 0x49 sub 6 (u32 tipo, f32 valor) em broadcast. O `QuickEquipAsync` cai no `default` (mascote). Validar a peça equipada como `CanUseSPItem` faz. Valor do f32: [P] (GB usa 2.0 ou 1.0).
2. **[C] 0xE6 manda o login** (`Player.Login`) para todo o lounge, porque o cliente localiza o avatar pelo "ID". Isso expõe o login das contas. Alternativa a testar: confirmar qual campo `FindAvatarByIDName` compara (se for `sSlotInfo.sDisplayID` @0xD1, preencher esse campo com o nick e mandar o nick).
3. **[P] Itens no 0xE4/0xE9/0xEA.** Saem só iIndex/tid/guid/iNum/preço. Para tacos melhorados, UCC e partes com carta, preencher byItemType, wTime, arrEnchant, UccIndex, ItemName e attachCardTid a partir do item real. O cliente mostra o diálogo com esses dados.
4. **[R] Travar os itens à venda** contra outras operações (usar, reciclar, Tiki, correio, presentear, equipar), como o `checkPersonalShopItem` do GB. Hoje a transferência só confere na hora da compra se o item ainda existe (falha limpa com código 13, mas o anúncio fica errado).
5. **[C] 0x7A:** já usa `SlotKey` (0xFFFF no lounge, índice da sala nas outras). OK.
6. **[P] 0x9C (clima)** não é enviado. O lounge fica sempre com tempo bom. Opcional (o GB sorteia chuva).
7. **[C] 0x26 (expulsar), 0x3C (bilhete), 0xDB (troca direta), 0x11A (pacote)** não estão implementados. O 0x11A responde 0xEA código 29; por ser de pacote, o certo seria **0x1D7** com err ≠ 0 [C].
8. **[C] Proteções do 0x03 no lounge:** o filtro de 256 unidades é do cliente. O servidor pode mandar para a sala toda.

---

## 6. Troca direta e offline trade (fora do escopo principal)

**Troca direta (private trade)** [C]
- C→S **0xDB** = u8 sub, u32 guid, Str nick; nos subs 5/6/7 vem um buffer de 0x368 bytes.
- S→C **0x187** vai para o ator "PrivateTrade" (case @0x732724).
- Subs, pela fonte de referência: 0 pedido, 1 recusa, 2 cancelar, 3 ocupado, 4 aceitar, 5 atualizar, 6 aguardar final, 7 OK final, 9 falha [C ref / P no 645].
- Exige os dois avatares na mesma sala. É um recurso de lounge, mas opcional.

**Offline trade** (estante 진열대 do MyRoom + 팡야마켓, `IsLocalContent(0x66)`)
- C→S:

  | Id | Uso |
  |---|---|
  | 0xC6 | compra |
  | 0xC8 | u32 stallKey, u16 1, u8 slot+1 |
  | 0xC9 | busca no mercado |
  | 0xCA | u16 página |

- S→C: 0x161–0x169, 0x16C, 0x16D.
- **sStall** (0x927 bytes):
  - typeID [P]
  - stallKey = id da estante, usado como chave da lista [C]
  - state
  - maxSlotNum / useSlotNum
  - 12 × sStallSlot
  - time
- **sStallSlot** (0xC1 bytes):
  - stallKey
  - Idx
  - sTradeItem
  - time = validade do slot [P]
  - IsPermanence = slot permanente [P]
  - IsValid
- Os slots são comprados com pang ou cookie (mensagens [C]). O conteúdo 0x66 está desligado no KR segundo o emulador [R]. O bitset de `IsLocalContent` (0xb7c4b0) é preenchido em tempo de execução e não foi resolvido.
- **Não é do lounge.**

**Armazém** (`itemstorage`): C→S 0xCE–0xD6, conteúdo 0x71. Fora do lounge.

---

## 7. Lista priorizada

| # | Item | Estado no C# |
|---|---|---|
| 1 | [C] 0x47 → 0x46 sub 0 com **u16 0xFFFF**, reenviado até o 0x63 sub 4; 0x46 sub 1/2 a quem já está; 0x7A com 0xFFFF | feito |
| 2 | [C] 0x63 sub 4/5/6/7/8 → gravar e repassar 0xC2 (guid, sub, dados) à sala, sem o remetente; validar finito e passo | feito |
| 3 | [C] sSlotInfo com x/z/ângulo (0x77..), action (0x73), state (0x6D), título (0x87) para quem entra depois; manter o curso e os buracos pedidos (o cenário é curso + buraco) | feito |
| 4 | [C] Chat 0x03 → 0x3E para a sala toda, **inclusive o remetente** | feito |
| 5 | [C] 0x0F/desconexão: 0x4A, 0x46 sub 2, fechar a loja do jogador e tirá-lo da loja visitada | feito |
| 6 | [C] Loja: 0x76→0xE3, 0x79→0xE6, 0x7C→0xE9 (dono) + 0xE1 (outros) + estado 0x80, 0x77→0xE4, 0x78→0xE5, 0x75→0xE2, 0x74→0xE1, 0x7A→0xE7, 0x7B→0xE8 | feito |
| 7 | [C/R] Compra 0x7D: preço da loja, visitante registrado, estoque, pang, reserva + transação, 0xEA (vendedor: delta; comprador: absoluto + kind + sItemInfo), 0xEB ao dono e visitantes, esgotada = 3 / 0x100. Sem taxa | feito |
| 8 | [R] Travar itens à venda contra outras operações | **falta** |
| 9 | [C] 0x0C sub 6 (SP) → 0x49 sub 6 broadcast, com dois u32 no pedido; 0x0C sub 4 → 0x49 sub 4 (roupa) | **falta** o sub 6; sub 4 existe |
| 10 | [P] sTradeItem completo (nome, UCC, enchant, cartas, tempo) nas listas da loja | **falta** |
| 11 | [C] 0xE6: decidir entre o login e o campo que `FindAvatarByIDName` lê (privacidade) | **revisar** |
| 12 | [P] 0x19B como resposta ao 0xED (valores SP); 0x9C clima | opcional |
| 13 | [C] 0x26 expulsar (só GM), 0x3C bilhete, 0x8C ausente, 0x92 denúncia | opcional |
| 14 | [C] Venda em pacote 0x11A → 0x1D6/0x1D7/0x1D8; troca direta 0xDB/0x187 | opcional, grande |
| 15 | [C] Offline trade / estante (sStall) e armazém | fora do lounge; conteúdo provavelmente desligado |
