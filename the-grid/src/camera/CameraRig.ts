import { MathUtils, PerspectiveCamera, Vector3 } from 'three';
import type { CameraConfig } from '../config/types';
import type { CameraMode } from '../core/GameEvents';
import type { GameSystem } from '../core/GameSystem';
import type { CollisionWorld } from '../physics/CollisionWorld';
import { angleDelta, clonePose, type CameraPose } from './CameraPose';
import { CinematicController } from './CinematicController';
import { FreeFlyController, type FlyIntent } from './FreeFlyController';

/**
 * Dono da câmera de renderização. Seleciona o controlador ativo, faz a
 * transição suave entre modos e aplica a pose final à PerspectiveCamera.
 * Equivalente Unity: Cinemachine Brain com duas Virtual Cameras e blend.
 */
export class CameraRig implements GameSystem {
  readonly name = 'camera';
  readonly camera: PerspectiveCamera;
  readonly free: FreeFlyController;
  readonly cinematic: CinematicController;

  private modeValue: CameraMode = 'cinematic';
  private blendFrom: CameraPose | null = null;
  private blendElapsed = 0;
  private readonly current: CameraPose = { position: new Vector3(), yaw: 0, pitch: 0 };
  private baseFov: number;
  /** Fornece a intenção de voo a cada quadro (entrada já traduzida). */
  intentProvider: () => FlyIntent | null = () => null;

  constructor(private readonly cfg: CameraConfig, collision: CollisionWorld) {
    this.camera = new PerspectiveCamera(cfg.fov, 1, cfg.near, cfg.far);
    this.camera.rotation.order = 'YXZ';
    this.baseFov = cfg.fov;
    this.free = new FreeFlyController(cfg.free, collision);
    this.cinematic = new CinematicController(cfg.cinematic);
    this.cinematic.update(0);
    this.copyPose(this.cinematic.pose, this.current);
    this.apply();
  }

  get mode(): CameraMode {
    return this.modeValue;
  }

  get pose(): Readonly<CameraPose> {
    return this.current;
  }

  setBaseFov(fov: number): void {
    this.baseFov = fov;
  }

  /** Pose de entrada do jogador definida em camera.json. */
  spawnPose(): CameraPose {
    const s = this.cfg.spawn;
    return {
      position: new Vector3(...s.position),
      yaw: MathUtils.degToRad(s.yawDeg),
      pitch: MathUtils.degToRad(s.pitchDeg),
    };
  }

  /** Troca para o modo livre na pose dada (ou na atual), com transição. */
  enterFree(pose?: CameraPose): void {
    this.startBlend();
    this.free.setPose(pose ?? this.current);
    this.modeValue = 'free';
  }

  /** Troca para o voo cinematográfico a partir do ponto mais próximo da rota. */
  enterCinematic(): void {
    this.startBlend();
    this.cinematic.syncTo(this.current.position);
    this.cinematic.update(0);
    this.modeValue = 'cinematic';
  }

  /** Conclui imediatamente a transição em andamento. */
  skipBlend(): void {
    this.blendFrom = null;
  }

  update(dt: number): void {
    if (this.modeValue === 'free') {
      const intent = this.intentProvider();
      if (intent) this.free.update(dt, intent);
    } else {
      this.cinematic.update(dt);
    }
  }

  lateUpdate(dt: number): void {
    const target = this.modeValue === 'free' ? this.free.pose : this.cinematic.pose;
    if (this.blendFrom) {
      this.blendElapsed += dt;
      const t = MathUtils.smoothstep(this.blendElapsed / this.cfg.modeBlendSeconds, 0, 1);
      this.current.position.lerpVectors(this.blendFrom.position, target.position, t);
      this.current.yaw = this.blendFrom.yaw + angleDelta(this.blendFrom.yaw, target.yaw) * t;
      this.current.pitch = MathUtils.lerp(this.blendFrom.pitch, target.pitch, t);
      if (t >= 1) this.blendFrom = null;
    } else {
      this.copyPose(target, this.current);
    }

    // Leve abertura de FOV com a velocidade (sensação de impulso).
    const fc = this.cfg.free;
    const boostT = this.modeValue === 'free'
      ? MathUtils.clamp((this.free.speed - fc.speed) / (fc.speed * (fc.boostMultiplier - 1)), 0, 1)
      : 0;
    const fov = this.baseFov + fc.boostFovKick * boostT;
    if (Math.abs(this.camera.fov - fov) > 0.01) {
      this.camera.fov += (fov - this.camera.fov) * Math.min(1, dt * 6);
      this.camera.updateProjectionMatrix();
    }
    this.apply();
  }

  private startBlend(): void {
    this.blendFrom = clonePose(this.current);
    this.blendElapsed = 0;
  }

  private copyPose(from: CameraPose, to: CameraPose): void {
    to.position.copy(from.position);
    to.yaw = from.yaw;
    to.pitch = from.pitch;
  }

  private apply(): void {
    this.camera.position.copy(this.current.position);
    this.camera.rotation.set(this.current.pitch, this.current.yaw, 0, 'YXZ');
    this.camera.updateMatrixWorld();
  }
}
