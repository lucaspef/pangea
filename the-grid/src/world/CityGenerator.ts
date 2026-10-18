import type { CityConfig } from '../config/types';
import { Rng } from '../core/Rng';
import type { AccentColor, BridgeDef, CityLayout, PlatformDef, StripStyle, TowerDef, TowerShape, TowerTier } from './CityLayout';

interface Lot { x: number; z: number; w: number; d: number; block: [number, number] }

/**
 * Gera proceduralmente a Cidade Digital (Setor A) de forma determinística.
 *
 * Regras de layout:
 *  - malha de quarteirões separados por avenidas; a avenida central em cada
 *    eixo é um boulevard que conecta a cidade às arenas e fica sempre livre;
 *  - quarteirões são divididos em 1–4 lotes; cada lote recebe uma torre ou
 *    vira praça (que pode ganhar uma plataforma suspensa);
 *  - torres ficam mais altas perto do centro; os quatro quarteirões em volta
 *    do cruzamento central recebem torres monumentais;
 *  - pontes ligam torres vizinhas por cima de avenidas comuns (nunca dos boulevards).
 */
export class CityGenerator {
  private readonly rng: Rng;

  constructor(private readonly cfg: CityConfig, seed: number) {
    this.rng = new Rng(seed);
  }

  generate(): CityLayout {
    const c = this.cfg;
    const pitch = c.blockSize + c.avenueWidth;
    const halfX = (c.blocksX * c.blockSize + (c.blocksX + 1) * c.avenueWidth) / 2;
    const halfZ = (c.blocksZ * c.blockSize + (c.blocksZ + 1) * c.avenueWidth) / 2;
    const avenuesX = Array.from({ length: c.blocksX + 1 }, (_, i) => -halfX + c.avenueWidth / 2 + i * pitch);
    const avenuesZ = Array.from({ length: c.blocksZ + 1 }, (_, i) => -halfZ + c.avenueWidth / 2 + i * pitch);

    const towers: TowerDef[] = [];
    const plazas: Lot[] = [];
    const maxDist = Math.hypot(halfX, halfZ);

    for (let bi = 0; bi < c.blocksX; bi++) {
      for (let bj = 0; bj < c.blocksZ; bj++) {
        const bx = (avenuesX[bi]! + avenuesX[bi + 1]!) / 2;
        const bz = (avenuesZ[bj]! + avenuesZ[bj + 1]!) / 2;
        const monumentBlock = this.isCentralBlock(bi, bj);
        const lots = monumentBlock
          ? [{ x: bx, z: bz, w: c.blockSize, d: c.blockSize, block: [bi, bj] as [number, number] }]
          : this.subdivide(bx, bz, [bi, bj]);

        for (const lot of lots) {
          if (!monumentBlock && this.rng.chance(c.plazaChance)) {
            plazas.push(lot);
            continue;
          }
          towers.push(this.makeTower(towers.length, lot, monumentBlock, Math.hypot(lot.x, lot.z) / maxDist));
        }
      }
    }

    const layout: CityLayout = {
      halfX, halfZ,
      avenueWidth: c.avenueWidth,
      blockSize: c.blockSize,
      avenuesX, avenuesZ,
      boulevardX: c.blocksX / 2,
      boulevardZ: c.blocksZ / 2,
      towers,
      bridges: [],
      platforms: [],
    };
    layout.bridges = this.makeBridges(layout);
    layout.platforms = this.makePlatforms(plazas);
    return layout;
  }

  private isCentralBlock(bi: number, bj: number): boolean {
    const cx = this.cfg.blocksX / 2, cz = this.cfg.blocksZ / 2;
    return (bi === cx - 1 || bi === cx) && (bj === cz - 1 || bj === cz);
  }

  private subdivide(bx: number, bz: number, block: [number, number]): Lot[] {
    const s = this.cfg.blockSize;
    const h = s / 2;
    const pattern = this.rng.pick(['single', 'splitX', 'splitZ', 'quad', 'quad'] as const);
    switch (pattern) {
      case 'single':
        return [{ x: bx, z: bz, w: s, d: s, block }];
      case 'splitX':
        return [-1, 1].map((k) => ({ x: bx + (k * h) / 2, z: bz, w: h, d: s, block }));
      case 'splitZ':
        return [-1, 1].map((k) => ({ x: bx, z: bz + (k * h) / 2, w: s, d: h, block }));
      case 'quad':
        return [[-1, -1], [1, -1], [-1, 1], [1, 1]].map(([kx, kz]) => ({
          x: bx + (kx! * h) / 2, z: bz + (kz! * h) / 2, w: h, d: h, block,
        }));
    }
  }

  private makeTower(id: number, lot: Lot, monument: boolean, distNorm: number): TowerDef {
    const c = this.cfg;
    const r = this.rng;
    const availW = lot.w - 2 * c.lotMargin;
    const availD = lot.d - 2 * c.lotMargin;

    let shape: TowerShape;
    if (monument) shape = r.pick(['tiered', 'octagon'] as const);
    else if (Math.min(availW, availD) < 22) shape = r.pick(['block', 'spire', 'spire', 'octagon', 'slab'] as const);
    else shape = r.pick(['block', 'tiered', 'tiered', 'octagon', 'slab'] as const);

    let w = availW * r.range(monument ? 0.8 : 0.62, 0.95);
    let d = availD * r.range(monument ? 0.8 : 0.62, 0.95);
    if (shape === 'octagon') w = d = Math.min(w, d);
    if (shape === 'slab') {
      if (r.chance(0.5)) w *= r.range(0.32, 0.45); else d *= r.range(0.32, 0.45);
    }
    // Desloca a torre dentro da folga do lote para quebrar o alinhamento perfeito.
    const x = lot.x + r.range(-0.5, 0.5) * (availW - w);
    const z = lot.z + r.range(-0.5, 0.5) * (availD - d);

    let height: number;
    if (monument) {
      height = c.monumentHeight * r.range(0.82, 1);
    } else {
      const falloff = 1 - c.centerFalloff * Math.pow(Math.min(1, distNorm), 0.8);
      height = c.heightMin + (c.heightMax - c.heightMin) * Math.pow(r.next(), 1.6) * falloff;
      if (shape === 'spire') height *= 1.35;
      height = Math.max(c.heightMin, Math.min(height, c.monumentHeight * 0.8));
    }

    const tiers = this.makeTiers(shape, w, d, height);
    const accentRoll = r.next();
    const accent: AccentColor = accentRoll < 0.16 ? 'orange' : accentRoll < 0.26 ? 'white' : 'cyan';
    const style: StripStyle = monument ? 'corners' : r.pick(['corners', 'corners', 'bands', 'crown'] as const);

    return { id, shape, x, z, height, tiers, accent, style, block: lot.block, monument };
  }

  private makeTiers(shape: TowerShape, w: number, d: number, height: number): TowerTier[] {
    const r = this.rng;
    if (shape === 'tiered') {
      const count = r.int(2, 4);
      const tiers: TowerTier[] = [];
      // Frações decrescentes de altura por nível (o primeiro é o mais alto).
      const weights = Array.from({ length: count }, (_, i) => 1 / (i + 1.4));
      const sum = weights.reduce((a, b) => a + b, 0);
      let baseY = 0, tw = w, td = d;
      for (let i = 0; i < count; i++) {
        const h = (height * weights[i]!) / sum;
        tiers.push({ width: tw, depth: td, baseY, height: h });
        baseY += h;
        tw *= r.range(0.62, 0.8);
        td *= r.range(0.62, 0.8);
      }
      return tiers;
    }
    if (shape === 'spire') {
      const baseH = height * 0.78;
      return [
        { width: w, depth: d, baseY: 0, height: baseH },
        { width: w * 0.22, depth: d * 0.22, baseY: baseH, height: height - baseH },
      ];
    }
    return [{ width: w, depth: d, baseY: 0, height }];
  }

  /** Pontes entre torres de quarteirões vizinhos, por cima de avenidas comuns. */
  private makeBridges(layout: CityLayout): BridgeDef[] {
    const c = this.cfg;
    const r = this.rng;
    const byBlock = new Map<string, TowerDef[]>();
    for (const t of layout.towers) {
      const key = `${t.block[0]},${t.block[1]}`;
      const list = byBlock.get(key) ?? [];
      list.push(t);
      byBlock.set(key, list);
    }

    const bridges: BridgeDef[] = [];
    const used = new Set<string>();
    const width = 5;
    for (let attempt = 0; attempt < c.bridgeCount * 12 && bridges.length < c.bridgeCount; attempt++) {
      const axis = r.chance(0.5) ? 'x' : 'z';
      const bi = r.int(0, c.blocksX - (axis === 'x' ? 2 : 1));
      const bj = r.int(0, c.blocksZ - (axis === 'z' ? 2 : 1));
      const ni = axis === 'x' ? bi + 1 : bi;
      const nj = axis === 'z' ? bj + 1 : bj;
      // A avenida entre os dois quarteirões tem índice igual ao do segundo.
      if (axis === 'x' && ni === layout.boulevardX) continue;
      if (axis === 'z' && nj === layout.boulevardZ) continue;
      const pairKey = `${axis}:${bi},${bj}`;
      if (used.has(pairKey)) continue;

      const a = byBlock.get(`${bi},${bj}`), b = byBlock.get(`${ni},${nj}`);
      if (!a?.length || !b?.length) continue;
      // nearFace: face voltada para o quarteirão seguinte (+eixo); farFace: face oposta (-eixo).
      const nearFace = (t: TowerDef) => (axis === 'x' ? t.x + t.tiers[0]!.width / 2 : t.z + t.tiers[0]!.depth / 2);
      const farFace = (t: TowerDef) => (axis === 'x' ? t.x - t.tiers[0]!.width / 2 : t.z - t.tiers[0]!.depth / 2);
      const ta = a.reduce((m, t) => (nearFace(t) > nearFace(m) ? t : m));
      const tb = b.reduce((m, t) => (farFace(t) < farFace(m) ? t : m));

      // Sobreposição no eixo perpendicular.
      const perpMin = (t: TowerDef) => (axis === 'x' ? t.z - t.tiers[0]!.depth / 2 : t.x - t.tiers[0]!.width / 2);
      const perpMax = (t: TowerDef) => (axis === 'x' ? t.z + t.tiers[0]!.depth / 2 : t.x + t.tiers[0]!.width / 2);
      const oMin = Math.max(perpMin(ta), perpMin(tb)) + 1;
      const oMax = Math.min(perpMax(ta), perpMax(tb)) - 1;
      if (oMax - oMin < width) continue;

      const topY = Math.min(ta.tiers[0]!.height, tb.tiers[0]!.height) - 8;
      if (topY <= c.bridgeMinY) continue;

      const start = nearFace(ta), end = farFace(tb);
      const length = end - start;
      if (length <= 0) continue;
      const perp = r.range(oMin + width / 2, oMax - width / 2);
      const y = r.range(c.bridgeMinY, topY);
      const mid = (start + end) / 2;
      bridges.push({
        axis,
        x: axis === 'x' ? mid : perp,
        z: axis === 'x' ? perp : mid,
        y, length, width, thickness: 1.4,
        accent: r.chance(0.25) ? 'orange' : 'cyan',
      });
      used.add(pairKey);
    }
    return bridges;
  }

  private makePlatforms(plazas: Lot[]): PlatformDef[] {
    const r = this.rng;
    const list = [...plazas];
    const out: PlatformDef[] = [];
    while (list.length && out.length < this.cfg.platformCount) {
      const lot = list.splice(r.int(0, list.length - 1), 1)[0]!;
      out.push({
        x: lot.x, z: lot.z,
        y: r.range(22, 70),
        width: lot.w * r.range(0.55, 0.8),
        depth: lot.d * r.range(0.55, 0.8),
        thickness: 1.6,
      });
    }
    return out;
  }
}
