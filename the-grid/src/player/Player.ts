import type { Scene } from 'three';
import type { PlayerConfig } from '../config/types';
import type { GameSystem } from '../core/GameSystem';
import type { CollisionWorld } from '../physics/CollisionWorld';
import type { Palette } from '../rendering/Palette';
import { PlayerController } from './PlayerController';
import { PlayerView } from './PlayerView';

/**
 * Sistema do jogador: lógica em passo fixo (PlayerController) e visual
 * interpolado entre os dois últimos passos (sem tremores em telas de 120 Hz+).
 */
export class Player implements GameSystem {
  readonly name = 'player';
  readonly controller: PlayerController;
  private readonly view: PlayerView;
  private readonly prev = { x: 0, y: 0, z: 0 };
  /** Posição visual (pés) do último quadro, usada pela câmera. */
  readonly renderPosition = { x: 0, y: 0, z: 0 };

  constructor(private readonly cfg: PlayerConfig, world: CollisionWorld, worldHalfExtent: number, scene: Scene, palette: Palette) {
    this.controller = new PlayerController(cfg, world, worldHalfExtent);
    this.view = new PlayerView(scene, palette, cfg.movement);
    this.respawn();
  }

  respawn(): void {
    const [x, y, z] = this.cfg.spawn.position;
    this.controller.spawn(x, y, z, (this.cfg.spawn.yawDeg * Math.PI) / 180);
    this.syncPrev();
    Object.assign(this.renderPosition, this.controller.motor.position);
  }

  fixedUpdate(dt: number): void {
    this.syncPrev();
    const events = this.controller.fixedUpdate(dt);
    const p = this.controller.motor.position;
    if (events.landed && events.landImpact > 0.25) this.view.pulse(p.x, p.y, p.z, events.landImpact);
    if (events.dodged) this.view.pulse(p.x, p.y, p.z, 0.2);
  }

  lateUpdate(dt: number, alpha: number): void {
    const p = this.controller.motor.position;
    const r = this.renderPosition;
    r.x = this.prev.x + (p.x - this.prev.x) * alpha;
    r.y = this.prev.y + (p.y - this.prev.y) * alpha;
    r.z = this.prev.z + (p.z - this.prev.z) * alpha;
    this.view.update(r.x, r.y, r.z, this.controller.motor.groundY, this.controller.snapshot(), dt);
  }

  dispose(): void {
    this.view.dispose();
  }

  private syncPrev(): void {
    Object.assign(this.prev, this.controller.motor.position);
  }
}
