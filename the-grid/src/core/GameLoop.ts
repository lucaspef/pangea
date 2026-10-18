import { FixedStepClock } from './FixedStepClock';
import type { GameSystem } from './GameSystem';

/**
 * Laço principal: passos fixos para simulação, passo variável para
 * apresentação (câmera, efeitos), depois renderização.
 */
export class GameLoop {
  private readonly systems: GameSystem[] = [];
  private readonly clock: FixedStepClock;
  private rafId = 0;
  private lastTime = -1;
  private running = false;

  /** 0 pausa a simulação; a renderização continua. */
  timeScale = 1;
  /** Média móvel de quadros por segundo (para o HUD). */
  fps = 0;

  constructor(
    private readonly render: (dt: number) => void,
    fixedDt = 1 / 60,
  ) {
    this.clock = new FixedStepClock(fixedDt);
  }

  add(system: GameSystem): void {
    this.systems.push(system);
  }

  start(): void {
    if (this.running) return;
    this.running = true;
    this.lastTime = -1;
    this.rafId = requestAnimationFrame(this.frame);
  }

  stop(): void {
    this.running = false;
    cancelAnimationFrame(this.rafId);
  }

  dispose(): void {
    this.stop();
    for (const s of this.systems) s.dispose?.();
    this.systems.length = 0;
  }

  private readonly frame = (now: number): void => {
    if (!this.running) return;
    this.rafId = requestAnimationFrame(this.frame);

    // Limita o dt: abas em segundo plano retornam com dt enorme.
    const rawDt = this.lastTime < 0 ? 1 / 60 : Math.min((now - this.lastTime) / 1000, 0.1);
    this.lastTime = now;
    if (rawDt > 0) this.fps += (1 / rawDt - this.fps) * 0.05;

    const dt = rawDt * this.timeScale;
    const { steps } = this.clock.advance(dt);
    for (let i = 0; i < steps; i++) {
      for (const s of this.systems) s.fixedUpdate?.(this.clock.fixedDt);
    }
    for (const s of this.systems) s.update?.(dt);
    for (const s of this.systems) s.lateUpdate?.(dt);
    // A renderização recebe o dt real: efeitos de UI animam mesmo em pausa.
    this.render(rawDt);
  };
}
