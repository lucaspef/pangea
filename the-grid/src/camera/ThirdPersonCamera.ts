import { MathUtils, Vector3 } from 'three';
import type { ThirdPersonCameraConfig } from '../config/types';
import type { CollisionWorld } from '../physics/CollisionWorld';
import { damp, forwardFromAngles, type CameraPose } from './CameraPose';

/**
 * Câmera de ombro em terceira pessoa.
 *
 *  - órbita controlada pelo mouse (yaw/pitch) em torno de um pivô acima dos pés;
 *  - deslocamento lateral (ombro direito) para não esconder a mira;
 *  - colisão: um raio do pivô até a posição desejada aproxima a câmera
 *    instantaneamente quando há obstáculo e se afasta suavemente depois;
 *  - o pivô segue o personagem com suavização (absorve degraus e pulos).
 *
 * Equivalente Unity: Cinemachine 3rd Person Follow + Deoccluder.
 */
export class ThirdPersonCamera {
  readonly pose: CameraPose = { position: new Vector3(), yaw: 0, pitch: 0 };
  sensitivity = 1;
  invertY = false;
  /** Sensibilidade base (rad/pixel), compartilhada com a câmera drone. */
  baseSensitivity = 0.0022;

  private targetYaw = 0;
  private targetPitch = -0.12;
  private distance: number;
  private readonly pivot = new Vector3();
  private initialized = false;
  private readonly back = new Vector3();
  private readonly right = new Vector3();
  private readonly shoulder = new Vector3();

  constructor(private readonly cfg: ThirdPersonCameraConfig, private readonly collision: CollisionWorld | null) {
    this.distance = cfg.distance;
  }

  get yaw(): number {
    return this.pose.yaw;
  }

  /** Coloca a câmera atrás do personagem, olhando na direção `yaw`. */
  reset(yaw: number, pitch = -0.12): void {
    this.targetYaw = this.pose.yaw = yaw;
    this.targetPitch = this.pose.pitch = pitch;
    this.initialized = false;
  }

  applyLook(dx: number, dy: number): void {
    const s = this.baseSensitivity * this.sensitivity;
    this.targetYaw -= dx * s;
    this.targetPitch -= dy * s * (this.invertY ? -1 : 1);
    const c = this.cfg;
    this.targetPitch = MathUtils.clamp(this.targetPitch, MathUtils.degToRad(c.pitchMinDeg), MathUtils.degToRad(c.pitchMaxDeg));
  }

  update(dt: number, feet: { x: number; y: number; z: number }, sprinting: boolean): void {
    const c = this.cfg;
    const look = damp(25, dt);
    this.pose.yaw += (this.targetYaw - this.pose.yaw) * look;
    this.pose.pitch += (this.targetPitch - this.pose.pitch) * look;

    const tx = feet.x, ty = feet.y + c.pivotHeight, tz = feet.z;
    if (!this.initialized) {
      this.pivot.set(tx, ty, tz);
      this.initialized = true;
    } else {
      // Horizontal acompanha firme; vertical mais solto (degraus e pulos não sacodem a câmera).
      const kh = damp(c.followSmoothing, dt), kv = damp(c.followSmoothing * 0.45, dt);
      this.pivot.x += (tx - this.pivot.x) * kh;
      this.pivot.z += (tz - this.pivot.z) * kh;
      this.pivot.y += (ty - this.pivot.y) * kv;
    }

    forwardFromAngles(this.pose.yaw, this.pose.pitch, this.back).negate();
    this.right.set(Math.cos(this.pose.yaw), 0, -Math.sin(this.pose.yaw));
    this.shoulder.copy(this.pivot).addScaledVector(this.right, c.shoulderOffset);

    const desired = c.distance + (sprinting ? c.sprintDistanceAdd : 0);
    let allowed = desired;
    if (this.collision) {
      // Verifica também o trecho pivô → ombro (paredes encostadas no personagem).
      const toShoulder = this.collision.raycast(this.pivot, this.right, c.shoulderOffset + c.collisionPadding);
      if (toShoulder < c.shoulderOffset + c.collisionPadding) {
        this.shoulder.copy(this.pivot).addScaledVector(this.right, Math.max(0, toShoulder - c.collisionPadding));
      }
      const hit = this.collision.raycast(this.shoulder, this.back, desired + c.collisionPadding);
      allowed = Math.max(c.minDistance, hit - c.collisionPadding);
    }
    const target = Math.min(desired, allowed);
    // Aproxima rápido (nunca atravessa paredes); afasta devagar.
    const rate = target < this.distance ? c.zoomInSmoothing : c.zoomOutSmoothing;
    this.distance += (target - this.distance) * damp(rate, dt);
    if (this.distance > allowed) this.distance = allowed;

    this.pose.position.copy(this.shoulder).addScaledVector(this.back, this.distance);
    if (this.pose.position.y < 0.3) this.pose.position.y = 0.3;
  }
}
