import { CatmullRomCurve3, MathUtils, Vector3 } from 'three';
import type { CinematicCameraConfig } from '../config/types';
import { angleDelta, anglesFromDirection, damp, type CameraPose } from './CameraPose';

/**
 * Voo cinematográfico em loop por uma spline que atravessa os três setores.
 * Usado como fundo do menu principal e como modo "tour" (tecla C).
 * Velocidade constante via parametrização por comprimento de arco.
 */
export class CinematicController {
  readonly pose: CameraPose = { position: new Vector3(), yaw: 0, pitch: 0 };
  private readonly curve: CatmullRomCurve3;
  private t = 0;
  private initialized = false;
  private readonly ahead = new Vector3();
  private readonly dir = new Vector3();

  constructor(private readonly cfg: CinematicCameraConfig) {
    const points = cfg.waypoints.map(([x, y, z]) => new Vector3(x, y, z));
    this.curve = new CatmullRomCurve3(points, true, 'centripetal');
    this.curve.arcLengthDivisions = 2000;
  }

  /** Posiciona o progresso no ponto da curva mais próximo de `position`. */
  syncTo(position: Vector3): void {
    let best = 0, bestD = Infinity;
    const tmp = new Vector3();
    for (let i = 0; i < 400; i++) {
      const u = i / 400;
      const d = this.curve.getPointAt(u, tmp).distanceToSquared(position);
      if (d < bestD) { bestD = d; best = u; }
    }
    this.t = best;
    this.initialized = false;
  }

  /** Amostra de pontos da curva (diagnóstico e testes). */
  samplePoints(count: number): Vector3[] {
    return this.curve.getSpacedPoints(count);
  }

  update(dt: number): void {
    this.t = (this.t + dt / this.cfg.loopSeconds) % 1;
    this.curve.getPointAt(this.t, this.pose.position);
    this.curve.getPointAt((this.t + this.cfg.lookAhead) % 1, this.ahead);
    this.dir.subVectors(this.ahead, this.pose.position);
    // Olha levemente para baixo para valorizar o piso e a cidade.
    this.dir.y -= this.dir.length() * 0.12;
    const target = anglesFromDirection(this.dir);

    if (!this.initialized) {
      this.pose.yaw = target.yaw;
      this.pose.pitch = target.pitch;
      this.initialized = true;
      return;
    }
    const k = damp(this.cfg.rotationSmoothing, dt);
    this.pose.yaw += angleDelta(this.pose.yaw, target.yaw) * k;
    this.pose.pitch += (target.pitch - this.pose.pitch) * k;
    this.pose.pitch = MathUtils.clamp(this.pose.pitch, -1.2, 1.2);
  }
}
