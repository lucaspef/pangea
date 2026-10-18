import type { WorldConfig } from '../config/types';

export type SectorId = 'city' | 'combatArena' | 'vehicleArena' | 'transit';

export const SECTOR_LABELS: Record<SectorId, { code: string; name: string }> = {
  city: { code: 'SETOR A', name: 'Cidade Digital' },
  combatArena: { code: 'SETOR B', name: 'Arena de Combate' },
  vehicleArena: { code: 'SETOR C', name: 'Arena de Veículos' },
  transit: { code: 'TRÂNSITO', name: 'Vias de Conexão' },
};

/** Classifica uma posição do plano XZ no setor correspondente. */
export class SectorMap {
  private readonly cityHalfX: number;
  private readonly cityHalfZ: number;

  constructor(private readonly config: WorldConfig) {
    const c = config.city;
    this.cityHalfX = (c.blocksX * c.blockSize + (c.blocksX + 1) * c.avenueWidth) / 2;
    this.cityHalfZ = (c.blocksZ * c.blockSize + (c.blocksZ + 1) * c.avenueWidth) / 2;
  }

  sectorAt(x: number, z: number): SectorId {
    const arena = this.config.sectors.combatArena;
    if (Math.hypot(x - arena.center[0], z - arena.center[1]) <= arena.outerRadius) return 'combatArena';
    const va = this.config.sectors.vehicleArena;
    if (Math.abs(x - va.center[0]) <= va.halfSize[0] && Math.abs(z - va.center[1]) <= va.halfSize[1]) {
      return 'vehicleArena';
    }
    if (Math.abs(x) <= this.cityHalfX && Math.abs(z) <= this.cityHalfZ) return 'city';
    return 'transit';
  }
}
