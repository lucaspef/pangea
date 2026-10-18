import { Scene, Vector3 } from 'three';
import { CameraRig } from '../camera/CameraRig';
import { cameraConfig, inputConfig, playerConfig, renderingConfig, worldConfig } from '../config';
import { InputManager } from '../input/InputManager';
import { Player } from '../player/Player';
import { createEnvironment } from '../rendering/Environment';
import { Palette } from '../rendering/Palette';
import { RenderContext } from '../rendering/RenderContext';
import { WorldView } from '../rendering/WorldView';
import { UIManager } from '../ui/UIManager';
import type { SectorId } from '../world/SectorMap';
import { World } from '../world/World';
import { EventBus } from './EventBus';
import type { CameraMode, GameEvents, GameState } from './GameEvents';
import { GameLoop } from './GameLoop';
import type { GameSystem } from './GameSystem';
import { SettingsStore, browserStorage, type PlayerSettings } from './Settings';

/**
 * Raiz de composição: cria e conecta todos os sistemas, e mantém a máquina
 * de estados do jogo (carregando → menu ⇄ jogando ⇄ pausado).
 *
 * Durante o jogo há três modos de câmera: terceira pessoa (padrão, controla
 * o personagem), drone (V, exploração livre) e cinematográfico (C).
 */
export class Game implements GameSystem {
  readonly name = 'game';
  readonly events = new EventBus<GameEvents>();
  readonly scene = new Scene();
  readonly world: World;
  readonly rig: CameraRig;
  readonly settings: SettingsStore;
  player!: Player;

  private readonly ui: UIManager;
  private input!: InputManager;
  private render!: RenderContext;
  private view!: WorldView;
  private readonly loop: GameLoop;
  private readonly palette = new Palette(renderingConfig);

  private stateValue: GameState = 'loading';
  private sector: SectorId | null = null;
  private elapsed = 0;
  private hasSpawned = false;
  private wasPointerLocked = false;
  /** Modo de câmera "de jogo" para onde o cinematográfico retorna. */
  private gameplayMode: Exclude<CameraMode, 'cinematic'> = 'thirdPerson';
  private readonly move = new Vector3();

  constructor(private readonly viewport: HTMLElement) {
    this.settings = new SettingsStore(browserStorage(), (s) => this.applySettings(s));
    this.ui = new UIManager(
      {
        start: () => this.startPlaying(),
        resume: () => this.resume(),
        mainMenu: () => this.toMainMenu(),
        toggleCinematic: () => this.toggleCinematic(),
      },
      this.settings,
      renderingConfig,
    );
    this.world = new World(worldConfig);
    this.rig = new CameraRig(cameraConfig, this.world.collision, playerConfig.camera);
    this.loop = new GameLoop((dt) => this.renderFrame(dt));
  }

  get state(): GameState {
    return this.stateValue;
  }

  /** Inicialização assíncrona: cria o renderer, a cena e pré-compila shaders. */
  async init(): Promise<void> {
    const step = async (progress: number, text: string) => {
      this.ui.showLoading(progress, text);
      // Cede o quadro para o navegador pintar a barra de progresso.
      await new Promise((r) => requestAnimationFrame(() => r(null)));
    };

    await step(0.1, 'Gerando a cidade digital');
    const s = this.settings.get();
    const preset = renderingConfig.presets[s.quality];
    try {
      this.render = new RenderContext(this.viewport, this.scene, this.rig.camera, renderingConfig, preset);
    } catch (err) {
      this.ui.showError('Não foi possível criar o contexto WebGL. Verifique se a aceleração de hardware está ativa no navegador.');
      throw err;
    }
    this.input = new InputManager(this.render.canvas, inputConfig);

    await step(0.35, 'Construindo arquitetura');
    const env = createEnvironment(this.render.renderer, this.palette);
    this.view = new WorldView(this.scene, this.world, this.palette, renderingConfig, env);
    this.player = new Player(playerConfig, this.world.collision, worldConfig.worldHalfExtent, this.scene, this.palette);
    this.applySettings(s);

    await step(0.6, 'Compilando shaders');
    await this.render.renderer.compileAsync(this.scene, this.rig.camera);

    await step(0.9, 'Sincronizando setores');
    // Ordem importa: entrada (game) → tráfego/jogador (passo fixo) → câmera (lateUpdate após o jogador).
    this.loop.add(this);
    this.loop.add(this.world.traffic);
    this.loop.add(this.player);
    this.loop.add(this.rig);

    window.addEventListener('resize', this.onResize);
    document.addEventListener('pointerlockchange', this.onPointerLockChange);
    this.render.canvas.addEventListener('click', this.onCanvasClick);

    this.loop.start();
    await step(1, 'Pronto');
    this.setState('menu');
  }

  // ---------- Máquina de estados ----------

  private setState(next: GameState): void {
    const previous = this.stateValue;
    if (previous === next) return;
    this.stateValue = next;

    this.input.setEnabled(next === 'playing');
    this.loop.timeScale = next === 'paused' ? 0 : 1;
    this.ui.hud.setVisible(next === 'playing' || next === 'paused');
    this.ui.screens.show(next === 'menu' ? 'menu' : next === 'paused' ? 'pause' : 'none');
    if (next === 'menu' && this.rig.mode !== 'cinematic') this.rig.enterCinematic();
    this.events.emit('state:changed', { state: next, previous });
  }

  private startPlaying(): void {
    if (this.stateValue !== 'menu') return;
    if (!this.hasSpawned) {
      this.player.respawn();
      this.gameplayMode = 'thirdPerson';
      // Primeira entrada: câmera atrás do personagem.
      this.enterGameplayCamera(this.player.controller.facing);
    } else {
      this.enterGameplayCamera();
    }
    this.hasSpawned = true;
    this.setState('playing');
    this.input.requestPointerLock();
  }

  private resume(): void {
    if (this.stateValue !== 'paused') return;
    this.setState('playing');
    this.input.requestPointerLock();
  }

  private pause(): void {
    if (this.stateValue !== 'playing') return;
    this.setState('paused');
    this.input.exitPointerLock();
  }

  private toMainMenu(): void {
    this.hasSpawned = false;
    this.input.exitPointerLock();
    this.setState('menu');
  }

  /** Entra no modo de câmera de jogo; `yaw` padrão = direção atual da câmera. */
  private enterGameplayCamera(yaw = this.rig.pose.yaw): void {
    if (this.gameplayMode === 'thirdPerson') this.rig.enterThirdPerson(yaw);
    else this.rig.enterFree();
    this.onCameraModeChanged();
  }

  private toggleCinematic(): void {
    if (this.rig.mode === 'cinematic') this.enterGameplayCamera();
    else {
      this.rig.enterCinematic();
      this.onCameraModeChanged();
    }
  }

  private toggleDrone(): void {
    if (this.rig.mode === 'cinematic') return;
    this.gameplayMode = this.rig.mode === 'free' ? 'thirdPerson' : 'free';
    this.enterGameplayCamera();
  }

  private onCameraModeChanged(): void {
    this.ui.hud.setMode(this.rig.mode);
    this.events.emit('camera:mode', { mode: this.rig.mode });
  }

  // ---------- Ciclo ----------

  /** Entrada do quadro, antes da simulação (passos fixos). */
  earlyUpdate(): void {
    const input = this.input;
    if (input.wasPressed('pause')) {
      if (this.stateValue === 'playing') this.pause();
      else if (this.stateValue === 'paused' && !this.ui.back()) this.resume();
      else if (this.stateValue === 'menu') this.ui.back();
    }
    const playing = this.stateValue === 'playing';
    if (playing) {
      if (input.wasPressed('toggleCinematic')) this.toggleCinematic();
      if (input.wasPressed('toggleDrone')) this.toggleDrone();
      if (input.wasPressed('toggleHud')) this.ui.hud.toggleHints();
    }

    const look = input.consumeLook();
    const mode = this.rig.mode;
    const ctrl = this.player.controller;

    if (playing && mode === 'thirdPerson') {
      this.rig.thirdPerson.applyLook(look.x, look.y);
      // Movimento relativo à câmera: frente = direção do olhar projetada no chão.
      const f = input.axis('moveBack', 'moveForward');
      const r = input.axis('moveLeft', 'moveRight');
      const yaw = this.rig.thirdPerson.yaw;
      let x = -Math.sin(yaw) * f + Math.cos(yaw) * r;
      let z = -Math.cos(yaw) * f - Math.sin(yaw) * r;
      const len = Math.hypot(x, z);
      if (len > 1) { x /= len; z /= len; }
      ctrl.intent = { x, z, sprint: input.isDown('sprint') };
      if (input.wasPressed('jump')) ctrl.requestJump();
      if (input.wasPressed('dodge')) ctrl.requestDodge();
      if (input.wasPressed('toggleWalk')) ctrl.toggleWalk();
    } else {
      ctrl.intent = { x: 0, z: 0, sprint: false };
    }

    if (playing && mode === 'free') {
      this.move.set(input.axis('moveLeft', 'moveRight'), input.axis('moveDown', 'moveUp'), input.axis('moveBack', 'moveForward'));
      this.rig.setFreeIntent({ move: this.move, lookDeltaX: look.x, lookDeltaY: look.y, boost: input.isDown('boost') });
    } else {
      this.rig.setFreeIntent(null);
    }
  }

  update(): void {
    this.rig.setFollowTarget(this.player.renderPosition, this.player.controller.isSprinting);
  }

  private renderFrame(dt: number): void {
    this.elapsed += dt;
    const cam = this.rig.camera;
    this.view.update(this.elapsed, cam);

    const ctrl = this.player.controller;
    const followingPlayer = this.rig.mode === 'thirdPerson';
    const focus = followingPlayer ? this.player.renderPosition : cam.position;
    const sector = this.world.sectors.sectorAt(focus.x, focus.z);
    if (sector !== this.sector) {
      const announce = this.sector !== null && this.stateValue === 'playing';
      this.sector = sector;
      this.ui.hud.setSector(sector, announce);
      this.events.emit('sector:changed', { sector });
    }
    this.ui.hud.update(dt, {
      speed: followingPlayer ? ctrl.horizontalSpeed : this.rig.mode === 'free' ? this.rig.free.speed : 0,
      altitude: focus.y,
      x: focus.x,
      z: focus.z,
      fps: this.loop.fps,
      energy: ctrl.energy / playerConfig.energy.max,
      locomotion: ctrl.state,
      walkMode: ctrl.walkMode,
    });

    this.render.render();
    this.input.endFrame();
  }

  // ---------- Configurações ----------

  private applySettings(s: PlayerSettings): void {
    this.rig.setBaseFov(s.fov);
    this.rig.free.sensitivity = s.mouseSensitivity;
    this.rig.free.invertY = s.invertY;
    this.rig.thirdPerson.sensitivity = s.mouseSensitivity;
    this.rig.thirdPerson.invertY = s.invertY;
    this.ui.hud.setShowFps(s.showFps);
    if (!this.render || !this.view) return; // ainda carregando
    const preset = renderingConfig.presets[s.quality];
    this.render.applyQuality(preset, s.bloom);
    const size = this.render.drawingSize;
    this.view.applyQuality(preset, size.x, size.y, this.render.pixelRatio);
    this.events.emit('settings:changed', { settings: s });
  }

  // ---------- Eventos do navegador ----------

  private readonly onResize = (): void => {
    this.render.resize();
    const size = this.render.drawingSize;
    this.view.resize(size.x, size.y, renderingConfig.presets[this.settings.get().quality]);
  };

  private readonly onPointerLockChange = (): void => {
    const locked = this.input.pointerLocked;
    // Perder o cursor durante o jogo (Esc do navegador) pausa.
    if (this.wasPointerLocked && !locked && this.stateValue === 'playing') this.pause();
    this.wasPointerLocked = locked;
  };

  private readonly onCanvasClick = (): void => {
    if (this.stateValue === 'playing' && !this.input.pointerLocked) this.input.requestPointerLock();
  };

  // ---------- Diagnóstico ----------

  /** Diagnóstico: posiciona a câmera drone (entra no jogo se estiver no menu). */
  debugTeleport(x: number, y: number, z: number, yawDeg = 0, pitchDeg = 0): void {
    this.ensurePlaying();
    const pose = { position: new Vector3(x, y, z), yaw: (yawDeg * Math.PI) / 180, pitch: (pitchDeg * Math.PI) / 180 };
    this.gameplayMode = 'free';
    this.rig.enterFree(pose);
    this.rig.skipBlend();
    this.onCameraModeChanged();
  }

  /** Diagnóstico: posiciona o personagem e volta para a terceira pessoa. */
  debugTeleportPlayer(x: number, y: number, z: number, yawDeg = 0): void {
    this.ensurePlaying();
    const yaw = (yawDeg * Math.PI) / 180;
    this.player.controller.spawn(x, y, z, yaw);
    this.gameplayMode = 'thirdPerson';
    this.rig.enterThirdPerson(yaw);
    this.rig.skipBlend();
    this.onCameraModeChanged();
  }

  private ensurePlaying(): void {
    if (this.stateValue === 'menu') this.startPlaying();
    if (this.stateValue === 'paused') this.resume();
  }

  /** Informações para diagnóstico e testes automatizados. */
  debugSnapshot() {
    const p = this.rig.camera.position;
    const c = this.player?.controller;
    return {
      state: this.stateValue,
      cameraMode: this.rig.mode,
      sector: this.sector,
      fps: Math.round(this.loop.fps),
      camera: {
        x: +p.x.toFixed(2), y: +p.y.toFixed(2), z: +p.z.toFixed(2),
        yawDeg: Math.round((this.rig.pose.yaw * 180) / Math.PI), pitchDeg: Math.round((this.rig.pose.pitch * 180) / Math.PI),
      },
      player: c && {
        x: +c.motor.position.x.toFixed(2), y: +c.motor.position.y.toFixed(2), z: +c.motor.position.z.toFixed(2),
        state: c.state, grounded: c.motor.grounded, energy: Math.round(c.energy), speed: +c.horizontalSpeed.toFixed(2),
      },
      towers: this.world.city.towers.length,
      bridges: this.world.city.bridges.length,
      platforms: this.world.city.platforms.length,
      props: this.world.props.length,
      colliders: this.world.collision.size,
      render: this.view?.stats,
      drawCalls: this.render?.renderer.info.render.calls,
      triangles: this.render?.renderer.info.render.triangles,
    };
  }

  dispose(): void {
    this.loop.dispose();
    window.removeEventListener('resize', this.onResize);
    document.removeEventListener('pointerlockchange', this.onPointerLockChange);
    this.input?.dispose();
    this.view?.dispose();
    this.render?.dispose();
    this.events.clear();
  }
}
