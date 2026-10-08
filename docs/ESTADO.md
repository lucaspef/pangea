# Estado do servidor C# (atualizado em 2026-10-07)

Mapa rápido do que funciona, do que falta e de como testar. Detalhes de protocolo em `docs/protocolo/SPEC-*.md`;
plano e regras em `docs/PLANO.md`. Pacotes que o cliente manda e o servidor ainda não trata: `python3 tools/coverage.py`.

**Cliente de teste:** `E:\dev\pangya-test\KR642\ABRIR_TESTE_CS5.bat` (CS4 = reserva). Portas do C#: web 30080,
login 30101, game 30201. Dados do jogo: `tools/sync-iff.py` copia o `pangya.iff` do último pak de correção do cliente.

Legenda: ✅ feito e testado (testes automáticos) · 🟡 parcial · ⬜ falta. "Testado no cliente" só onde o usuário confirmou.

## Conta, login e lobby
- ✅ Cadastro web, login, criação de nickname/personagem (testado no cliente), lista de servidores, canais, lobby.
- ✅ Lista de jogadores do lobby (0x44) atualizada ao entrar/sair de salas.
- ✅ Sussurro (0x2A→0x82), convite (0xB2/0x29→0x127/0x81), ir até a sala do amigo (0xAC).
- 🟡 Partida rápida: responde "sem alvo".
- ⬜ Mensageiro (amigos; fase 7), ranking (fase 8).

## Salas e partida
- ✅ Stroke, dupla, match, pang battle, torneio, approach, Wiz City (moedas, caixas → Spin Cube).
- ✅ Tela de resultado: EXP no placar (fórmula estrelas × buracos × jogadores, −10 %/posição no VS), itens ganhos,
  0x77 do torneio, presentes de subida de nível, recompensa gravada depois do 0x06.
- ✅ Recordes por curso no perfil; estatísticas (chip-in, HIO, albatross, putts...).
- ✅ Expulsar, detalhe da sala, chat de equipe, ícone sobre a cabeça, desistir no solo.
- ⬜ Troféus e medalhas do torneio (≥10/18 jogadores), Treasure Hunter, ladder do match, entrar em partida em andamento.

## Bot
- ✅ Mira e força como o oponente do cliente; memória do buraco (água, OB, obstáculos, rota segura dos outros).
- ✅ Níveis `!bot easy|normal|hard|veryhard|impossible`: driver 240/250/260/280/300 jd, controle 30, spin 7/9/11/15/30
  (equipamento real no 0x74, stats pela fórmula do cliente).
- ✅ Power shot simples/duplo com gauge espelhado (0x56 antes da tacada).
- ⬜ Tomahawk/Spike/Cobra, efeito/curva, erro por fase/impacto (SPEC-bot-especiais.md).

## Itens, loja e economia
- ✅ Loja (pang/cookie, pacotes), equipamento, armário, upgrades, cards (efeitos que dependem do servidor), mascote.
- ✅ Papel Shop, raspadinha, Caixa Mágica do caddie, Spin Cube, bolsa da sorte, envelope de ano novo, caixas de evento,
  aluguel (estender/apagar), fita de replay, apagar item, recontratar caddie, escola, missões do tutorial.
- ⬜ Troca de nick (responde "suspenso"), upgrade de caddie (0xEC), buff (0xDA), composição (0x68), pacote de suprimentos.

## Lounge (sala de avatar)
- ✅ Avatares aparecem, andam, fazem emote/pose e se veem (0x46 com 0xFFFF, 0x63→0xC2, posição para quem entra depois).
- ✅ Loja pessoal: abrir, título, publicar até 6 itens, visitar, comprar (item e pang das duas contas numa transação).
- ⬜ Troca direta entre jogadores (0xDB), itens SP (gigante, cabeça grande...), clima.

## GM
- ✅ Chat azul, `/notice`, `/kick`, `/disconnect`, `/identity` (só na própria tela).
- ⬜ `/visible`, vento/clima, `/giveitem`, observar.

## Em pesquisa / próximos
- 🔎 Correio e caixa de presentes (SPEC-correio-presentes.md).
- 🔎 Guilda (SPEC-guilda.md).
- ⬜ Mercado/barraca offline (desligado no KR), eventos (quase todos desligados no KR), UCC.

## Comandos de administração (`dotnet src/Pangya.Server/bin/Release/net10.0/Pangya.Server.dll --config config/pangya.json ...`)
`account-create`, `player-set <login> pang= cookie= level= identity=`, `item-give <login> <tid> [qtd] [dias]`,
`give-all <login>`, `server-add/remove`. O jogador tem de estar fora do jogo.
`tools/restart-if-offline.sh` reinicia o servidor de desenvolvimento só se ninguém estiver conectado.
