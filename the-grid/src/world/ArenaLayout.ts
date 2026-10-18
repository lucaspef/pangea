import type { WorldConfig } from '../config/types';
import { aabb, aabbFromFootprint, type Aabb, type RingWall } from '../physics/Colliders';
import type { AccentColor } from './CityLayout';

/** Peça sólida decorativa das arenas (pilares, torres de iluminação, pórticos). */
export interface ArenaStructure {
  box: Aabb;
  accent: AccentColor;
  /** Sinalizador luminoso no topo. */
  beacon: boolean;
}

/** Estrutura física das arenas (Setores B e C) derivada de `world.json`. */
export interface ArenaLayout {
  combatRing: RingWall;
  vehicleWalls: Aabb[];
  structures: ArenaStructure[];
}

/** Ângulo do portão da arena de combate (atan2(dz, dx)): voltado para +Z, a cidade. */
export const COMBAT_GATE_ANGLE = Math.PI / 2;

export function buildArenaLayout(cfg: WorldConfig): ArenaLayout {
  const ca = cfg.sectors.combatArena;
  const gateHalf = ((ca.gateWidthDeg / 2) * Math.PI) / 180;
  const combatRing: RingWall = {
    kind: 'ring',
    cx: ca.center[0], cz: ca.center[1],
    innerRadius: ca.innerRadius,
    outerRadius: ca.outerRadius,
    height: ca.wallHeight,
    gateAngle: COMBAT_GATE_ANGLE,
    gateHalfWidth: gateHalf,
  };

  const structures: ArenaStructure[] = [];
  const polar = (r: number, a: number): [number, number] => [ca.center[0] + r * Math.cos(a), ca.center[1] + r * Math.sin(a)];

  // Pilares do portão, nas extremidades do anel.
  const mid = (ca.innerRadius + ca.outerRadius) / 2;
  for (const side of [-1, 1]) {
    const [x, z] = polar(mid, COMBAT_GATE_ANGLE + side * gateHalf);
    structures.push({ box: aabbFromFootprint(x, z, 6, 6, ca.wallHeight + 16), accent: 'orange', beacon: true });
  }
  // Obeliscos ao redor da arena (exceto na frente do portão).
  const pylonCount = 10;
  for (let i = 0; i < pylonCount; i++) {
    const a = COMBAT_GATE_ANGLE + Math.PI / pylonCount + (i * 2 * Math.PI) / pylonCount;
    const [x, z] = polar(ca.outerRadius + 26, a);
    structures.push({ box: aabbFromFootprint(x, z, 4, 4, 64), accent: 'cyan', beacon: true });
  }

  const va = cfg.sectors.vehicleArena;
  const [cx, cz] = va.center;
  const [hx, hz] = va.halfSize;
  const t = va.wallThickness, h = va.wallHeight, g = va.gateWidth / 2;
  const vehicleWalls: Aabb[] = [
    aabb(cx - hx, 0, cz - hz, cx + hx, h, cz - hz + t), // norte
    aabb(cx - hx, 0, cz + hz - t, cx + hx, h, cz + hz), // sul
    aabb(cx + hx - t, 0, cz - hz, cx + hx, h, cz + hz), // leste
    aabb(cx - hx, 0, cz - hz, cx - hx + t, h, cz - g),  // oeste, ao norte do portão
    aabb(cx - hx, 0, cz + g, cx - hx + t, h, cz + hz),  // oeste, ao sul do portão
  ];
  // Torres de iluminação nos cantos.
  for (const [kx, kz] of [[-1, -1], [1, -1], [-1, 1], [1, 1]] as const) {
    structures.push({ box: aabbFromFootprint(cx + kx * (hx + 6), cz + kz * (hz + 6), 8, 8, 78), accent: 'orange', beacon: true });
  }
  // Pórtico do portão oeste: dois pilares e uma viga superior.
  for (const side of [-1, 1]) {
    structures.push({ box: aabbFromFootprint(cx - hx + t / 2, cz + side * (g + 3), 6, 6, h + 14), accent: 'orange', beacon: false });
  }
  structures.push({ box: aabb(cx - hx - 1, h + 9, cz - g - 6, cx - hx + t + 1, h + 14, cz + g + 6), accent: 'orange', beacon: false });

  return { combatRing, vehicleWalls, structures };
}
