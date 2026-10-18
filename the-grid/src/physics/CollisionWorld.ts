import type { Aabb, Collider, RingWall } from './Colliders';

export interface MutableVec3 { x: number; y: number; z: number }

/**
 * Mundo de colisão estático com hash espacial uniforme no plano XZ.
 *
 * Marco 1: usado pela câmera para não atravessar construções.
 * Marco 2+: base para colisão do personagem (o controlador cinemático
 * resolverá cápsulas contra estas mesmas formas).
 */
export class CollisionWorld {
  private readonly cells = new Map<string, Aabb[]>();
  private readonly rings_: RingWall[] = [];
  private count = 0;

  constructor(private readonly cellSize = 64) {}

  get size(): number {
    return this.count;
  }

  /** Paredes anelares registradas (poucas; testadas sem hash espacial). */
  get rings(): readonly RingWall[] {
    return this.rings_;
  }

  add(collider: Collider): void {
    this.count++;
    if (collider.kind === 'ring') {
      this.rings_.push(collider);
      return;
    }
    const [x0, z0, x1, z1] = this.cellRange(collider.minX, collider.minZ, collider.maxX, collider.maxZ);
    for (let cx = x0; cx <= x1; cx++) {
      for (let cz = z0; cz <= z1; cz++) {
        const key = `${cx},${cz}`;
        let list = this.cells.get(key);
        if (!list) this.cells.set(key, (list = []));
        list.push(collider);
      }
    }
  }

  addAll(colliders: Iterable<Collider>): void {
    for (const c of colliders) this.add(c);
  }

  /** AABBs cujas células tocam a região informada (sem duplicatas). */
  queryAabbs(minX: number, minZ: number, maxX: number, maxZ: number): Aabb[] {
    const [x0, z0, x1, z1] = this.cellRange(minX, minZ, maxX, maxZ);
    const out = new Set<Aabb>();
    for (let cx = x0; cx <= x1; cx++) {
      for (let cz = z0; cz <= z1; cz++) {
        const list = this.cells.get(`${cx},${cz}`);
        if (list) for (const b of list) out.add(b);
      }
    }
    return [...out];
  }

  /** Verdadeiro se a esfera intersecta alguma forma. */
  overlapsSphere(p: MutableVec3, radius: number): boolean {
    for (const b of this.queryAabbs(p.x - radius, p.z - radius, p.x + radius, p.z + radius)) {
      if (sphereAabbPenetration(p, radius, b) !== null) return true;
    }
    return this.rings_.some((r) => sphereRingPush(p, radius, r) !== null);
  }

  /**
   * Empurra a esfera para fora de todas as formas que ela penetra.
   * Modifica `p` e retorna true se houve correção.
   */
  resolveSphere(p: MutableVec3, radius: number, iterations = 3): boolean {
    let corrected = false;
    for (let it = 0; it < iterations; it++) {
      let any = false;
      for (const b of this.queryAabbs(p.x - radius, p.z - radius, p.x + radius, p.z + radius)) {
        const push = sphereAabbPenetration(p, radius, b);
        if (push) {
          p.x += push.x; p.y += push.y; p.z += push.z;
          any = true;
        }
      }
      for (const r of this.rings_) {
        const push = sphereRingPush(p, radius, r);
        if (push) {
          p.x += push.x; p.z += push.z;
          any = true;
        }
      }
      if (!any) break;
      corrected = true;
    }
    return corrected;
  }

  /**
   * Distância até a primeira superfície ao longo de um raio (dir normalizada),
   * ou `maxDist` se nada for atingido. Usado pela câmera em terceira pessoa.
   */
  raycast(origin: MutableVec3, dir: MutableVec3, maxDist: number): number {
    const ex = origin.x + dir.x * maxDist, ez = origin.z + dir.z * maxDist;
    let best = maxDist;
    for (const b of this.queryAabbs(Math.min(origin.x, ex), Math.min(origin.z, ez), Math.max(origin.x, ex), Math.max(origin.z, ez))) {
      const t = rayAabb(origin, dir, b);
      if (t !== null && t < best) best = t;
    }
    // Anéis: amostragem ao longo do raio (o raio da câmera é curto).
    for (const r of this.rings_) {
      const step = 0.2;
      const probe = { x: 0, y: 0, z: 0 };
      for (let t = 0; t < best; t += step) {
        probe.x = origin.x + dir.x * t; probe.y = origin.y + dir.y * t; probe.z = origin.z + dir.z * t;
        if (sphereRingPush(probe, 0.01, r)) { best = Math.min(best, t); break; }
      }
    }
    return best;
  }

  private cellRange(minX: number, minZ: number, maxX: number, maxZ: number): [number, number, number, number] {
    const s = this.cellSize;
    return [Math.floor(minX / s), Math.floor(minZ / s), Math.floor(maxX / s), Math.floor(maxZ / s)];
  }
}

/** Vetor mínimo para tirar a esfera de dentro do AABB, ou null se não há contato. */
export function sphereAabbPenetration(p: MutableVec3, r: number, b: Aabb): MutableVec3 | null {
  const qx = Math.max(b.minX, Math.min(p.x, b.maxX));
  const qy = Math.max(b.minY, Math.min(p.y, b.maxY));
  const qz = Math.max(b.minZ, Math.min(p.z, b.maxZ));
  const dx = p.x - qx, dy = p.y - qy, dz = p.z - qz;
  const d2 = dx * dx + dy * dy + dz * dz;

  if (d2 > 0) {
    if (d2 >= r * r) return null;
    const d = Math.sqrt(d2);
    const k = (r - d) / d;
    return { x: dx * k, y: dy * k, z: dz * k };
  }

  // Centro dentro da caixa: sai pela face mais próxima.
  const candidates: [number, MutableVec3][] = [
    [p.x - b.minX, { x: -(p.x - b.minX + r), y: 0, z: 0 }],
    [b.maxX - p.x, { x: b.maxX - p.x + r, y: 0, z: 0 }],
    [p.y - b.minY, { x: 0, y: -(p.y - b.minY + r), z: 0 }],
    [b.maxY - p.y, { x: 0, y: b.maxY - p.y + r, z: 0 }],
    [p.z - b.minZ, { x: 0, y: 0, z: -(p.z - b.minZ + r) }],
    [b.maxZ - p.z, { x: 0, y: 0, z: b.maxZ - p.z + r }],
  ];
  candidates.sort((a, c) => a[0] - c[0]);
  return candidates[0]![1];
}

/** Empurrão horizontal para fora da parede anelar, ou null se não há contato. */
export function sphereRingPush(p: MutableVec3, r: number, ring: RingWall): { x: number; z: number } | null {
  if (p.y - r > ring.height || p.y + r < 0) return null;
  const dx = p.x - ring.cx, dz = p.z - ring.cz;
  const dist = Math.hypot(dx, dz);
  if (dist < ring.innerRadius - r || dist > ring.outerRadius + r) return null;

  if (ring.gateHalfWidth > 0) {
    let delta = Math.atan2(dz, dx) - ring.gateAngle;
    delta = Math.atan2(Math.sin(delta), Math.cos(delta));
    // Margem angular equivalente ao raio da esfera: as bordas do portão também colidem.
    const margin = dist > 0 ? Math.asin(Math.min(1, r / Math.max(dist, r))) : 0;
    if (Math.abs(delta) < ring.gateHalfWidth - margin) return null;
  }

  const nx = dist > 1e-6 ? dx / dist : 1, nz = dist > 1e-6 ? dz / dist : 0;
  const mid = (ring.innerRadius + ring.outerRadius) / 2;
  const target = dist < mid ? ring.innerRadius - r : ring.outerRadius + r;
  const amount = target - dist;
  return { x: nx * amount, z: nz * amount };
}

/** Interseção raio × AABB pelo método das placas; retorna t ≥ 0 ou null. */
export function rayAabb(o: MutableVec3, d: MutableVec3, b: Aabb): number | null {
  let tmin = 0, tmax = Infinity;
  for (const axis of ['x', 'y', 'z'] as const) {
    const min = axis === 'x' ? b.minX : axis === 'y' ? b.minY : b.minZ;
    const max = axis === 'x' ? b.maxX : axis === 'y' ? b.maxY : b.maxZ;
    if (Math.abs(d[axis]) < 1e-9) {
      if (o[axis] < min || o[axis] > max) return null;
      continue;
    }
    let t1 = (min - o[axis]) / d[axis];
    let t2 = (max - o[axis]) / d[axis];
    if (t1 > t2) [t1, t2] = [t2, t1];
    tmin = Math.max(tmin, t1);
    tmax = Math.min(tmax, t2);
    if (tmin > tmax) return null;
  }
  return tmin;
}
