import {
  BoxGeometry, CylinderGeometry, DoubleSide, Group, MeshBasicMaterial, type Color, type Material,
} from 'three';
import type { BridgeDef, CityLayout, PlatformDef, TowerDef, TowerTier } from '../world/CityLayout';
import { InstanceBatch } from './InstanceBatch';
import type { Palette } from './Palette';

const STRIP = 0.45;
const OCT_VERTEX_RADIUS = 0.5 / Math.cos(Math.PI / 8); // octógono com faces planas a 0.5 do centro

/**
 * Constrói a geometria visual da Cidade Digital a partir do CityLayout.
 * Toda a cidade cabe em 4 InstancedMesh: corpos retangulares, corpos
 * octogonais, filetes de luz e anéis de luz.
 */
export class CityRenderer {
  readonly group = new Group();
  private readonly owned: { dispose(): void }[] = [];

  private readonly bodies = new InstanceBatch();
  private readonly octBodies = new InstanceBatch();
  private readonly strips = new InstanceBatch();
  private readonly rings = new InstanceBatch();

  constructor(layout: CityLayout, private readonly palette: Palette, facade: Material) {
    this.group.name = 'City';
    for (const t of layout.towers) this.addTower(t);
    for (const b of layout.bridges) this.addBridge(b);
    for (const p of layout.platforms) this.addPlatform(p);
    this.addIntersectionBeacons(layout);

    const box = new BoxGeometry(1, 1, 1);
    const oct = new CylinderGeometry(OCT_VERTEX_RADIUS, OCT_VERTEX_RADIUS, 1, 8, 1);
    oct.rotateY(Math.PI / 8);
    const ring = new CylinderGeometry(OCT_VERTEX_RADIUS, OCT_VERTEX_RADIUS, 1, 8, 1, true);
    ring.rotateY(Math.PI / 8);
    const glow = new MeshBasicMaterial({ color: 0xffffff });
    const ringGlow = new MeshBasicMaterial({ color: 0xffffff, side: DoubleSide });
    this.owned.push(box, oct, ring, glow, ringGlow);

    this.group.add(
      this.bodies.build(box, facade, 'TowerBodies'),
      this.octBodies.build(oct, facade, 'TowerBodiesOctagon'),
      this.strips.build(box, glow, 'LightStrips'),
      this.rings.build(ring, ringGlow, 'LightRings'),
    );
  }

  /** Estatísticas para o painel de diagnóstico. */
  get stats(): { bodies: number; strips: number } {
    return { bodies: this.bodies.count + this.octBodies.count, strips: this.strips.count + this.rings.count };
  }

  dispose(): void {
    this.group.removeFromParent();
    for (const o of this.owned) o.dispose();
  }

  private addTower(t: TowerDef): void {
    const accent = this.palette.accent(t.accent);
    const glow = (k: number) => accent.clone().multiplyScalar(k);
    const octagon = t.shape === 'octagon';

    t.tiers.forEach((tier, index) => {
      const cy = tier.baseY + tier.height / 2;
      if (octagon) this.octBodies.add(t.x, cy, t.z, tier.width, tier.height, tier.depth, accent);
      else this.bodies.add(t.x, cy, t.z, tier.width, tier.height, tier.depth, accent);

      const isTop = index === t.tiers.length - 1;
      const isNeedle = t.shape === 'spire' && isTop;
      if (octagon) this.octagonStrips(t, tier, glow);
      else if (isNeedle) this.needleStrips(t, tier, glow);
      else this.boxStrips(t, tier, glow, index);
    });

    // Linha de base que "assenta" a torre no piso luminoso.
    const base = t.tiers[0]!;
    if (octagon) this.rings.add(t.x, 0.3, t.z, base.width * 1.03, 0.5, base.width * 1.03, glow(2.2));
    else this.rectangle(t.x, 0.3, t.z, base.width, base.depth, STRIP, glow(2.2));

    if (t.monument) {
      // Halo laranja flutuando sobre as torres monumentais.
      const top = t.height + 6;
      this.rings.add(t.x, top, t.z, base.width * 0.9, 0.8, base.width * 0.9, this.palette.glow('orange', 3.2));
      this.strips.add(t.x, t.height + 1.5, t.z, 1.2, 3, 1.2, this.palette.glow('white', 6));
    }
  }

  /** Retângulo de filetes ao redor de uma pegada, na altura y. */
  private rectangle(x: number, y: number, z: number, w: number, d: number, s: number, color: Color): void {
    this.strips.add(x, y, z - d / 2, w + s, s, s, color);
    this.strips.add(x, y, z + d / 2, w + s, s, s, color);
    this.strips.add(x - w / 2, y, z, s, s, d + s, color);
    this.strips.add(x + w / 2, y, z, s, s, d + s, color);
  }

  private boxStrips(t: TowerDef, tier: TowerTier, glow: (k: number) => Color, index: number): void {
    const { width: w, depth: d, baseY, height: h } = tier;
    const top = baseY + h;
    // Coroa: contorno do topo de cada nível.
    this.rectangle(t.x, top - 0.25, t.z, w, d, STRIP, glow(3));

    if (t.style === 'corners') {
      for (const [kx, kz] of [[-1, -1], [1, -1], [-1, 1], [1, 1]] as const) {
        this.strips.add(t.x + (kx * w) / 2, baseY + h / 2, t.z + (kz * d) / 2, STRIP, h, STRIP, glow(2.4));
      }
    } else if (t.style === 'bands') {
      const spacing = 12 + ((t.id + index) % 4) * 5;
      for (let y = baseY + spacing; y < top - 5; y += spacing) {
        this.rectangle(t.x, y, t.z, w, d, STRIP * 0.6, glow(1.7));
      }
    } else {
      // 'crown': linha vertical central em cada face, terminando antes da coroa.
      const lh = h * 0.82;
      const cy = baseY + lh / 2;
      const s = STRIP * 0.7;
      this.strips.add(t.x, cy, t.z - d / 2, s, lh, s, glow(2));
      this.strips.add(t.x, cy, t.z + d / 2, s, lh, s, glow(2));
      this.strips.add(t.x - w / 2, cy, t.z, s, lh, s, glow(2));
      this.strips.add(t.x + w / 2, cy, t.z, s, lh, s, glow(2));
      this.rectangle(t.x, top - 3, t.z, w, d, STRIP * 0.6, glow(1.6));
    }
  }

  private needleStrips(t: TowerDef, tier: TowerTier, glow: (k: number) => Color): void {
    const { width: w, depth: d, baseY, height: h } = tier;
    for (const [kx, kz] of [[-1, -1], [1, 1]] as const) {
      this.strips.add(t.x + (kx * w) / 2, baseY + h / 2, t.z + (kz * d) / 2, STRIP * 0.7, h, STRIP * 0.7, glow(2.8));
    }
    // Sinalizador no topo.
    this.strips.add(t.x, baseY + h + 0.8, t.z, 1.1, 1.6, 1.1, this.palette.glow('white', 5));
  }

  private octagonStrips(t: TowerDef, tier: TowerTier, glow: (k: number) => Color): void {
    const { width: w, baseY, height: h } = tier;
    const top = baseY + h;
    this.rings.add(t.x, top - 0.3, t.z, w * 1.02, 0.6, w * 1.02, glow(3));
    if (t.style === 'bands' || t.style === 'crown') {
      const spacing = t.style === 'bands' ? 14 : 30;
      for (let y = baseY + spacing; y < top - 6; y += spacing) {
        this.rings.add(t.x, y, t.z, w * 1.02, 0.35, w * 1.02, glow(1.8));
      }
    }
    if (t.style !== 'bands') {
      const rv = (w / 2) * (OCT_VERTEX_RADIUS / 0.5);
      for (let k = 0; k < 8; k += t.style === 'corners' ? 1 : 2) {
        const a = Math.PI / 8 + (k * Math.PI) / 4;
        this.strips.add(t.x + Math.sin(a) * rv, baseY + h / 2, t.z + Math.cos(a) * rv, STRIP, h, STRIP, glow(2.2));
      }
    }
  }

  private addBridge(b: BridgeDef): void {
    const along = b.axis === 'x';
    const sx = along ? b.length : b.width;
    const sz = along ? b.width : b.length;
    const accent = this.palette.accent(b.accent);
    this.bodies.add(b.x, b.y, b.z, sx, b.thickness, sz, accent.clone().multiplyScalar(0.4));
    const topY = b.y + b.thickness / 2;
    const edge = accent.clone().multiplyScalar(2.6);
    const off = b.width / 2;
    if (along) {
      this.strips.add(b.x, topY, b.z - off, b.length, STRIP * 0.8, STRIP * 0.8, edge);
      this.strips.add(b.x, topY, b.z + off, b.length, STRIP * 0.8, STRIP * 0.8, edge);
      this.strips.add(b.x, b.y - b.thickness / 2, b.z, b.length, 0.2, 0.6, accent.clone().multiplyScalar(1.4));
    } else {
      this.strips.add(b.x - off, topY, b.z, STRIP * 0.8, STRIP * 0.8, b.length, edge);
      this.strips.add(b.x + off, topY, b.z, STRIP * 0.8, STRIP * 0.8, b.length, edge);
      this.strips.add(b.x, b.y - b.thickness / 2, b.z, 0.6, 0.2, b.length, accent.clone().multiplyScalar(1.4));
    }
  }

  private addPlatform(p: PlatformDef): void {
    const cyan = this.palette.cyan;
    this.bodies.add(p.x, p.y, p.z, p.width, p.thickness, p.depth, cyan.clone().multiplyScalar(0.5));
    this.rectangle(p.x, p.y + p.thickness / 2, p.z, p.width, p.depth, STRIP, this.palette.glow('cyan', 2.6));
    this.rectangle(p.x, p.y + p.thickness / 2 + 0.05, p.z, p.width * 0.55, p.depth * 0.55, STRIP * 0.6, this.palette.glow('white', 1.4));
    // Feixe de sustentação: coluna de energia até o piso.
    const beamH = p.y - p.thickness / 2;
    this.strips.add(p.x, beamH / 2, p.z, 0.8, beamH, 0.8, this.palette.glow('blue', 1.1));
  }

  /** Balizas luminosas nas esquinas de cada cruzamento. */
  private addIntersectionBeacons(layout: CityLayout): void {
    const off = layout.avenueWidth / 2 + 1.5;
    const color = this.palette.glow('cyan', 2.2);
    const boulevardColor = this.palette.glow('white', 2.6);
    layout.avenuesX.forEach((x, i) => {
      layout.avenuesZ.forEach((z, j) => {
        const c = i === layout.boulevardX || j === layout.boulevardZ ? boulevardColor : color;
        for (const [kx, kz] of [[-1, -1], [1, -1], [-1, 1], [1, 1]] as const) {
          this.strips.add(x + kx * off, 1.2, z + kz * off, 0.45, 2.4, 0.45, c);
        }
      });
    });
  }
}
