# THE GRID — Arquitetura

Este documento descreve a organização do protótipo web (Fase 1) e como cada
módulo será reimplementado na Unity (Fase 2). O código TypeScript **não** será
importado na Unity: o que se preserva são os **contratos**, os **dados** (JSON)
e os **algoritmos** (ex.: gerador da cidade com seed determinística).

## Camadas

```
┌──────────────────────────────────────────────────────────────┐
│ main.ts → core/Game (raiz de composição + máquina de estados) │
├───────────────┬───────────────┬───────────────┬──────────────┤
│ input/        │ camera/       │ ui/           │ rendering/   │
│ ações abstr.  │ controladores │ DOM/HUD       │ Three.js     │
├───────────────┴───────┬───────┴───────────────┴──────────────┤
│ world/ (dados + regras puras)  │ physics/ (colisão estática)  │
├────────────────────────────────┴─────────────────────────────┤
│ core/ (loop, eventos, RNG, settings)   config/ (JSON + tipos) │
└──────────────────────────────────────────────────────────────┘
```

Regras de dependência:

* `world/`, `physics/`, `core/` e `config/` **não importam Three.js** (exceção
  documentada: `camera/` usa `Vector3`/`CatmullRomCurve3` apenas como álgebra).
* `rendering/` lê dados do `world/` e nunca altera regras de jogo.
* `ui/` não conhece regras: chama ações do `Game` (`UIActions`).
* Sistemas se comunicam pelo `EventBus` tipado (`core/GameEvents.ts`).

## Ciclo de vida

`GameLoop` executa, a cada quadro:

1. `fixedUpdate(1/60)` — 0..5 vezes (simulação determinística: tráfego, e nos
   próximos marcos personagem, combate e veículos);
2. `update(dt)` — entrada e controladores;
3. `lateUpdate(dt)` — câmera (depois de tudo se mover);
4. `render` — atualização visual (`WorldView`) + pós-processamento.

Pausa = `timeScale = 0` (a simulação congela, a renderização continua).

## Módulos do Marco 1

| Pasta | Arquivo | Responsabilidade |
|---|---|---|
| core | `Game.ts` | Cria e conecta tudo; estados `loading → menu ⇄ playing ⇄ paused` |
| core | `GameLoop.ts`, `FixedStepClock.ts` | Passo fixo + passo variável |
| core | `EventBus.ts`, `GameEvents.ts` | Eventos tipados |
| core | `Rng.ts` | PRNG mulberry32 (seed → mesma cidade) |
| core | `Settings.ts` | Preferências do jogador, saneamento, persistência |
| config | `*.json`, `types.ts`, `index.ts` | Dados de gameplay/render com validação |
| input | `InputManager.ts` | Teclas → ações (`input.json`), mouse com pointer lock ou arraste |
| world | `CityGenerator.ts`, `CityLayout.ts` | Geração procedural da Cidade Digital |
| world | `ArenaLayout.ts` | Estrutura física das arenas (Setores B e C) |
| world | `SectorMap.ts` | Posição → setor |
| world | `TrafficSystem.ts` | Corredores de luz nas avenidas (lógica pura) |
| world | `World.ts`, `WorldColliders.ts` | Agrega layout + colisão + sistemas |
| physics | `Colliders.ts`, `CollisionWorld.ts` | AABB e parede anelar; hash espacial; resolução de esfera |
| camera | `FreeFlyController.ts` | Câmera drone com inércia e colisão |
| camera | `CinematicController.ts` | Voo em spline pelos três setores |
| camera | `CameraRig.ts` | Seleção de modo, blend, FOV dinâmico |
| rendering | `RenderContext.ts` | WebGLRenderer, HDR, bloom, ACES, FXAA/MSAA |
| rendering | `GridFloor.ts` | Piso com reflexo planar + marcações em shader |
| rendering | `FacadeMaterial.ts` | Painéis, janelas e varreduras procedurais nas fachadas |
| rendering | `CityRenderer.ts`, `ArenaRenderer.ts` | Malhas instanciadas a partir dos layouts |
| rendering | `SkyDome.ts`, `DataParticles.ts`, `TrafficRenderer.ts`, `Environment.ts` | Atmosfera |
| ui | `UIManager.ts`, `Hud.ts`, `SettingsPanel.ts`, `Screens.ts` | Menus, HUD e configurações |

## Desempenho

* A cidade inteira usa **4 InstancedMesh** (corpos retangulares, octogonais,
  filetes e anéis): ~20 draw calls por passada de renderização.
* Detalhes de fachada e piso são **procedurais no shader** (sem texturas).
* Bloom seletivo por **HDR**: só cores > limiar brilham; não há passada extra
  por camada.
* O reflexo do piso renderiza a cena uma segunda vez em resolução reduzida
  (35 % na qualidade média, 60 % na alta; desligado na baixa).
* Partículas são dobradas em torno da câmera no vertex shader (custo de CPU zero).

## Mapeamento para Unity (Fase 2)

| Web (Fase 1) | Unity (Fase 2) |
|---|---|
| `GameSystem` (`fixedUpdate/update/lateUpdate/dispose`) | `MonoBehaviour` (`FixedUpdate/Update/LateUpdate/OnDestroy`) |
| `Game` (composição) | Cena bootstrap + `GameManager` + injeção via inspector |
| `EventBus` / `GameEvents` | Eventos C# ou ScriptableObject event channels |
| `config/*.json` + `types.ts` | `ScriptableObject` com os mesmos campos (importador de JSON no editor) |
| `InputManager` + `input.json` | Input System (`InputActionAsset`) |
| `CityGenerator` + `Rng` | Port 1:1 em C# (mesma seed → mesma cidade); `CityBuilder` instancia prefabs |
| `CollisionWorld` (AABB/anel) | `BoxCollider` estáticos + `MeshCollider` para o anel da arena |
| `FreeFlyController` | Componente de câmera de depuração / espectador |
| `CinematicController` + `CameraRig` | Cinemachine (Dolly Track + Brain com blend) |
| `RenderContext` (bloom, ACES) | URP + Volume (Bloom, Tonemapping ACES) |
| `GridFloor` (Reflector) | Planar Reflection Probe/SSR + Shader Graph para as marcações |
| `FacadeMaterial` (onBeforeCompile) | Shader Graph com emissão procedural |
| `InstanceBatch` | GPU Instancing / `Graphics.RenderMeshInstanced` |
| `DataParticles` | VFX Graph ou Particle System |
| `ui/*` (DOM) | UI Toolkit (UXML/USS) |
| `SettingsStore` (localStorage) | `PlayerPrefs` |

## Próximos marcos (pendentes)

* **Marco 2** — `player/` (controlador cinemático em cápsula sobre o
  `CollisionWorld`, gravidade, salto, esquiva), câmera em terceira pessoa,
  modelo provisório e máquina de estados de animação.
* **Marco 3** — `combat/` (disco: corpo a corpo, arremesso com trajetória,
  retorno, bloqueio), `ai/` (inimigos), vida/dano, desintegração.
* **Marco 4** — `vehicles/` (motos de luz, rastros com colisão, câmera de
  pilotagem, IA de adversários) dentro do Setor C.
* **Marco 5** — gameplay das arenas, HUD de combate/velocímetro, áudio.
* **Marco 6** — perfis de desempenho, LOD, especificação final da migração.
