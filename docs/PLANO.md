# Plano: servidor PangYa em C# (aprovado em 2026-10-07)

## Objetivo
Conjunto completo de servidores para o cliente PangYa KR "645 QA" (o cliente decompilado no projeto rebang),
com contas reais, banco de dados e testes automáticos, com arquitetura pronta para evoluir até a Season 9
junto com o cliente. Primeiro tudo pronto localmente; a abertura pública com segurança é a última fase.

## Regras
1. Código enxuto: menos linhas possível, mas legível (sem código-charada).
2. Documentação e comentários em pt-BR.
3. Nomes (variáveis, classes, métodos) em inglês.
4. Otimizado.
5. Performático: buffers reaproveitados (ArrayPool/Span), I/O assíncrono, SQL direto.
6. Nunca confiar no cliente: todo pacote é validado; pacote inválido derruba só aquela conexão.
7. Senhas com hash forte (PBKDF2/Argon2) e contas reais.
8. Toda funcionalidade com teste automático.
9. Configuração por arquivo: IP, portas, banco e segredos fora do código.
10. Limites contra abuso: conexões por IP, pacotes por segundo, tentativas de login.
11. Handles únicos: todo item/personagem/caddie/card/mascote tem identificador (guid) único gerado pelo banco.
    (Lição real: guids repetidos entre jogador e bot quebraram a física da tacada no cliente.)
12. Núcleo independente de versão: lógica do jogo (Domain) separada do protocolo; uma camada de protocolo
    por versão de cliente (KR645 hoje, S9 no futuro); banco genérico (tipo + quantidade + atributos jsonb);
    funcionalidades ligáveis por versão (como o IsLocalContent do cliente).
13. Sem LINQ no código do servidor (src/): laços explícitos; Dictionary/HashSet para buscas por chave
    (evita alocações e buscas lineares escondidas). Testes e ferramentas (tests/, tools/) podem usar LINQ.
    (Pedido do usuário em 2026-10-07.)

## Servidores
| Servidor | Quem conecta | Função |
|---|---|---|
| Web/Auth (HTTP) | cliente | `POST /Secure/Login/LoginForGame.aspx` (multipart: id, pwd, gamecode) -> XML `<result>true</result>` + `<arg>AuthKey=..|MemberNo=..|PCBangNo=0</arg>`; página de cadastro |
| Auth (interno) | só servidores | chaves de sessão, registro dos servidores online |
| Login | cliente | login, nickname/personagem, lista de servidores (porta 10101; o cliente 642 live usa 10101/10102) |
| Game | cliente | lobby, canais, salas, partida, loja, My Room (várias instâncias) |
| Messenger | cliente | amigos, online, mensagens, sussurro |
| Ranking | cliente | rankings |
O cliente 645 tem 4 conexões TCP (NET_GAME, NET_MSN, NET_LOGIN, NET_RANK em networksystem.h) + o HTTP.
O ShopUnit do cliente (127.0.0.1:7777) não é usado nesta versão.

## Tecnologia
- C# / .NET 10 (SDK já instalado no WSL Ubuntu: dotnet-sdk-10.0). Desenvolvimento no WSL; roda também no Windows e em VPS Linux.
- PostgreSQL (pacote oficial do Ubuntu) + Npgsql + Dapper; migrações SQL numeradas.
- Redis a partir do Messenger (online, sessões, mensagens entre servidores).
- Testes: xUnit + testes Python de protocolo já existentes (ver "Referências").
- Git local, sem push.

## Estrutura
```
src/Pangya.Core              cifra, pacotes, leitor IFF, config, logs, limites
src/Pangya.Domain            lógica do jogo (sem bytes/IDs de pacote)
src/Pangya.Protocol.KR645    tradução pacotes <-> lógica para o cliente 645
src/Pangya.Data              PostgreSQL, migrações, repositórios
src/Pangya.Web | .Auth | .Login | .Game | .Messenger | .Ranking
tools/StructGen              gera structs C# dos headers do rebang (com teste de tamanho)
tests/                       xUnit + testes de protocolo
docs/                        protocolo (SPEC-*.md) e guias
verify.sh                    compila + roda todos os testes
```

## Fases (cada fase termina com testes verdes E confirmação do usuário no cliente real)
| Fase | Entrega | Pronto quando | Estimativa |
|---|---|---|---|
| 0. Preparação | repositório, PostgreSQL no WSL, verify.sh, configuração | verify.sh verde | ½ dia |
| 1. Base comum | cifra, pacotes, structs geradas, IFF, logs, limites, camada KR645 | cifra validada contra o código do cliente; structs com tamanho exato | 1–2 dias |
| 2. Contas + Web/Auth + Auth + Login | cadastro, hash, AuthKey, sessões, login, nickname, lista de servidores | cliente real chega à lista de servidores com conta cadastrada | 1–2 dias |
| 3. Game: base | dados do jogador do banco, canais, lobby | cliente real no lobby com personagem e inventário do banco | 1–2 dias |
| 4. Salas + Stroke + bot | porte do emulador Python | partida completa com bot no cliente real | 2–3 dias |
| 5. Loja + My Room | compra, equipamentos, caddies, cards, mascotes, upgrades, info do jogador | tudo persiste após relogar | 2–3 dias |
| 6. Modos + tacadas especiais | torneio, approach, match, pang battle...; power shot, cobra, tomahawk, spike, itens | cada modo jogável do início ao resultado | 2–4 dias |
| 7. Messenger + Redis | amigos, online, mensagens | 2 contas se adicionam e conversam | 2–3 dias |
| 8. Ranking | telas de ranking | dados reais na tela | 1–2 dias |
| 9. Administração | comandos de GM, painel web simples, auditoria | ações de GM registradas e funcionando | 1–2 dias |
| 10. Abertura pública | revisão de segurança, teste de carga, backups, guia VPS/Windows, pacote do cliente | checklist aprovado pelo usuário | 2–3 dias |

## Decisões aprovadas
1. Local: `E:\dev\pangya-server` (este repositório).
2. Cadastro: página de cadastro no Web/Auth + auto-cadastro no primeiro login ligável por configuração (só testes).
3. Ordem das fases: como na tabela.

## Riscos
- O cliente manda a senha por HTTP sem criptografia; a cifra do protocolo é fraca. Servidor guarda hash; avisar jogadores para não reaproveitar senhas.
- Física e julgamento da tacada ficam no cliente; o servidor só valida limites.
- Distribuição do cliente patchado é decisão/responsabilidade do usuário.
- Season 9: o ritmo depende da evolução do cliente (rebang) e dos dados (modelos, IFF, mapas).

## Referências (tudo o que já foi descoberto — leia antes de começar)
- Emulador Python funcional (login -> lobby -> salas -> partida com bot -> loja/My Room), no WSL Ubuntu (root):
  `/root/pangya-server-work/emu` — server.py, game.py, room.py, ingame.py, player.py, shop.py, pkt.py, pycrypt.py;
  specs: REPORT.md, SPEC-login.md, SPEC-game.md, SPEC-room.md, SPEC-ingame.md, SPEC-player-shop.md,
  SPEC-client-data.md, SPEC-physics.md (+ SPEC-myroom.md / SPEC-modes.md quando os agentes terminarem);
  testes: test/run_all.sh, test/fakeclient.py, test/private_run.sh <porta-base> <teste.py>, test_room.py, test_ingame.py,
  test_player_shop.py, run_cryptotest.sh (valida a cifra contra jrencrypt.cpp do cliente).
  Inicia/para: emu/start.sh -v / emu/stop.sh (portas 80, 10101, 10102, 20201 — instância do usuário; NÃO derrubar
  com o usuário conectado; para testes use portas privadas).
- Cliente decompilado: `/root/rebang/source/client/ProjectG/*.cpp/.h` (structs em /root/rebang/source/shared/globalgamedefine.h),
  pseudocódigo Ghidra em `/root/ghidra-out/<unidade>.c`, mapa de endereços `/root/rebang/build/projectg/ProjectG_ReleaseQA.map`.
  NUNCA modificar /root/rebang nem /root/ghidra-out (projeto de decompilação separado).
- Servidores de referência (outras versões; só semântica): `/root/pangya-server-work/ref/Pangya-Server-Source-master/S6`
  (GB/US S6, C#) e `/root/pangya-server/Server` (JP 983 / Season 9, C#; Modern/JP é .NET 10 mas não compila no HEAD).
- Cliente de teste (Windows): `E:\dev\pangya-test\KR642` — ABRIR_TESTE_FIX.bat (exe sem shader) ou ABRIR_TESTE.bat
  (exe com pangya.fx adaptado). Nunca executar o jogo (quem roda é o usuário); nunca alterar arquivos existentes lá.
- Ambiente: Git Bash no Windows; WSL via `MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -u root -- bash <script>`.
  NUNCA colocar `$` na linha de comando do wsl.exe (é expandido no Windows; já causou um `cp -r /.` acidental) —
  escreva scripts em arquivo. Escrita em caminhos \\wsl.localhost é bloqueada; arquivos .sh editados no Windows
  precisam de fim de linha LF.
