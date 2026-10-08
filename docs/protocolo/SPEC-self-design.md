# KR 645 — Self Design (셀프디자인 / UCC)

Pesquisa só de leitura no cliente KR 645 QA (`ProjectG_ReleaseQA.exe`) para implementar o Self Design (roupa desenhada
pelo jogador) no servidor C#.

Marcas: **[C]** confirmado no 645 (decompile ghidra ou `pg645.asm`); **[R]** só no rebang (fonte reconstruída, pode
diferir); **[I]** inferido / sugestão.

Fontes (abreviações):
- `UDD` = `ghidra-out/uccdrawdlg.c` (FrUccDrawDlg: janela de desenho, Upload, Finish, OnProc)
- `UL` = `ghidra-out/ucclibrary.c` (IsUccClothes @0x5763b0, GenerateTextureName_F @0x577790, Zip/UnzipUccClothes,
  RequestUccInfo @0x576690/0x576790)
- `UM` = `ghidra-out/uccmanager.c` (CUccManager::AddClothes @0x57b2f0, AddDisplayClothes)
- `NRM` = `ghidra-out/netresourcemanager.c` (UploadUccClothes @0x71c3f0, UploadUccTempClothes @0x71c4f0,
  DownloadUccClothes @0x71cde0, _RealDownLoad @0x71bc60, WorkUploadThread @0x71ea80, construtor com as URLs L10320)
- `RMM` = `ghidra-out/realmyroommainui.c` (abrir desenho/cópia L33212-33296; mensagens 0x212-0x215 L35113-35420)
- `IS` = `ghidra-out/itemstorage.c` L8416 (CItemStorage::AddStoredItemInfo)
- `OPC` = `CTask::OnPacketCommon` @0x732640, só no `pg645.asm` (jump table idx = id-0x2D, bytes @0x746118, ponteiros
  @0x745E1C). Casos usados aqui: **0x71 @0x736f48**, **0x126 @0x7441a8** (sub-tabela @0x7469a4), **0x14B @0x7451c5**.
- `SKM` = `pg645.asm` CSecurityKeyManager: SecurityKeyManager() @0x566940, ReqCreateKey @0x5661c0, ResCreateKey
  @0x5667e0, ResCheckKey @0x565ef0. Rebang: `securitykeymanager.cpp`.
- Rebang: `ucccopydlg.cpp`, `uccsetinfodlg.cpp`, `uccmanager.cpp`, `netresourcemanager.cpp` L4100-4300.

---

## 0. Resumo

- O Self Design é **conteúdo local 0x57** (`IsLocalContent(0x57)`), que o servidor já liga (SPEC-room). Tudo abaixo só
  roda com ele ligado. [C]
- Peça UCC = `Part.iff` com **Category (sPart+0xB8) 7, 8 ou 9** e grupo de peça (`tid & 0xFC000000 == 0x08000000`)
  (`IsUccClothes`). No pangya.iff do servidor: **8 = "그리기용" (para desenhar, cash), 9 = "복제용" (para copiar,
  pang)**; não há categoria 7. 40 peças (§1). [C]
- O desenho é feito **dentro do cliente** (editor com pincel/caneta/borracha/cores/padrões, frente e costas). Não há
  importação de arquivo de imagem. O resultado vira um **zip com bitmaps crus**, enviado por **HTTP multipart** para
  `.../UCC/upload_one.asp` e baixado de `.../UCC/UCC_ONE/clothes/<1º char do índice>/<textura>_<índice>.jpg`. [C]
- O desenho é identificado pelo **UccIndex** (char[9], até 8 chars) do item (`sItemInfo+0x52`). Quem cria esse índice
  é o servidor. Com o índice vazio, a janela de desenho não abre ("서버로부터 필요한 정보를 얻지 못했습니다(코드:1)").
  [C]
- Pacotes no game server: C->S **0xB1** (sub 0 = registrar desenho final, 1 = pedir info de um item, 2 = copiar,
  3 = registrar salvamento temporário), C->S **0xC1** (pedir chave de upload), S->C **0x126** (respostas UCC, sub
  0-3), S->C **0x14B** (resposta da chave). O 0x126 e o 0x14B passam pelo `OnPacketCommon`, então valem no lobby, na
  MyRoom e no jogo. [C]
- Outros jogadores veem o desenho assim: o cliente acha uma peça UCC no `sCharacterInfo` (tidParts + ItemIdList), manda
  **0xB1 {1, guid, 0}**, recebe o `sItemInfo` do item no **0x126 sub 1** e baixa o zip por HTTP. [C]
- Hoje o servidor não trata 0xB1 nem 0xC1 e manda as peças UCC com UccIndex vazio. Os pedidos 0xB1 sem resposta não
  travam nada; quem só quer evitar a mensagem de erro precisa pelo menos gerar o UccIndex (§6). [C]/[I]

---

## 1. Itens Self Design (Part.iff) [C]

`IsUccClothes(tid)` (`UL` @0x5763b0): `(tid & 0xFC000000) == 0x08000000` e `FindPart(tid)->Category` (byte @0xB8 do
sPart) ∈ {7, 8, 9}. A mesma regra está inline em `RequestUccInfo(sCharacterInfo&)` e no caso 0x71 do `OPC`.

A UI (`RMM` L33229-33296) usa a categoria assim:
- **7 ou 8** e `status & 1 == 0` (ainda não desenhado): o clique na área de desenho do ícone abre `ucc_draw`
  (FrUccDrawDlg).
- **7 ou 9** e `status & 1 == 0`: o clique na área de cópia abre `ucc_copy` (FrUccCopyDlg).

Itens no pangya.iff do servidor (`data/pangya.iff`, todos `InStock`=1, sem tempo):

| Personagem | Desenhar (cat 8, cash) | Copiar (cat 9, pang) | Textura base (Tex[0]) |
|---|---|---|---|
| Nuri (상의/하의) | 0x0800602F / 0x0800A020 | 0x08006033 / 0x0800A021 | M_TS_u01f-01.jpg / M_PV_u01f-01.jpg |
| Hana | 0x08044058 / 0x08048019 | 0x08044059 / 0x0804801A | F_TS_u01f-01.jpg / F_PV_u01f-01.jpg |
| Azer | 0x08084027 / 0x0808A019 | 0x0808402B / 0x0808A01A | A_TS_u01f-01.jpg / A_PV_u01f-01.jpg |
| Cecilia | 0x080C403C / 0x080CA018 | 0x080C403F / 0x080CA019 | C_TS_U01f-01.jpg / 2#C_PV_U01f-01.jpg |
| Max | 0x08104038 / 0x08108025 | 0x08104039 / 0x08108026 | D_TS_u01f-01.jpg / D_PV_u01f-01.jpg |
| Kooh | 0x08144050 / 0x0814A01E | 0x08144053 / 0x0814A01F | E_TS_U01f-01.jpg / 2#E_PV_U01f-01.jpg |
| Arin | 0x0818405A / 0x08188013 | 0x0818405B / 0x08188014 | G_TS_u01f-01.jpg / G_PV_u01f-01.jpg |
| Kaz | 0x081C402B / 0x081CA017 | 0x081C402E / 0x081CA018 | H_TS_U01f-01.jpg / H_PV_U01f-01.jpg |
| Lucia | 0x08204007 / 0x0820A003 | 0x08204008 / 0x0820A004 | 2#I_TS_U01f-01.jpg / 2#I_PV_U01f-01.jpg |
| Nell | 0x08244002 / 0x0824A002 | 0x08244003 / 0x0824A003 | j_ts_u01f-01.jpg / 2#j_pv_u01f-01.jpg |

Nomes: "그리기용상의1(누리)" etc. Preço: cat 8 em cash (23-32), cat 9 em pang (21 000-34 000).

"Mesma roupa" (`IsSameClothes`, `UL` @0x576400): `Data`, `Tex[0..2]` e `OrgTex[0..2]` iguais (sem diferenciar
maiúsculas). Cada par desenhar/copiar do mesmo personagem e posição é a mesma roupa, então os dois usam o mesmo arquivo
de desenho. [C]

### 1.1 Campos do sItemInfo (0xA8) usados pelo UCC [C]

| off | campo | uso no UCC |
|---|---|---|
| 0x00 | guid | id do item |
| 0x04 | tid | typeid |
| 0x18 | ItemDate (SYSTEMTIME) | data do desenho (vai para `sUccClothes.date`) |
| 0x29 | ItemName[41] | **nome do desenho** (`uccName`, mostrado no tooltip e na cópia) |
| 0x52 | UccIndex[9] | **índice do desenho** (máx. 8 chars + NUL). Vazio = sem desenho possível |
| 0x5B | status (u8) | bit0 = **desenho registrado** (final); bit1 = **tem salvamento temporário** |
| 0x5C | Seq (u16) | nº de série do desenho. A lista de origens da cópia só mostra itens com `status&1` e `Seq == 1` [R] |
| 0x5E | CopierNick[22] | nick de quem copiou (o cliente preenche com o próprio nick depois da cópia) |

Os mesmos campos (UccIndex, Seq, status, CopierNick, ItemName) existem em `sTradeItem` (loja pessoal/troca),
`sMailIncludeItem.csUCCIndex` (correio), `sBuyItemResult.UccIndex` (compra) e `sStoredItemInfo` (armazém: UccIndex
@0x2F, Seq @0x38, status @0x3A, nome @0x6F; `IS` L8460-8475). [C] O `sCharacterInfo` tem `UccIndexList[24][9]`
@0x84, mas o 645 não o usa para desenhar: ele usa `ItemIdList[24]` @0x15C (guid) + o 0xB1. [C]/[R]

### 1.2 Onde o cliente guarda os itens UCC [C]

Com 0x57 ligado, peças UCC **não** entram na lista normal de peças (`AddMyPartsList`), e sim em `Doc+0x12A0`
(`m_uccItemList`, `std::list<sItemInfo>`):
- 0x71 (lista de itens do login, `OPC` @0x736fcd-0x737847): se `status & 1`, cria um `sUccClothes{id=guid, typeId,
  uccIndex, name=ItemName, date=ItemDate}` e chama `AddClothes(slot=1, bSend=true)`: isso **baixa o zip** e **manda
  0xB1 {1, guid, 1}**. Se só `status & 2`, chama `DownloadUccTempClothes(tid, uccIndex, Seq)`. Depois faz push_back em
  m_uccItemList.
- Item novo (`CTaskManager::AddNewItemToDoc` @0x731400): também vai para m_uccItemList.
- Correio (`realmyroomtask.c` L6740): item UCC anexado manda 0xB1 {1, id, 1}.
- Armazém (`IS`): item UCC guardado vira sItemInfo e chama `AddClothes(slot 0, bSend=true)` (manda 0xB1 {1, id, 0}).

---

## 2. O que o jogador faz (UI) [C]

Tudo fica na **MyRoom** (`CRealMyRoomMain`, abas de peças 0x15/0x1A).

1. **Desenhar** (item cat 8 sem desenho): abre `ucc_draw`. `SetUccDrawInfo(mode 0, item)` (`UDD` @0x573340) exige
   `tid != 0`, `UccIndex` não vazio e `IsUccClothes(tid)`. Senão aparece "서버로부터 필요한 정보를 얻지 못했습니다(코드:1)"
   e a janela não abre. Carrega a textura base `Tex[0]` da peça e a máscara `<tex>_mask.<ext>`. Se o item tem
   `status & 2` e o temporário já foi baixado, começa a partir dele. Ferramentas: pincel, caneta, borracha, conta-gotas,
   balde, zoom, grade, virar, desfazer/refazer, cores e padrões; frente e costas (`side1`/`side2`). As abas
   emoticon/texto são funções vazias.
2. **Salvar temporário** (`OnTempSaveBtnUp`, Ctrl+SAVE): C->S 0xC1 (pede chave) e abre a espera "셀프디자인 의상을
   임시 저장중입니다.".
3. **Completar** (`OnFinishBtnUp`): abre `ucc_setinfo` (nome do desenho, regras de nick: 4-16 letras latinas ou 2-8
   hangul, até 40 bytes [R]). No OK: C->S 0xC1 e espera "셀프디자인 의상을 업로드 중입니다.".
4. Chave recebida (0x14B ok) -> msg 0x212 -> `FrUccDrawDlg::Upload` (`UDD` @0x574810): grava o zip em
   `capture\<nome>` e põe na fila de upload HTTP (§4).
5. HTTP OK -> C->S 0xB1 sub 0 (final) ou sub 3 (temporário) -> S->C 0x126. Final ok: "셀프디자인 의상이
   완성되었습니다." e a janela fecha. Falha: "셀프디자인 의상 등록에 실패했습니다." / "셀프디자인 의상의 임시 저장에
   실패했습니다.".
6. **Copiar** (item cat 9 sem desenho): `ucc_copy` lista os meus itens de m_uccItemList com `status&1`, `Seq==1` e
   mesma roupa [R]. A confirmação manda 0xB1 sub 2 (`pg645.asm` @0x56eba0). A resposta 0x126 sub 2 abre
   `ucc_copy_completed` ("성공적으로 복제되었습니다.") ou mostra "셀프디자인 의상 복제에 실패했습니다.".

Espera do upload (`UDD` OnProc @0x572df0): enquanto `this[0x11C]` está ligado, aparece a janela "wait" sem
cancelar. **Depois de 20 s** o cliente consulta `IsFailedToUpload`. Se o upload falhou ou nunca foi para a fila,
mostra "업로드가 실패했습니다. 다시 시도해주세요.(코드:2)" e destrava. Não fica preso para sempre. [C]

---

## 3. Pacotes

Strings = `u16 len + bytes` cp949 (EncodeStr/DecodeStr). Ints little-endian.

### 3.1 C->S 0xC1 — chave de segurança (CSecurityKeyManager) [C]

| sub | layout | quando |
|---|---|---|
| 0 | `u8 0, u32 myUID, u8 content(=1 UCC), u32 itemGuid` | ReqCreateKey: antes de todo upload UCC (temporário ou final). `itemGuid` = guid do item que está sendo desenhado |
| 1 | `u8 1, u32 myUID, u8 content, u32 index, str key` | ReqCheckKey (o fluxo UCC não chama) |

### 3.2 S->C 0x14B — resposta da chave [C] (`OPC` @0x7451c5)

`u8 sub, u8 content, u32 index, str key, u8 result`.
- sub 0 -> `ResCreateKey(content, index, key, result)` @0x5667e0: se `result == 1`, guarda a chave (content, index);
  se também `content == 1`, manda msg **0x212** ao actor "RealMyRoom" -> `FrUccDrawDlg::Upload()`. Com
  `result != 1` não faz nada (a espera cai no timeout de 20 s).
- sub 1 -> `ResCheckKey` (vazio no rebang).
- O `index` deve ser o `itemGuid` do pedido: a chave é lida no upload com `GetKey(1, item.guid)`.

### 3.3 C->S 0xB1 — UCC [C]

| sub | layout | origem |
|---|---|---|
| 0 | `u8 0, u32 typeId, str uccIndex, str name` | WorkUploadThread @0x71f396, depois do HTTP OK do upload **final** (type 0). Atenção: é o **typeId**, não o guid |
| 1 | `u8 1, u32 itemGuid, u8 flag` | pedido de info: `AddClothes(bSend)` (flag = slot: 1 no login/correio/fim do desenho, 0 no armazém), `RequestUccInfo(sCharacterInfo)` (flag 0, para peças de **outros** jogadores: ficha, sala, lobby, avatar, ranking), `AddDisplayClothes` (flag 0) |
| 2 | `u8 2, u32 srcTypeId, str srcUccIndex, u16 srcSeq, u32 targetGuid` | confirmação da cópia (@0x56eba0) |
| 3 | `u8 3, u32 typeId, str uccIndex` | WorkUploadThread @0x71f20b, depois do HTTP OK do upload **temporário** (type 4) |

### 3.4 S->C 0x126 — respostas UCC [C] (`OPC` @0x7441a8, `u8 sub` e salto pela tabela @0x7469a4)

**sub 0 — desenho final registrado**
`u8 0, u8 result, u32 itemGuid, u32 typeId, str uccIndex(≤8), str name(≤40)`
-> msg 0x214 a RealMyRoom (`RMM` L35185). Só age se a janela de desenho está aberta com `typeId` e `uccIndex` iguais
aos do item (senão é ignorado e a espera vence em 20 s). `result != 0`: põe as texturas no modelo, cria
`sUccClothes{id=itemGuid, typeId, uccIndex, name, date=agora}` e chama `AddClothes(slot 1, bSend=true)`, que **manda
0xB1 {1, itemGuid, 1}**. Nos itens de m_uccItemList com mesmo tid+índice faz `status = (status & ~2) | 1`, mostra
"셀프디자인 의상이 완성되었습니다." e fecha a janela. `result == 0`: "셀프디자인 의상 등록에 실패했습니다.".

**sub 1 — info de um item (resposta do 0xB1 sub 1)**
`u8 1, u32 (ignorado), str (ignorado, ≤8), u8 flag, sItemInfo[0xA8]`
- `flag == 1`: se m_uccItemList não tem item com mesmo tid + UccIndex + Seq, faz push_back e manda msg 0xE1 à
  RealMyRoom (redesenha a lista).
- Depois, se `item.status & 1`: se já existe `sUccClothes` para `item.guid`, só atualiza date e nome (`ItemName`). Se
  não existe, cria `{id=guid, typeId=tid, uccIndex, name, date}` e chama `AddClothes(slot=flag, bSend=false)`, que
  **baixa o zip** (§4.3).
- `status & 1 == 0`: nada para baixar.
- Sugestão [I]: devolver no u32/str o guid e o índice pedidos, e o `flag` igual ao recebido.

**sub 2 — resultado da cópia**
`u8 2, u32 srcTypeId, str srcUccIndex, u16 srcSeq, u32 targetGuidOld, u32 targetGuidNew, u32 targetTypeId,
str newUccIndex, u16 newSeq, u8 result`
- `result == 1`: `AddClothes(slot 1, {id=targetGuidNew, typeId=targetTypeId, uccIndex=newUccIndex}, bSend=false)`,
  abre `ucc_copy_completed`. No item de m_uccItemList com `guid == targetGuidOld` faz: guid = targetGuidNew,
  tid = targetTypeId, UccIndex = **srcUccIndex**, Seq = newSeq, `status = (status & ~2) | 1`, CopierNick = meu nick,
  ItemName = nome do item de origem (ou "정보를 받아오는 중..."). Depois manda msg 0xE1.
- `result != 1`: caixa "셀프디자인 의상 복제에 실패했습니다.".
- Sugestão [I]: mesmo guid em old/new, targetTypeId = tid do item de cópia, newUccIndex = srcUccIndex.

**sub 3 — salvamento temporário registrado**
`u8 3, u32 typeId, str uccIndex, u8 result`
- `result == 1`: nos itens de m_uccItemList com mesmo tid+índice faz `status |= 2`.
- Sempre manda msg 0x215: se a janela está aberta com o mesmo tid/índice e `result == 1`, guarda a textura temporária
  e marca como salvo. Senão: "셀프디자인 의상의 임시 저장에 실패했습니다.".

---

## 4. HTTP

### 4.1 URLs no exe [C] (`NRM` L10320-10360; para trocar pelo `tools/client/make_client.py`, como as da guilda)

| uso | string | VA | tamanho (sem NUL) | cópia no construtor |
|---|---|---|---|---|
| download (type 0; type 4 copia a mesma) | `http://qa.contents.pangya.gametree.co.kr:50006/UCC/UCC_ONE/clothes/` | 0xA13A98 | 67 | 68 bytes |
| upload (type 0; type 4 copia a mesma) | `http://qa.contents.pangya.gametree.co.kr:50006/UCC/upload_one.asp` | 0xA13A50 | 65 | 66 bytes |

Substituições sugeridas [I]: `http://<ip>:30080/UCC/UCC_ONE/clothes/` e `http://<ip>:30080/UCC/upload_one.asp`
(cabem). O type 4 (temporário) usa as **mesmas** URLs: o construtor copia `m_download[0]` para `m_download[4]` e o
endereço de upload [4] = [0]. [C] para o download; [R] para o upload.

### 4.2 Nome do arquivo [C] (`UL` GenerateTextureName_F @0x577790)

`lower( semExt( FilterFilename(part.Tex[0]) ) + "_" + uccIndex + ".jpg" )`. FilterFilename tira os caracteres de
`[]{}`~!@#$%^&()-+=\|<>/?` [R]. Exemplos: `M_TS_u01f-01.jpg` + `a1b2c3d4` -> `m_ts_u01f01_a1b2c3d4.jpg`;
`2#C_PV_U01f-01.jpg` -> `2c_pv_u01f01_<idx>.jpg`. Como o par desenhar/copiar tem o mesmo Tex[0], o arquivo é o mesmo.

Cache local do upload: `<pasta do jogo>\capture\<nome>` (`GetUccTempFoler` = "capture"), apagado depois do envio. O
download fica em memória (texturas + mapa de ícones), sem cache em disco [C]. O ícone se chama
`<base>_<idx>_i.jpg` e é só uma chave interna. [R]

### 4.3 Download [C] (`NRM` _RealDownLoad, casos 0/1 e 4)

`GET <url_base> + uccIndex[0] + "/" + <nome do arquivo>`, por exemplo
`.../UCC/UCC_ONE/clothes/a/m_ts_u01f01_a1b2c3d4.jpg`. O **diretório é o 1º caractere do índice SEM lower**, e o nome
do arquivo é lower. Use índices em minúsculas/dígitos.

O corpo é um **zip** (ZIP_MEMORY do XUnzip), não um JPEG:
- `front`, `back`: pixels crus do Bitmap com o tamanho da textura base da peça (largura × altura de `Tex[0]`).
  **24 bpp no final** (type 0), **32 bpp no temporário** (type 4). O tamanho da entrada tem que ser exatamente
  `bpp/8 × W × H`, senão o unzip falha.
- `icon` (só no final): 64 × 84, 32 bpp (ícone do inventário).
- `header` (opcional, `sUccHeader` 268 bytes: `u32 flags, u32, u32, u8[256]`): se `flags & 1`, o cliente troca o
  desenho pela "roupa de aviso" (`LoadWarningClothes`). Serve para o servidor **bloquear** um desenho (moderação) sem
  apagar o item. O cliente nunca manda essa entrada.
- Falha (HTTP ou zip inválido) = a peça fica com a textura padrão, sem erro na tela e sem crash. [C]/[I]

### 4.4 Upload [C] (`NRM` WorkUploadThread @0x71ea80, L9700-9800)

`POST <upload_one.asp>`, HTTP/1.0, `User-Agent: MERONG(0.9/;p)`, multipart (mesmo GenericHTTPClient da guilda,
boundary `--MULTI-PARTS-FORM-DATA-BOUNDARY`). Antes, para type 0/4, o cliente acha o item com
`GetMyItemInfo(typeId, uccIndex)`. Se não acha, mostra erro e não envia. Campos:

| campo | valor |
|---|---|
| `ucctype` | `clothes` |
| `uccfile` | arquivo (binário); o filename é o caminho local completo `...\capture\<nome>.jpg` |
| `userid` | id de login (`Doc+0x3E2`) |
| `type` | `1` (sempre, também no temporário) |
| `item_id` | guid do item (decimal) |
| `key` | chave recebida no 0x14B para esse guid |
| `uid` | MyUID (`Doc+0x4EB`, decimal) |

Resposta: o corpo tem que **começar com** `PANGYA_UPDATE_OK`. Qualquer outra resposta manda a msg 0x213 com o texto:
o cliente compara (sem diferenciar maiúsculas) com `"" , ERR_FILE, ERR_UCC, ERR_KEY, ERR_USER, ERR_SRVVAR` (códigos
0-5) e mostra "업로드가 실패했습니다. 다시 시도해주세요.(코드:N)", fechando a espera. Use esses textos nas recusas.

Tamanho: o zip tem 2-3 bitmaps crus comprimidos (centenas de KB descomprimido). **O Kestrel do `Pangya.Web` hoje
limita o corpo a 8 KB** (`WebServer.cs` L31), então o endpoint UCC precisa de um limite próprio (por exemplo 2 MB
com `IHttpMaxRequestBodySizeFeature`). [I]

---

## 5. Cuidados para não travar nem dar crash

1. **UccIndex não vazio** em toda peça UCC mandada ao cliente (0x71, compra, correio, armazém, troca). Sem ele não há
   crash, mas o desenho dá "코드:1" e a cópia/download não funcionam. [C]
2. **Responder o 0xC1 com 0x14B result 1** e `index = guid`. Sem isso a janela fica em "업로드 중" por 20 s e cai no
   erro (코드:2). [C]
3. No 0x126 sub 0 e sub 3, `typeId` e `uccIndex` têm que ser **exatamente** os do item aberto na janela, senão a
   resposta é ignorada e a janela espera o timeout. [C]
4. 0x126 sub 1: o `sItemInfo` tem que ter 0xA8 bytes completos. Com `status & 1` e o arquivo ausente só não aparece o
   desenho. [C]/[I]
5. Strings: UccIndex ≤ 8 chars (o cliente faz strncpy 9 sem garantir o NUL; com 9 chars fica sem terminador), nome
   ≤ 40. [C]
6. Não mande 0x126 sub 2 com `targetGuidOld` que o cliente não tem: ele só não acha o item, mas a janela de cópia
   concluída abre do mesmo jeito. [C]
7. O 0xB1 sub 1 chega também para itens de **outros** jogadores (flag 0). Responda com o sItemInfo do dono real, ou
   ignore se o guid não existir. Não é preciso checar o dono. [I]

---

## 6. Plano mínimo no servidor [I]

### 6.1 Dados
- `items.attrs` (jsonb, sem migração de coluna): `ucc_idx` (string 8, minúsculas/dígitos, único no servidor),
  `ucc_status` (bit0 final, bit1 temporário), `ucc_seq` (1 = original desenhado; cópias 2, 3...), `ucc_name`,
  `ucc_copier`, `ucc_date`.
- Gerar `ucc_idx` ao **criar** qualquer item com `IsUccClothes(tid)` (loja, presente, correio, GM `give-parts`). No
  login, preencher os que estão sem índice (itens antigos) e gravar. Sugestão: sequência global em base36/hex com 8
  dígitos (`object_id_seq` serve).
- Opcional: tabela `ucc_designs(idx text pk, account_id, type_id, name, file text, blocked bool, created_at)` para
  moderação. Sem ela, o arquivo em disco e os attrs bastam.
- Chaves de upload: só em memória, `(accountId, itemGuid) -> (key, expira em ~5 min)`.

### 6.2 `PlayerStructs.ItemInfo` (`src/Pangya.Protocol.KR645/Game/PlayerStructs.cs`)
Para peças UCC: `UccIndex = ucc_idx`, `status = ucc_status`, `Seq = ucc_seq`, `ItemName = ucc_name`,
`CopierNick = ucc_copier`, `ItemDate = ucc_date`. Opcional: `Character()` preenche `UccIndexList[i]` com o índice
das peças equipadas.

### 6.3 Game server (`src/Pangya.Protocol.KR645/Game/GameHandler.Ucc.cs`)
- **0xC1 sub 0** (`content == 1`, item do jogador, UCC, cat 7/8, `status & 1 == 0`): gerar a chave e responder
  `0x14B {0, 1, guid, key, 1}`. Item inválido: `result 0`.
- **0xB1 sub 1**: achar o item pelo guid em qualquer jogador (online ou no banco). Responder
  `0x126 {1, guid, ucc_idx, flag, sItemInfo}`. Guid inexistente: não responder.
- **0xB1 sub 3** (temporário): item do jogador por `typeId` + `ucc_idx`, upload temporário recebido (§6.4). Fazer
  `status |= 2` e responder `0x126 {3, typeId, idx, 1}`. Falha: `result 0`.
- **0xB1 sub 0** (final): mesma checagem, upload final recebido, nome válido (filtro de nick). Gravar `status = 1`,
  `seq = 1`, `name`, `date = agora` e responder `0x126 {0, 1, guid, typeId, idx, name}`. O cliente depois manda
  `0xB1 {1, guid, 1}`; responder pelo caminho do sub 1.
- **0xB1 sub 2** (cópia): origem = item do jogador com `tid == srcTypeId`, `ucc_idx == srcUccIndex`,
  `seq == srcSeq == 1`, `status & 1`. Alvo = item do jogador, cat 7/9, `status & 1 == 0`, mesma roupa
  (`IsSameClothes`: Data/Tex/OrgTex iguais). Alvo recebe `ucc_idx = src`, `status = 1`, `seq = próximo nº de cópia
  dessa origem (≥ 2)`, `name = src`, `copier = nick`. Responder `0x126 {2, srcTid, srcIdx, srcSeq, guid, guid,
  tidAlvo, srcIdx, novoSeq, 1}`. Falha: o mesmo com `result 0`. O arquivo não muda, porque o nome de arquivo é o
  mesmo para a mesma roupa.

### 6.4 Web (`src/Pangya.Web/WebServer.cs`, no estilo do emblema da guilda)
- `POST /UCC/upload_one.asp` (limite de corpo próprio, por exemplo 2 MB): ler `item_id`, `key`, `uid`, `uccfile`.
  Validar a chave (do mesmo uid/guid, não expirada, uso único). O item tem que ser do uid e UCC. O filename (basename,
  minúsculas) tem que bater com `GenerateTextureName_F(tid, ucc_idx)` (§4.2). O arquivo tem que ser um zip válido com
  `front`/`back`. Gravar em `<dados>/UCC/clothes/<idx[0]>/<nome>` e marcar o pendente (final ou temporário, pelo bpp:
  tamanho de `front` = W×H×3 ou ×4) para o 0xB1 sub 0/3 conferir. Responder `PANGYA_UPDATE_OK`; erros:
  `ERR_KEY`/`ERR_FILE`/`ERR_UCC`/`ERR_USER`.
- `GET /UCC/UCC_ONE/clothes/{d}/{file}`: servir o arquivo (`application/octet-stream`). Validar `{d}` = 1 char
  alfanumérico e `{file}` só `[a-z0-9_]+\.jpg` (sem path traversal). Desenho bloqueado: servir o zip com `header`
  `flags = 1`.
- `tools/client/make_client.py`: acrescentar as duas URLs de §4.1 (download ≤ 67 chars, upload ≤ 65 chars).

### 6.5 Resposta mínima para destravar (sem HTTP ainda)
1. Gerar `ucc_idx` e mandar no 0x71/0xAA: a janela de desenho abre.
2. Responder 0xB1 sub 1 com o `sItemInfo` do item: outros jogadores e o próprio jogador ficam com o item certo
   (sem desenho enquanto não houver arquivo).
3. Responder 0xC1 com 0x14B result 1. Sem o endpoint HTTP, o POST falha e o cliente mostra "코드:0" e destrava,
   sem crash.

---

## 7. Pontos em aberto
- Lista de origem da cópia (`status&1 && Seq==1`) e regras do nome só vistas no rebang (`ucccopydlg.cpp`,
  `uccsetinfodlg.cpp`); o FrUccCopyDlg não está no decompile do 645. [R]
- Como a loja trata a compra de item UCC (`sBuyItemResult.UccIndex` no 0xAA): não seguido até o fim. Suposição:
  preencher o índice novo ali. [I]
- Categoria 7 (desenhar + copiar no mesmo item) existe no código, mas não tem nenhum item no pangya.iff atual.
- `AddDisplayClothes` / `UCCDISPLAY` (vitrine de concurso de UCC) não foi estudado; não é preciso para o fluxo básico.
