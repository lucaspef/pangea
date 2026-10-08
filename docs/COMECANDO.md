# Começando: do zero até a primeira partida

Este guia monta o Pangea numa máquina Windows com WSL (Ubuntu), cria uma conta e entra no jogo com o cliente
KR 645. Leva uns 20 minutos. Em Linux puro, pule o passo 1 e rode os comandos direto no terminal.

> Os comandos `bash` deste guia rodam **dentro do Ubuntu** (WSL), como `root`. Os caminhos do Windows aparecem no
> Ubuntu em `/mnt/<letra>/...`: `E:\dev\pangea` vira `/mnt/e/dev/pangea`.

---

## 1. Preparar o WSL

No PowerShell do Windows (como administrador):

```powershell
wsl --install -d Ubuntu
```

Reinicie se for pedido, abra o **Ubuntu** pelo menu Iniciar e crie o usuário. Para trabalhar como `root`:

```bash
sudo -i
```

## 2. Instalar o .NET 10 e o PostgreSQL

```bash
apt update
apt install -y postgresql python3 git curl

# .NET 10 SDK pelo instalador oficial da Microsoft
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
dotnet --version        # deve mostrar 10.x
```

## 3. Baixar o código

Escolha uma pasta do Windows (assim o editor do Windows enxerga os arquivos), por exemplo `E:\dev`:

```bash
cd /mnt/e/dev
git clone https://github.com/lucaspef/pangea.git
cd pangea
```

## 4. Dados do jogo (`pangya.iff`)

O servidor lê itens, mapas, preços e roupas do mesmo `pangya.iff` que o cliente usa. Ele **não vem no
repositório**. O `setup-dev.sh` (próximo passo) copia o arquivo da pasta do cliente sozinho, se você disser onde ela
está:

```bash
export GAME_DIR=/mnt/e/Jogos/PangYa-KR645      # a pasta onde ficam os projectg*.pak do cliente
```

O script pega o `pangya.iff` do **último** pak de correção, que é o que o cliente realmente usa. Para fazer isso à
mão: `python3 tools/sync-iff.py "$GAME_DIR" data/pangya.iff`.

## 5. Preparar o banco e a configuração

```bash
bash tools/setup-dev.sh
```

O script:

- liga o PostgreSQL;
- cria o usuário `pangya` e os bancos `pangya` (jogo) e `pangya_test` (testes);
- gera `config/pangya.json` e `config/test.json` com uma senha aleatória (esses arquivos ficam fora do git);
- copia o `pangya.iff` para `data/`.

Pode rodar de novo quando quiser: ele não recria o que já existe. As tabelas são criadas pelo próprio servidor
na primeira subida (migrações em `src/Pangya.Data/Migrations`).

## 6. Conferir que está tudo certo

```bash
bash verify.sh
```

Compila e roda todos os testes (de unidade e de protocolo). Tem de terminar com **`VERIFY: VERDE`**.

## 7. Subir o servidor

```bash
bash tools/start.sh
```

Os servidores sobem em segundo plano. Acompanhe com `tail -f logs/server.out`; quando aparecer
`GAME: escutando` e `WEB: escutando`, está no ar. Para parar: `bash tools/stop.sh`.

| Servidor | Porta |
|---|---|
| Web (login HTTP, cadastro, uploads, painel) | 30080 |
| Login | 30101 |
| Game | 30201 |
| Mensageiro | 30303 |
| Ranking | 30474 |

O WSL repassa essas portas para o `127.0.0.1` do Windows automaticamente.

## 8. Criar uma conta

Pelo navegador: **http://127.0.0.1:30080/register**

Ou pela linha de comando, já com nickname e personagem prontos:

```bash
dotnet src/Pangya.Server/bin/Release/net10.0/Pangya.Server.dll --config config/pangya.json \
  account-create jogador senha123 MeuNick
```

Para virar GM (com o jogador fora do jogo):

```bash
dotnet src/Pangya.Server/bin/Release/net10.0/Pangya.Server.dll --config config/pangya.json \
  player-set jogador identity=0x14
```

## 9. Apontar o cliente para o servidor

O executável do cliente traz os endereços dos servidores oficiais. O `make_client.py` gera uma **cópia nova**
apontando para o Pangea, sem tocar no original:

```bash
python3 tools/client/make_client.py \
  "$GAME_DIR/LocalServer_ReleaseQA.exe" \
  "$GAME_DIR/Pangea_ReleaseQA.exe" \
  127.0.0.1 30080 30101
```

- A entrada é o executável **LocalServer** do cliente 645 (já apontado para `127.0.0.1`, com o renderizador sem
  shader).
- O nome da saída **tem de terminar em `_ReleaseQA.exe`**: o cliente carrega a DLL pelo sufixo do nome.
- Para jogar de outra máquina, troque `127.0.0.1` pelo IP do servidor e ajuste `Network.PublicIp` no
  `config/pangya.json`.

O script troca: login web, cadastro, porta do login, upload/download do emblema de guilda e do Self Design.

## 10. Jogar

Abra o `Pangea_ReleaseQA.exe`, entre com a conta do passo 8, escolha um canal e crie uma sala. Para jogar sozinho,
digite no chat da sala:

```
!bot                 adiciona o bot (nível normal)
!bot hard            nível: easy, normal, hard, veryhard, impossible
!bot off             tira o bot
```

---

## Extras

### Painel de administração

Acrescente no `config/pangya.json`, dentro de `"Web"`:

```json
"AdminEnabled": true,
"AdminAllowedIps": ["127.0.0.1"]
```

Reinicie (`bash tools/stop.sh && bash tools/start.sh`) e abra **http://127.0.0.1:30080/admin** com uma conta GM.
Dá para procurar contas, bloquear, expulsar, ajustar pang/cookie/nível, entregar itens e ver a auditoria.

### Itens para testar

```bash
S="dotnet src/Pangya.Server/bin/Release/net10.0/Pangya.Server.dll --config config/pangya.json"
$S give-all jogador          # personagens, tacos, bolas, caddies, mascotes, itens e cards do jogo
$S give-parts jogador        # todas as roupas dos personagens da conta e os anéis
$S player-set jogador pang=9999999 cookie=99999 level=70
```

Esses comandos exigem o jogador fora do jogo (senão a sessão aberta sobrescreve).

### Reiniciar sem derrubar ninguém

`bash tools/restart-if-offline.sh` só reinicia se não houver ninguém conectado.

### Configurações úteis (`config/pangya.json`, seção `"Game"`)

| Chave | Para quê |
|---|---|
| `Courses` | Mapas oferecidos (vazio = os ativos no `pangya.iff`) |
| `BotDelaySeconds` | Espera do bot antes de tacar |
| `BotFastForward` | Velocidade da bola do bot no VS (0 = normal) |
| `Rewards.ExpRate` | Taxa de EXP do servidor em % |
| `TreasureHunter` | Liga/desliga, taxa de caixas e lista de prêmios com peso |

### Algo deu errado?

| Sintoma | Onde olhar |
|---|---|
| `verify.sh` vermelho | A saída mostra o teste que falhou; `setup-dev.sh` de novo resolve banco/configuração |
| Cliente não conecta | `logs/server.out`; confira se o exe gerado aponta para o IP certo e se as portas estão livres |
| Item, mapa ou roupa "não existe" | O `data/pangya.iff` tem de ser o do mesmo cliente que você usa (passo 4) |
| Scripts `.sh` com erro de `$'\r'` | O arquivo foi salvo com fim de linha do Windows; o git já converte com o `.gitattributes` do projeto |

Para entender o que cada sistema faz e o que ainda falta: [`ESTADO.md`](ESTADO.md) e as especificações em
[`protocolo/`](protocolo).
