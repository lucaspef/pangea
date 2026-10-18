import type { PlayerConfig } from '../config/types';
import { CharacterMotor } from '../physics/CharacterMotor';
import type { CollisionWorld } from '../physics/CollisionWorld';
import type { AnimationInput, LocomotionState, MoveIntent } from './PlayerState';

const angleDelta = (from: number, to: number) => Math.atan2(Math.sin(to - from), Math.cos(to - from));

/** Eventos de gameplay de um passo (para efeitos visuais/sonoros). */
export interface PlayerStepEvents {
  jumped: boolean;
  dodged: boolean;
  landed: boolean;
  landImpact: number;
}

/**
 * Regras de movimento do jogador, independentes de renderização:
 * aceleração, corrida/sprint/caminhada, pulo com coyote time e buffer,
 * esquiva com custo de energia, regeneração de energia e máquina de estados.
 *
 * Roda em passo fixo. Equivalente Unity: PlayerMotor (MonoBehaviour) com
 * FixedUpdate + CharacterController.
 */
export class PlayerController {
  readonly motor: CharacterMotor;
  state: LocomotionState = 'idle';
  stateTime = 0;
  facing = 0;
  energy: number;
  walkMode = false;
  intent: MoveIntent = { x: 0, z: 0, sprint: false };

  private sprinting = false;
  private jumpBuffer = 0;
  private dodgeBuffer = 0;
  private coyote = 0;
  private dodgeTimer = 0;
  private dodgeCooldown = 0;
  private readonly dodgeDir = { x: 0, z: -1 };
  private regenDelay = 0;
  private landTimer = 0;
  private landImpact = 0;

  constructor(private readonly cfg: PlayerConfig, world: CollisionWorld, worldHalfExtent: number) {
    this.motor = new CharacterMotor(world, cfg.capsule, worldHalfExtent);
    this.energy = cfg.energy.max;
  }

  spawn(x: number, y: number, z: number, facing: number): void {
    this.motor.teleport(x, y, z);
    this.facing = facing;
    this.state = 'idle';
    this.stateTime = 0;
    this.energy = this.cfg.energy.max;
    this.dodgeTimer = this.dodgeCooldown = this.jumpBuffer = this.dodgeBuffer = 0;
  }

  requestJump(): void {
    this.jumpBuffer = this.cfg.movement.jumpBuffer;
  }

  requestDodge(): void {
    this.dodgeBuffer = 0.12;
  }

  toggleWalk(): void {
    this.walkMode = !this.walkMode;
  }

  get horizontalSpeed(): number {
    return Math.hypot(this.motor.velocity.x, this.motor.velocity.z);
  }

  get isSprinting(): boolean {
    return this.sprinting;
  }

  fixedUpdate(dt: number): PlayerStepEvents {
    const m = this.cfg.movement;
    const motor = this.motor;
    const v = motor.velocity;
    const events: PlayerStepEvents = { jumped: false, dodged: false, landed: false, landImpact: 0 };

    this.jumpBuffer = Math.max(0, this.jumpBuffer - dt);
    this.dodgeBuffer = Math.max(0, this.dodgeBuffer - dt);
    this.dodgeCooldown = Math.max(0, this.dodgeCooldown - dt);
    this.regenDelay = Math.max(0, this.regenDelay - dt);
    this.landTimer = Math.max(0, this.landTimer - dt);

    const ix = this.intent.x, iz = this.intent.z;
    const inputLen = Math.min(1, Math.hypot(ix, iz));
    const dirX = inputLen > 1e-4 ? ix / Math.hypot(ix, iz) : 0;
    const dirZ = inputLen > 1e-4 ? iz / Math.hypot(ix, iz) : 0;

    // ---- Esquiva ----
    const d = this.cfg.dodge;
    if (this.dodgeBuffer > 0 && this.dodgeTimer <= 0 && this.dodgeCooldown <= 0 && motor.grounded && this.energy >= d.energyCost) {
      if (inputLen > 0.1) { this.dodgeDir.x = dirX; this.dodgeDir.z = dirZ; }
      else { this.dodgeDir.x = -Math.sin(this.facing); this.dodgeDir.z = -Math.cos(this.facing); }
      this.facing = Math.atan2(-this.dodgeDir.x, -this.dodgeDir.z);
      this.dodgeTimer = d.duration;
      this.dodgeBuffer = 0;
      this.energy -= d.energyCost;
      this.regenDelay = this.cfg.energy.regenDelay;
      this.sprinting = false;
      events.dodged = true;
    }

    if (this.dodgeTimer > 0) {
      // Perfil de velocidade linear decrescente; a área sob a curva = distância.
      const u = 1 - this.dodgeTimer / d.duration;
      const speed = 2 * (d.distance / d.duration) * (1 - u);
      v.x = this.dodgeDir.x * speed;
      v.z = this.dodgeDir.z * speed;
      this.dodgeTimer -= dt;
      if (this.dodgeTimer <= 0) { this.dodgeTimer = 0; this.dodgeCooldown = d.cooldown; }
    } else {
      // ---- Locomoção ----
      const e = this.cfg.energy;
      const wantsSprint = this.intent.sprint && inputLen > 0.1 && !this.walkMode;
      if (!wantsSprint || this.energy <= 0) this.sprinting = false;
      else if (!this.sprinting && this.energy >= e.minToSprint) this.sprinting = true;

      let maxSpeed = this.walkMode ? m.walkSpeed : this.sprinting ? m.sprintSpeed : m.runSpeed;
      if (this.landTimer > 0) maxSpeed *= 1 - 0.6 * this.landImpact; // aterrissagem pesada freia
      const tx = dirX * inputLen * maxSpeed, tz = dirZ * inputLen * maxSpeed;
      const rate = (inputLen > 0.01 ? m.acceleration : m.deceleration) * (motor.grounded ? 1 : m.airControl);
      const k = 1 - Math.exp(-rate * dt);
      v.x += (tx - v.x) * k;
      v.z += (tz - v.z) * k;

      if (inputLen > 0.1) {
        const target = Math.atan2(-dirX, -dirZ);
        this.facing += angleDelta(this.facing, target) * (1 - Math.exp(-m.turnSpeed * dt));
      }
      if (this.sprinting) {
        this.energy = Math.max(0, this.energy - e.sprintDrainPerSec * dt);
        this.regenDelay = e.regenDelay;
      }
    }

    // ---- Pulo e gravidade ----
    this.coyote = motor.grounded ? m.coyoteTime : Math.max(0, this.coyote - dt);
    if (this.jumpBuffer > 0 && this.coyote > 0 && this.dodgeTimer <= 0) {
      v.y = Math.sqrt(2 * m.gravity * m.jumpHeight);
      this.jumpBuffer = 0;
      this.coyote = 0;
      events.jumped = true;
    }
    if (!motor.grounded || v.y > 0) {
      v.y = Math.max(v.y - m.gravity * dt, -m.maxFallSpeed);
    }

    const res = motor.step(dt);
    if (res.landed) {
      this.landImpact = Math.min(1, res.impactSpeed / m.hardLandingSpeed);
      if (res.impactSpeed > 5) this.landTimer = 0.1 + 0.3 * this.landImpact;
      events.landed = true;
      events.landImpact = this.landImpact;
    }

    if (this.regenDelay <= 0 && !this.sprinting) {
      this.energy = Math.min(this.cfg.energy.max, this.energy + this.cfg.energy.regenPerSec * dt);
    }

    this.setState(this.resolveState(), dt);
    return events;
  }

  snapshot(): AnimationInput {
    return {
      state: this.state,
      stateTime: this.stateTime,
      speed: this.horizontalSpeed,
      verticalSpeed: this.motor.velocity.y,
      grounded: this.motor.grounded,
      dodgeProgress: this.dodgeTimer > 0 ? 1 - this.dodgeTimer / this.cfg.dodge.duration : 0,
      landImpact: this.landImpact,
      facing: this.facing,
    };
  }

  private resolveState(): LocomotionState {
    const m = this.cfg.movement;
    if (this.dodgeTimer > 0) return 'dodge';
    if (!this.motor.grounded) return this.motor.velocity.y > 0 ? 'jump' : 'fall';
    if (this.landTimer > 0) return 'land';
    const speed = this.horizontalSpeed;
    if (speed < 0.3) return 'idle';
    if (this.sprinting && speed > m.runSpeed + 0.5) return 'sprint';
    if (speed < (m.walkSpeed + m.runSpeed) / 2) return 'walk';
    return 'run';
  }

  private setState(next: LocomotionState, dt: number): void {
    if (next === this.state) this.stateTime += dt;
    else { this.state = next; this.stateTime = 0; }
  }
}
