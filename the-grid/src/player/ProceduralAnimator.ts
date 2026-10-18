import type { PlayerMovementConfig } from '../config/types';
import { SKELETON, type CharacterJoints } from './CharacterModel';
import type { AnimationInput } from './PlayerState';

type JointName = Exclude<keyof CharacterJoints, 'body'>;
interface Rot { x: number; y: number; z: number }

const JOINTS: JointName[] = [
  'hips', 'spine', 'chest', 'neck', 'head',
  'upperArmL', 'forearmL', 'upperArmR', 'forearmR',
  'thighL', 'shinL', 'footL', 'thighR', 'shinR', 'footR',
];

const clamp01 = (v: number) => Math.min(1, Math.max(0, v));
const lerp = (a: number, b: number, t: number) => a + (b - a) * t;
const easeInOut = (t: number) => t * t * (3 - 2 * t);

/**
 * Animação procedural do personagem.
 *
 * Cada quadro monta uma pose-alvo a partir do estado de locomoção e a
 * suaviza sobre a pose atual, o que dá transições contínuas sem clipes.
 * O ciclo de passada avança pela distância percorrida (sem "patinar").
 *
 * Convenções de rotação (frente = -Z): thigh/upperArm.x > 0 → membro à
 * frente; shin.x < 0 → joelho dobra; spine.x < 0 → inclina para a frente.
 *
 * Substituição futura: um animador baseado em clipes (AnimationMixer na web,
 * Animator na Unity) consome o mesmo AnimationInput.
 */
export class ProceduralAnimator {
  private readonly current = new Map<JointName, Rot>();
  private readonly target = new Map<JointName, Rot>();
  private phase = 0;
  private time = 0;
  private hipsOffset = 0;
  private hipsOffsetTarget = 0;

  constructor(private readonly joints: CharacterJoints, private readonly move: PlayerMovementConfig) {
    for (const j of JOINTS) {
      this.current.set(j, { x: 0, y: 0, z: 0 });
      this.target.set(j, { x: 0, y: 0, z: 0 });
    }
  }

  update(input: AnimationInput, dt: number): void {
    this.time += dt;
    for (const r of this.target.values()) r.x = r.y = r.z = 0;
    this.hipsOffsetTarget = 0;

    this.locomotion(input, dt);
    if (input.state === 'jump' || input.state === 'fall') this.air(input);
    if (input.state === 'land') this.land(input);
    const dodging = input.state === 'dodge';
    if (dodging) this.dodge(input);

    // Suavização exponencial (mais rápida durante a esquiva).
    const k = 1 - Math.exp(-(dodging ? 40 : 18) * dt);
    for (const j of JOINTS) {
      const c = this.current.get(j)!, t = this.target.get(j)!;
      c.x += (t.x - c.x) * k; c.y += (t.y - c.y) * k; c.z += (t.z - c.z) * k;
      this.joints[j].rotation.set(c.x, c.y, c.z);
    }
    this.hipsOffset += (this.hipsOffsetTarget - this.hipsOffset) * k;
    this.joints.hips.position.y = SKELETON.hipsY - SKELETON.bodyPivotY + this.hipsOffset;

    // A rolagem é aplicada diretamente (2π ≡ 0: sem "desenrolar" ao terminar).
    const body = this.joints.body;
    if (dodging) {
      const u = easeInOut(clamp01(input.dodgeProgress));
      body.rotation.x = -Math.PI * 2 * u;
      body.position.y = SKELETON.bodyPivotY - 0.22 * Math.sin(Math.PI * u);
    } else {
      body.rotation.x = 0;
      body.position.y = SKELETON.bodyPivotY;
    }
  }

  private set(j: JointName, x: number, y = 0, z = 0): void {
    const r = this.target.get(j)!;
    r.x = x; r.y = y; r.z = z;
  }

  private add(j: JointName, x: number, y = 0, z = 0): void {
    const r = this.target.get(j)!;
    r.x += x; r.y += y; r.z += z;
  }

  /** Idle ↔ caminhada ↔ corrida ↔ sprint, misturados pela velocidade. */
  private locomotion(input: AnimationInput, dt: number): void {
    const m = this.move;
    const speed = input.grounded && input.state !== 'dodge' ? input.speed : 0;
    const g = clamp01(speed / m.walkSpeed);                         // idle → movimento
    const a = clamp01((speed - m.walkSpeed) / (m.runSpeed - m.walkSpeed)); // caminhada → corrida
    const b = clamp01((speed - m.runSpeed) / (m.sprintSpeed - m.runSpeed)); // corrida → sprint

    const stride = lerp(0.72, 1.2, a) + 0.4 * b;
    this.phase = (this.phase + (dt * speed * Math.PI) / stride) % (Math.PI * 2);
    const sp = Math.sin(this.phase), cp = Math.cos(this.phase);

    const leg = lerp(0.42, 0.82, a) + 0.22 * b;
    const knee = lerp(0.55, 1.3, a) + 0.4 * b;
    const arm = lerp(0.3, 0.7, a) + 0.25 * b;
    const elbow = lerp(0.25, 1.3, a) + 0.25 * b;
    const lean = lerp(0.04, 0.2, a) + 0.16 * b;
    const bob = lerp(0.02, 0.06, a);

    // Idle: respiração e leve balanço.
    const breathe = Math.sin(this.time * 1.7);
    const idle = 1 - g;
    this.set('chest', 0.02 * breathe * idle);
    this.set('head', 0, 0.08 * Math.sin(this.time * 0.37) * idle);
    this.set('upperArmL', 0, 0, -0.12 * idle - 0.06 * g);
    this.set('upperArmR', 0, 0, 0.12 * idle + 0.06 * g);
    this.set('forearmL', 0.18 * idle);
    this.set('forearmR', 0.18 * idle);
    this.set('thighL', 0.03 * idle);
    this.set('thighR', -0.02 * idle);

    if (g <= 0) return;
    this.add('thighL', g * leg * sp);
    this.add('thighR', -g * leg * sp);
    const shinL = -(0.08 + knee * Math.max(0, cp));
    const shinR = -(0.08 + knee * Math.max(0, -cp));
    this.add('shinL', g * shinL);
    this.add('shinR', g * shinR);
    // Pé acompanha para tocar o chão plano.
    this.add('footL', -g * (leg * sp + shinL) * 0.45);
    this.add('footR', -g * (-leg * sp + shinR) * 0.45);
    this.add('upperArmL', -g * arm * sp);
    this.add('upperArmR', g * arm * sp);
    this.add('forearmL', g * (elbow + 0.15 * sp));
    this.add('forearmR', g * (elbow - 0.15 * sp));
    this.add('spine', -g * lean);
    this.add('head', g * lean * 0.6);
    this.add('hips', 0, -0.08 * g * sp);
    this.add('chest', 0, 0.14 * g * sp);
    this.hipsOffsetTarget -= g * bob * Math.abs(sp);
  }

  private air(input: AnimationInput): void {
    // 0 = subindo (encolhido), 1 = caindo (braços abertos).
    const f = clamp01(0.5 - input.verticalSpeed / 8);
    this.set('thighL', lerp(0.95, 0.35, f));
    this.set('shinL', lerp(-1.3, -0.7, f));
    this.set('thighR', lerp(0.15, 0.1, f));
    this.set('shinR', lerp(-0.4, -0.45, f));
    this.set('footL', lerp(0.3, 0.2, f));
    this.set('footR', 0.15);
    this.set('upperArmL', lerp(0.5, 0.15, f), 0, lerp(-0.45, -0.95, f));
    this.set('upperArmR', lerp(0.3, 0.15, f), 0, lerp(0.45, 0.95, f));
    this.set('forearmL', lerp(0.7, 0.35, f));
    this.set('forearmR', lerp(0.7, 0.35, f));
    this.set('spine', lerp(-0.1, 0.04, f));
  }

  private land(input: AnimationInput): void {
    const k = input.landImpact * clamp01(1 - input.stateTime / 0.4);
    this.add('thighL', 0.75 * k);
    this.add('thighR', 0.75 * k);
    this.add('shinL', -1.35 * k);
    this.add('shinR', -1.35 * k);
    this.add('footL', 0.6 * k);
    this.add('footR', 0.6 * k);
    this.add('spine', -0.35 * k);
    this.add('upperArmL', 0.3 * k, 0, -0.35 * k);
    this.add('upperArmR', 0.3 * k, 0, 0.35 * k);
    this.hipsOffsetTarget -= 0.3 * k;
  }

  private dodge(input: AnimationInput): void {
    const tuck = Math.sin(Math.PI * clamp01(input.dodgeProgress));
    this.set('thighL', 1.75 * tuck);
    this.set('thighR', 1.6 * tuck);
    this.set('shinL', -2.1 * tuck);
    this.set('shinR', -2.0 * tuck);
    this.set('spine', -0.7 * tuck);
    this.set('chest', -0.3 * tuck);
    this.set('head', -0.5 * tuck);
    this.set('upperArmL', 1.3 * tuck, 0, -0.2 * tuck);
    this.set('upperArmR', 1.3 * tuck, 0, 0.2 * tuck);
    this.set('forearmL', 1.6 * tuck);
    this.set('forearmR', 1.6 * tuck);
  }
}
