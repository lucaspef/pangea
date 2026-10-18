# THE GRID

Jogo 3D de ficção científica digital, inspirado na estética de "mundo dentro do
computador" do cinema. Identidade própria: sem personagens, marcas, músicas
ou assets protegidos de nenhuma franquia.

**Fase 1:** protótipo jogável no navegador (TypeScript + Three.js + Vite).
**Fase 2:** reimplementação em Unity/C# (veja `docs/ARCHITECTURE.md`).

## Estado atual: Marco 1 — Cidade Digital

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

| Tecla | Ação |
|---|---|
| W A S D / setas | mover |
| Mouse | olhar (clique na tela para capturar o cursor, ou arraste) |
| Espaço / E · Q | subir · descer |
| Shift | impulso |
| C | alternar voo cinematográfico |
| H | ocultar dicas |
| Esc / P | pausar |

## Diagnóstico

No console do navegador:

```js
__THE_GRID__.snapshot()                    // estado, setor, câmera, draw calls…
__THE_GRID__.teleport(0, 70, -330, 0, -14)  // x, y, z, yaw°, pitch° (vista da arena)
__THE_GRID__.scene                          // cena Three.js (inspeção)
```

## Estrutura

```
src/
  core/       loop, estados, eventos, RNG, configurações do jogador
  config/     JSON de gameplay/render + tipos + validação
  input/      ações abstratas de entrada
  world/      geração da cidade, arenas, setores, tráfego (sem Three.js)
  physics/    colisores estáticos e resolução de colisão
  camera/     câmera drone, cinematográfica e rig com blend
  rendering/  renderer, pós-processamento, piso, materiais, céu, partículas
  ui/         menus, HUD, configurações
tests/        testes unitários da lógica
docs/         arquitetura e plano de migração para Unity
```
