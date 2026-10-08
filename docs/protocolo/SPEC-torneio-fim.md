# SPEC — Fim do torneio (modo em massa, GameMode 4): por que o FrUniteResultDlg não abre

Legenda: [C] confirmado na decompilação do 645 (`/root/ghidra-out`), [R] rebang, [GB] servidor de referência GB
(`/root/pangya-server/Server/GB`, ids JP = KR + 2 nesta faixa: JP 0x6D = KR 0x6B, 0x6C = 0x6A, 0x79 = 0x77,
0xCE = 0xCC, 0x65 = 0x63), [I] inferido. Abreviações: `gt` = golftask.c, `gr` = golfrule.c, `lm` = lobbymain.c,
`lt` = lobbytask.c, `sd` = shareddoc.c. `Doc` = CSharedDoc, `golfdoc` = `*(Doc+0x5040)`.

Caso relatado: torneio com 1 humano + 1 bot simulado. O bot termina **muito antes**; o humano é o último (closing).
No último buraco o cliente vai direto ao lobby, sem placar/animação; no lobby a lista "현재 게임순위" mostra os dois
com "End", mas o resultado (FrUniteResultDlg) nunca abre.

## 1. Causa (resumo)

O servidor manda, no último buraco do humano (MassGame.cs `FinishHole` → `EndGame(closing)`):

```
0x6B(humano) a todos, [0x12A], 0x63 a ele, [0x12A, 0x12B], 0xCC, 0x77 a ele, e só então 0x6A(humano, 2) a todos
```

O **0x77 chega no campo antes do 0x6A do próprio jogador**. No campo, o 0x77 faz o cliente voltar imediatamente para
o lobby e é "consumido" lá, e o 0x6A chega tarde demais para o único ponto que abre o resultado nesse caminho.

1. **Volta imediata, sem placar** [C gt 5158-5227]: o handler 0x77 do `CGolfTask` grava EXP/troféu/prêmios em
   `Doc+0x415E/0x4568/0x456C/0x456D/0x456E` e, se `golfdoc+0x794` (StatUpdated, ligado a cada tacada por
   `UpdateShotResultStat`) ≠ 0, manda **GolfRule msg 0x8F(1)**. A msg 0x8F com param 1 (gr 31039-31083, modo ≠ 3)
   chama **`ReturnLobby(false)`** na hora: corta o placar (0x2000 / DoScoreBoard) e a animação.
   Quem zera `golfdoc+0x794` é justamente o **0x6A(meu guid, 2)** no campo (gt 4879-4930: `golfdoc+0x14C = 1`,
   `golfdoc+0x794 = 0`). Com o 0x6A depois do 0x77, o 0x794 ainda está ligado → volta direta.
2. **Resultado nunca abre** [C]: no torneio (tipo 4) a msg Lobby **0x2D** (cria o FrUniteResultDlg, lm 26662-26690,
   exige `Doc[0x455C] ≠ 0`) só é postada por:
   - `CLobbyTask` **0x77 recebido no lobby** (lt 4275-4310: lê os dados e posta Lobby 0x2D sem olhar estados); ou
   - `CLobbyMain::MakeRoomUserList` (lm 24214-24280), quando nenhum slot tem `rival[GetIndex(uid)]+0x36 == 0`.
     Ela roda só no OnCreate do lobby (com `this[0x618]`) e na msg Lobby **0xE**, que o `ReturnLobby` posta uma vez
     (gr 14164-14167: Lobby 0 "GAMEROOM_EXTRES", 0x2F, 0xF, 0xE). Só no modo 6 (guild) o `ReturnLobby` posta 0x2D
     direto se `Doc+0x415E ≠ −1`; no modo 4, não.
   - O 0x6A no lobby (lt 4034-4110) só grava o estado, posta Lobby msg 7 (usado só nos modos 5/6, lm 25852) e 0x2A
     (`SortRank`, lm 26630) — **não** posta 0x2D. O 0x6B idem (0x2A).

   Com a nossa ordem: o 0x77 já foi lido no campo (`Doc+0x415E ≠ −1`, então um 0x77 no lobby seria ignorado e não
   há outro 0x77); o `MakeRoomUserList` da msg 0xE roda quando o 0x6A(humano, 2) ainda não foi aplicado (o estado
   do próprio jogador é 0 → `local_29c > 0` → não posta 0x2D) [I: corrida entre a fila de msgs do novo task e o
   pacote seguinte; o print confirma o resultado]. Depois o 0x6A chega, marca "End" (por isso o print mostra os dois
   com End, via SortRank) e mais nada dispara o 0x2D. Travado para sempre.

O comentário atual do servidor ("o 0x6A do closing só sai depois do resultado, porque é ele que abre a tela") está
errado para o 645: o 0x6A nunca abre a tela; quem abre é o 0x77 no lobby ou o MakeRoomUserList na volta [C].

## 2. Respostas às perguntas

1. **Rival data / bot** [C+I]: `GetIndex(uid)` (sd 52391) devolve o índice do mapa `Doc+0x12C4` (uid → índice) ou
   0xFF. O 0x6A no campo (gt 4879) e no lobby (lt 4034) gravam `rival[GetIndex(guid)]+0x36 = estado`. O bot
   aparece com "End" no print, ou seja, o 0x6A(bot, 2) foi aplicado e o `GetIndex` acha o bot — o bot **não** é a
   causa. O próprio jogador também tem entrada no vetor e o MakeRoomUserList percorre todos os slots (inclusive o
   dele), então o 0x6A(meu guid, 2) é obrigatório antes da volta.
2. **Doc[0x455C]** = `IsGameOver`/`SetGameOver` (gr 6606-6624). Liga: `ChangeGameMode(0x2000)` no mass
   (gr 22808-22812: `= buraco atual == último`), `ReturnLobby` (gr 14099-14102, modo ≠ 10), GolfRule 0x80 (não
   mass), `DoGameOver`. Desliga: o 0x47 (entrar na sala, lt 3710), `OnResultDlgResult` (fechar o resultado,
   lm 14764), as saídas de sala/família. No nosso fluxo **está ligado** (ReturnLobby sempre liga) [C].
3. **Slot da sala do bot**: nada nele prende o estado em 0 (ver 1) [I, pelo print].
4. **Último buraco do próprio jogador** [C]: depois do `DoHoleInPose` o cliente manda 0x1C e entra sozinho em
   `ChangeGameMode(0x2000)` (placar, BGM, `Doc[0x455C] = 1` no último buraco); `DoScoreBoard` mass (gr 27484-27552)
   espera a msg 0x138 (fim do movimento do ranking, ~4 s) e, com `0x455C` ou `golfdoc+0x14C`, chama `ReturnLobby`.
   O 0x63 é opcional (só força 0x2000 de novo, SPEC-torneio-next-hole §3). O que corta tudo é o 0x77 chegar com
   `golfdoc+0x794 ≠ 0`, i.e. antes do 0x6A(meu guid, 2).
5. **GB** [GB Tourney.cs 252-342, 627-660, 1376-1383; TourneyBase.cs 2281-2455]: `checkEndShotOfHole` →
   `changeHole` → se é o último buraco `finish_tourney(0)`: `sendFinishMessage`, **0x6D (=0x6B)** a todos,
   **0x6C (=0x6A) estado 2** a todos; marca FINISH; se `AllCompleteGameAndClear()` → `finish()` **na mesma hora**:
   medalhas, troféus, EXP e, por jogador, `requestFinishData` → **0xCE (=0xCC)**, **0x79 (=0x77)**, 0x12B (treasure).
   Buraco intermediário: só 0x6D. O GB **não manda 0x65 (=0x63)** no torneio. Ou seja, no GB o 0x6A do closing vem
   **antes** do 0xCE/0x79, sem esperar nenhum C->S. O 0x43/0xC8 (=0xC6) vêm depois do C->S 0x06 (`finish_game`).

## 3. Correção recomendada (servidor)

Ordem por fim de buraco no torneio (KR):

```
buraco intermediário: 0x6B(p) a todos; [0x12A a p]; 0x63 a p                  (sem mudança)
último buraco de p:   0x6B(p) a todos; 0x6A(p, 2) a todos; [0x12A a p]; 0x63 a p (opcional, GB não manda)
  se todos acabaram (p é o closing), logo em seguida, para cada humano que não saiu:
                      [0x12A, 0x12B]; 0xCC; 0x77                                 (GameOver)
depois do C->S 0x06 de cada um: 0x43, 0xC6                                        (sem mudança)
```

Efeito no closing (ainda no campo): 0x6A zera `golfdoc+0x794` e liga `0x14C` → o 0x77 só grava os dados, sem 0x8F →
placar normal → `ReturnLobby` → MakeRoomUserList vê todos com estado 2 → Lobby 0x2D → FrUniteResultDlg lê
`0x415E/0x456C/0x456E` já preenchidos. Nos humanos que já estão no lobby: 0x6A(closing, 2) marca End e o 0x77 no
lobby posta 0x2D (com o ranking já completo).

Mudanças (src/Pangya.Domain/Rooms/MassGame.cs):
- **linha 428**: `if (!last) Output.RivalState(p, 2);` → `Output.RivalState(p, 2);` (sempre, logo após o 0x6B e
  antes do `HoleDone`/`NextHole` da linha 430).
- **linha 177**: remover `if (closing != null) Output.RivalState(closing, 2);` (e o parâmetro `closing` de
  `EndGame`, linhas 168-169 e a chamada `EndGame(p)` da linha 431 → `EndGame()`).
- Atualizar os comentários errados: MassGame.cs 103-106 (IMassOutput.GameOver: "vem ANTES do último 0x6A") e 168,
  e o resumo da classe (linha 184). Opcional: no último buraco não mandar 0x63 (`if (!p.IsBot && !p.Finished)` para
  o `NextHole`), como o GB.
- Docs: corrigir SPEC-resultado-fim-de-jogo.md §3.1 ("0xCC/0x77 têm de chegar ANTES do 0x6A que fecha o torneio"
  está invertido) e SPEC-modes.md (linha da tabela 0x1C já diz 0x6B, 0x6A, 0x63 — correto).

Teste sugerido (sem jogo): no teste do TourneyGame com 1 humano + 1 bot, bot termina antes; conferir que a saída do
último 0x1C do humano é `RivalHole, RivalState(2), HoleDone, NextHole, GameOver` e que nenhum `RivalState` sai
depois do `GameOver`.
