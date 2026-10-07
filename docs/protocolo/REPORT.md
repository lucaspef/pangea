# Servidor privado para o PangYa KR "645 QA" — relatório

Data: 2026-10-07. Pasta de trabalho: `/root/pangya-server-work`. Emulador: `/root/pangya-server-work/emu`.
Nada foi alterado em `/root/rebang`, `/root/ghidra-out` nem no exe de teste existente.

## TL;DR
- O servidor indicado (luismk/Pangya-Server-Community) é para o **JP R7.983 (2017)** e GB. Ele **não serve** para o KR 645 sem
  reescrever praticamente todos os pacotes. Além disso, o `Modern/JP` (.NET 10) **não compila** no HEAD atual
  (2 erros do próprio upstream) e depende de **MSSQL + ODBC** (procedures T-SQL; não há SQL Server suportado para Ubuntu 26.04 nem Docker no WSL).
- O que o cliente KR 645 tem **em comum** com ele: o algoritmo de cifra e as **tabelas de chave são idênticas** (4096+4096 bytes) e o formato de
  cabeçalho é o mesmo. Os IDs e estruturas dos pacotes são diferentes (ex.: KR 0x42 = JP 0x44; KR 0x4B/0x4C = lista/entrada de canal).
- Rota escolhida (a mais barata): **emulador mínimo próprio em Python** (≈400 linhas, sem banco), escrito a partir do código decompilado
  do próprio cliente, + **cópia patchada** do exe de teste (2 strings). Esforço real: ~1 dia de engenharia reversa; falta o teste com o
  cliente de verdade (só o usuário roda o jogo).
- Status testado: emulador rodando no WSL; portas 80/10101/20201 abertas e acessíveis do Windows por 127.0.0.1; um cliente falso
  que segue as regras de parsing do cliente decompilado faz HTTP → login → lista de servidores → game server → 0x42 → canais → entra
  no canal (saída abaixo). **Ainda não foi testado com o ProjectG real.**

## 1. O servidor da comunidade (luismk/Pangya-Server-Community)
- Clone: `/root/pangya-server`. Árvores `Server/JP` (.NET Framework 4.8, Windows), `Server/GB` (idem) e `Server/Modern/JP` (.NET 10).
- Alvo: `LS.ini` → `CLIENTVERSION = JP.R7.983.00`, `PACKETVERSION = 2017110200`. Porta do C++ "SuperSS" para C#.
- Arquitetura: Auth 7777 (interno), Login 10103/10903, Game 20201, Ranking 4774, Messenger 30303; tudo em **MSSQL** via ODBC DSN.
- Build no Linux: instalei `dotnet-sdk-10.0` (pacote oficial do Ubuntu 26.04, 10.0.112). `dotnet build` de cada servidor falha em
  `PangyaAPI.Network` (log: `/root/pangya-server-work/csharp-build.log`):
  - `Repository/CmdServerList.cs(50,36): CS0246 'EventFlag' not found`
  - `Service/Auth/UnitAuthClient.cs(250,34): CS1503 ServerType -> int`
- Cifra (`PangyaAPI.Network/Cryptor/Cipher.cs`): XOR em cadeia com passo de 4 bytes + byte de checagem das tabelas; servidor→cliente
  com LZO. É equivalente ao `SimpleStreamEncrypt_Alpha` do cliente KR (`jrencrypt.cpp`).

## 2. Protocolo do cliente KR 645 vs servidor JP — diferenças concretas
| Item | KR 645 (cliente) | Servidor JP 983 |
|---|---|---|
| Tabelas de chave Public/Private | `packet.cpp:8,299` | `CryptoOracle.cs` — **idênticas** (verificado byte a byte) |
| Cifra | dword XOR encadeado (`jrencrypt.cpp`) | byte XOR passo 4 — mesmo resultado |
| Compressão S→C | `CCompressBuffer`: `[flag][tamanho 3 dígitos base 255]` + LZO1X; **flag=1 = sem compressão** é aceito | sempre LZO |
| Endereço do login | **hardcoded** `211.44.251.73:10101` (`loginunit.cpp:6-7`) | 10103/10903 |
| Autenticação | antes do TCP: **POST HTTP** `http://qa.pangya.gametree.co.kr/Secure/Login/LoginForGame.aspx` (`logindlg.cpp:88`), resposta XML `<result>true</result>` + `<arg>AuthKey=..\|MemberNo=..\|PCBangNo=0</arg>`; o AuthKey vira a "senha" enviada ao login server | ID/senha direto no 0x01 |
| Hello login | raw `0x0000`: u32 parseKey(0..15), u32 serverUID | parecido |
| Login 0x01 (C→S) | str id, str authkey, u32 provType=2, u8 web, u32 memberNo, u64, u8 pcbang | layout JP diferente |
| Resposta de login | `0x0001` sub 0: str id, u32 uid, u32 identity, u8 level, u8 adult, u32, u32, str nick; **tem de vir `0x0001` sub `0xDE` antes da lista** senão a lista é descartada (gate de 2ª senha, `secondarypassword.cpp:351`) | — |
| Lista de servidores | `0x0002`: u8 n + n×92 bytes `sGameServerInfo` (`globalgamedefine.h:1037`) | struct maior |
| Chave p/ game server | `0x0010` str | — |
| Hello game | raw `0x003D`: u8,u8,u8 parseKey | 0x3F (JP) |
| Login game (C→S) | `0x0002`: str id, u32 uid, u32 memberNo, u16 0x6696, str authkey, str "645.00", u32 0x2A8ED069, u32 pcbang | JP 0x02 com outro layout |
| Info do jogador | `0x0042` sub 0: 2 str + `sUserInfo` **0xB92 bytes** + SYSTEMTIME + ... + GUILD_USER_INFO 0x119 | JP 0x44, struct bem maior |
| Canais | `0x004B` u8 n + n×77 bytes; entrar: C→S `0x0004` u8 id, S→C `0x004C` u8 1 | JP 0x4D / 0x4E |

Referências detalhadas (arquivo:linha) estão em `SPEC-login.md` e `SPEC-game.md` nesta pasta.

## 3. Rota escolhida e esforço
- **Adaptar o servidor C#**: consertar build, portar BD (MSSQL→MySQL/SQLite ou instalar SQL Server fora do WSL), e reescrever login +
  0x44/inventário/canais para o layout KR. Estimativa honesta: semanas.
- **Emulador próprio** (feito): só o necessário até o lobby; contas aceitas automaticamente (qualquer ID/senha); sem banco.
- **Patch de cliente**: obrigatório de qualquer jeito, porque o IP do login é literal no exe (o arquivo hosts não resolve) e o login
  sempre faz o POST HTTP. Patch numa **cópia** (`LocalServer_ReleaseQA.exe`), só 2 strings, 67 bytes:
  - `@0x615f84` `211.44.251.73` → `127.0.0.1`
  - `@0x623454` URL de auth → `http://127.0.0.1/Secure/Login/LoginForGame.aspx`
  O exe de teste atual (`ProjectG_ReleaseQA.exe`, md5 `0d57ce31…`) e o oficial do rebang ficam intocados.
  **Importante:** não tente logar com um exe não patchado — o POST iria para o domínio real `gametree.co.kr` (hoje de terceiros),
  levando o ID/senha digitados.

## 4. Como rodar
No WSL (Ubuntu, root):
```
bash /root/pangya-server-work/emu/start.sh        # sobe HTTP :80, login :10101, game :20201 (log em emu/server.log)
bash /root/pangya-server-work/emu/stop.sh
python3 /root/pangya-server-work/emu/test/fakeclient.py 127.0.0.1 tester   # teste sem o jogo
```
Opção `--no-char` (passe para o start.sh) envia o `sUserInfo` sem personagem, caso o cliente quebre ao desenhar o avatar.

No Windows (o usuário):
1. Gerar a cópia patchada (já gerada em `E:\dev\pangya-test\KR642\LocalServer_ReleaseQA.exe`; para regerar):
   `python \\wsl.localhost\Ubuntu\root\pangya-server-work\emu\patch_client.py E:\dev\pangya-test\KR642\ProjectG_ReleaseQA.exe E:\dev\pangya-test\KR642\LocalServer_ReleaseQA.exe 127.0.0.1`
2. Com o emulador rodando, abrir `LocalServer_ReleaseQA.exe` como você já abre o de teste, digitar qualquer ID (letras/números/_) e qualquer senha.
3. Esperado: "login OK" → lista com o servidor "Rebang Local" → clicar → lista de canais "Rebang Channel 1" → clicar → lobby (TOPPAGE).
4. Se travar, mande o `emu/server.log` (cada pacote recebido/enviado é logado).

## 5. Saída dos testes
Cifra (Python vs `jrencrypt.cpp` real compilado com g++): `crypto cross-check: 300 random cases each direction, failures = 0`.

Portas vistas do Windows: `porta 80 : ABERTA`, `porta 10101 : ABERTA`, `porta 20201 : ABERTA`.

Cliente falso rodando no Windows contra 127.0.0.1:
```
LOGIN hello key=15 serverUID=10101
LOGIN 0x10 authkey 0EAEF98015EAC029
LOGIN 0x01 success id=wintest uid=3721188 nick=wintest
LOGIN 0x01/0xDE second password gate open
LOGIN 0x09 messenger servers: 0
LOGIN 0x02 server 'Rebang Local' id=20201 0/3000 127.0.0.1:20201
GAME hello key=3
GAME 0x42 ok id=wintest nick=wintest uid=3721188 lvl=1 ...
GAME 0x4B channel 'Rebang Channel 1' id=0 0/100
GAME 0x4C enter channel OK
FAKECLIENT: reached lobby
```

## 6. Riscos / incertezas (para o primeiro teste real)
- O cliente falso valida o **meu entendimento** do protocolo, não o binário. Pontos inferidos: significado de vários campos zerados
  do `sUserInfo`/`GUILD_USER_INFO`, o bit `DoTutorial`, personagem `tid 0x04000000` sem partes (pode faltar visual ou quebrar — use `--no-char`).
- `_GGC_SendUserID` (GameGuard) é chamado no sucesso do login; não verifiquei se os 4 patches de teste já neutralizam isso.
- Heartbeat `0xF6` é ignorado; não achei timeout do lado do cliente, mas não foi exercitado.
- Depois do lobby (salas, loja, myroom, partida) nada está implementado.
- Incidente durante o trabalho: um comando meu com `$` expandido pelo wsl.exe começou a copiar `/` para `emu/` (só cópia, nada apagado
  nem alterado fora da pasta); interrompi e apaguei a cópia (`rm -rf --one-file-system /root/pangya-server-work/emu`), depois recriei o emulador.

## 7. Nome do exe patchado (correção após o 1º teste)
O cliente carrega `wangreal<sufixo>.dll` e `LoadingRes<sufixo>.dll`, e o sufixo é o que vem depois do último `_` no nome do exe
(`Wangreal/source/wdevmng.cpp:62`, `winmain.cpp:559`). Com o nome `ProjectG_ReleaseQA.local.exe` ele procurava
`wangreal_ReleaseQA.local.dll`, não achava, e mostrava o erro "DirectX 9.0c / driver de vídeo". **A cópia tem de terminar em `_ReleaseQA.exe`**:
agora ela se chama `LocalServer_ReleaseQA.exe` (o `ABRIR_TESTE.bat` foi ajustado).
