import type { PropConfig } from '../config/types';
import { aabbFromFootprint, type Aabb } from '../physics/Colliders';
import type { AccentColor } from './CityLayout';

/** Bloco sólido do cenário montado à mão (definido em world.json → props). */
export interface PropDef {
  id: string;
  box: Aabb;
  accent: AccentColor;
  beacon: boolean;
}

export function buildProps(configs: readonly PropConfig[]): PropDef[] {
  return configs.map((c) => ({
    id: c.id,
    box: aabbFromFootprint(c.x, c.z, c.w, c.d, c.h, c.y ?? 0),
    accent: c.accent ?? 'cyan',
    beacon: c.beacon ?? false,
  }));
}
