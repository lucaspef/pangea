# KR 645: janela de perfil — recordes por mapa (0x154 / 0x152 / 0x153) e estatística de mapa

Tags: **[C]** confirmado no cliente (decompilação Ghidra `gh/` = /root/ghidra-out, desmontagem do
`/root/rebang/tools/original/ProjectG_ReleaseQA.exe`, ou `opc.c` = decompilação de `CTask::OnPacketCommon` no scratchpad
`E--dev-rebang--claude-worktrees-keen-nobel-8c039a/.../scratchpad/opc.c`; os `case N` de lá são índices da jump table,
o id do pacote vem de `summ.txt`); **[R]** referência (fonte rebang `src/` = /root/rebang/source, servidor GB
/root/pangya-server/Server/GB); **[P]** palpite. Nada foi testado no cliente real.

## 0. Causa do bug "todos os mapas iguais e com o mesmo recorde" [C]

1. O cache por jogador (`sUserInfoTime`, 0x13FE bytes) só é criado no 0x14F/0x150 e é **zerado com memset**
   (opc.c L12536-12546, `for 0x4ff dwords = 0`). Portanto os 4 arrays `sMapStatistics[20]` do cache começam com
   `bMap = 0` e `cBestScore = 0` em todas as 20 posições.
2. O 0x154 com `n = 0` dá `break` sem copiar nada (opc.c L12780-12784: `if (uVar29 != 0) {...}`).
3. `FrUserInfoForm::UpdateEditMapStatistics` (@00647fe0, user_info.c L5659) percorre **as 20 posições** e adiciona à
   lista toda posição cujo `bMap` exista no Course.iff ativo. Com `bMap = 0` nas 20 → 20 linhas do curso 0, todas com
   recorde "0", personagem "-" e pang 0. É exatamente o sintoma.

Correção: mandar sempre **20 registros posicionais** (índice = número do curso), com `bMap = índice` e
`cBestScore = 127` onde não há recorde (detalhes no §4).

## 1. sMapStatistics (0x2B = 43 bytes, packed) [C]+[R]

`shared/globalgamedefine.h:245`; offsets confirmados pelo uso no cliente (OnScoreListOwnerDraw, SortMapStat):

| off | tipo | campo | uso no cliente |
|---|---|---|---|
| 0x00 | u8 | bMap | número do curso; `FindCourse(bMap \| 0x28000000)`; ≥0x80 (exceto 0xFD) → `bMap-0x80` |
| 0x01 | u32 | dwDrive | (não mostrado na lista) |
| 0x05 | u32 | dwPutt | |
| 0x09 | u32 | dwHole | |
| 0x0D | u32 | dwFairway | |
| 0x11 | u32 | dwHoleIn | |
| 0x15 | u32 | dwPuttIn | |
| 0x19 | i32 | iTotalScore | |
| 0x1D | i8 | cBestScore | melhor score relativo ao par, `%d`; **127 = sem recorde** (mostra "-", imagem escurecida); ordena a lista ascendente |
| 0x1E | i64 | i64MaxPang | `%I64d` |
| 0x26 | u32 | tidChar | 0 → "-"; senão imagem `bar_<nome IFF>` (ITEMS) |
| 0x2A | u8 | eventScore | não usado na tela [P] |

O construtor `sMapStatistics()` só põe `bMap = 0xFF` (src), mas o cache é zerado depois — não confie no construtor.

## 2. Layout dos pacotes de perfil [C]

Mapeamento `sUserInfoTime` (endereço do registro no `std::map`; o form recebe `+0x10`):
`+0x14 u16 roomIndex, +0x16 sPangYaUserInfo, +0x123 stat (temporada 5), +0x20E troféus (5), +0x25C sUserEquip,
+0x2C8 mapStat[20], +0x624 classicMapStat[20], +0x980 sCharacterInfo, ... +0xBA6/+0xBAA troféus especiais (5),
+0xBAE/+0xBB2 troféus de guilda (5), +0xBB6 guildPang, +0xBBA guildPoint, +0xBBE stat (temp. 0), +0xCA9 mapStat[20]
(temp. 0), +0x1005 classicMapStat[20] (temp. 0), +0x1361 troféus (0), +0x13AF/+0x13B3 especiais (0), +0x13B7/+0x13BB
guilda (0)`.

### S->C 0x154 — estatística por mapa (opc.c L12776, case 0x94)
`u8 kind, u32 uid, u16 n, n × sMapStatistics (0x2B)`.
- `n == 0` → nada é gravado (cache fica zerado → bug).
- Os n registros são lidos de uma vez e **copiados em bloco (memcpy) a partir da posição 0** do array destino; não
  são indexados por `bMap`. Posições ≥ n mantêm o que já havia (zeros).
- `kind` escolhe o destino:

| kind | destino no cache | aba do perfil |
|---|---|---|
| **5** | +0x2C8 (`mapStat`, temporada atual) | "Curso", temporada atual |
| **0x33** | +0x624 (`classicMapStat`, temporada atual) | "Clássico", temporada atual |
| **0** | +0xCA9 (temporada anterior, normal) | usado no "Total" |
| **0x0A** | +0x1005 (temporada anterior, clássico) | usado no "Total" |
| outro | ignorado | |

[R] O GB (cliente mais novo, pacote 0x15C com outro layout) usa 0x33/0x0A para "Natural" e 0x34/0x0B para Grand Prix.
No 645 o 0x33/0x0A cai no array `classicMapStat` (servidor clássico, `S5::CLASSICSRV::IsClassicServer`). Não existe
destino para Grand Prix no 645.

### S->C 0x152 — troféus especiais (opc.c L12666, case 0x92)
`u8 season, u32 uid, u16 n, n × 12 bytes` = `sSpecialTrophy {u32 guid, u32 tid, u32 count}`. season 5 → +0xBA6/+0xBAA,
0 → +0x13AF/+0x13B3, outro → ignora. `n = 0` está correto (lista vazia).

### S->C 0x153 — troféus de guilda (opc.c L12719, case 0x93)
`u8 season, u32 uid, u16 n, n × u32` = `sGuildTrophy {u32 TypeID}`. Mesma regra de season. `n = 0` está correto.

### Fluxo de temporada [C]
- `RequestSeasonData` (user_info.c L4624) manda `0x2F u32 uid, u8 season`; `CUserInfo::IsRecvedData` (L6776):
  season 0 → bit 2, season 5 → bit 4. Na aba "Total" (`m_seasonType == 1`) o cliente pede **também** a season 0.
- `FrUserInfoForm::SetInfo` (user_info.c, @0064ba30) copia do cache: `+0x2C8 → form+0x598` (atual normal),
  `+0x624 → form+0x183B` (atual clássico), `+0xCA9 → form+0xF7B` (anterior normal), `+0x1005 → form+0x1B97`
  (anterior clássico).
- `BuildTotalStat` (@0064b090) monta o "Total" **por índice**: para i = 0..19, `total[i].bMap = i`,
  `cBestScore = 127`; copia `atual[i]` se tiver recorde; troca por `anterior[i]` se este for melhor (menor). Isto
  confirma que o array é **posicional por número do curso**.
- Por isso: em resposta a 0x2F season 5 mande 0x154 kind 5 **e** kind 0x33; a season 0 mande kind 0 **e** kind 0x0A.

## 3. Como a lista é montada (`UpdateEditMapStatistics`, @00647fe0) [C]

- Arrays: season 0 (atual) → form+0x598 (Curso) / +0x183B (Clássico); season 1 (Total) → +0x142B / +0x1EF3. O botão
  Curso chama com `bool=false` (OnRecordCourseBtnUp), Clássico com `true` (OnRecordClassicBtnUp).
- Para cada uma das 20 posições: pula se `bMap == 0x11` (cmp al,0x11 @00648037; curso 17, [P] SSC/curso especial);
  normaliza (`≥0x80` e `≠0xFD` → `-0x80`); `FindCourse(n | 0x28000000)` precisa existir e ter byte 0 (ativo) ≠ 0.
  **`bMap = 0xFF` → curso 0x7F não existe → a linha some.** Depois `SortItem(ScoreListCompare)` = `cBestScore` crescente
  (127 vai pro fim).
- Desenho (`OnScoreListOwnerDraw`, L6494): imagem `CAPTION/room_<nome do curso>` (nome = `sCourse+0x31`, vem do
  Course.iff do cliente — o servidor não manda nome), escurecida (alpha 0x60) se `cBestScore == 127`; colunas
  melhor score / personagem (`tidChar`) / max pang, ou "-" quando 127.
- `SortMapStat` (@006480a0, chamado em SetContent) usa o array normal para os 5 campos "melhor score por dificuldade"
  (estrelas = `sCourse[0xE0] & 0xF`, texto `"%+d [%I64dp]"`), pulando a **posição** 0x11. Com zeros mostra "+0 [0p]".

## 4. O que o servidor deve mandar [C para o formato, P para a política]

Para cada array (normal e clássico), 20 registros, **índice i = curso i**:
- `bMap = i` (ou 0xFF para esconder a linha; o próprio cliente usa `bMap = i` no Total, então i é o natural);
- sem recorde: `cBestScore = 127`, todo o resto 0 → a linha aparece escurecida com "-" (igual ao Total do cliente);
- com recorde: preencher os campos (§5).
- Cursos inexistentes/inativos no Course.iff e o curso 17 somem sozinhos; não precisa filtrar.

Mensagens para 0x2F:
- season 5: `0x154 u8 5, u32 uid, u16 20, 20×43` + `0x154 u8 0x33, u32 uid, u16 20, 20×43`.
- season 0: `0x154 u8 0, ...20...` + `0x154 u8 0x0A, ...20...` (temporada anterior; sem dados → 20 vazios).
- 0x152/0x153 com `u16 0` estão corretos.

Também corrigir `PlayerStructs.UserInfo` (sUserInfo/sMyInfo do 0x42/salas): hoje `bMap = 0xFF` e `cBestScore = 0`.
O 0x42 copia `mapStat`/`classicMapStat` para o Doc (`Doc+0x694` / `+0x9F0`, opc.c L2770-2782), que é lido **por índice
de curso**: `Doc.mapStat[GetCurMap()].cBestScore` aparece no HUD de jogo (fontinfo.c L15389-15400, "-" se 127) e no
diálogo de novo recorde (`newrecorddlg.cpp:78` [R]). Com 0 o HUD mostra recorde "0". Usar `bMap = i, cBestScore = 127`.
A janela de perfil **não** usa o mapStat do sUserInfo (o cache só nasce do 0x14F/0x150 [C]; `CUserInfo::SetInfo`
@0064c170 só sobrescreve charInfo/userEquip do próprio jogador).

## 5. Gravação de recordes ao fim da partida

### O que o cliente manda [C]
- **C->S 0x31** (`CGolfRule::SendHoleStat` @00451960, golfrule.c L18990, chamado em `RecordScoreByPlayer` ao fim de
  cada buraco): `sPangYaUserStatistics` (0xEB) **acumulado da partida até aquele buraco** (`Doc+0x4118 + idx*0xEB`,
  zerado em `CSharedDoc::ClearGameVars`; `dwShotTime` = tempo/1000). Só em online, não para GM-observador; modos
  0/3/7 só o jogador da vez, 4/5/6/9/10 slot 0, 2/8 não manda.
- **C->S 0x06** (`CGolfRule::ChangeGameMode` fim de jogo, golfrule.c ~L23013): o mesmo bloco 0xEB do meu índice, uma
  vez por partida (flag `Doc[0x90]`), não em mass game/galeria. Hoje o servidor não trata o 0x06 de jogo e ignora o 0x31.
- Não há registro `sMapStatistics` vindo do cliente: o servidor monta.

### O que o servidor manda depois [C]
- **S->C 0x43** (opc.c L3043, case 0x11): `sPangYaUserStatistics 0xEB, sTrophyStatistics 0x4E, u8 iNormal,
  [0x2B sMapStatistics se iNormal ≠ 0xFF], u8 iClassic, [0x2B se iClassic ≠ 0xFF]` → grava em `Doc.mapStat[iNormal]` /
  `Doc.classicMapStat[iClassic]` (índice = curso). Atualiza o HUD/novo recorde; o cache do perfil só é renovado num novo
  0x2F (o cliente re-pede após 60 s, `CUserInfo::SetInfo`).
- **S->C 0x19A** (opc.c L14602): `u8 kind, u32 n, n × {u8 curso, sMapStatistics}` → `GetMyPastMapStat(kind)`
  (títulos "Course Master", `titles_client.cpp` [R]). Opcional.

### Campos a acumular por curso (normal; clássico idem em servidor clássico) [P]
Do 0x06/0x31 final da partida (`g`) e do resultado calculado pelo servidor:
- `dwDrive += g.dwDrive`, `dwPutt += g.dwPutt`, `dwHole += g.dwHole`, `dwFairway += g.dwFairway`,
  `dwHoleIn += g.dwHoleIn`, `dwPuttIn += g.dwPuttIn` (contadores que só o cliente sabe; aplicar limites de sanidade);
- `iTotalScore += score` (score = tacadas − par somado, calculado no servidor com as tacadas que ele já conta em 0x1B/0x1C);
- se `score < cBestScore` (ou 127): `cBestScore = score`, `tidChar = personagem equipado`;
- `i64MaxPang = max(i64MaxPang, pang da partida)` (pang do resultado 0x64 / do servidor);
- `eventScore` = 0.
- [P] Gravar `cBestScore` só em partida de 18 buracos completa no mesmo curso (o GB grava por `m_ri.course & 0x7F`);
  curso aleatório/shuffle → usar o curso real jogado.

## 6. Pontos em aberto
- Significado exato de `bMap ≥ 0x80` (≠0xFD) e de `eventScore` [P].
- Se o servidor oficial mandava 20 registros sempre ou só até o maior curso — o formato exige pelo menos até o último
  curso existente; mandar 20 é seguro (o buffer temporário tem 20 posições, opc.c L12786).
