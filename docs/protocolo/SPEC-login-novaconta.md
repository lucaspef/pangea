# KR 645 — conta nova, erros do login web e sub-códigos (pesquisa no cliente decompilado, 2026-10-07)

`lt.c` = ghidra-out/lobbytask.c, `lm.cpp` = lobbymain.cpp, `ld.cpp` = logindlg.cpp, `cn.cpp` = createnickdlg.cpp.
str = u16 tamanho + cp949. [inferido] = não comprovado no código.

## Conta nova: nickname e personagem são duas telas
1. **Nickname** — S→C `0x01 u8 0xD8, u32 0xFFFFFFFF` (lt.c:2859-2893) → layout "BLANK" → diálogo "createnick".
   - C→S `0x07 str nick` (conferir) → S→C `0x0E u32 código [+ str nick se 0]`. 0 ok, 1 erro, 2 em uso, 3 tamanho/formato,
     4 sem cookies, 5 palavra proibida, 6 erro de banco, 8 mesmo nickname.
   - C→S `0x06 str nick` (criar; só depois de um 0x0E ok com o mesmo texto) → S→C `0x0D u32 código [+ str nick se 0]`;
     mesmos códigos + 10 = difere do conferido. Após 0 o cliente fica na tela vazia: **o servidor manda o próximo passo**.
2. **Personagem** — S→C `0x01 u8 0xD9` → layout "CREATE". Só oferece `0x04000000 | gênero` (lm.cpp:14689); cores 0..2.
   - C→S `0x08 u32 charTid, u8 cores` (cabelo = bits 0-3, camisa = bits 4-7) → S→C `0x11 u8` (0 ok; ≠0 mensagem de falha).
3. Depois: `0x10 str chave`, `0x01 sub 0`, `0x01 sub 0xDE`, `0x09`, `0x02` (o 0x02 troca o layout para "SERVERLIST").
   Regra rígida: 0xDE antes do 0x02.

Regras do nickname no cliente (cn.cpp:227-256, hatmanager.cpp:3877-3910): 4..16 bytes cp949; só ASCII 0x21-0x7E ou
sílabas Hangul (sem espaço); sem apóstrofo; diferente do login; lista de palavrões (exceto GM).

## Login web (ld.cpp:88-155, logininfo.cpp:432-517)
- O status HTTP não é olhado; corpo ≤ 520 bytes. Sucesso = substring `<result>true</result>`.
- `<arg>`: `AuthKey=..|MemberNo=<decimal u32>|PCBangNo=0` (PCBangNo diferente de "0" ou ausente LIGA PC bang).
- Falha: o texto de `<messages>` (UTF-8 → cp949, até 260 bytes) aparece no log da tela de login. Não há códigos
  separados para senha errada/bloqueio: o servidor escreve a mensagem (em coreano/ASCII).
- (Na prática o cliente manda multipart/form-data com Content-Length — visto nos logs do emulador.)

## Sub-códigos do 0x01 (lt.c:2769-2794, ld.cpp:472-536)
- 2: sem dados → "ID errado". 6: sem dados → "ID ou senha errados"; na 5ª falha o cliente fecha.
- 4: sem dados → "ID em uso, encerrar a conexão anterior?"; OK → C→S `0x0004` (sem dados) e layout SERVERLIST;
  o servidor derruba a sessão antiga e manda `0x01 sub 0`, `0xDE`, `0x10`, `0x02` [inferido].
- 5: `str motivo` → mostra o motivo em vermelho + "contate o suporte".

## Botão de cadastro (ld.cpp:350-361)
Só por clique do usuário: pergunta OK/Cancelar e abre `SignUp/Join.aspx?rsn=9` no navegador (ShellExecute);
em tela cheia o cliente fecha em seguida. A cópia gerada por tools/client/make_client.py aponta para `/register`.
