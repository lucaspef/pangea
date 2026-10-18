import type { CapsuleConfig } from '../config/types';
import type { RingWall } from './Colliders';
import type { CollisionWorld, MutableVec3 } from './CollisionWorld';

/**
 * Controlador cinemático de personagem.
 *
 * O corpo é um cilindro vertical (raio, altura) com a origem nos pés. Como o
 * cenário é composto por caixas alinhadas aos eixos e anéis, o cilindro dá
 * resultados exatos e estáveis:
 *  - horizontal: círculo × retângulo, só contra obstáculos mais altos que o degrau;
 *  - vertical: chão = topo mais alto sob a pegada; teto = base mais baixa acima da cabeça;
 *  - degraus até `stepHeight` são subidos automaticamente e descidos com "snap".
 *
 * Equivalente Unity: CharacterController (stepOffset, skinWidth) + Move().
 */
export interface MotorStepResult {
  /** Tocou o chão neste passo vindo do ar. */
  landed: boolean;
  /** Velocidade vertical (positiva) no instante do toque. */
  impactSpeed: number;
  hitCeiling: boolean;
  hitWall: boolean;
}

const SKIN = 0.001;

export class CharacterMotor {
  readonly position: MutableVec3 = { x: 0, y: 0, z: 0 };
  readonly velocity: MutableVec3 = { x: 0, y: 0, z: 0 };
  grounded = false;
  /** Altura do chão sob o personagem no último passo. */
  groundY = 0;

  constructor(
    private readonly world: CollisionWorld,
    readonly shape: CapsuleConfig,
    private readonly bounds: number,
  ) {}

  teleport(x: number, y: number, z: number): void {
    this.position.x = x; this.position.y = y; this.position.z = z;
    this.velocity.x = this.velocity.y = this.velocity.z = 0;
    this.grounded = false;
    this.groundY = this.findGround(x, z, y + this.shape.stepHeight);
    if (y <= this.groundY + this.shape.stepHeight) {
      this.position.y = this.groundY;
      this.grounded = true;
    }
  }

  /** Integra a velocidade atual por `dt` resolvendo colisões. */
  step(dt: number): MotorStepResult {
    const result: MotorStepResult = { landed: false, impactSpeed: 0, hitCeiling: false, hitWall: false };
    const p = this.position, v = this.velocity, s = this.shape;
    const wasGrounded = this.grounded;

    // ---- Horizontal ----
    p.x += v.x * dt;
    p.z += v.z * dt;
    for (let i = 0; i < 3; i++) {
      const push = this.horizontalPush();
      if (!push) break;
      result.hitWall = true;
      p.x += push.x; p.z += push.z;
      // Remove a componente da velocidade contra a parede (deslizar).
      const len = Math.hypot(push.x, push.z);
      if (len > 0) {
        const nx = push.x / len, nz = push.z / len;
        const into = v.x * nx + v.z * nz;
        if (into < 0) { v.x -= into * nx; v.z -= into * nz; }
      }
    }
    const b = this.bounds - s.radius;
    p.x = Math.max(-b, Math.min(b, p.x));
    p.z = Math.max(-b, Math.min(b, p.z));

    // ---- Vertical ----
    const prevY = p.y;
    let ny = p.y + v.y * dt;

    if (v.y > 0) {
      const ceiling = this.findCeiling(p.x, p.z, prevY + s.height);
      if (ny + s.height > ceiling) {
        ny = ceiling - s.height - SKIN;
        v.y = 0;
        result.hitCeiling = true;
      }
    }

    // O chão aceito: tudo abaixo dos pés + degrau (se estava no chão) ou só abaixo dos pés (no ar).
    const reach = Math.max(prevY, ny) + (wasGrounded ? s.stepHeight : SKIN * 10);
    const ground = this.findGround(p.x, p.z, reach);
    this.groundY = ground;

    if (ny <= ground) {
      if (!wasGrounded) {
        result.landed = true;
        result.impactSpeed = Math.max(0, -v.y);
      }
      ny = ground;
      if (v.y < 0) v.y = 0;
      this.grounded = true;
    } else if (wasGrounded && v.y <= 0 && ny - ground <= s.stepHeight) {
      // Descendo um degrau: gruda no chão em vez de "voar" da borda.
      ny = ground;
      v.y = 0;
      this.grounded = true;
    } else {
      this.grounded = false;
    }
    p.y = ny;
    return result;
  }

  /** Topo mais alto (≤ maxY) sob a pegada do personagem; o piso do mundo é y = 0. */
  findGround(x: number, z: number, maxY: number): number {
    const r = this.shape.radius * 0.85;
    let ground = 0;
    for (const box of this.world.queryAabbs(x - r, z - r, x + r, z + r)) {
      if (box.maxY > maxY || box.maxY <= ground) continue;
      if (circleRectOverlap(x, z, r, box.minX, box.minZ, box.maxX, box.maxZ)) ground = box.maxY;
    }
    for (const ring of this.world.rings) {
      if (ring.height > maxY || ring.height <= ground) continue;
      if (ringContainsCircle(ring, x, z, r)) ground = ring.height;
    }
    return ground;
  }

  /** Base mais baixa (≥ fromY) acima da cabeça. */
  private findCeiling(x: number, z: number, fromY: number): number {
    const r = this.shape.radius * 0.85;
    let ceiling = Infinity;
    for (const box of this.world.queryAabbs(x - r, z - r, x + r, z + r)) {
      if (box.minY < fromY - SKIN * 10 || box.minY >= ceiling) continue;
      if (circleRectOverlap(x, z, r, box.minX, box.minZ, box.maxX, box.maxZ)) ceiling = box.minY;
    }
    return ceiling;
  }

  /** Correção horizontal contra o obstáculo mais penetrado, ou null. */
  private horizontalPush(): { x: number; z: number } | null {
    const p = this.position, s = this.shape, r = s.radius;
    const feet = p.y + s.stepHeight, head = p.y + s.height;
    let best: { x: number; z: number } | null = null;
    let bestDepth = 0;

    for (const box of this.world.queryAabbs(p.x - r, p.z - r, p.x + r, p.z + r)) {
      // Só bloqueia o que está na faixa vertical do corpo acima do degrau.
      if (box.maxY <= feet || box.minY >= head) continue;
      const push = circleRectPush(p.x, p.z, r, box.minX, box.minZ, box.maxX, box.maxZ);
      if (push && push.depth > bestDepth) { best = push; bestDepth = push.depth; }
    }
    for (const ring of this.world.rings) {
      if (ring.height <= feet || 0 >= head) continue;
      const push = circleRingPush(p.x, p.z, r, ring);
      if (push && push.depth > bestDepth) { best = push; bestDepth = push.depth; }
    }
    return best;
  }
}

// ---------- Geometria 2D (plano XZ) ----------

export function circleRectOverlap(x: number, z: number, r: number, minX: number, minZ: number, maxX: number, maxZ: number): boolean {
  const qx = Math.max(minX, Math.min(x, maxX)), qz = Math.max(minZ, Math.min(z, maxZ));
  return (x - qx) ** 2 + (z - qz) ** 2 < r * r;
}

function circleRectPush(x: number, z: number, r: number, minX: number, minZ: number, maxX: number, maxZ: number) {
  const qx = Math.max(minX, Math.min(x, maxX)), qz = Math.max(minZ, Math.min(z, maxZ));
  const dx = x - qx, dz = z - qz;
  const d2 = dx * dx + dz * dz;
  if (d2 > 0) {
    if (d2 >= r * r) return null;
    const d = Math.sqrt(d2);
    const depth = r - d + SKIN;
    return { x: (dx / d) * depth, z: (dz / d) * depth, depth };
  }
  // Centro dentro do retângulo: sai pelo lado mais próximo.
  const options = [
    { depth: x - minX + r, x: -(x - minX + r), z: 0 },
    { depth: maxX - x + r, x: maxX - x + r, z: 0 },
    { depth: z - minZ + r, x: 0, z: -(z - minZ + r) },
    { depth: maxZ - z + r, x: 0, z: maxZ - z + r },
  ];
  return options.reduce((a, b) => (b.depth < a.depth ? b : a));
}

function angleInGate(ring: { gateAngle: number; gateHalfWidth: number }, x: number, z: number, cx: number, cz: number, margin: number): boolean {
  if (ring.gateHalfWidth <= 0) return false;
  let delta = Math.atan2(z - cz, x - cx) - ring.gateAngle;
  delta = Math.atan2(Math.sin(delta), Math.cos(delta));
  return Math.abs(delta) < ring.gateHalfWidth - margin;
}

function circleRingPush(x: number, z: number, r: number, ring: RingWall) {
  const dx = x - ring.cx, dz = z - ring.cz;
  const dist = Math.hypot(dx, dz);
  if (dist < ring.innerRadius - r || dist > ring.outerRadius + r) return null;
  const margin = Math.asin(Math.min(1, r / Math.max(dist, r)));
  if (angleInGate(ring, x, z, ring.cx, ring.cz, margin)) return null;
  const nx = dist > 1e-6 ? dx / dist : 1, nz = dist > 1e-6 ? dz / dist : 0;
  const toInner = dist - (ring.innerRadius - r);
  const toOuter = ring.outerRadius + r - dist;
  if (toInner < toOuter) return { x: -nx * (toInner + SKIN), z: -nz * (toInner + SKIN), depth: toInner };
  return { x: nx * (toOuter + SKIN), z: nz * (toOuter + SKIN), depth: toOuter };
}

function ringContainsCircle(ring: RingWall, x: number, z: number, r: number): boolean {
  const dist = Math.hypot(x - ring.cx, z - ring.cz);
  if (dist < ring.innerRadius - r || dist > ring.outerRadius + r) return false;
  return !angleInGate(ring, x, z, ring.cx, ring.cz, 0);
}
