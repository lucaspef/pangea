import { MathUtils, PerspectiveCamera, Vector3 } from 'three';
import type { CameraConfig, ThirdPersonCameraConfig } from '../config/types';
import type { CameraMode } from '../core/GameEvents';
import type { GameSystem } from '../core/GameSystem';
import type { CollisionWorld } from '../physics/CollisionWorld';
import { angleDelta, clonePose, type CameraPose } from './CameraPose';
import { CinematicController } from './CinematicController';
import { FreeFlyController, type FlyIntent } from './FreeFlyController';
import { ThirdPersonCamera } from './ThirdPersonCamera';

/**
 * Dono da câmera de renderização. Seleciona o controlador ativo (terceira
 * pessoa, drone ou cinematográfico), faz a transição suave entre modos e
 * aplica a pose final à PerspectiveCamera.
 * Equivalente Unity: Cinemachine Brain com Virtual Cameras e blend.
 */
export class CameraRig implements GameSystem {
  readonly name = 'camera';
  readonly camera: PerspectiveCamera;
  readonly free: FreeFlyController;
  readonly cinematic: CinematicController;
  readonly thirdPerson: ThirdPersonCamera;

  private modeValue: CameraMode = 'cinematic';
  private blendFrom: CameraPose | null = null;
  private blendElapsed = 0;
  private readonly current: CameraPose = { position: new Vector3(), yaw: 0, pitch: 0 };
  private baseFov: number;
  private freeIntent: FlyIntent | null = null;
  /** Alvo seguido pela câmera em terceira pessoa (pés do personagem, posição visual). */
  private followTarget: { x: number; y: number; z: number } = { x: 0, y: 0, z: 0 };
  private followSprinting = false;

  constructor(private readonly cfg: CameraConfig, collision: CollisionWorld, private readonly tpCfg: ThirdPersonCameraConfig) {
    this.camera = new PerspectiveCamera(cfg.fov, 1, cfg.near, cfg.far);
    this.camera.rotation.order = 'YXZ';
    this.baseFov = cfg.fov;
    this.free = new FreeFlyController(cfg.free, collision);
    this.cinematic = new CinematicController(cfg.cinematic);
    this.thirdPerson = new ThirdPersonCamera(tpCfg, collision);
    this.thirdPerson.baseSensitivity = cfg.free.lookSensitivity;
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

  /** Intenção de voo do quadro atual (modo drone). */
  setFreeIntent(intent: FlyIntent | null): void {
    this.freeIntent = intent;
  }

  /** Alvo e estado do personagem seguido em terceira pessoa. */
  setFollowTarget(target: { x: number; y: number; z: number }, sprinting: boolean): void {
    this.followTarget = target;
    this.followSprinting = sprinting;
  }

  /** Câmera de ombro atrás do personagem, olhando na direção `yaw`. */
  enterThirdPerson(yaw: number): void {
    this.startBlend();
    this.thirdPerson.reset(yaw);
    this.modeValue = 'thirdPerson';
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
      if (this.freeIntent) this.free.update(dt, this.freeIntent);
    } else if (this.modeValue === 'cinematic') {
      this.cinematic.update(dt);
    }
  }

  lateUpdate(dt: number): void {
    // A terceira pessoa roda aqui: o personagem já foi posicionado neste quadro.
    if (this.modeValue === 'thirdPerson') this.thirdPerson.update(dt, this.followTarget, this.followSprinting);
    const target = this.modeValue === 'free' ? this.free.pose
      : this.modeValue === 'thirdPerson' ? this.thirdPerson.pose
      : this.cinematic.pose;
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
    let kick = 0;
    if (this.modeValue === 'free') {
      kick = fc.boostFovKick * MathUtils.clamp((this.free.speed - fc.speed) / (fc.speed * (fc.boostMultiplier - 1)), 0, 1);
    } else if (this.modeValue === 'thirdPerson' && this.followSprinting) {
      kick = this.tpCfg.sprintFovKick;
    }
    const fov = this.baseFov + kick;
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
