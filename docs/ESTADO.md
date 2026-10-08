# Estado do servidor C# (atualizado em 2026-10-07)

Mapa rápido do que funciona, do que falta e de como testar. Detalhes de protocolo em `docs/protocolo/SPEC-*.md`;
plano e regras em `docs/PLANO.md`. Pacotes que o cliente manda e o servidor ainda não trata: `python3 tools/coverage.py`.

**Cliente de teste:** `E:\dev\pangya-test\KR642\ABRIR_TESTE_CS6.bat` (CS5 + URLs do emblema de guilda no C#;
CS5 = confirmado no cliente, CS4 = reserva). Portas do C#: web 30080,
login 30101, game 30201. Dados do jogo: `tools/sync-iff.py` copia o `pangya.iff` do último pak de correção do cliente.

Legenda: ✅ feito e testado (testes automáticos) · 🟡 parcial · ⬜ falta. "Testado no cliente" só onde o usuário confirmou.

## Conta, login e lobby
- ✅ Cadastro web, login, criação de nickname/personagem (testado no cliente), lista de servidores, canais, lobby.
- ✅ Lista de jogadores do lobby (0x44) atualizada ao entrar/sair de salas.
- ✅ Sussurro (0x2A→0x82), convite (0xB2/0x29→0x127/0x81), ir até a sala do amigo (0xAC).
- 🟡 Partida rápida: responde "sem alvo".
- 🟡 Mensageiro (fase 7, SPEC-messenger.md), porta 30303, no mesmo processo do game (que confirma quem está jogando;
  o login do MSN só tem uid e nick): lista de amigos, procurar/pedir/aceitar/apagar/bloquear/apelido, online/offline,
  posição (canal/sala vinda do game, não do cliente), status ocupado/ausente/jogando, conversa entre amigos;
  game 0x88 → 0xFA e lista de amigos pelo game (0x3C/0x11F). Falta: bilhetes (0x3C/0x111 responde "falha"),
  guilda no mensageiro (aba e chat 0x25), convites entre servidores. Vários processos exigiriam Redis (PLANO).
- ⬜ Ranking (fase 8).

## Salas e partida
- ✅ Stroke, dupla, match, pang battle, torneio, approach, Wiz City (moedas, caixas → Spin Cube).
- ✅ Tela de resultado: EXP no placar (fórmula estrelas × buracos × jogadores, −10 %/posição no VS), itens ganhos,
  0x77 do torneio, presentes de subida de nível, recompensa gravada depois do 0x06.
- ✅ Recordes por curso no perfil; estatísticas (chip-in, HIO, albatross, putts...).
- ✅ Expulsar, detalhe da sala, chat de equipe, ícone sobre a cabeça, desistir no solo.
- ✅ Troféus do torneio: troféu da sala pela média de nível (Match.iff 0x2C0x0000), ouro/prata/bronze por posição
  (18 buracos com 10+ jogadores, 9 buracos com 15+), contagem no perfil (0x43/0x151/salas), item do prêmio por carta.
  Bots não contam como jogadores, a não ser com `Game.Rewards.TrophiesCountBots: true` (para testar sozinho).
- ⬜ Medalhas do torneio (18+ jogadores), Treasure Hunter, ladder do match, entrar em partida em andamento.

## Bot
- ✅ Mira e força como o oponente do cliente; memória do buraco (água, OB, obstáculos, rota segura dos outros).
- ✅ Níveis `!bot easy|normal|hard|veryhard|impossible`: driver 240/250/260/280/300 jd, controle 30, spin 7/9/11/15/30
  (equipamento real no 0x74, stats pela fórmula do cliente).
- ✅ Power shot simples/duplo com gauge espelhado (0x56 antes da tacada).
- ✅ Tomahawk (hard+) e Spike (very hard+) com power shot quando nem o duplo alcança; alcance aprendido por tipo
  (começa em ×1,25).
- ⬜ Cobra, efeito/curva, erro por fase/impacto (SPEC-bot-especiais.md).

## Itens, loja e economia
- ✅ Loja (pang/cookie, pacotes), equipamento, armário, upgrades, cards (efeitos que dependem do servidor), mascote.
- ✅ Papel Shop, raspadinha, Caixa Mágica do caddie, Spin Cube, bolsa da sorte, envelope de ano novo, caixas de evento,
  aluguel (estender/apagar), fita de replay, apagar item, recontratar caddie, escola, missões do tutorial.
- ⬜ Troca de nick (responde "suspenso"), upgrade de caddie (0xEC), buff (0xDA), composição (0x68), pacote de suprimentos.

## Lounge (sala de avatar)
- ✅ Avatares aparecem, andam, fazem emote/pose e se veem (0x46 com 0xFFFF, 0x63→0xC2, posição para quem entra depois).
- ✅ Loja pessoal: abrir, título (com o nick), publicar até 6 itens, visitar, comprar (item e pang das duas contas
  numa transação); venda em pacote recusada com 0x1D7 (SPEC-lounge-loja.md).
- ⬜ Itens SP (gigante, cabeça grande...), clima, travar itens à venda contra outras operações.
- ⬜ Troca direta (0xDB): desligada no cliente KR (conteúdo 0x77); só com patch do exe, se o usuário pedir.

## GM
- ✅ Chat azul, `/notice`, `/kick`, `/disconnect`, `/identity` (só na própria tela).
- ⬜ `/visible`, vento/clima, `/giveitem`, observar.

## Correio (SPEC-correio-presentes.md)
- ✅ Listar, ler, pegar anexos, apagar, enviar (com item/pang), aviso de carta nova (0x15E), presente da loja.
- ✅ Presentes de subida de nível chegam como carta do sistema (uma por nível).
- ✅ Prêmio do Spin Cube (0xF1 → 0x1A2) chega numa carta; cubo e chave saem na mesma transação.

## Guilda (SPEC-guilda.md)
- ✅ Criar, listar/buscar, pedidos (entrar/desistir/aprovar/recusar), cargos, expulsar, sair, encerrar, notícia,
  apresentação, mensagem, trocar nome; guilda no perfil e nas salas.
- ✅ Emblema: 0x112 → upload HTTP no Pangya.Web (`/Guild/upload.asp`) → 0x113; download em `/_Files/GuildMark/`.
  Precisa do cliente CS6.
- ⬜ GuildMatch (modo 6), pontos e troféus de guilda.

## Próximos
- ⬜ Mercado/barraca offline (desligado no KR), eventos (quase todos desligados no KR), UCC.

## Comandos de administração (`dotnet src/Pangya.Server/bin/Release/net10.0/Pangya.Server.dll --config config/pangya.json ...`)
`account-create`, `player-set <login> pang= cookie= level= identity=`, `item-give <login> <tid> [qtd] [dias]`,
`give-all <login>`, `server-add/remove`. O jogador tem de estar fora do jogo.
`tools/restart-if-offline.sh` reinicia o servidor de desenvolvimento só se ninguém estiver conectado.
