import type { LocomotionState } from '../player/PlayerState';
import { SECTOR_LABELS, type SectorId } from '../world/SectorMap';
import type { CameraMode } from '../core/GameEvents';
import { el } from './Screens';

export interface HudTelemetry {
  speed: number;
  altitude: number;
  x: number;
  z: number;
  fps: number;
  /** Energia do jogador normalizada (0..1). */
  energy: number;
  locomotion: LocomotionState;
  walkMode: boolean;
}

const LOCOMOTION_LABELS: Record<LocomotionState, string> = {
  idle: 'PARADO', walk: 'ANDANDO', run: 'CORRENDO', sprint: 'SPRINT',
  jump: 'SALTO', fall: 'QUEDA', land: 'ATERRISSAGEM', dodge: 'ESQUIVA',
};
const MODE_LABELS: Record<CameraMode, string> = { thirdPerson: 'TERCEIRA PESSOA', free: 'DRONE', cinematic: 'CINEMÁTICO' };

/** HUD do Marco 1: setor atual, modo de câmera, telemetria e dicas. */
export class Hud {
  private readonly root = el('hud');
  private readonly sectorCode = el('hud-sector-code');
  private readonly sectorName = el('hud-sector-name');
  private readonly mode = el('hud-mode');
  private readonly fps = el('hud-fps');
  private readonly speed = el('hud-speed');
  private readonly alt = el('hud-alt');
  private readonly pos = el('hud-pos');
  private readonly hints = el('hud-hints');
  private readonly banner = el('sector-banner');
  private readonly bannerCode = el('banner-code');
  private readonly bannerName = el('banner-name');
  private readonly energy = el('hud-energy');
  private readonly locomotion = el('hud-state');
  private bannerTimer = 0;
  private sinceRefresh = 0;

  setVisible(visible: boolean): void {
    this.root.classList.toggle('hidden', !visible);
  }

  setShowFps(show: boolean): void {
    this.fps.classList.toggle('hidden', !show);
  }

  toggleHints(): void {
    this.hints.classList.toggle('hidden');
  }

  setMode(mode: CameraMode): void {
    this.mode.textContent = MODE_LABELS[mode];
    this.root.dataset.mode = mode;
  }

  setSector(sector: SectorId, announce: boolean): void {
    const label = SECTOR_LABELS[sector];
    this.sectorCode.textContent = label.code;
    this.sectorName.textContent = label.name;
    if (announce) {
      this.bannerCode.textContent = label.code;
      this.bannerName.textContent = label.name;
      this.banner.classList.add('show');
      window.clearTimeout(this.bannerTimer);
      this.bannerTimer = window.setTimeout(() => this.banner.classList.remove('show'), 2600);
    }
  }

  /** Atualiza números no máximo 10x por segundo (evita custo de layout). */
  update(dt: number, t: HudTelemetry): void {
    // A barra de energia anima a cada quadro (só transform: sem custo de layout).
    this.energy.style.transform = `scaleX(${Math.max(0, Math.min(1, t.energy)).toFixed(3)})`;
    this.energy.classList.toggle('low', t.energy < 0.25);
    this.sinceRefresh += dt;
    if (this.sinceRefresh < 0.1) return;
    this.sinceRefresh = 0;
    this.speed.textContent = t.speed.toFixed(0);
    this.alt.textContent = t.altitude.toFixed(0);
    this.pos.textContent = `${t.x.toFixed(0)} · ${t.z.toFixed(0)}`;
    this.fps.textContent = `${Math.round(t.fps)} FPS`;
    this.locomotion.textContent = LOCOMOTION_LABELS[t.locomotion] + (t.walkMode && t.locomotion !== 'idle' ? ' · MODO ANDAR' : '');
  }
}
