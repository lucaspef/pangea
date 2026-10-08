# KR 645 — GuildMatch (modo 6, "길드대전") e pontos/pang/troféus de guilda

Pesquisa somente leitura no cliente KR 645 QA (`ProjectG_ReleaseQA.exe`) para implementar o modo 6 no servidor C#.

Marcas: **[C]** lido no cliente (decompile/asm/reconstrução fiel); **[R]** tirado de uma referência (GB/JP
`/root/pangya-server/Server/GB`, que é de um cliente bem mais novo); **[P]** proposta/inferência nossa.

Fontes e abreviações:
- `LM` = `rebang/source/client/ProjectG/lobbymain.cpp`, `SD` = `scoredlg.cpp`, `MR` = `makeroomdlg.cpp`,
  `DR` = `detailedroominfodlg.cpp`, `GRGM` = `golfruleguildmatch.cpp`, `SH` = `shareddoc.h`, `HI` = `headicon.cpp`,
  `GGD` = `rebang/source/shared/globalgamedefine.h`
- `lt` = `ghidra-out/lobbytask.c` (CLobbyTask::OnPacket), `gt` = `ghidra-out/golftask.c` (CGolfTask::OnPacket),
  `gr` = `ghidra-out/golfrule.c`, `rd` = `ghidra-out/resultdlg.c`, `sdc` = `ghidra-out/shareddoc.c`,
  `lmc` = `ghidra-out/lobbymain.c`, `fi` = `ghidra-out/fontinfo.c`, `ui` = `ghidra-out/user_info.c`
- `OPC` = `CTask::OnPacketCommon` @0x732640 (só no `pg645.asm`; tabela de saltos idx = id−0x2D, bytes @0x746118,
  ponteiros @0x745E1C; id fora da tabela = 0x745DFD = ignora)
- GB: `Game/GameModes/GuildBattle.cs`, `Game/Manager/GuildRoomManager.cs`, `Game/Manager/DuplaManager.cs`,
  `Game/Dupla.cs`, `Game/Guild.cs`, `Game/Room.cs` (ids JP = KR + 2 nesta faixa: JP 0xBF = KR 0xBD, JP 0xC2 = KR 0xC0,
  JP 0x79 = KR 0x77; JP 0x157 = KR 0x14F)
- Já documentado: SPEC-guilda.md (estruturas GUILD_*), SPEC-modes.md (fluxo mass), SPEC-resultado-fim-de-jogo.md
  (§3: 0x77/0xCC), SPEC-room.md, SPEC-perfil-mapas.md (0x14F/0x153).

---------------------------------------------------------------------------------------------------------------------
## 0. Resumo

O GuildMatch do 645 é um **torneio em massa (fluxo do modo 4) com duelos 1×1 por buraco ("match play") entre pares
de jogadores de duas guildas**. Cada par é um "조" (grupo). Em cada buraco, quem fez menos tacadas no par ganha
**2 pontos**, empate dá **1** a cada um [C mensagens/placar; R pontuação]. O placar da guilda é a soma dos pontos dos seus
membros; ganha a guilda com mais pontos (desempate por pang) [R].

Pacotes específicos (S->C; não há C->S próprio do modo):

| id | quando | layout | onde o cliente trata |
|----|--------|--------|----------------------|
| **0xBD** | no começo, **antes do 0x50** | `u8 n, n × sGuildMatchup {u8 grupo, u32 guidVermelho, u32 guidAzul}` | só `CLobbyTask` (lt 5247) [C] |
| **0xC0** | a cada buraco decidido de um par (e quando alguém sai) | `u32 guid, i16 placarVermelho, i16 placarAzul, u8 pontosDoGuid, u8 pontosDoAdversário` | `CGolfTask` (gt 5697) e `CLobbyTask` (lt 5314) [C] |
| 0x77 (tipo 6) | fim | `… u8 equipeVencedora, 4 × u32 {…}, 12 × sAwardItem` | gt 5158 / lt 4277 [C] |
| 0xBE | — | `0x1C bytes` -> Doc+0x4540 (`m_guildRefreshData`) + `CUserInfo::Refresh` | lt 5279; **ninguém lê** [C] |
| 0xBF | — | `u32 uid, u16 n (≤10), n × sGuildMatchFlagInfo(0x2E)` -> lista Doc+0x4534 | lt 5284; **ninguém lê** [C] |

Fora isso o modo usa o fluxo mass normal (0x51/0x59/0x8E/0x6C/0x6B/0x6A/0x63/0xCC/0x77) já implementado em
`MassGame`/`MassOutput`.

---------------------------------------------------------------------------------------------------------------------
## 1. Sala: criar, entrar, times

### 1.1 Criar (FrMakeRoomDlg, C->S 0x08) [C]
- O tipo 6 fica no grupo "mass" (`MR` 1766-1784): figura `picture_guild.tga`, `text_mass`, **jogadores 10/20/30**
  (padrão 30), **buracos 9/18** (padrão 18), tempo **30/35/40 min** em 18 buracos e **20 min** em 9 (`MR` 2826-2845;
  `m_gameTime` padrão 1 800 000 ms), `m_holeType = 0`.
- O **mapa é travado em aleatório**: trocar para o tipo 6 faz `ChangeCourse(0x7F)` e desliga o clique no mapa
  (`MR` 1612-1620, `OnCourseUp` 2119 retorna para 6). O 0x08 chega com `course = 0x7F`.
- `IsControlServerService(0x400)` bloqueia o modo 6 (msg 0x272) (`MR` 1146-1150, 1383-1388) — o servidor manda esse
  bit no 0x42 (`controlServerService`); deixar 0.
- Sem guilda (`sUserInfo.dwGuildId == 0`) aparece "길드 가입자만 방을 만드실 수 있습니다." (`MR` 1196-1205, 1420-1426),
  mas o envio não é bloqueado em todos os caminhos → **o servidor tem de validar**.
- Tempo de tacada: `GetShotTimeLimitByGameType(6) = 0` (sem limite por tacada) e `GetGameTimeLimitByGameType(6) =
  2 400 000` (`LM` 192-235).

### 1.2 sGuildRoomInfo dentro do sRoomInfo (`GGD` 866-915) [C]
`sRoomInfo` (0xB2, pack 1) tem `sGuildRoomInfo GuildInfo` em **+0x5B** (74 bytes, pack 1):

| off (no GuildInfo) | off (no sRoomInfo) | tipo | campo |
|----|----|----|----|
| 0x00 | 0x5B | u32 | `nGuildID[0]` — guilda **vermelha** (time 0) |
| 0x04 | 0x5F | u32 | `nGuildID[1]` — guilda **azul** (time 1) |
| 0x08 | 0x63 | char[21] | `szName[0]` |
| 0x1D | 0x78 | char[21] | `szName[1]` |
| 0x32 | 0x8D | char[12] | `szEmblemName[0]` (marca sem ".png") |
| 0x3E | 0x99 | char[12] | `szEmblemName[1]` |

No Doc: `m_roomInfo` = Doc+0x4998, `GuildInfo` = Doc+0x49F3 (ids 0x49F3/0x49F7, nomes 0x49FB/0x4A10, marcas
0x4A25/0x4A31) (lmc 13626-13706, 21126-21173). O C# já tem `sGuildRoomInfo`/`sRoomInfo.GuildInfo` em `Structs.g.cs`
(hoje fica zerado).

**Como o cliente recebe a atualização:** `m_roomInfo` inteiro só é copiado (a) no 0x47 entrar na sala (lt 3711),
(b) no 0xBA "entrar em jogo em andamento" (lt 5143), e (c) **no 0x45 lista de salas, quando o registro tem o
`roomGuid` da minha sala** (`OPC` caso 0x45 @0x735006: `cmp cx,[Doc+0x49E1]` e `rep movs` 0xB2 bytes para Doc+0x4998
@0x735277/0x735547/0x7357FC) [C]. O 0x48 (configurações) **não** leva o GuildInfo. Portanto, quando a 2ª guilda chega
ou uma guilda esvazia, os que já estão na sala só ficam sabendo por um **0x45 (sub 3, a própria sala)** — hoje o C#
só manda 0x45 para quem está na lista de salas (`Lobby(...)`).

### 1.3 Entrar (C->S 0x09) [C]
- Lista/`OnRoomList` (`LM` 9586-9615) e `DetailedRoomInfoDlg` (`DR` 309-332): exige `dwGuildId != 0`
  ("길드에 가입하셔야 길드대전이 가능합니다.") e que a minha guilda seja uma das duas da sala **ou** que haja um lado
  vazio (`nGuildID[k] == 0`), senão "타길드의 대전입니다.". GM (`IsIdentity(0x14)`) pula a checagem no `DR`.
- Erros do 0x47 (`u8 código ≠ 0`, msg 0x3B, `LM` 4551-4630) [C]: **2** "정원이 초과되었습니다" (cheia), **3** "존재하지 않는
  방입니다", **4** "비밀번호가 틀렸습니다", **5** nível, **7/9** "방만들기 요청이 실패했습니다", **8** "게임 진행중인 방입니다",
  **13 "길드에 가입해야 합니다."**, 18 ilha. Não há texto para "outra guilda" → usar 13 (sem guilda) e, para
  guilda de fora, 2 (cheia) ou 8 [P].
- ⚠ O `JoinResult` do C# usa 1 = cheia/jogando, 2 = não existe, 3 = senha — **não bate com o cliente** (1 não tem
  texto; 2 mostra "cheia"; 3 mostra "não existe"). Corrigir junto: 2/3/4/8 e 13 [C].
- GB (`Room.cs` 120-128, 4630-4680) [R]: sem guilda -> erro; sala com duas guildas e eu de outra -> erro 11000; a
  **1ª guilda (vermelha) é a de quem criou/primeiro entrou**, a 2ª (azul) a do primeiro de outra guilda; quando uma
  guilda esvazia, o lado é zerado (`Room.cs` 564-612) e a próxima guilda nova ocupa o lado vazio.
- Cargo: o cliente só olha `dwGuildId`. No C# um pedido pendente (cargo 9) também tem `dwGuildId` [SPEC-guilda §2.1];
  **exigir cargo 1..3** no servidor [P].

### 1.4 Times e tela da sala [C]
- `RemakeTeam` (`LM` 7930-7975): vermelho = slots com `GuildId == GuildInfo.nGuildID[0]`, azul = `nGuildID[1]`; quem
  não casa com nenhum **não aparece** em nenhum time; lado sem ninguém -> o cliente zera aquele `GuildInfo` localmente.
- O **`bTeam` do sSlotInfo tem de ser o lado da guilda** (0 vermelho, 1 azul): ícones da lista (`LM` 13740-13752),
  `rival.team = slot.bTeam` no jogo (`GRGM` 55), soma por time na tela de resultado (rd 2641, `rival+0x33`) e
  "minha guilda venceu" (rd 6311/6342). Hoje `RoomManager.Join` faz `Team = count % 2`.
- Cabeçalho da sala (`OnGameRoomExt_GuildNumOwnerDraw`, `LM` 13970-14030): emblema+nome de cada guilda, `(n)` de
  cada time e, na tela pós-jogo ("GAMEROOM_EXTRES"), `m_guildScore[0/1]` (Doc+0x47B0/0x47B2, vêm do 0xC0).
- Lista de salas: nome do tipo "길드대전", emblemas das duas guildas e o título trocado por `"VS"` (`LM` 9328-9396);
  ordenação por proximidade da minha guilda (`LM` 8830-8845).
- Restrições do cliente no modo 6: mapa/buracos não mudam (`LM` 7602-7612, 13305-13330), **sem expulsar**
  (`LM` 11765-11773, 13835-13840), **sem início automático** (o contador de 6 s não dispara: `LM` 3022-3075),
  sem "relatório de uso" (`LM` 15221). Chat da sala começa no alvo "time" (`LM` 11124-11131 -> C->S 0x54, já tratado
  no C# por `Team`).
- Botão "convidar guilda" (`OnGameRoomExt_GuildInviteLBtnUp`, `LM` 14032-14110): conta, no lobby, jogadores livres de
  **outras** guildas; para cada guilda com ≥ `nUserNum` livres manda a cada membro **C->S 0xB2** `str nick, u32 uid`
  (se `IsLocalContent(S4_INVITE_FRIEND)`) ou **0x29** `u32 uid`; sem guilda elegível: "초대할 상대 길드가 없습니다."
  (tratamento do convite: SPEC-guilda §8 item 15).

### 1.5 Começar (C->S 0x0E) [C códigos / R regras]
Textos do 0x7D (lt 4378, strings do exe): **1** "게임을 시작하기 위해 필요한 유저의 수가 부족합니다." · **2** "팀의 인원수가
서로 맞지 않습니다." · 3 prontos insuficientes · 4 gente demais · 6 galeria · 7/8 falhou · 9 atualizar · 12 match ·
13 pang insuficiente.
GB `requestStartGame` (`Room.cs` 5389-5420): total ímpar -> erro; `isGoodToStart`: menos de 2 guildas -> erro;
**contagens diferentes** -> erro; **menos de 2 por guilda** -> erro [R]. Mapear: falta guilda/jogador -> **0x7D 1**;
contagens diferentes -> **0x7D 2** [P]. Bots não têm guilda: recusar `!bot` em sala de guilda [P].

---------------------------------------------------------------------------------------------------------------------
## 2. Durante a partida

### 2.1 Pares (sGuildMatchup) — S->C 0xBD [C]
`sGuildMatchup` (`SH` 63-67, pack 1, 9 bytes): `u8 team` (número do grupo "조", mostrado como `"%d 조"`),
`u32 uid[2]` — **guids** (o C# usa guid = uid da conta), `uid[0]` = jogador da guilda vermelha, `uid[1]` = azul.

0xBD (lt 5247-5278): `u8 n` (0 = nada), depois `n × 9 bytes` empurrados em `m_guildMatchupList` (vector @Doc+0x4524)
**sem limpar antes**. Para o par que contém **o meu guid** (Doc+0x43B, `MyGuid(false)`):
`m_teamPlayerGuid[0]` (Doc+0x5010) = eu, `m_teamPlayerGuid[1]` (Doc+0x5014) = meu adversário,
`m_guildTeam` (Doc+0x5020) = número do grupo.
- `GetRival(guid)` (sdc 52505 @0x41FCD0) percorre a lista **do começo** e devolve o **adversário** de `guid`.
- A lista só é zerada no 0x47 entrar na sala (lt 3704-3709), no 0xBA (`intrusion.cpp` 130) e no `ClearAll`
  (`ClearRivalVars`, sdc 51505). **O 0xBD só é tratado pelo CLobbyTask** (não está no `CGolfTask` nem no `OPC`:
  tabela -> 0x745DFD) → tem de chegar **antes do 0x50** (que troca para o CGolfTask). Ordem proposta no `Start()`:
  0x74 (jogadores) -> **0xBD** -> 0x50 [C].
- GB manda o equivalente (JP 0xBF) no `sendInitialData`, já dentro do jogo (`GuildBattle.cs` sendInitialData) — o
  cliente JP trata lá; o KR não [C/R].
- Formação dos pares (GB `DuplaManager.init_duplas`) [R]: i-ésimo jogador da guilda 1 com o i-ésimo da guilda 2
  (ordem de entrada na guilda da sala), grupo = i+1. O KR original não está no cliente [P: mesma regra, ordem do slot].

Uso no cliente [C]: cabeça do jogador mostra `"%d 조"` (`HI` 1177-1182); placar `score_guild` com o duelo do meu par
(`SD` 122-170 `SetGuildPlayer`, `GetGuildVersus` 981-1000: ganhou/perdeu/empate/sem resultado por buraco);
lista "guild rank" da tela pós-jogo (`LM` 14427-14590: grupo, emblema, nick, `rival.guildPoint`, `totalPang`,
"End"/"Out"/"%d홀").

### 2.2 Resultado do buraco no par — pelo 0x6B (cliente) [C]
No 0x6B (gt 4960-5045) em modo 6: além do normal, `GolfRule` msg 0x5E(hole, guid) e, se `guid` é do meu par, msg 0x9F
(se for eu: `ChangeGameMode(0x2000)`) (gt 5010-5035; gr 29942, 31291). A msg 0x5E (`LM` 4831-4866 / gr 29942) compara
`holeStroke[hole-1]` dos dois do meu par quando ambos têm valor e põe no chat:
"%s님이 %d홀에서 승리하셨습니다" / "%s님은 %d홀에서 패배하셨습니다" / "%d홀은 무승부가 되었습니다", e marca
`holeData[+0x1E]` = 0/1/2 (quem ganhou o buraco). `holeStroke` vem da diferença de tacadas totais do 0x6B
(gt ~4993) → o servidor não manda nada extra para isso, mas a sua pontuação precisa usar a mesma regra
(menos tacadas no buraco).

### 2.3 Placar de guilda — S->C 0xC0 [C layout / R valores]
```
u32 guid                -> rival[GetIndex(guid)]
i16 placar vermelho     -> Doc+0x47B0 (m_guildScore[0])
i16 placar azul         -> Doc+0x47B2 (m_guildScore[1])
u8  pontos de guid      -> rival[guid].guildPoint        (sRivalData+0x170)
u8  pontos do adversário-> GetRival(guid)->guildPoint
```
- No campo (gt 5697-5735): manda `Score` msg 0x57 (placar) e `Screen` msg 0x1CE -> HUD `CGuildInfo::SetScore(red,
  blue)` (fi 181: guarda e toca som; desenha no canto direito, fi 2473). No lobby (lt 5314-5345): `Lobby` msg 0x57.
- Se `guid` não está em nenhum par, `GetRival` = NULL e o pacote não tem efeito visível (só grava o placar) [C].
- `sRivalData.guildPoint` (+0x170) começa 0 (`GRGM` 77) e **só muda pelo 0xC0**; é ele que a tela de resultado soma
  por time (§3.2) → mandar 0xC0 a **todos da sala** (inclusive quem já voltou ao lobby).
- GB `sendFinishHoleDupla` (`GuildBattle.cs`) [R]: broadcast de JP 0xC2 com o oid de quem acabou de fechar o buraco,
  pontos das guildas (`Guild.getPoint` = soma dos pontos dos membros), `sumScore` dele e do adversário. Vai quando
  `finishHoleDupla` decide o buraco (os dois terminaram, ou o adversário saiu), quando alguém sai e quando o tempo
  acaba.

### 2.4 Pontuação (GB `DuplaManager.finishHoleDupla`, `GuildBattle.deletePlayer/finish_guild_battle`) [R]
- Por buraco da sequência: os dois terminaram -> menos tacadas = **2**, mais = 0, empate = **1/1**.
- Adversário fora do jogo quando eu termino o buraco -> **2** para mim.
- Alguém sai: para cada buraco até onde o adversário já chegou que o que saiu não terminou, o adversário ganha **2**
  (e o buraco conta como fechado). GB também **encerra o jogo do adversário** nessa hora (`finish_guild_battle`).
- Tempo esgotado: buraco que só um terminou -> 2 para ele.
- Pontos do jogador = soma dos 18 buracos (cabe em u8, máx. 36); guilda = soma dos membros (i16).

### 2.5 Diferenças para torneio (4) e torneio em equipe (5) [C]
| | 4 torneio | 5 torneio equipe | 6 guild |
|--|--|--|--|
| times | — | `bTeam` escolhido (0x10/0x7B) | `bTeam` = lado da guilda (`GuildInfo`) |
| entrada | livre | livre | só membros das 2 guildas |
| mapa | livre | livre | sempre aleatório |
| pacotes extras | — | — | 0xBD, 0xC0 |
| lista pós-jogo | ranking (`RoomUserRank`) | ranking + times | `RoomGuildRank` (pares), ranking escondido (`LM` 14398-14404) |
| abre o resultado | todos terminaram (lm 24255) ou 0x77 | todos terminaram (`LM` 7896) ou 0x77 | **só pelo 0x77** (§3.1) |
| troféu da sala/medalhas | sim | não | não (tidMatch 0) |
| 0x77 | sem extra | sem extra (equipe vencedora) | +4 × u32 |
| início automático/expulsar | sim | sim | não |

---------------------------------------------------------------------------------------------------------------------
## 3. Fim

### 3.1 Quando o resultado abre [C]
- No campo, o 0x77 é guardado (gt 5158-5210). Ao voltar ao lobby (`CGolfRule::ReturnLobby`, gr 14089; trecho
  gr 14160-14175) **no modo 6 com Doc+0x415E ≠ −1** (0x77 já chegou) posta `Lobby` msg 0x2D -> `FrUniteResultDlg`.
- No lobby, o 0x77 (lt 4277-4305) termina com `Lobby` msg 0x2D (abre na hora).
- Ao contrário do modo 5, o `RemakeTeam` do modo 6 **não** posta 0x2D quando todos acabam (`LM` 7824-7851 × 7863-7894).
  → no modo 6 a tela de resultado depende **só** do 0x77. Mandar o 0x77 a todos quando o jogo termina (como o GB, em
  `finish()`), **depois** de todos os 0xC0 (a tela soma `guildPoint` ao abrir).

### 3.2 0x77 completo no tipo 6 (gt 5158 / lt 4277) [C layout; R valores]
```
u32 EXP ganho                        -> Doc+0x415E (só lido se ainda for −1)
u32 tidMatch                         -> Doc+0x4568   (0 no modo 6: não há troféu de sala)
u8  troféu ganho                     -> Doc+0x456C   (0)
u8  equipe vencedora                 -> Doc+0x456D   (0 vermelho, 1 azul, 2 empate/nenhuma)
u32 pang de guilda ganho por mim     -> Doc+0x5018 (m_guildBonusPang)   GB: dup.pang_win[eu]
u32 meus pontos de guilda            -> Doc+0x501C (m_guildPoint)       GB: sumScore(eu)
u32 pang de guilda da vermelha       -> Doc+0x47B4 (m_guildRoundScore[0]) GB: Guild(RED).getPangWin()
u32 pang de guilda da azul           -> Doc+0x47B8 (m_guildRoundScore[1]) GB: Guild(BLUE).getPangWin()
12 × sAwardItem {u32 guid, u32 tid}  -> Doc+0x456E (todos 0xFFFFFFFF/0: sem medalhas no modo 6)
```
O tipo testado é `Doc+0x4668` no campo e `Doc+0x49E0` (gameType do `m_roomInfo`) no lobby. GB `sendPlacar`
(`GuildBattle.cs`) manda exatamente essa ordem (+24 B de medalhas que o KR não lê) [R].
**Onde os 4 × u32 aparecem:** só no `FrResultTeamDlg` (`OnMatchInit` rd 1657 = 0x501C "%d…", `OnBonusInit`
rd 5125 = 0x5018; 0x47B4/0x47B8 **ninguém lê**) — e o **FrResultTeamDlg não é criado no 645** (nenhuma chamada ao
construtor 0x81E520 no `pg645.asm`; o resultado mass é sempre `FrUniteResultDlg`, `LM` 4320-4395) [C]. Ou seja: os
4 × u32 têm de existir para o alinhamento (as 12 medalhas vêm depois), mas hoje são invisíveis. Mandar os valores
do GB mesmo assim.

### 3.3 FrUniteResultDlg no modo 6 [C]
- `SetResultDlgType` (rd 2294): tipo 4 -> 0 (individual), 5 -> 1 (equipe), **6 -> 2 (guilda)**.
- `FindWinner` (rd 2641): zera `m_teamResult` e, no tipo 2, soma por `rival.team` o **`rival.guildPoint`** em
  `m_teamResult[t].score` (Doc+0x4790 / 0x47A0) e o `totalPang` em `m_teamResult[t].pang` (Doc+0x4798 / 0x47A8).
- `OnTeamResultOwnerDraw` (rd 5887, trecho ~6020-6070): pontos e pang de cada lado; imagem
  `30in_team_redteam_win` / `30in_team_blueteam_win` pelo Doc+0x456D; com 2 (empate) usa
  `30in_team_{red|blue}team_draw` pelo time do jogador.
- `OnDescAreaOwnerDraw` (rd 2138): tipo ≠ 0 -> "RED 팀이 승리하였습니다." se 0x456D == 0, senão
  "BLUE 팀이 승리하였습니다." (também no empate — limitação do cliente).
- EXP/itens/medalhas como no torneio (SPEC-resultado §3); depois do diálogo o cliente manda C->S 0x06 e o servidor
  responde 0x43/0xC6 (fluxo atual do C#).
- (Morto) `FrResultTeamDlg::OnInit` (rd 6342) mostraria "%49s %s길드가 승리하였습니다." / "%49s 다시 한번 자웅을
  가리세요^^" e o troféu "TROPHY/guild" se a guilda do jogador venceu (`OnStageInit` rd 6311).

### 3.4 Vencedor e prêmios (GB `GuildRoomManager.calcGuildWin`/`saveGuildsData`, `DuplaManager`) [R]
- Vence a guilda 1 se: a guilda 2 não tem ninguém / todos da 2 saíram / `ponto1 > ponto2` / pontos iguais e
  `pang1 > pang2` (pang = soma do pang de jogo dos membros). Simétrico para a 2. Senão **empate (2)**.
- "Pang de guilda" por jogador (`pang_win`): vencedores `(nPares + nSaíram) × 50`; perdedores `nSaíram × 50 + 50`.
  No empate ninguém é "vencedor" → todos recebem o valor de perdedor (efeito do código do GB).
- `saveGuildMembersData`: membro `guild_pang += pang_win`, `guild_point += sumScore` (contribuição pessoal);
  `saveGuildsData`: guilda recebe `point` (soma), `pang` (soma dos `pang_win`) e vitória/derrota/empate
  (`CmdUpdateGuildPoints`, comentado no GB), e uma linha de histórico `GuildMatch {uid[2], pang[2], point[2]}`
  (`CmdRegisterGuildMatch`). O `pang_win` **não** vai para o pang do jogador (carteira) no GB.
- EXP no GB: igual ao torneio (`requestFinishExpGame`): `nJogadores × buracos × estrelas × taxas`, sem desconto por
  posição na prática (divisão inteira) [R]. O C# já faz o equivalente (`positionPenalty: false`).

### 3.5 guildPang / guildPoint no cliente [C]
- `GUILD_USER_INFO`/`GUILD_INFO`/`GUILD_LIST` +0x19 `guildPang`, +0x1D `guildPoint` (SPEC-guilda §1): o cliente só
  mostra; atualiza ao reabrir a janela da guilda (0x101 -> 0x1B6) ou por push 0x1BD/0x3B.
- Perfil (cache `sUserInfoTime`): **0x14F** termina com `u32` -> registro+0xBBA (`guildPoint` do perfil)
  (`OPC` @0x744A6D-0x744B23; GB `pacote157` manda aqui `mi.guild_point`, a contribuição do membro) [C/R].
  Hoje o C# manda 0.
- **0x155** `u32 uid, GUILD_INFO(0x129)` (`OPC` @0x744CBB-0x744D4F): registro+0xBB6 = `guildPang` da GUILD_INFO;
  se `uid == MyUID()` também +0xBBA = `guildPoint`; copia o nome da guilda para o registro (+0x57). O cliente não pede
  (só existe C->S 0x2F, ui 4637/9356) → o servidor pode mandar junto da resposta do 0x2F [C; P quando].
- `sUserInfo+0x6F` (0x42) recebe o `guildPang` do GUILD_USER_INFO (SPEC-guilda) — só no login.
- `m_guildPoint` (Doc+0x501C) seria o máximo da barra `GuildScoreGauge`, que está escondida (`LM` 13954-13962,
  `return` antes do `SetRange`).

---------------------------------------------------------------------------------------------------------------------
## 4. Troféus e ranking de guilda

### 4.1 sGuildTrophy [C]
- `struct sGuildTrophy { u32 TypeID; }` (`GGD` 355-358); listas `m_guildTrophyList` / `m_oldGuildTrophyList`
  (`SH` 465/524) e no cache de perfil (+0xBAE/+0xBB2 temporada atual, +0x13B7/+0x13BB temporada 0).
- Pacote: **S->C 0x153** `u8 temporada (5 atual, 0 anterior; outro ignora), u32 uid, u16 n, n × u32 TypeID`
  (SPEC-perfil-mapas; `OPC` @0x744D78). O C# já manda `n = 0`.
- Desenho: `FrUserInfoForm::OnGuildTrophyListOwnerDraw` (ui 5205) faz `FindMatch(TypeID)` no **Match.iff**, escreve o
  nome (quebrado em `\n`) e o bitmap `TROPHY/<Icon[0]>`.
- Os troféus de guilda do Match.iff 645 são **troféus de evento** do grupo 0x2D0A (dumpado de `data/pangya.iff`):
  "세기의대결 길드대항전 우승" 0x2D0A0300; "제N회 길드대전 1/2/3위" (ícones `s8_1..3`), "…1/2/3위 트로피" (`s6_1..3`),
  "베스트길드원" (`s7_1`) para N = 1 (0x2D0A11xx-17xx), 2 (0x2D0A18xx-1Exx), 3 (0x2D0A25xx-2Bxx), 4 (0x2D0A3Cxx-42xx),
  5 (0x2D0A43xx-49xx), 6 (0x2D0A4Axx-50xx) e "길드 챔피언십" 2011 (0x2D0A56xx-5Cxx); mais os PH "Trophy Guild
  Master" 0x2D0A3000 etc. **Nenhum é ganho numa partida comum** → entregues por GM/evento [P].
- O resultado não mostra troféu de guilda no 645 (só o `FrResultTeamDlg` morto teria "TROPHY/guild").

### 4.2 Ranking de guilda [C]
- Não existe tela de ranking de guildas no 645: `rankingdlg.c` não fala de guilda; a lista de guildas (0x10D ->
  0x1BA/0x1BB, 15 por página) mostra `guildPang`/`guildPoint` de cada uma em ordem definida pelo servidor (SPEC-guilda
  §3.1). Ordenar a lista por `point` desc (desempate `pang`) serve de "ranking" [P].
- Dentro da partida: `RoomGuildRank` (pares) e o placar das guildas (0xC0).

---------------------------------------------------------------------------------------------------------------------
## 5. Plano de implementação (C#)

Ordem sugerida; cada passo com teste. Sem LINQ em `src/` (regra 13).

**Passo 1 — Domínio da sala** (`Pangya.Domain/Rooms`)
1. `Room`: `GuildSide?[] Guilds = new GuildSide?[2]` com `record GuildSide(int Id, string Name, string Mark)`.
2. `RoomManager.CanJoin(room, password, player)`: para `GuildMatch`, sem guilda ou cargo ∉ 1..3 -> `GuildRequired`
   (13); duas guildas ocupadas e a minha não é nenhuma -> `Full` (2). **Corrigir os códigos** do `JoinResult` para
   os do cliente: `Full = 2, NotFound = 3, WrongPassword = 4, Playing = 8, GuildRequired = 13` (§1.3).
3. `RoomManager.Join`: em `GuildMatch`, lado = índice da guilda do jogador; ocupa o primeiro lado vazio se a guilda é
   nova (criador = lado 0); `p.Team = lado`. `Leave`: se o lado ficou sem ninguém, `Guilds[lado] = null`.
4. `MakeRoom` modo 6: recusar sem guilda/cargo ∉ 1..3 com 0x47 13; forçar `Course = 0x7F`, `Holes ∈ {9,18}`,
   `MaxPlayers ∈ {10,20,30}`, `GameTimeMs` 20 min (9) ou 30/35/40 min (18).
5. `SetTeam` (0x10): ignorar no modo 6. Comando `!bot`: recusar no modo 6. Expulsar: recusar no modo 6.

**Passo 2 — Pacotes da sala** (`RoomPackets.cs`, `GameHandler.Room.cs`)
1. `RoomInfo(r)`: preencher `i.GuildInfo` (`nGuildID`, `szName`, `szEmblemName` em cp949, 20/11 bytes + NUL).
2. Em entrar/sair de sala `GuildMatch`, quando o `GuildInfo` muda: mandar **0x45 sub 3 com a sala** também aos
   **membros da sala** (o `OPC` copia para `m_roomInfo`) **antes** do 0x46 (slot novo), para o `RemakeTeam` já achar
   o lado. (Hoje `Lobby(RoomPackets.RoomList(1, r))` vai só a quem está na lista de salas.)
3. `Start()` modo 6: validar (duas guildas, ≥ 1 por lado — GB exige 2 —, contagens iguais; senão 0x7D 1/2).

**Passo 3 — Jogo** (`MassGame.cs`)
1. `MassGame.For`: `GuildMatch` -> nova `GuildMatchGame : TourneyGame` (ou um componente dentro do `TourneyGame`):
   precisa de ganchos `protected virtual` em `FinishHole`, `PlayerLeft` e `Results`/`EndGame`.
2. Pares na construção: vermelhos e azuis ordenados por `Slot`, par i = (vermelho[i], azul[i]), grupo i+1.
   `MassPlayer`: `Side`, `Opponent`, `HolePoints[18]`, `HoleDone[18]`, `GuildPoints`.
3. `IMassOutput`: `GuildPairs(List<(byte Group, MassPlayer Red, MassPlayer Blue)>)` (0xBD) e
   `GuildScore(MassPlayer p, short red, short blue)` (0xC0).
4. Em `Start()` (`GameHandler.Room.cs`): 0x74 -> **0xBD a todos** -> 0x50. (O jogo é criado antes do 0x74; dá para
   pedir os pares ao `GuildMatchGame` ali.)
5. `FinishHole(p)`: marcar `HoleDone[i]`; se o adversário já fechou o buraco i: 2/0 ou 1/1 pelas tacadas (`Strokes[i]`);
   se o adversário saiu: 2 para p. Somar, atualizar placar das guildas e mandar **0x6B primeiro, depois 0xC0** (guid =
   p, `pontos(p)`, `pontos(adversário)`), broadcast na sala.
6. `PlayerLeft(p)`: buracos que o adversário já fechou e p não -> 2 para o adversário; 0xC0 com guid = adversário.
   Se uma guilda ficou sem jogadores ativos -> fim (GB `AllTeamQuit`). [P] Não encerrar à força o jogo do adversário
   (GB faz): ele segue e ganha 2 em cada buraco que fechar. Forçar fim exigiria 0x6A(guid,2)+0x63 para quem está no
   campo — testar antes.
7. Bots: não existem no modo 6 (ver passo 1.5).

**Passo 4 — Fim** (`MassOutput.GameOver`, `GameHandler.SendMassResult`)
1. `Results()` do modo 6: vencedor (§3.4), `pang_win` por jogador e por guilda; `TourneyResult` ganha campos
   `GuildWinner (0/1/2)`, `GuildPang[2]` e por jogador `(PangWin, Points)`.
2. `SendMassResult(..., guild: true)`: trocar o `U8(2)` fixo pelo vencedor e os `Zeros(16)` por
   `U32(pangWin).U32(points).U32(pangRed).U32(pangBlue)`; `tidMatch = 0`, troféu 0, medalhas vazias.
3. Garantir que o **último 0xC0 sai antes do 0x77** (o `FrUniteResultDlg` soma `guildPoint` ao abrir) e que o 0x77 vai a
   todos quando o jogo termina (já é o que o `EndGame` faz).
4. Não somar `pang_win` ao pang do jogador [R].

**Passo 5 — Banco** (`Pangya.Data`, nova migração `0xx_guild_match.sql`)
1. `guild_members`: `+ point int not null default 0, + pang int not null default 0` (contribuição).
2. `guilds`: já tem `pang`/`point`; `+ wins, losses, draws int not null default 0`.
3. `guild_matches (id, at, guild_red, guild_blue, point_red, point_blue, pang_red, pang_blue, winner smallint)`.
4. Gravar numa transação no fim: membros (`point += pontos`, `pang += pang_win`), guildas (`point += soma`,
   `pang += soma dos pang_win`, vitória/derrota/empate), linha em `guild_matches`. Quem saiu conta (GB grava todos
   da dupla).
5. `guild_trophies (guild_id, type_id, at)` ou por jogador (`account_id`) para o 0x153 — só via GM [P].

**Passo 6 — Perfil e guilda**
1. 0x14F: último `u32` = `guild_members.point` do alvo (hoje 0).
2. Opcional: 0x155 `u32 uid, GUILD_INFO` na resposta do 0x2F, para o perfil mostrar pang/pontos da guilda.
3. 0x153 com os troféus de guilda gravados (temporada 5; 0 = lista vazia).
4. Lista de guildas (0x1BA/0x1BB) ordenada por `point` desc, `pang` desc [P].

**Testes**
- Unidade: pontuação por buraco (vitória/empate/saída/tempo), vencedor e `pang_win`, formação de pares, `CanJoin`
  (13/2), lados da sala ao entrar/sair, layout de 0xBD/0xC0/0x77 tipo 6 (tamanhos: 0xBD = 1 + 9n, 0xC0 = 10 bytes de
  corpo, 0x77 tipo 6 = 4+4+1+1+16+96).
- Manual (cliente): duas contas de guildas diferentes (2×2 se exigir 2 por lado), conferir cabeçalho "VS", "N 조",
  mensagens de buraco no chat, HUD de placar, lista de pares pós-jogo e a tela de resultado (pontos/pang por time e
  imagem de vitória).

### Riscos e dúvidas
1. **Segunda partida na mesma sala** [C, alto]: `m_guildMatchupList` (e também `m_rivalList`) só são limpos no 0x47 /
   0xBA / ClearAll. Uma 2ª partida sem sair da sala **acumula** pares antigos; `GetRival` acha o par antigo primeiro
   e o 0xC0 pode gravar o ponto do adversário no rival errado. Testar; mitigação [P]: reenviar 0x47+0x48+0x46 aos
   membros ao voltar para a sala após um GuildMatch (o 0x47 limpa as duas listas) ou só permitir a partida seguinte
   depois de reentrar.
2. O 0xBD **tem de** chegar antes do 0x50 (no campo é ignorado). Se o `ChangeTask` for adiado e o 0xBD vier logo após o
   0x50 no mesmo lote, o comportamento é incerto → mandar antes.
3. Os 4 × u32 do 0x77 e o troféu "TROPHY/guild" só apareceriam no `FrResultTeamDlg`, que o 645 não cria (sem xref ao
   construtor). Pode existir criação indireta não encontrada [C com ressalva].
4. Regras de pontos, prêmio em pang, vencedor e "fim para o adversário quando o par sai" são do GB (cliente mais novo);
   o KR original pode ter usado outros valores [R].
5. Formação dos pares no KR original desconhecida (ordem de slot, nível?) [P].
6. Limite de tempo da partida (20-40 min) não existe hoje em nenhum modo mass do C#; no GB o fim por tempo dá 2 pontos a
   quem fechou o buraco sozinho. Implementar junto com o tempo do torneio [P].
7. "Outra guilda" não tem código de erro próprio no 0x47; o cliente já bloqueia antes (`LM` 9599-9605, `DR` 318-323).
8. 0xBE/0xBF (refresh 0x1C e `sGuildMatchFlagInfo`) são lidos e guardados mas nada no 645 usa → não mandar.
9. GM (`IsIdentity(0x14)`) passa pela checagem do cliente em `DetailedRoomInfoDlg`; decidir se o servidor deixa um GM
   sem guilda entrar (não teria lado/time) [P: não].
10. `dwGuildId` de quem tem pedido pendente (cargo 9) é ≠ 0 no C# → o cliente deixa tentar; o servidor recusa com 13.
