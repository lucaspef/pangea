/**
 * Formas de colisão estáticas, independentes de renderização.
 * Unity: BoxCollider (Aabb) e um MeshCollider/segmentos para o anel da arena.
 */
export interface Aabb {
  kind: 'aabb';
  minX: number; minY: number; minZ: number;
  maxX: number; maxY: number; maxZ: number;
}

/** Parede circular (anel) com uma abertura opcional, centrada no plano XZ. */
export interface RingWall {
  kind: 'ring';
  cx: number; cz: number;
  innerRadius: number;
  outerRadius: number;
  height: number;
  /** Ângulo (rad, atan2(dz, dx)) do centro da abertura. */
  gateAngle: number;
  /** Meia-largura angular da abertura (rad); 0 = sem abertura. */
  gateHalfWidth: number;
}

export type Collider = Aabb | RingWall;

export function aabb(minX: number, minY: number, minZ: number, maxX: number, maxY: number, maxZ: number): Aabb {
  return { kind: 'aabb', minX, minY, minZ, maxX, maxY, maxZ };
}

/** AABB a partir de centro na base (x, z), dimensões e altura da base. */
export function aabbFromFootprint(x: number, z: number, width: number, depth: number, height: number, baseY = 0): Aabb {
  return aabb(x - width / 2, baseY, z - depth / 2, x + width / 2, baseY + height, z + depth / 2);
}

export function aabbOverlap(a: Aabb, b: Aabb, epsilon = 0): boolean {
  return a.minX < b.maxX - epsilon && a.maxX > b.minX + epsilon &&
         a.minY < b.maxY - epsilon && a.maxY > b.minY + epsilon &&
         a.minZ < b.maxZ - epsilon && a.maxZ > b.minZ + epsilon;
}
