# Bot: tacadas especiais, equipamento e dificuldade (KR 645 QA)

Pesquisa somente leitura (2026-10-07). Fontes: decompile `\\wsl.localhost\Ubuntu\root\ghidra-out` (arquivo:linha),
`/root/pg645.asm` (endereços), fonte de referência `/root/rebang/source` (cópias do binário: rival.cpp, shotpreview.cpp,
spin.cpp, golfrulebase.cpp, golfdoc.cpp, shared/itemmanager.cpp), servidor GB (`/root/pangya-server/Server/GB`), dados
`data/pangya.iff` (dump com um script Python descartável).

Legenda: **[C]** confirmado no cliente (decompile/asm), **[R]** referência (rebang/GB/emulador), **[P]** palpite.

Complementa `SPEC-ingame.md` (seções "Bot" e "Power shot and special shots"); o que já estava lá não é repetido.

---

## 1. O bloco de 46 bytes visto por quem SIMULA a tacada de outro jogador

O receptor (cada cliente, inclusive para o bot) copia o bloco em msg 0x7a (golfrule.c:30297-30331) e simula tudo em
`ChangeGameMode(0x40)` (golfrule.c:23414+). Campos relevantes **[C]**:

| off | vai para | efeito na simulação |
|---|---|---|
| +0x00 | doc+0xd0 (barra de força) | r = (barra−140)/360; velocidade × √r (golfrule.c:23460) |
| +0x04 | doc+0xd4/d8/dc (impacto) | só conta se fase 2/3 (desvio, ver §3) |
| +0x08 | **ball+0x34 = m_initCurve** (curva, não "spin x") | yaw inicial −curva×20° (BallSpeedFactor, golfrule.c:10783, const 0x3eb2b8c2 = 0,349 rad); velocidade ×(1+0,1×\|curva\|); força lateral em voo = clube.curve × m_curve × 0,75 (ApplyForce quadtree.c:4687) |
| +0x0C | **ball+0x38 = m_initSpin** (spin; >0 = backspin) | sustentação em voo = clube.spin × m_spin × 3; decaimento do spin (0,5 − initSpin×0,1)/s; quique/rolagem |
| +0x10 | doc+0xe0 (fase 1 bad, 2 normal, 3 good, 4 pangya) | ver §3; **o receptor não recalcula a fase** |
| +0x11 | ball+0x44 (flags especiais) | ver §2 |
| +0x19 | doc+0x10c mira | |
| +0x25 | CClub::SetClub(índice) | não zera o power shot (club.c:1688) |
| +0x26 | **CClub+100 = "RandomPower"** (CClub::SetRandomPower @0043e020) | só soma no GetPower/GetRange **se houver power shot armado**; o bot deve mandar 0 |
| +0x2A | CGolfRule+0x278 | soma na mira (BallSpeedFactor param_3) |

Faixa válida de curva/spin **[C]** (spin.cpp:98-100, msg 353 em spin.cpp:444): o jogador clica num ponto (x, y) dentro de
uma elipse de raios (stat curve, stat spin) em pixels; o cliente manda curva = x/30, spin = y/30. Logo
**\|+0x08\| ≤ curve/30, \|+0x0C\| ≤ spin/30** e (x/curve)² + (y/spin)² < 1. O receptor não valida **[P]** (BallSpeedFactor usa
ball+0x34/0x38 direto), mas o bot deve respeitar para parecer legítimo. Power Curve troca a curva por ±1,5 (ver §2).

Velocidade inicial **[C]** (golfrule.c:23460-23475):
`v = CClub::GetPower() × √r × ball.GetPowerFactor() × GetBarClickPowerFactor()`, com
GetPowerFactor = **1,3 se flags & 0x50 (Tomahawk ou Spike)**, senão 1 (golfball.h:61, golfrule.c:34) e
GetBarClickPowerFactor = √(MaxPowerOnGround do lie) na fase 4 (golfrule.c:6987; fases 2/3 interpolam com o MinPower).

---

## 2. Tacadas especiais

### 2.1 Power shot (simples / duplo) — fora do bloco

- Receptor **[C]**: S->C **0x56 u32 guid, u8 tipo** → golftask.c case 0x56 (ignora o próprio guid e quem não está na sala)
  → Player msg 0x11f (golfplayer.c:11181) → `CClub::SetPowerShot(tipo)` (golfrule.c:10751) + animação de carga do jogador
  da vez. **Sem checagem de gauge no receptor.**
- Tipo → extra de alcance **[C]** `CClub::GetPowerShotFactor` @004749e0: tabela `{0, 10, 20, 15}` jardas
  (1 = simples +10, 2 = duplo +20, 3 = item +15; o item 0x18000027 força o tipo 3, golfplayer.c:8405).
- O power shot é zerado em `DoToDefaultCamera` (golfrule.c:28875: club+0x2c/+100/+0x68 = 0), i.e. no fim de cada tacada,
  antes do 0x1C. **Por isso o 0x56 do bot tem de chegar depois que os clientes entraram no turno do bot (depois do 0x61/0x51)
  e antes do 0x53.** Hoje o bot espera ~4 s no turno: mandar 0x56 ~1 s antes do 0x53 (tempo da animação) **[P]**.
- Efeito no alcance **[C]** `CClub::GetRange` @00476600 e `GetPower` @00476950 (club.c, ver §4): madeiras **e ferros**
  ganham +10/+20 jardas; PW/SW nos modos de aproximação: 30 → 60 (modos 0-2) e 60 → 80 (modo 3).
- Gauge: cada cliente desconta 33 (tipo 1) / 66 (tipos 2/3) do gauge do jogador da vez e prende em 0 (SPEC-ingame.md
  "Gauge"). O receptor não recusa a tacada se o gauge do bot for insuficiente; para o bot ser "honesto" o servidor deve
  espelhar o gauge (regras do SPEC-ingame) e só usar PS com ≥33/≥66.

### 2.2 Flags de +0x11 (ball+0x44)

| flag | nome | pré-requisitos no ATIRADOR **[C]** (CheckSpecialShotSpinAndCurve @00444b80, golfrule.c:8869) | física no RECEPTOR **[C]** |
|---|---|---|---|
| 0x10 | Tomahawk | power shot armado; qualquer taco (não putter); fase 2/3/4 | velocidade ×1,3; voo normal (vento ×0,02); marca m_tomahawkFrame quando perde o flag (golfrule.c:23887) |
| 0x20 | Cobra | power shot + **madeira (tipo 0)** | frame 1: vy = 0, rasante sem nenhuma força (nem gravidade, ApplyForce:4760); quando a distância horizontal passa de `(GetRange × √r − 100) × 3,2` sobe: v = dir × {74, 76, 80}[1W/2W/3W] × √r, spin = 2,5 (golfrule.c:23891-23935) |
| 0x40 | Spike | power shot + **madeira** | v ×1,3; frame 1: vy = 0, depois v = dir × 72,5 × 2√r, spin = 3,1; 60 frames após o topo mergulha: vy = −8 − (344 − min(P√r, 344)) × 0,1917 (1W; 2W −10,3/307/0,1949; 3W −10,8/273/0,2019), v ×7√r, spin = initSpin (golfrule.c:23938-24015); vento pela metade (×0,01); curva por inclinação ×0,15 em vez de ×0,5 |
| 0x01 / 0x02 | Power Spin top / back | **fase 4**; spin < 0 (top) / spin > 0 (back) | rotação do spin com initSpin × 62,8 em vez de m_spin (quadtree.c:18547) **[P: efeito principal na rolagem]** |
| 0x04 / 0x08 | Power Curve esq. / dir. | **fase 4**; curva exatamente −1 / +1 (borda da elipse) | o atirador troca a curva por **−1,5 / +1,5** antes de mandar (golfrule.c:8955-8964) |
| combinações | ex. Tomahawk+Power Spin | idem, somadas | idem |

Também **[C]**: se a fase é 1 (bad), o receptor zera m_curve/m_spin (golfrule.c:23692) e BallSpeedFactor zera curva/spin;
flags com fase 1 não fazem sentido. O 0x12 do humano vem seguido de C->S 0x42 (lista de teclas) → S->C 0x9A só para o
cut-in (SPEC-ingame); **o bot não precisa mandar 0x9A** **[P]** (só perde a animação de corte).

### 2.3 Sequência recomendada para uma tacada especial do bot

```
(turno do bot já ativo nos clientes: 0x61 / 0x51 recebidos)
S->C 0x56  u32 guidBot, u8 1|2          -- arma o PS em todos os clientes (animação de carga)
  ~0,5-1,5 s
S->C 0x53  u32 guidBot + bloco(+0x10=4, +0x11=flags, +0x25=madeira p/ Cobra/Spike, +0x26=0) + trailer
(clientes simulam e mandam 0x1B / 0x1C como hoje)
```
Sem 0x56, Tomahawk/Spike ainda ganham ×1,3 na velocidade (o receptor não limpa as flags), mas sem os +10/+20 jardas e sem
a animação; não recomendado **[C/P]**.

---

## 3. Fase e impacto: o erro "natural" que o próprio cliente aplica

BallSpeedFactor (golfrule.c:10783) e GetBarClickPowerFactor (golfrule.c:6987) **[C]**, com N = GetNarrowAccuracy e
W = GetWideAccuracy do jogador da vez (club.c:3203/3232: N = max(2, ⌈acc⌉), W = min(N+10, 35), acc = stat de precisão +
ground_accuracy[lie]):

- **fase 4**: yaw −curva×20°; força × √MaxPower(lie).
- **fase 3**: yaw −curva×20°; curva −= 0,2 × (impacto − centro)/(N...) (desvio proporcional ao erro de impacto); força
  interpolada entre MaxPower e MinPower do lie.
- **fase 2**: curva = 0,2 fixo, spin = 0; yaw ± (|impacto−140| − N)/(W − N) × 10°; força × √MinPower (forte em rough).
- **fase 0/1**: curva = spin = 0, tacada "topada" (vetor fixo, GetInitBallState).

Como **todos os clientes usam a mesma fase/impacto do bloco e os mesmos stats do bot**, gerar o erro do bot via +0x04/+0x10
é determinístico e coerente entre clientes, e é exatamente como o oponente de computador do cliente erra (§5).

---

## 4. Alcance em função do equipamento (o que o cliente usa para o bot)

### 4.1 Os stats vêm do 0x74 (sUserInfo do bot) **[C]**

`CGolfRuleBase::AddPlayer(index)` → `CGolfDoc::SetPlayerLevel(index)` (golfrulebase.cpp:217, golfdoc.c:13314 @00436db0)
lê `m_userInfo[index]` (o sUserInfo de cada jogador que veio no 0x74): charInfo.tid, tidParts[24], tidAuxParts[5],
charInfo.PCL[5], clubInfo.tid, clubInfo.PCL[5], caddieInfo.tid, stat.Level. **Mudar o equipamento do bot no
0x46/0x74 muda a física simulada em todos os clientes.** Os stats ficam em sPlayerData: power +0x50c, accuracy +0x51c,
control +0x52c, spin +0x53c, curve +0x54c (WCrypticValue, xor com +8).

Fórmula por stat t (0 power, 1 control, 2 accuracy, 3 spin, 4 curve) **[C]** (`CItemManager::GetLevel`, itemmanager.cpp:4288
= call 0x7668f0):
```
cap   = Char.Attr[t] (+ (Level−1)/5 se t==0 e Level>=6) + Σ parts(Slot[t]+Attr[t]) + Σ aux(Attr+Slot) + cards
nível = Char.PCL[t] + Σ parts.Attr[t] + Σ aux.Attr[t] + charInfo.PCL[t];  nível = min(nível, cap)
stat  = nível + ClubSet.Attr[t] + clubInfo.PCL[t] + (Caddie.Attr[t] se Caddie.Level <= Level do jogador) [+ cards]
```
Depois (SetPlayerLevel): `power = clamp(power,0,100) − 15`; control e accuracy −= max(0, power − (CalcPowerPenalty(Level)+5))
(CalcPowerPenalty: 0 até nível 5, +1 a cada 5 níveis, 13 a partir do 66); accuracy e control em 0..30; spin e curve em
1..30. **O clubInfo.PCL não é limitado pelo Slot do club set no cliente** **[C]** (o limite é do servidor/loja).

Offline (CSharedDoc+0x4d6c ≠ 0) o cliente sorteia stats para os adversários **[C]** (golfdoc.c:13423):
power 10..29, control 15..24, accuracy/spin/curve 5..9 (antes do −15 e da penalidade).

### 4.2 Alcance e força por tipo de taco **[C]** (club.c:3266 GetRange, 3399 GetPower; P = stat power do jogador da vez)

Tabela base (CClub::Init club.c:3080): nome, alcance jd, loft, power, curve, spin, tipo:
1W 230/10/236/1,61/0,55/0 · 2W 210/13/204 · 3W 190/16/176 · 2I..9I 180..110 (power 161..110, tipo 1) · PW 100/52/107 ·
SW 80/56/93 (tipo 2) · 1PT 20 · 2PT 10 (tipo 3).

```
extra  = DriveUp dos aux parts (GetAuxPartProperty(p,0), shareddoc.c:35088) + cards de alcance (+ cards de PS se PS)
ps     = {0,10,20,15}[tipo de power shot]
madeira:  alcance = base + 2P + ps + extra
          power   = ((2P + extra + ps + RandomPower) / base × 1,5 + 1) × powerBase     (RandomPower só com PS)
ferro:    alcance = base + ps + extra          (o stat de força NÃO conta)
          power   = powerBase/base × extra × 1,3 + (ps/base + 1) × powerBase
PW/SW:    modos 0-2: 30 (60 com PS); modo 3: 60 (80 com PS); modo 4: como ferro
putter:   20/10 (40/30 com "putt longo")
```
Club set 0x10000012 (Air Knight Lucky) tem tabela própria (GetAirNightLuckyRange); evitar.

Bola **[C parcial]**: GetRange/GetPower não leem a bola; Ball.iff (KR642) não tem campos de física (COM[0] = quantidade do
pacote). A bola é visual **[P]**. Mascote: não entra no GetLevel nem no GetAuxPartProperty; Mascot.iff tem Attr/DriveUp
zerados nos dados KR642 → sem efeito **[C/dados]**. Caddie: stats via GetLevel; a roupa do caddie (CaddieItem COM[4]) é o
gauge inicial do jogador (golfrulebase.cpp:160-171 **[R]**, ex. 33 = começa com um PS).

### 4.3 Bot atual (Hana 0x04000001 + Air Knight 0x10000000, nível 1) **[C/dados]**

Hana PCL (9,11,6,2,2), Air Knight Attr (6,6,4,3,3) → power 15−15 = **0**, control 17, accuracy 10, spin 5, curve 5.
Confere com o "força 0" do ShotModel. Curva máxima ±0,167, spin máximo ±0,167.

### 4.4 Itens bons nos dados do cliente (pangya.iff do worktree) **[dados]**

| uso | typeid | nome | stats (P,C,A,S,Cv) / efeito |
|---|---|---|---|
| club set inicial | 0x10000000 | Air Knight | Attr 6,6,4,3,3 / Slot 8,9,8,3,3 |
| club médio | 0x10000007 | Twin Feather | 10,5,5,2,2 / 14,12,11,4,4 |
| club forte | 0x1000000A | Air Knight 3 | 9,6,6,4,1 / **16**,12,12,6,3 |
| club forte | 0x10000026 | Ruby Air Knight | 10,6,5,2,3 / **16**,12,13,5,6 |
| club força bruta | 0x10000004 | Spike Hammer | 15,2,2,1,1 / 15,8,11,3,4 |
| caddie | 0x1C000010 | Pippin (nível 6) | 2,2,0,0,0 |
| caddie | 0x1C000016 | Cadie (nível 16) | 2,2,0,1,0 |
| caddie (todos) | 0x1C00000B | Sporty Pippin | 1,1,1,1,1 |
| roupa de caddie | 0x22000001 | Pippin (novo) | COM[4] = 33 → gauge inicial 33 |
| anel (aux) | 0x70010008 | Midnight Ring | DriveUp +4 jd em todos os tacos |
| anel (aux) | 0x70000000 / 0x70000002 | anel de força / precisão | +1 power / +2 accuracy |
| anel (aux) | 0x70000006 | anel do gauge | ComboUp 110 |
| bola | 0x14000000 | Pangya Aztec | visual |

Personagem: manter Hana; roupas com Slot/Attr existem (ex. 0x0804408D, 0x08044017: Attr 1,1,0,0,0 Slot 2,3,0,0,0), mas
peças precisam de posições compatíveis — usar o mesmo validador de partes do servidor **[P]**. O caminho mais simples
para força é **nível** (cap de power +(L−1)/5) **+ charInfo.PCL[0]** + **clubInfo.PCL** + caddie + anel.

Exemplo de bot "very hard": nível 70 (cap +13, PCL[0]=13), Ruby Air Knight com PCL (6,6,8,3,3) até o Slot, Pippin,
Midnight Ring: power = 9+13+16+2−15 = **25**, 1W = 230+50+4 = **284 jd** (304 com PS duplo); control/accuracy perdem
max(0, 25−(13+5)) = 7.

---

## 5. O oponente de computador do cliente (CRival) **[C]**

- Criado sempre (golfrule.c:20308) mas só joga por um jogador cujo **sUserInfo.info.dwIdentity & 1** (byte doc+0x1323 =
  sUserInfo+0x53) está ligado: DoStand → CRival::Shot + HitShot + SetResult (golfrule.c:27999-28010) e o alvo vem de
  CRival::SetVariable (golfrule.c:13424). **Em online cada cliente rodaria a IA sozinho (Random local, HitShot de todos)
  → o servidor NUNCA deve ligar o bit 0 do dwIdentity do bot.**
- **Não há níveis de dificuldade.** O único ruído é `impacto = centro + Random(−10, 10)` (rival.cpp:88) → fase 4 se
  |x| < 2, 3 se < N, 2 se < W, senão 1. Com accuracy 10 (N = 10): 20 % pangya, 80 % good. Não há erro de mira nem de força:
  o desvio vem só da fase (§3). Offline os stats são sorteados (§4.1). `GetDifficulty()` (club.c:1275) é a dificuldade do
  curso para o placar, não da IA.
- Escolha (rival.cpp:184-239): `SelectBestClub` pelo ponto do AutoTarget × `GetMaxPowerOnGround` (lie); se o alvo cai fora
  (OB/água, índice < 0) PointFactor 0, bunker (tipo 4) 0,8, rough (tipo 3) 0,6 → escolhe outro taco.
- Mira (rival.cpp:241-303): putter = alvo − inclinação do green×5 (soma das normais a cada 3,2 u), força = dist/alcance;
  PW/SW em aproximação: vento×1 (modos ≤2) ou ×(3×força); demais: **alvo − 3×vento**; força = dist/alcance (1,0 em
  certas situações, ex. stroke==1 do buraco **[P: interpretação]**).
- Obstáculo (rival.cpp:305-381): raio de meio alcance com o loft; se bate entre 50 % e 100 % → curva ±1 (testa ±10°) ou
  backspin 1; se bate antes de 30 % → taco +3 e gira (flag+1)×5°; se o segundo trecho bate → taco +1.
- Depois de água/OB/obstáculo (SetResult, rival.cpp:384): modos 1 obstáculo / 2 rio-lago / 3 mar / 4 outro; repete com
  taco +2/+3 e corrige a mira pelo erro da última tacada (modo 4). sPlayerData+0x4d8 ("flag", zerado a cada buraco) muda
  esses ajustes **[P: significado exato]**.
- O CRival nunca usa power shot nem tacadas especiais.

---

## 6. Recomendações para o servidor (priorizadas)

1. **Equipar o bot por nível (sem risco de protocolo, maior ganho)**. Em `PlayerService.CreateBotAsync` aceitar o nível
   e montar: `Level` (stat.Level no 0x74), `charInfo.PCL[0]` = (Level−1)/5, club set + `pcl` até o Slot, caddie,
   `aux` com anel(s). Calcular no servidor o **mesmo** stat do cliente (§4.1, com os dados do pangya.iff) e passar
   `BotGolfer.PowerStat` = stat power real. Ajustar `ShotModel.RangeYards`: ferros **não** somam força; todos os tacos
   (menos putter) somam DriveUp dos anéis; PS soma 10/20.
2. **Power shot simples/duplo**: espelhar o gauge do bot (regras do SPEC-ingame, inclusive gauge inicial da roupa do
   caddie e −30 no time-out); quando a tacada longa não alcança, mandar **0x56 (guid, 1|2)** ~1 s antes do 0x53 e somar
   10/20 jd no alcance. Nada no bloco muda (+0x26 = 0).
3. **Erro pela fase/impacto em vez de (ou além de) ruído de mira**: sortear o impacto como o CRival
   (centro ± R, R por nível) e escrever +0x04 = impacto, +0x10 = fase coerente com N/W do bot (N = max(2,⌈acc⌉),
   W = min(N+10,35); o lie do bot é desconhecido → assumir fairway). Fase 4 só quando |x| < 2 (+ área pangya de itens).
   Os clientes aplicam o desvio de forma idêntica. Manter um ruído pequeno de força para o easy.
4. **Tomahawk (0x10)** com PS em tacadas longas (qualquer taco não-putter) e **Spike (0x40)/Cobra (0x20)** só com
   madeira: +0x11 = flag, fase ≥ 2 (usar 4). Alcance: aprender por tipo de tacada com a calibração existente (um
   DistanceFactor por {normal, PS, Tomahawk, Spike, Cobra}); chute inicial **[P]**: Tomahawk/Spike ×1,3 na velocidade ≈
   +20-30 % de carry, Cobra ≈ alcance nominal (sobe 100 jd antes). Alternativa melhor: portar o voo livre do GB
   (`GameServer/UTIL/QuadTree3D.cs` init_shot/ballProcess/applyForce, já com Cobra/Spike, vento ×0,01 no Spike,
   Magnus 8e-5) para estimar o carry no ar **[R]**.
5. **Spin/curva**: backspin (+0x0C > 0, ≤ spin/30) em aproximações para parar a bola; curva só se respeitar a elipse e
   compensando yaw −curva×20°. Power Spin/Curve (flags 1/2/4/8) só com fase 4 e spin/curva na borda (curva ±1,5 no
   bloco). Baixa prioridade.
6. Não mandar 0x9A/0x42 nem 0x192 para o bot (opcional). **Não** ligar dwIdentity bit 0 no bot.

### Desenho de níveis (sugestão **[P]**, base CRival + stats acima)

| nível | equipamento (power / 1W) | impacto (centro ± R) → fases | mira/força extra | especiais | vento / terreno |
|---|---|---|---|---|---|
| easy | kit novo, nível 1 (P 0 / 230) | R = 25 → muitas fases 2, algumas 1 | ±0,08 rad, ±15 % | nunca | ignora vento; sem memória |
| normal | nível 20, Twin Feather, Pippin (P ~9 / 248) | R = 10 (igual ao CRival) | ±0,03 rad, ±5 % | PS simples se gauge ≥ 33 e precisar | 3×vento; memória |
| hard | nível 40, Air Knight 3 + PCL, Pippin, Midnight (P ~19 / 272) | R = 6 | ±0,01 rad | PS simples/duplo; Tomahawk em par 5 | vento + calibração |
| very hard | nível 70, Ruby AK full, Pippin, Midnight (P 25 / 284) | R = 3 | 0 | PS duplo, Tomahawk/Spike, backspin no green | idem + rota segura |
| impossible | idem very hard (ou PCL acima do Slot, aceito pelo cliente) | R = 0 (sempre fase 4) | 0 | tudo, inclusive Power Spin para parar | idem |

---

## 7. Pontos em aberto

- Faixa exata do impacto no CRival (Random(−10,10) com centro doc+0xec) vs. área pangya com itens (CPowerGauge) **[P]**.
- Efeito real de Power Spin (flags 1/2) na rolagem e no quique: só vi o uso no giro (quadtree.c:18547) **[P]**.
- Carry real de Tomahawk/Spike/Cobra: depende da física completa (colisão, lie); medir com logs 0x1B.
- O "Screen" msg 0x1c1 subtrai inclinação×0,5 da curva (×0,15 no Spike): inclinação do lie, desconhecida no servidor.
- Se o 0x56 do bot chegar junto com o 0x53 (sem intervalo) a ordem de processamento ainda deve ser 0x56→0x53 (mesma fila
  do CGolfTask), mas não verificado; manter o intervalo.
