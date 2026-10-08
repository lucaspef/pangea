# SPEC — "Next Hole" no fim de buraco do Torneio (modo em massa, GameMode 4)

Legenda: [C] confirmado na decompilação do 645 (`/root/ghidra-out`), [R] rebang, [GB] servidor de referência GB,
[I] inferido. Abreviações: `gr` = golfrule.c, `gt` = golftask.c, `sb` = scoreboard.c, `td` = treasuredlg.c.

## 1. O que é o "Next Hole"

- É o form **`FrNextHole`** (layout Fresh `"nexthole"`), uma janela pequena sem botão e sem timer. Mostra a
  **miniatura do próximo buraco** recortada de uma folha de sprites do campo [C]:
  - `FrNextHole::OnInit` (td 7969): `FindCourse(0x28000000 | GetCurMap())`. Se falhar, **`OnInit` devolve false e
    `CreateForm` devolve NULL**: o form não existe na partida inteira. Se der certo, carrega `"<nome do campo>_1.png"`
    (+0x110), `"<nome>_2.png"` (+0x114) e `"next_chaos.tga"` (+0x118).
  - `SetNextHole(n)` (td 3512): células de 140×93. Buracos 1..10 ficam no `_1.png` (célula n-1); 11..18 no `_2.png`
    (célula n-11); a célula 8 do `_2.png` é usada no último buraco (`n == 19` ou buraco atual == `holeOrder[holes-1]`).
    Só calcula a célula se `_1` e `_2` existirem.
  - `OnAreaNextHole_OwnerDraw` (td 3551): desenha só com estado != 1. Se `doc[0x4A49]` (realGameType do sRoomInfo
    +0xB1) == 0x0E (Special Shuffle/Chaos), usa `next_chaos.tga` inteiro.
  - `GetNextHole()` (demonlanddlg.c 1782): procura `golfdoc+0x168` (buraco atual) em `doc[0x45F2..]` (ordem dos
    buracos vinda do 0x50) e devolve o seguinte; se não achar, devolve 19.
  - Posição (`GetCalcPoint`, td 1494): x = (largura da tela - largura do form)/2 + 210, y = altura da tela - altura - 38
    (embaixo, à direita do centro).
- Não há string "다음 홀" nem sprite `next_hole.tga`. Os outros "next" do cliente são da sala (`hole_next`, `mapnext`)
  ou do Demon Land (`FrDemonNextHole`) [C].

## 2. Quem cria e quem mostra (cliente)

- `CScore::OnLoad` (sb 20723): em todos os modos, exceto 6 (guild) e 7 (skins), cria `score`, `tPointdlg` e
  **`nexthole`** (CScore+0x68) e põe o estado 1 (escondido). O modo 4 (torneio) **cria** o form [C] (sb 20862-20896).
- Estado 2 (visível) [C]:
  - `CScore::HandleMsg` msg **0x12F com param 1** (sb 21392-21453): no ramo stroke (sb 21417) e **no ramo mass**
    (`IsMassGame`, sb 21452) faz `FrNextHole::SetState(2)`. Na mudança 1→2 o form aparece, é reposicionado e chama
    `SetNextHole(GetNextHole())`. Se o estado já for 2, `SetState(2)` não faz nada.
  - TAB durante o jogo (`CScore::OnProcess`, sb 21100-21132) também alterna entre 2 e 1.
- Estado 1 (escondido) [C]: msg 0x12F com param 0 (sb 21369). Ela sai em `ChangeGameMode` ao entrar no turno de
  tacada (gr 22588), no modo 0x10 (gr 23285), em `SetTurnChangeEvent` (gr 16021), em `DoHoleInPose`/`DoDespairPose`
  quando não pode ir ao próximo buraco (gr 27440/27746) e em `OnInit` (gr 8043). Também em
  `CScore::CheckOpenedDlg` (sb 18441). A msg 0x130 (0x51/PM_NET_GAMESYNC_START) **não** esconde o form.
- **0x12F(1) sai uma única vez: na entrada de `ChangeGameMode(0x2000)`** (placar entre buracos, gr 22804).
- Portanto, no torneio o "Next Hole" fica visível de 0x2000 até a primeira tacada do buraco seguinte. Isso inclui
  `DoScoreBoard` (≥ 4 s esperando `FrRankFormEx::IsMotionFinished`, sb 3842: 3 s + 1 s, no máximo 10 s), o 0x1A, o
  recarregamento do mapa, o 0x11, o 0x51 e a intro.

## 3. Como o cliente chega em 0x2000 no torneio

- [C] `DoHoleInPose` (gr 27600): depois da pose, com `OnlinePlay` e replayMode (`doc+0x5B9C`) == 1, envia **C->S 0x1C**
  sem `SetNetMsgWait` no mass. Em seguida chama `CanMoveToNextHole` (gr 21033). No mass ela devolve
  `estado do meu player == 1`, e esse estado é posto pelo próprio cliente em `RecordPlayerData` (gr 11438). Com isso
  o cliente faz **`ChangeGameMode(0x2000)` sozinho** (gr 27731-27749). Se não puder, manda 0x12F(0), fecha o form e
  vai para 0x800.
- [C] O S->C **0x63** também leva a 0x2000: `gt` case 99 → msg 0x7E `PM_NET_NEXT_HOLE` (gr 30441). Ela zera
  `this[0x3CB]` (a espera de rede, inclusive a de "DoScoreBoard") e, fora dos modos 7/10, chama `ChangeGameMode(0x2000)`.
- [C] `DoScoreBoard` mass (gr 27484): espera a msg 0x138 (fim do movimento do RankFormEx). Se for o último buraco
  (`doc[0x455C]`) ou `golfdoc+0x14C` estiver ligado, chama `ReturnLobby`. Senão avança `golfdoc+0x168`, liga
  `SetNetMsgWait(0x1079,"DoScoreBoard",40)`, envia 0x1A, recarrega o mapa (GolfBg 0x140) e manda 0x12F(2).
- [C] 0x6B (gt 4963): só atualiza o rival e manda Score 0x2A/0x136/0x137 e Spot 0xF9. Não mexe no FrNextHole. A
  msg 0x9F (→0x2000) só existe no modo 6.

## 4. O que o GB manda nesse momento [GB]

`TourneyBase`/`Tourney.cs` (ids JP = KR + 2 nesta faixa: JP 0x6D = KR 0x6B, 0x6C = 0x6A, 0x6E = 0x6C, 0x53 = 0x51,
0x5B = 0x59, 0x65 = 0x63):
- No 0x1C com o buraco terminado (`checkEndShotOfHole` → `finishHole` + `changeHole`): **só JP 0x6D**
  (`u32 oid, u8 buraco, u8 tacadas, i32 placar, u64 pang, u64 bônus, u8 1`) para todos. No último buraco, antes
  disso, manda JP 0x199 vazio ao jogador e depois `finish_tourney`.
- **O GB não manda 0x65 (KR 0x63) no torneio.** Só Versus/PangBattle/Approach usam o 0x65. O cliente entra em 0x2000
  sozinho (§3).
- Buraco seguinte: 0x1A → 0x9E clima, 0x5B vento, 0x8D tempo; 0x11 → 0x53 (KR 0x51). A ordem é a mesma do nosso
  servidor; o nosso só manda o vento no 0x11.

## 5. O que o nosso servidor manda hoje

- `src/Pangya.Domain/Rooms/MassGame.cs:411-430` (`TourneyGame.FinishHole`): 0x6B a todos (:417), 0x6A(2) se terminou
  o jogo e não é o último (:426) e **`Output.NextHole(p)` = S->C 0x63 vazio ao humano (:428)**, em todo buraco.
- `src/Pangya.Protocol.KR645/Game/MassOutput.cs:54-58`: `NextHole` → 0x63 sem payload. `:49-50`: 0x6B no mesmo
  layout do GB.
- `src/Pangya.Protocol.KR645/Game/RoomPackets.cs:183-191`: o 0x50 leva `CoursePlayed` (sem o bit 0x80; o
  `GetCurMap` aceita os dois) e a ordem dos buracos que vai para `doc[0x45F2]`.

Diferença de protocolo neste ponto: **o 0x63 extra**. O cliente já está em 0x2000 (entrou sozinho no mesmo frame em
que mandou o 0x1C), e o 0x63 provoca um **segundo `ChangeGameMode(0x2000)`**. Esse segundo passo repete o 0x12F(1),
o `ResetRank`, o BGM e o `DispatchMatchItems`, reinicia o movimento do RankFormEx e zera `this[0x3CB]`. Pelo código,
o segundo 0x12F(1) **não esconde** o FrNextHole: `SetState(2)` com o estado já em 2 não faz nada [C]. O 0x63 só causa
estrago se chegar depois que o `DoScoreBoard` já mandou o 0x1A. Nesse caso ele zera a espera "DoScoreBoard", e o
`DoScoreBoard` roda de novo, avança `golfdoc+0x168` mais uma vez e pula um buraco [C fluxo / I tempo].

## 6. Conclusão

- [C] O cliente **não precisa de nenhum pacote, campo ou flag do servidor** para mostrar o Next Hole no torneio. Basta
  entrar em 0x2000, e ele entra sozinho. Não há `IsLocalContent` no caminho do FrNextHole. As únicas dependências
  "de dados" são:
  1. `FindCourse(0x28000000|GetCurMap())` no `CScore::OnLoad`, ou seja, o mapa do 0x50 precisa existir no Course.iff
     do cliente. Senão o form nem é criado.
  2. Os arquivos `<nome do campo>_1.png` e `_2.png` precisam existir no pak. Sem os dois, nada é desenhado.
  3. A ordem dos buracos (`doc[0x45F2]`, do 0x50) precisa conter o buraco atual.
  4. `realGameType` (sRoomInfo +0xB1) não pode ser 0x0E (Chaos).
- [I] Pelo código, o 0x63 extra não deveria apagar o sprite, então a causa provavelmente está fora do fluxo de
  pacotes do torneio. É preciso confirmar:
  (a) se o Next Hole aparece no **Stroke** com o nosso servidor no mesmo campo. Se também não aparecer, a causa é o
  item 1/2 acima (campo ou arquivo de miniatura), não o modo mass;
  (b) se acontece em todos os campos ou só em alguns: o sorteio de campo aleatório (`RoomManager.cs:124`) pode
  escolher um campo sem `_1/_2.png` no pak do 645.

## 7. Correção mínima recomendada

1. [GB, baixo risco] Em `TourneyGame.FinishHole` (`MassGame.cs:428`), **parar de mandar o 0x63** no torneio (modos
   4/5/9/14 e 6), como o GB. O cliente vai sozinho para 0x2000 e, no último buraco, o `DoScoreBoard` já vê
   `doc[0x455C]` e chama `ReturnLobby`. O 0x6A(2) e o 0x6B continuam. O `ApproachGame` (:652) mantém o 0x63, porque o
   modo 10 depende dele para abrir o `approach_result`. Isso elimina a reentrada em 0x2000 e o risco de pular buraco.
   É a única diferença de protocolo encontrada neste ponto.
   - Cuidado: o caminho de intrusão/tempo (SPEC-intrusao-tempo §R4: 0x6A(próprio,2) + 0x63 para quem não tacou) usa
     o 0x63 fora do `FinishHole` e deve continuar como está.
2. Se o sprite continuar sumindo depois disso, verificar o item §6(a)/(b) e restringir a lista de campos sorteáveis
   aos que existem no Course.iff do 645 e têm `<nome>_1.png`/`_2.png`.
