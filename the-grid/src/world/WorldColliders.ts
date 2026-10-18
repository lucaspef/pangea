import { aabbFromFootprint, type Collider } from '../physics/Colliders';
import type { ArenaLayout } from './ArenaLayout';
import type { CityLayout } from './CityLayout';
import type { PropDef } from './Props';

/** Converte os dados do mundo em formas de colisão estáticas. */
export function buildWorldColliders(city: CityLayout, arenas: ArenaLayout, props: readonly PropDef[] = []): Collider[] {
  const out: Collider[] = [];
  for (const t of city.towers) {
    for (const tier of t.tiers) out.push(aabbFromFootprint(t.x, t.z, tier.width, tier.depth, tier.height, tier.baseY));
  }
  for (const b of city.bridges) {
    const w = b.axis === 'x' ? b.length : b.width;
    const d = b.axis === 'x' ? b.width : b.length;
    out.push(aabbFromFootprint(b.x, b.z, w, d, b.thickness, b.y - b.thickness / 2));
  }
  for (const p of city.platforms) {
    out.push(aabbFromFootprint(p.x, p.z, p.width, p.depth, p.thickness, p.y - p.thickness / 2));
  }
  out.push(arenas.combatRing, ...arenas.vehicleWalls, ...arenas.structures.map((s) => s.box));
  out.push(...props.map((p) => p.box));
  return out;
}
