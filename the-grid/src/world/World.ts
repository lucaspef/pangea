import type { WorldConfig } from '../config/types';
import { CollisionWorld } from '../physics/CollisionWorld';
import { buildArenaLayout, type ArenaLayout } from './ArenaLayout';
import { CityGenerator } from './CityGenerator';
import type { CityLayout } from './CityLayout';
import { buildProps, type PropDef } from './Props';
import { SectorMap } from './SectorMap';
import { TrafficSystem } from './TrafficSystem';
import { buildWorldColliders } from './WorldColliders';

/**
 * Estado lógico do mundo: layout gerado, colisão, setores e sistemas
 * ambientes. Não conhece Three.js — a representação visual fica em WorldView.
 */
export class World {
  readonly city: CityLayout;
  readonly arenas: ArenaLayout;
  readonly props: PropDef[];
  readonly collision: CollisionWorld;
  readonly sectors: SectorMap;
  readonly traffic: TrafficSystem;

  constructor(readonly config: WorldConfig) {
    this.city = new CityGenerator(config.city, config.seed).generate();
    this.arenas = buildArenaLayout(config);
    this.props = buildProps(config.props);
    this.collision = new CollisionWorld();
    this.collision.addAll(buildWorldColliders(this.city, this.arenas, this.props));
    this.sectors = new SectorMap(config);
    this.traffic = new TrafficSystem(this.city, config);
  }
}
