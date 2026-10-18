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
  private readonly rings: RingWall[] = [];
  private count = 0;

  constructor(private readonly cellSize = 64) {}

  get size(): number {
    return this.count;
  }

  add(collider: Collider): void {
    this.count++;
    if (collider.kind === 'ring') {
      this.rings.push(collider);
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
    return this.rings.some((r) => sphereRingPush(p, radius, r) !== null);
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
      for (const r of this.rings) {
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
