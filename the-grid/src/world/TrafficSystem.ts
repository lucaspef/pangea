import type { TrafficConfig, WorldConfig } from '../config/types';
import type { GameSystem } from '../core/GameSystem';
import { Rng } from '../core/Rng';
import type { CityLayout } from './CityLayout';

/** Uma faixa de luz em movimento ("pacote de dados" percorrendo as avenidas). */
export interface LightRunner {
  axis: 'x' | 'z';
  /** Coordenada fixa perpendicular ao movimento. */
  lane: number;
  dir: 1 | -1;
  min: number;
  max: number;
  pos: number;
  speed: number;
  length: number;
  y: number;
  orange: boolean;
}

/**
 * Tráfego ambiente: corredores de luz trafegando pelas avenidas e pelos
 * boulevards que levam às arenas. Lógica pura; o TrafficRenderer só lê o estado.
 */
export class TrafficSystem implements GameSystem {
  readonly name = 'traffic';
  readonly runners: LightRunner[] = [];

  constructor(city: CityLayout, world: WorldConfig, cfg: TrafficConfig = world.traffic, seed = world.seed + 7) {
    const rng = new Rng(seed);
    const lanes = buildLanes(city, world);
    for (let i = 0; i < cfg.count; i++) {
      const lane = rng.pick(lanes);
      const dir: 1 | -1 = rng.chance(0.5) ? 1 : -1;
      this.runners.push({
        axis: lane.axis,
        lane: lane.coord + dir * (city.avenueWidth / 4),
        dir,
        min: lane.min,
        max: lane.max,
        pos: rng.range(lane.min, lane.max),
        speed: rng.range(cfg.speedMin, cfg.speedMax) * (lane.boulevard ? 1.4 : 1),
        length: rng.range(cfg.lengthMin, cfg.lengthMax),
        y: 0.6,
        orange: lane.boulevard ? rng.chance(0.45) : rng.chance(0.12),
      });
    }
  }

  fixedUpdate(dt: number): void {
    for (const r of this.runners) {
      r.pos += r.dir * r.speed * dt;
      const span = r.max - r.min;
      if (r.pos > r.max) r.pos -= span;
      else if (r.pos < r.min) r.pos += span;
    }
  }
}

interface Lane { axis: 'x' | 'z'; coord: number; min: number; max: number; boulevard: boolean }

/** Faixas: todas as avenidas da cidade; os boulevards se estendem até as arenas. */
export function buildLanes(city: CityLayout, world: WorldConfig): Lane[] {
  const ca = world.sectors.combatArena;
  const va = world.sectors.vehicleArena;
  const lanes: Lane[] = [];
  city.avenuesX.forEach((x, i) => {
    const boulevard = i === city.boulevardX;
    // Boulevard norte-sul: da borda sul da cidade até o portão da arena de combate.
    const min = boulevard ? ca.center[1] + ca.outerRadius : -city.halfZ;
    lanes.push({ axis: 'z', coord: x, min, max: city.halfZ, boulevard });
  });
  city.avenuesZ.forEach((z, i) => {
    const boulevard = i === city.boulevardZ;
    // Boulevard leste-oeste: até o portão da arena de veículos.
    const max = boulevard ? va.center[0] - va.halfSize[0] : city.halfX;
    lanes.push({ axis: 'x', coord: z, min: -city.halfX, max, boulevard });
  });
  return lanes;
}
