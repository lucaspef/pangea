import { MathUtils, Vector3 } from 'three';
import type { FreeCameraConfig } from '../config/types';
import type { CollisionWorld } from '../physics/CollisionWorld';
import { PITCH_LIMIT, damp, forwardFromAngles, type CameraPose } from './CameraPose';

/** Intenção de movimento já traduzida da entrada (independente de dispositivo). */
export interface FlyIntent {
  /** x = direita, y = cima, z = frente; componentes em -1..1. */
  move: Vector3;
  lookDeltaX: number;
  lookDeltaY: number;
  boost: boolean;
}

/**
 * Câmera "drone" livre para explorar a cidade no Marco 1:
 * aceleração/desaceleração suaves, olhar suavizado e colisão com o cenário.
 */
export class FreeFlyController {
  readonly pose: CameraPose = { position: new Vector3(), yaw: 0, pitch: 0 };
  readonly velocity = new Vector3();
  /** Multiplicador vindo das configurações do jogador. */
  sensitivity = 1;
  invertY = false;

  private targetYaw = 0;
  private targetPitch = 0;
  private readonly tmpForward = new Vector3();
  private readonly tmpRight = new Vector3();
  private readonly desired = new Vector3();

  constructor(private readonly cfg: FreeCameraConfig, private readonly collision: CollisionWorld | null) {}

  setPose(pose: CameraPose): void {
    this.pose.position.copy(pose.position);
    this.pose.yaw = this.targetYaw = pose.yaw;
    this.pose.pitch = this.targetPitch = MathUtils.clamp(pose.pitch, -PITCH_LIMIT, PITCH_LIMIT);
    this.velocity.set(0, 0, 0);
  }

  /** Velocidade atual (m/s), para o HUD. */
  get speed(): number {
    return this.velocity.length();
  }

  update(dt: number, intent: FlyIntent): void {
    const c = this.cfg;
    const sens = c.lookSensitivity * this.sensitivity;
    this.targetYaw -= intent.lookDeltaX * sens;
    this.targetPitch -= intent.lookDeltaY * sens * (this.invertY ? -1 : 1);
    this.targetPitch = MathUtils.clamp(this.targetPitch, -PITCH_LIMIT, PITCH_LIMIT);

    const look = damp(c.lookSmoothing, dt);
    this.pose.yaw += (this.targetYaw - this.pose.yaw) * look;
    this.pose.pitch += (this.targetPitch - this.pose.pitch) * look;

    // Movimento relativo à direção de visão; subir/descer é sempre no eixo Y do mundo.
    forwardFromAngles(this.pose.yaw, this.pose.pitch, this.tmpForward);
    this.tmpRight.set(Math.cos(this.pose.yaw), 0, -Math.sin(this.pose.yaw));
    this.desired
      .set(0, 0, 0)
      .addScaledVector(this.tmpForward, intent.move.z)
      .addScaledVector(this.tmpRight, intent.move.x);
    this.desired.y += intent.move.y;
    if (this.desired.lengthSq() > 1) this.desired.normalize();
    this.desired.multiplyScalar(c.speed * (intent.boost ? c.boostMultiplier : 1));

    this.velocity.lerp(this.desired, damp(c.acceleration, dt));
    const p = this.pose.position;
    p.addScaledVector(this.velocity, dt);

    if (this.collision?.resolveSphere(p, c.collisionRadius)) {
      // Desliza ao longo da superfície: perde parte da velocidade ao bater.
      this.velocity.multiplyScalar(0.6);
    }
    if (p.y < c.minHeight) {
      p.y = c.minHeight;
      if (this.velocity.y < 0) this.velocity.y = 0;
    }
    if (p.y > c.maxHeight) {
      p.y = c.maxHeight;
      if (this.velocity.y > 0) this.velocity.y = 0;
    }
  }
}
