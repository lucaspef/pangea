# KR 645: troca direta entre jogadores (1:1 거래 / private trade)

Pesquisa somente leitura no cliente KR 645 QA. Legenda: **[C]** confirmado no cliente (decompile/asm/exe), **[R]** fonte de
referência (rebang `privatetrade.cpp`/`formbar.cpp`, emulador Python), **[P]** palpite/recomendação.

Fontes: `gh/` = `/root/ghidra-out` (`privatetradedlg.c`, `avatar_mainui.c`, `avatar_task.c`); `asm` = `/root/pg645.asm`
(o `CPrivateTrade` não tem decompile: lido no asm @0x516d00..0x517bc0); `src/` = `/root/rebang/source/client/ProjectG`
(`privatetrade.cpp` bate 1:1 com o asm do 645, ver §2); exe = `ProjectG_ReleaseQA.exe` (sha1 b08e4e53…, strings cp949);
UI = `data/ui/trade.xml` de `projectg642.pak`.

## 0. Resposta curta

- **No cliente KR 645 sem patch a troca direta está DESLIGADA.** Tudo depende de `IsLocalContent(0x77)` e o bit 0x77 **não** é
  ligado em `InitLocalizeSystem` (@0x7abee0; lista KR completa: 1-0xE, 0x10-0x14, …, 0x74, 0x75, **0x76**, 0x7D, 0x7F, …, 0xA3 —
  sem 0x77). [C] Consequências:
  - o item "1:1 거래" não aparece no menu de contexto do avatar (`ntAvatarChatMain::UpdateRightMenu` usa a lista sem trade);
  - o ator `CPrivateTrade` ("PrivateTrade") **não é criado** na tarefa do lounge (`ntAvatarChatTask::Register`, gh/avatar_task.c:642);
  - S->C 0x187 é **ignorado em silêncio** (o case @0x732724 faz `GetActor("PrivateTrade")` e pula se for nulo); nada manda 0xDB.
- Todo o resto existe e está completo no binário: ator, janela `FrPrivateTrade` (+ `FrPrivateTradeWnd` ×2, `FrMyWareHouse`,
  `FrTradeToolTip`, `FrNumEdit`, `FrPangEdit`), strings e o layout `trade.xml` em projectg623/625/642.pak. [C]
- Para ligar no cliente (decisão do usuário, não testado [P]): trocar `b9 77 00 00 00` (`mov ecx,0x77`) por `b9 76 00 00 00`
  (0x76 está ligado no KR) nos 5 lugares — offsets de arquivo **0x144194, 0x144236, 0x1442CC, 0x144B82, 0x14524A**
  (VA 0x544194/0x544236/0x5442CC em `UpdateRightMenu`, 0x544B82 em `OnRightMenuLBtnDown`, 0x54524A em `ntAvatarChatTask::Register`).
  Nenhum exe de `E:\dev\pangya-test\KR642` tem esse patch hoje.
- Só existe no **lounge de avatares** (`ntAvatarChatTask`, sala tipo lounge/대화방), iniciado pelo menu de contexto (clique direito)
  sobre outro avatar. Não há entrada pela lista do lobby nem na sala de jogo. [C]
- Os dois lados precisam estar na **mesma sala-lounge** (o cliente acha o parceiro com `ntAvatarManager::FindAvatarByGuid`). [C]
- **O cliente não mexe no inventário ao concluir** e **não tem mensagem/estado de "concluído"**: o servidor tem que mandar as
  atualizações (0xA5/0x71/0xC6) e fechar a janela ele mesmo (só dá com 0x187 sub 2 ou 9, que imprimem texto de cancelamento). [C]
- Servidores de referência: o GB e o S6 **não implementam** (no GB, C->S 0xDB = `CLIENT_VALENTIME_GIFT_REQ` e S->C 0x187 =
  `SERVER_TICKET_RESULT`, outra versão). O emulador Python responde 0x187 sub 9 a tudo. [R]

## 1. Onde o jogador inicia, pré-condições do cliente [C]

`ntAvatarChatMain::UpdateRightMenu` (@0x544140) monta o menu do avatar clicado (nick em `this+0x226`):
- clicou em si mesmo → menu próprio (loja etc.), sem troca;
- se **meu** avatar tem `[0xE8] != 0` → lista sem troca; se o **alvo** tem `[0xE8] == 1` → só "seguir/sussurrar" [? flag não
  identificada; P: avatar especial/NPC];
- senão, com 0x77 ligado → lista `"1:1 거래|…|store|separator|…"`.
- **Distância**: se `|pos_meu - pos_alvo| > 64.0` (0x42800000), o item "1:1 거래" fica desabilitado.
- Se **meu** estado de avatar tem o bit **0x80** (= loja pessoal aberta, `PersonalShopRules.StateShopOpen`), desabilita "1:1 거래"
  (e outros). A loja aberta do **alvo** não bloqueia a troca no cliente.
- Não há checagem de nível (diferente da loja pessoal, que usa doc[0x539]).

Clique em "1:1 거래" (`OnRightMenuLBtnDown` @0x544b50) → `SendMsg("PrivateTrade", 0x247, 0, avatar.guid(+0xE4), avatar.nick(+0x1A))`.
O `guid` do avatar é o mesmo `dwGuid` usado nas listas da sala (= uid da conta no nosso servidor, `RoomPlayer.Guid`). [C/P]

`CPrivateTrade::HandleMsg(0x247/0)`: ignora se `guid == meu dwGuid` ou se o estado ≠ NONE; senão estado REQUEST, manda
**C->S 0xDB sub 0** e abre a barra de espera ("%s님에게 거래를 요청중 입니다.", **12 s**, sem botões).

## 2. Pacotes

Todos C->S 0xDB vão para o game server (`Send(0)`). Verificados os 8 pontos de envio no asm (0x51724d … 0x517887). [C]

### 2.1 C->S 0xDB

```
u8   sub
u32  guid      do parceiro (ver tabela)
str  nick      do parceiro
[0x368 bytes]  sub 5 e 6: minha oferta
[0x368 bytes ×2] sub 7: minha oferta + a oferta do parceiro como eu a vejo
```

| sub | quem/quando | guid/nick |
|---|---|---|
| 0 | pedido (menu) | alvo |
| 1 | recusa: botão NÃO na barra de pedido **ou timeout de 10 s** (barra com botões fecha com FrNO → sub 1) [C+R formbar] | quem pediu |
| 2 | cancelar: botão Cancel da janela (`FrPrivateTrade::OnDownCancelBtn` → Close(FrCANCEL) → `OnResultTrade`) | parceiro |
| 3 | ocupado: o cliente responde **sozinho** a um 0x187 sub 0 quando já está em troca | quem pediu |
| 4 | aceitar: botão OK na barra de pedido; o aceitante **abre a janela na hora**, sem esperar o servidor | quem pediu |
| 5 | atualizar oferta (soltou/tirou item, mudou quantidade, mudou pang) — `OnSendItemUpdate` | parceiro |
| 6 | botão **Confirm** (확인/"pronto") | parceiro |
| 7 | botão **Permit** (승인/"OK final") | parceiro |

O tamanho do 0xDB sub 5/6 = 1+4+2+len(nick)+0x368; sub 7 = …+0x6D0.

### 2.2 S->C 0x187 → `CPrivateTrade::OnPacket` (@0x517920, jump table 0x517bc0, `u8 sub` 0..9) [C]

| sub | layout depois do u8 | efeito no cliente |
|---|---|---|
| 0 | u32 guid, str nick (de quem pede) | se estado ≠ NONE → responde 0xDB sub 3 (guid, nick) e para; senão guarda guid/nick, estado REQUEST e abre barra "%s님에게 거래 요청이 왔습니다." com OK/NÃO e **10 s** |
| 1 | — | chat "상대방이 거래를 거절 하였습니다.", fecha janelas, reset |
| 2 | — | se tem parceiro: chat "거래가 취소 되었습니다."; fecha janelas (Close(FrNONE), não reenvia nada), reset |
| 3 | — | chat "상대방은 현재 다른 사람과 거래중입니다.", fecha, reset |
| 4 | — | `OnOpenTradeWnd`: fecha a barra; se `FindAvatarByGuid(parceiro)` acha → estado OPEN, abre `FrPrivateTrade` com nick, guid e `tid` do personagem do parceiro; se não → chat "거래 상대가 대화방에 없습니다. 거래가 취소 되었습니다." |
| 5 | u32 (ignorado), str (ignorado), 0x368 | `FrPrivateTrade::UpdateFrTradeItem`: copia para o painel do **parceiro** e **destrava os dois lados** (zera "pronto" dos dois, reabilita Confirm) |
| 6 | u32, str, 0x368 | `FinalFrWait`: se o buffer difere do painel do parceiro, copia e destrava os dois; marca o parceiro como **pronto** (moldura amarela 0x40FFFF00) |
| 7 | u32, str, 0x368 | `FinalFrOK`: se difere, copia (sem destravar); marca o parceiro como **OK final** (moldura verde 0x4000FF00) |
| 8 | — | nada |
| 9 | u8 (ignorado) | se tem parceiro: chat "현재 거래를 더이상 진행 할 수 없습니다."; fecha, reset |

Os subs 5/6/7 só agem se a janela "FrPrivateTrade" existir. Os ids/strings conferem com `src/privatetrade.cpp`. Não existe sub de
"concluído", e nenhum código da janela fecha com FrOK (o caminho `OnResultTrade(FrOK)` → estado 6 é inalcançável). [C]

### 2.3 Estrutura `_sPrivateTradeItem` (0x368 bytes) [C]

```
+0x000  i64  pang oferecido          (FrPrivateTradeWnd+0x128/+0x12C; OnResultEditPang grava os 2 dwords)
+0x008  4 × _sPrivateTradeItemSingle (0xD8 cada, slot vazio = tudo zero)
```

`_sPrivateTradeItemSingle` (0xD8), preenchido por `Formator` (@0x670a10) a partir do `sTradeDropItem {u32 guid, u32 tid, u16 qtd}`:
```
+0x00  u32  guid do item (id no inventário)
+0x04  u32  typeid
+0x08  u16  quantidade
+0x0A  u16  (0)
+0x0C  8 × 0x18  cartas presas à peça (só grupo 2; campos copiados de GetCardsAttachedToPart: +0 c[1], +4 c[0], +8 c[4],
                 +0xC c[5], +0x10 c[6], +0x14 c[15]) — só para tooltip
+0xCC  u16[5] níveis do club set (só grupo 4; Common[5] do sItemInfo) — só para tooltip
+0xD6  u16  (0)
```
Máximo **4 slots** por lado. A quantidade só é mostrada/editável para grupo 6 (item) e 0x1F (carta).

## 3. Máquina de estados

### 3.1 Cliente (`CPrivateTradeImpl::m_state`) [C]
NONE(0) → REQUEST(2) ao pedir ou ao receber pedido → OPEN(3) ao abrir a janela (sub 4 ou aceite local) → 4 após Confirm → 5 após
Permit → (6 nunca acontece). Qualquer 1/2/3/9 (ou o timeout de 12 s de quem pediu, que fecha a barra com FrCANCEL sem mandar
nada) volta a NONE.

Janela, por lado (`FrPrivateTradeWnd+0x175`): 0 editando, 1 pronto, 2 OK final; `+0x174` = 1 só no **meu** painel enquanto editável.
Botões (`+0x144` Confirm, `+0x148` Permit, `+0x14C` Cancel):
- inicial / após qualquer destrava: Confirm ON, Permit OFF, Cancel ON;
- Confirm → Confirm OFF, **Permit ON** (não espera o parceiro estar pronto!), meu lado = 1, envia sub 6;
- Permit → Confirm OFF, Permit OFF, meu lado = 2 e **trava minha edição** (`+0x174 = 0`), envia sub 7 (minha + a dele).
- Qualquer edição minha (`OnSendItemUpdate`) destrava os dois lados localmente e envia sub 5.

### 3.2 Fluxo completo
```
A: menu "1:1 거래"         -> 0xDB 0 (B)                     A: barra 12 s
S: valida                  -> B: 0x187 0 (A.uid, A.nick)
B: ocupado (auto)          -> 0xDB 3 (A)  -> S -> A: 0x187 3
B: NÃO / 10 s              -> 0xDB 1 (A)  -> S -> A: 0x187 1
B: OK                      -> 0xDB 4 (A)  (B já abre a janela) -> S -> A: 0x187 4 (A abre a janela)
X edita                    -> 0xDB 5 (Y, ofertaX)     -> S -> Y: 0x187 5 (ofertaX)     [zera prontos]
X Confirm                  -> 0xDB 6 (Y, ofertaX)     -> S -> Y: 0x187 6 (ofertaX)
X Permit                   -> 0xDB 7 (Y, ofertaX, ofertaY-vista) -> S -> Y: 0x187 7 (ofertaX)
S: os dois deram 7 sobre as mesmas versões -> executa -> inventário (0xA5/0x71/0xC6) aos dois -> 0x187 2 aos dois (+ aviso)
X Cancel / sai / cai        -> S -> Y: 0x187 2
```

## 4. Inventário ao concluir [C]

O ator e a janela **não alteram** o Doc (inventário/pang) em nenhum caminho; a janela só mostra uma prévia (o `FrMyWareHouse`
mostra o pang que sobraria e esconde os itens já ofertados, mas isso é só visual). O servidor deve mandar, para cada jogador, o mesmo
que já usa em caixas/My Room (ver SPEC-caixas-reciclagem §6):
- itens que saíram: **0xA5** `u8 n, n × {u32 tid, u32 guid, u16 novoTotal}` (0 apaga peça/clubset/item; count é absoluto);
- itens que entraram: **0x71** `u16 n, u16 n, n × sItemInfo(0xA8)` para guid novo; pilha já existente (grupo 6) → **0xA5** com o novo total;
- pang: **0xC6** `u64 pang, u64 0`.
Depois disso fechar com **0x187 sub 2** (único jeito limpo de fechar; imprime "거래가 취소 되었습니다.") — mandar em seguida um aviso de
chat "거래가 완료되었습니다." (ex.: 0x3E kind 7) para não confundir. [P] Não usar 0x187 sub 4 para fechar (reabre a janela).

## 5. Regras do servidor e anti-trapaça

O cliente só valida para a UI; tudo abaixo precisa ser imposto no servidor.

Do cliente (para espelhar) [C]:
- Lista "armazém": peças (exclui equipadas em qualquer personagem: `IsEquipParts` olha guids das 24 partes e tids das 5 aux),
  club sets (exclui o equipado, doc+0x630), itens grupo 6, cartas; todos exigem `IFF flags & 0x1E != 0` (IsSalable ≠ 0).
- Quantidade (`OnResultEditNum`) ≤ quantidade possuída (`GetMyItemCount`).
- Pang: aceita `p` só se `p + p*5/100 ≤ meu pang` (FrPangEdit taxa = 5 em `+0x120`, `OpenPangEditWnd`); o armazém mostra
  `pang - p - 5%` → o cliente pressupõe **taxa de 5% paga por quem oferece o pang**.
- Arrastar a mesma peça duas vezes faz `qtd = 2` numa peça (bug do `DropItemDown`) — o servidor tem que recusar.

Para impor [P, reaproveitando o que já existe]:
1. **Pareamento**: os dois na mesma sala do tipo lounge, ambos sem troca ativa, sem loja pessoal aberta (estado 0x80), não em jogo/
   carregando, alvo ≠ si mesmo. Ignorar o guid/nick que o cliente manda nos subs 1..7: usar o par guardado na sessão (só conferir).
   Opcional: distância ≤ 64 se o servidor tiver a posição do avatar.
2. **Pedido**: guardar `(A, B, t0)`; expirar em **12 s** (barra de A). Sub 4 depois disso → mandar 0x187 2 só para B. Sub 1/3/4
   só valem vindos de B para o pedido pendente de A.
3. **Validação da oferta (sub 5)**: até 4 slots, sem guid repetido, cada item do dono e em inventário (`Player.Find`), tid confere,
   grupo ∈ {2 peça, 4 club set, 6 item} (cartas 0x1F e peças com carta: recusar na v1), `IGameData.CanTrade` (IsSalable 1/3, mais
   estrito que o cliente — ok), não equipado (`PlayerActions.IsEquipped`), não é a bola básica nem item de slot equipado, não está
   à venda na loja pessoal, não-empilhável com qtd == 1, empilhável 1..possuído, pang 0..(pang − 5%). Inválido → **0x187 9 aos dois**
   e encerra (o cliente do autor já mostra a oferta inválida no próprio painel, não há como "corrigir" só um lado).
4. **Versões**: cada sub 5 aceito incrementa `versão[X]` e **zera pronto/final dos dois** (o cliente faz o mesmo localmente).
   Repassar ao parceiro um buffer **montado pelo servidor** (guid/tid/qtd/pang validados; cartas/PCL a partir dos dados do servidor ou
   zerados) e comparar 6/7 **semanticamente** (pang + lista de (guid, tid, qtd)), não byte a byte.
5. **Sub 6**: só se o buffer == oferta guardada de X → `pronto[X]`; senão tratar como sub 5 (ou abortar 9).
6. **Sub 7**: exigir `pronto[X]`, minha parte == oferta[X] e a parte do parceiro == oferta[Y] na versão atual → `final[X] = (verA, verB)`.
   Concluir só quando `final[A] == final[B] == (verA, verB)` atuais. Isso fecha o golpe de trocar itens depois do "pronto": qualquer
   sub 5 muda a versão e anula os finais.
7. **Execução atômica**: revalidar tudo contra o inventário atual (item ainda existe, não equipou nesse meio-tempo, quantidades, pang
   + taxa) dentro do lock da sala; montar `PlayerChanges` dos dois lados (generalizar `PersonalShopRules.Transfer` para N itens + pang
   nos dois sentidos; empilháveis somam na pilha do recebedor, o resto vira item novo com id reservado, mantendo Attrs/ExpiresAt) e
   gravar com **uma** chamada `IPlayerStore.ApplyTradeAsync(aId, changesA, bId, changesB)`; só depois `Commit` em memória e pacotes.
   Falha de gravação → 0x187 9 aos dois, nada muda.
8. **Encerramento forçado**: saída da sala, desconexão, abrir loja, entrar em jogo, mudar equipamento de um item ofertado → 0x187 2 ao
   outro. Ociosidade (ex.: 3-5 min sem pacote) → 0x187 2 aos dois.
9. Limites: 1 troca por jogador; ignorar 0xDB fora do lounge; taxa de 5% sobre o pang oferecido (cobrar do ofertante e não creditar)
   — ou 0% se preferir, desde que a checagem `p*1.05 ≤ pang` continue (o cliente nunca deixa oferecer mais que isso).
10. Log de cada troca concluída (contas, itens, pang).

## 6. Comparação com as referências

| fonte | o que tem |
|---|---|
| rebang `privatetrade.cpp`/`.h`, `privatetradedlg.h` | ator completo (igual ao asm do 645); `_sPrivateTradeItem` marcado "incomplete" (só tamanho) — o layout acima veio do `privatetradedlg.c`. `formbar.cpp`: timeout com botões → FrNO, sem botões → FrCANCEL. [R] |
| rebang `shared/localize_kor.h` | também não liga 0x77 (KR nunca liberou). [R] |
| GB `/root/pangya-server/Server/GB` | nada; 0xDB = presente de Valentine (C->S) e 0x187 = resultado de ticket (S->C) nessa versão. Só a loja pessoal 0x74..0x7D. [R] |
| S6 `ref/Pangya-Server-Source-master/S6` | nada. [R] |
| emulador `ext_trade.py h_private_trade` / `coverage-notes/notes_trade.md` | sub 0/5/6/7 → 0x187 `u8 9, u8 0`; 1..4 só log. Correto quanto ao layout, mas no 645 sem patch o 0x187 é descartado. [R] |

## 7. Lista priorizada de implementação

1. [C] Decidir/registrar que a feature só funciona com o cliente patchado (0x77→0x76 nos 5 offsets). Sem patch, o servidor nunca
   recebe 0xDB; um handler que só descarta/loga já é suficiente para o 645 vanilla.
2. [C] Handler C->S 0xDB: `u8 sub, u32 guid, str nick` + 0x368 (5/6) ou 2×0x368 (7); tolerar pacote curto (descartar, não derrubar).
3. [C] Relé do pedido: sub 0 → 0x187 `0, u32 uid, str nick` ao alvo; sub 1/3/4 → 0x187 `1/3/4` ao pedinte; sub 2 → 0x187 2 ao outro.
4. [P] Estado de troca no domínio (`Pangya.Domain/Rooms/PrivateTrade.cs`): par, ofertas, versões, prontos/finais, t0; sem LINQ.
5. [C/P] Validação da oferta (sub 5) reaproveitando `CanTrade`, `IsEquipped`, `Player.Find`, bola básica, itens da loja pessoal;
   0x187 5 ao parceiro com buffer montado pelo servidor; zera prontos.
6. [C/P] Sub 6/7 com comparação semântica e versões; 0x187 6/7 ao parceiro.
7. [P] Execução atômica: generalizar `PersonalShopRules.Transfer`/`Commit` para troca bilateral; `ApplyTradeAsync` única.
8. [C] Pacotes de inventário pós-troca: 0xA5 (saídas e pilhas somadas), 0x71 (itens novos), 0xC6 (pang), depois 0x187 2 (+aviso de chat [P]).
9. [P] Encerramentos: sair/cair/abrir loja/entrar em jogo/timeout do pedido (12 s) e da troca.
10. [P] Taxa de 5% sobre o pang (ou 0%), log, testes (dois clientes roteirizados no lounge: pedido, recusa, ocupado, cancelar,
    troca de peça+item+pang, troca após "pronto" deve anular, falha de validação → 9).
11. [P] Depois: cartas (0x1F) e peças com cartas, se o servidor passar a modelar cartas presas.
