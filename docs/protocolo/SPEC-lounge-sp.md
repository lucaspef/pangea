# KR 645: itens SP do lounge (gigante, cabeça grande, velocidade, brilho) e clima

Pesquisa somente leitura no cliente KR 645 QA. Complementa `SPEC-lounge-loja.md` §2.5 (que tinha o layout do 0x0C sub 6 e a
semântica do valor como incertos). Legenda: **[C]** confirmado no cliente, **[R]** servidor de referência, **[P]** palpite.

Fontes: `gh/` = `/root/ghidra-out`; `shared/` = `/root/rebang/source/shared`; `asm` = `/root/pg645.asm`;
`exe` = `ProjectG_ReleaseQA.exe` original (strings lidas em cp949); `iff` = `data/pangya.iff` do worktree (`SpecialPrizeItem.iff`,
`Part.iff`, `AuxPart.iff`); `GB/` = `/root/pangya-server/Server/GB/GameServer` (S->C GB = KR +2: GB 0x4B = KR 0x49,
GB 0x9E = KR 0x9C).

## Resposta curta

- São **peças de roupa/acessório equipadas** (Part/AuxPart), **não consumíveis**. O jogador digita `/거인`, `/왕머리`, `/광속` ou
  `/반짝이` no chat do lounge; o cliente confere se a peça está equipada e manda **C->S 0x0C `u8 6, u32 meuGuid, u32 tipo`**. [C]
- O servidor responde a **toda a sala (inclusive o remetente)** com **S->C 0x49 `u8 6, u32 guid, u32 tipo, f32 valor`**. Cada 0x49
  sub 6 **inverte** o estado "ligado" do efeito no cliente e grava `valor` como o multiplicador atual. Então o servidor alterna:
  ligado → `valor = rate` do `SpecialPrizeItem.iff` (2.0, ou 1.0 no brilho); desligado → `1.0`. [C]
- Quem entra depois pede o estado com **C->S 0xED `u32 guid`**; resposta **S->C 0x19B `u32 guid, 5 × f32`** (valor atual de cada
  tipo 0..4). [C]
- Clima do lounge: **S->C 0x9C `u8 clima, u8, u8`** (0 bom, 1 nublado, 2 chuva, 3 neve). [C]

## 1. Tabela de itens (`SpecialPrizeItem.iff`) [C]

Estrutura `IFF_STRUCT::sSpecialPrizeItem` = `{u32 typeId, u32 ability, f32 rate}` (12 bytes; `shared/classdefine.h:400-406`).
O cliente monta `m_SPItemMap[ability][typeId]` (`shared/itemmanager.cpp:4623-4641`) e `FindSPItem(tid, tipo)` procura nele
(`:4643-4668`). O IFF do servidor tem **40 registros**:

| tipo (`eSPAVILITYTYPE`) | comando (exe 0x9F4410 + tipo × 0x40) | efeito | itens (tid) | rate |
|---|---|---|---|---|
| 0 | `/거인` (gigante) | escala do corpo inteiro | `0x70010081` 거인의 반지 (AuxPart, anel) | 2.0 |
| 1 | `/왕머리` (cabeça grande) | escala do osso "Bip01 Head" | 왕머리의가발 (peruca, Part) — Nuri `0x08000848`, Hana `0x08040863`, Azer `0x0808082B`, Cecilia `0x080C002C`, Max `0x08100033`, Kooh `0x0814003E`, Arin `0x0818005E`, Kaz `0x081C002B`, Lucia `0x08200018`, Nell `0x08240018` | 2.0 |
| 2 | `/광속` (velocidade da luz) | velocidade de andar | 광속의신발 (sapato, Part) — `0x08010032`, `0x0804E058`, `0x0808E025`, `0x080CE041`, `0x0810A030`, `0x0814E05E`, `0x0818A060`, `0x081CE02F`, `0x0820E02F`, `0x0824E003` | 2.0 |
| 3 | `/반짝이` (brilho) | efeito único (fogos `BFX_B2.seq`/`BFX_B3.seq` + som "Ball_B"), não é estado | 반짝반짝안경 (óculos, Part) — `0x0801A812`, `0x08050811`, `0x0809081D`, `0x080D481C`, `0x0811201B`, `0x08162810`, `0x08196013`, `0x081DA817`, `0x0821A808`, `0x0825A802` | 1.0 |
| 4 | (texto vazio na tabela: **sem comando**) | nenhum uso no cliente 645 (só é lido no 0x19B) | 기교의장갑 (luva, Part) — `0x08008813`, `0x0804681B`, `0x08088812`, `0x080C8021`, `0x08106016`, `0x08148020`, `0x08186015`, `0x081C8014`, `0x0820801C` (Nell não tem) | 2.0 |

(Nomes de `Part.iff`/`AuxPart.iff`, tids lidos do `SpecialPrizeItem.iff` do worktree.)

## 2. Como o cliente usa [C]

1. Chat do lounge → `ntAvatarChatMain::SpCommand` (`gh/avatar_main.c:2483-2597`, chamado em :3862 antes de enviar o chat):
   - só texto começando com `/`; compara com os 5 nomes da tabela (tipo = índice);
   - tipo 3 tem **recarga de 1 s** (senão mensagem de erro rosa);
   - `CanUseSPItem(tipo)` (`gh/avatar_main.c:1640-1680`): percorre as **24 peças** do `sCharacterInfo` do próprio avatar
     (ntAvatar+0x3E1) e os **5 acessórios** (ntAvatar+0x449) procurando um tid em `m_SPItemMap[tipo]`. Sem peça → mensagem de
     erro, nada é enviado;
   - com peça → `ntAvatar::UseSPItem(tipo)`.
   - Comando reconhecido **não vai para o servidor como chat**.
2. `ntAvatar::UseSPItem` (`gh/avatar_avatar.c:1353-1382`): **C->S 0x0C** `u8 6, u32 guid (ntAvatar+0xE4, o próprio), u32 tipo`.
   Não há envio de valor nem de tid do item. Não mexe no inventário (nada é consumido).
3. **S->C 0x49** (OnPacketCommon; tabela de subs 0x746444, sub 6 em `asm 0x734cac-0x734db6`):
   `u8 6, u32 guid, u32 tipo, f32 valor` → posta msg **0x24E** `(guid, tipo, (int)valor)` ao ator do lounge.
   **Atenção: o f32 é truncado para inteiro (`fistp`) e depois volta a float** no handler, então só valores inteiros
   funcionam (1.5 vira 1.0).
4. Msg 0x24E em `ntAvatarChatMain` (`gh/avatar_main.c:4185-4273`):
   - acha o avatar pelo guid (se não existir, ignora);
   - tipos 0, 1, 2, 4: recria o "burden" (colisão/modelo) do avatar; tipo 0 no próprio avatar também reajusta a câmera
     (altura = valor × 28,8; :1516-1520);
   - `flag = !flag` e `valor = valor recebido` (`GetMySpItem(tipo)` = ntAvatar + 0xAC8 + tipo × 12; flag +4, valor +8;
     `gh/avatar_avatar.c:276-283`);
   - `StartSpEffect` (`gh/avatar_main.c:1043-1118`): tipos 0/1/2 → efeito "change_up-small/middle/big.seq" conforme gigante e
     cabeça ligados; tipo 3 → fogos na altura valor × 10.
5. Uso do valor no desenho/movimento:
   - tipo 0 (0xAD0): `AxisScale(valor)` no modelo e raio de colisão × valor (`gh/avatar_avatar.c:4625-4632`, :2283-2286);
   - tipo 1 (0xADC): `ScaleBone("Bip01 Head", valor)` (:4638-4642);
   - tipo 2 (0xAE8): velocidade = 16,5 × valor por segundo e o limiar de envio do 0x63 sub 6 = 16,5 × valor
     (:4378-4381, :1247-1248).
6. **C->S 0xED `u32 guid`**: enviado quando um avatar de outro jogador é criado/atualizado a partir da lista (`gh/avatar_main.c`
   :3994-3996, :4084-4086 → :4171-4178). **S->C 0x19B** `u32 guid, 5 × f32` só grava os 5 valores (não mexe nos flags e não
   toca efeito) (`gh/avatar_task.c:2172-2190`). Como o desenho usa só o valor, isso basta para mostrar quem já está gigante.
7. Fora do lounge o 0x49 sub 6 é lido mas a msg 0x24E não tem quem trate; o comando só existe no chat do lounge.

GB [R] (`GB/Game/Room.cs:3608-3700`): tipo de alteração 6 = `TC_ITEM_EFFECT_LOUNGE`; valida a peça (erros 0x57007/8/9),
alterna `scale_head`/`walk_speed` entre 1.0 e 2.0 e manda 0x4B (= KR 0x49) tipo 6 à sala. Coincide com o cliente.

## 3. O que o servidor deve fazer

C->S **0x0C** `u8 6, u32 guidCliente, u32 tipo` (o sub 6 tem 8 bytes depois do u8, não 4):

1. Só no lounge (`GameMode.AvatarChat`) e com o avatar já confirmado (`loungeReady`); senão ignorar.
2. **Ignorar `guidCliente`**; usar o guid do próprio slot (`me.Guid`).
3. `tipo` em 0..3 (o 4 não tem comando; aceitar só se quiser, sem efeito visível).
4. Conferir no servidor que o personagem equipado tem nas peças (24) ou acessórios (5) um tid do `SpecialPrizeItem.iff` com
   esse `ability` (mesma regra do `CanUseSPItem`) **e** que o jogador possui o item.
5. Recarga: tipo 3 ≥ 1 s; tipos 0..2 ≥ ~1 s para evitar spam de recriação de modelo [P].
6. Estado por slot do lounge: `bool On[5]`, `float Value[5]` (inicial `false`/1.0). Tipos 0..2 (e 4): `On = !On`;
   `Value = On ? rate : 1.0`. Tipo 3: não guardar estado; mandar sempre `valor = rate` (1.0).
7. Responder a **todos da sala, incluindo o remetente**: **0x49 `u8 6, u32 guid, u32 tipo, f32 valor`** (valor inteiro: 1.0 ou 2.0).
8. Nada é consumido; não gravar no banco. Zerar o estado ao sair do lounge (o cliente recria avatares com valor 1.0).
9. Se o jogador trocar a roupa (0x0C sub 4) e tirar a peça, desligar o efeito correspondente (mandar 0x49 sub 6 com 1.0) [P].

C->S **0xED** `u32 guid` → responder só ao pedinte com **0x19B `u32 guid, f32 v0, v1, v2, v3, v4`** do slot desse guid (1.0 se
não existir estado). Hoje o 0xED é engolido (`GameHandler.Room.cs:48`).

Clima do lounge: guardar `Room.Weather` (0..3) e mandar **0x9C `u8 clima, u8 0, u8 0`** a quem entra (depois do 0x47, quando a
avatar task já existe) e à sala quando mudar. O cliente parte do clima da opção local (`COption::dGetWeather`,
`gh/avatar_main.c:2872-2874`) e só muda com 0x9C (`gh/avatar_task.c:2247-2253`). Fontes de mudança: GM `/weather`
(`SPEC-gm-comandos.md` §2.3) ou sorteio periódico (GB sorteia chuva [R]). Não há outro efeito de ambiente no lounge.

## 4. Segurança

- Nunca confiar no guid do pacote nem no fato de o cliente ter checado a peça: um cliente adulterado pode mandar 0x0C sub 6 com
  qualquer tipo e qualquer frequência.
- Validar posse + equipamento no servidor; tipo fora de 0..4 → ignorar.
- Valor sempre definido pelo servidor (IFF), nunca vindo do cliente.
- Gigante muda colisão e câmera: limitar a frequência evita "flood" de recriação de modelo nos outros clientes.
- O passo máximo do 0x63 sub 6 (`MaxStep = 200`) continua valendo com velocidade 2× (limiar 33 unidades).

## 5. Plano de implementação

| # | item | onde |
|---|---|---|
| 1 | Carregar `SpecialPrizeItem.iff` (struct `sSpecialPrizeItem` já existe em `Structs.g.cs:1395`) num dicionário `tid → (ability, rate)` (sem LINQ) | `Pangya.Core/Iff` ou catálogo de itens |
| 2 | No switch de `GameHandler.MyRoom.cs:58`, tratar `0x0C` com `u8 == 6` antes do `QuickEquipAsync` (que hoje lê só `u32` e cai no `default` mascote): ler `u32 guid, u32 tipo` | `GameHandler.MyRoom.cs:58` → novo `LoungeSpItem` em `GameHandler.Lounge.cs` |
| 3 | Estado `SpOn[5]`/`SpValue[5]` + `SpLastUse` no membro da sala (o objeto de `room.Find(this)`, que já guarda X/Z/Action) | `Pangya.Domain/Rooms` |
| 4 | Validar peça equipada (parts 24 + aux 5 do personagem atual) e posse | `GameHandler.Lounge.cs` |
| 5 | Broadcast `0x49 u8 6, u32 guid, u32 tipo, f32 valor` à sala inteira (`InGameOutput.Broadcast(r, w)` sem `except`) | idem |
| 6 | `0xED` → `0x19B u32 guid + 5 × f32` ao pedinte | `GameHandler.Room.cs:48` |
| 7 | Clima do lounge: `Room.Weather`, 0x9C na entrada e na mudança (junto com o `/weather` de GM) | `GameHandler.Lounge.cs`, `GameHandler.Gm.cs` |
| 8 | Desligar efeitos ao trocar roupa sem a peça; zerar ao sair | `QuickEquipAsync` sub 4 / saída da sala |
| 9 | Testes: layout do 0x49 sub 6 e do 0x19B, alternância 2.0/1.0, recusa sem peça, recusa fora do lounge | `tests/` |
