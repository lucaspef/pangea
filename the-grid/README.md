# THE GRID

Jogo 3D de ficção científica digital, inspirado na estética de "mundo dentro do
computador" do cinema. Identidade própria: sem personagens, marcas, músicas
ou assets protegidos de nenhuma franquia.

**Fase 1:** protótipo jogável no navegador (TypeScript + Three.js + Vite).
**Fase 2:** reimplementação em Unity/C# (veja `docs/ARCHITECTURE.md`).

## Estado atual: Marco 2 — Personagem em terceira pessoa

* Personagem humanoide procedural (armadura preta com contorno, circuitos
  luminosos, visor e disco de energia nas costas) com esqueleto hierárquico
  pronto para ser trocado por um modelo glTF rigado.
* Animação procedural com mistura contínua: parado, andar, correr, sprint,
  salto, queda, aterrissagem (leve e pesada) e esquiva com rolamento. A
  passada avança pela distância percorrida, sem "patinar".
* Controlador cinemático com gravidade, subida e descida automática de
  degraus (até 0,45 m), teto, deslizamento em paredes, colisão com o anel
  da arena, coyote time e buffer de pulo.
* Barra de energia: o sprint drena e a esquiva custa energia, que regenera
  depois de um intervalo.
* Câmera de ombro em terceira pessoa com colisão (aproxima ao encostar em
  paredes), FOV dinâmico no sprint e suavização vertical em pulos e degraus.
* Três modos de câmera: terceira pessoa (padrão), drone (V) e
  cinematográfico (C), com transições suaves.
* Praça de entrada com percurso de treino (escada, saltos progressivos,
  mirante, coberturas, pilares, arco baixo e muro) e coberturas dentro da
  Arena de Combate, todos definidos em `world.json → props`.

### Marco 1 — Cidade Digital

* Cidade procedural determinística (seed em `src/config/world.json`): ~250
  torres em 5 formatos (bloco, escalonada, octogonal, agulha, lâmina), torres
  monumentais no centro, pontes elevadas, plataformas suspensas e balizas nos
  cruzamentos.
* Os três setores fazem parte do mesmo mapa: **A** Cidade Digital, **B** Arena
  de Combate (anel com portão, obeliscos e holograma) e **C** Arena de Veículos
  (recinto com portão e torres de iluminação), ligados por boulevards com
  pulsos de energia.
* Iluminação cinematográfica: metal preto com reflexos de ambiente, filetes de
  luz HDR com bloom, tone mapping ACES, névoa exponencial, partículas
  digitais, céu com horizonte luminoso, megaestruturas distantes e anel
  orbital.
* Piso com **reflexo planar real** (qualidades média e alta) e marcações
  procedurais: grade, avenidas, círculos da arena e traçado da arena de veículos.
* Tráfego de luz percorrendo as avenidas.
* Câmera drone com inércia, impulso, FOV dinâmico e **colisão** com o cenário,
  e um modo cinematográfico que sobrevoa os três setores.
* Menu principal, pausa, configurações (qualidade, bloom, sensibilidade, eixo Y,
  campo de visão, FPS) salvas no navegador, tela de controles e HUD com setor,
  telemetria e aviso ao entrar em outro setor.

## Executar

Requisitos: Node.js 22.12+ (ou 24+) e um navegador com WebGL2.

```bash
npm install
npm run dev        # http://localhost:5173
```

Outros scripts:

```bash
npm test           # testes unitários (Vitest)
npm run typecheck  # verificação de tipos
npm run build      # build de produção em dist/
npm run preview    # serve o build
```

## Controles

| Tecla | Personagem (terceira pessoa) | Drone (V) |
|---|---|---|
| W A S D / setas | mover (relativo à câmera) | mover |
| Mouse | girar a câmera (clique para capturar o cursor, ou arraste) | olhar |
| Shift | sprint (gasta energia) | impulso |
| Espaço | pular | subir (também E) |
| Q | esquiva com rolamento | descer |
| X | alternar andar/correr | — |
| V | entrar no drone | voltar ao personagem |
| C | voo cinematográfico | voo cinematográfico |
| H / Esc, P | ocultar dicas / pausar | idem |

## Diagnóstico

No console do navegador:

```js
__THE_GRID__.snapshot()                    // estado, setor, câmera, draw calls…
__THE_GRID__.teleport(0, 70, -330, 0, -14)  // drone: x, y, z, yaw°, pitch° (vista da arena)
__THE_GRID__.teleportPlayer(0, 0.8, -560)   // personagem: x, y, z, yaw° (estrado da arena)
__THE_GRID__.scene                          // cena Three.js (inspeção)
```

## Estrutura

```
src/
  core/       loop, estados, eventos, RNG, configurações do jogador
  config/     JSON de gameplay/render + tipos + validação
  input/      ações abstratas de entrada
  world/      geração da cidade, arenas, setores, tráfego (sem Three.js)
  physics/    colisores estáticos, raycast e motor de personagem
  player/     personagem: regras de movimento, modelo, animação, visual
  camera/     terceira pessoa, drone, cinematográfica e rig com blend
  rendering/  renderer, pós-processamento, piso, materiais, céu, partículas
  ui/         menus, HUD, configurações
tests/        testes unitários da lógica
docs/         arquitetura e plano de migração para Unity
```
