import {
  BoxGeometry, Color, CylinderGeometry, DoubleSide, FrontSide, Group, Mesh, MeshBasicMaterial, RingGeometry, TorusGeometry,
  type Material,
} from 'three';
import type { WorldConfig } from '../config/types';
import type { ArenaLayout } from '../world/ArenaLayout';
import { InstanceBatch } from './InstanceBatch';
import type { Palette } from './Palette';

/**
 * Exterior das arenas (Setores B e C). No Marco 1 elas são marcos visuais
 * exploráveis com colisão; o gameplay interno chega nos Marcos 3 e 4.
 */
export class ArenaRenderer {
  readonly group = new Group();
  private readonly owned: { dispose(): void }[] = [];
  private readonly hologram: Mesh;

  constructor(cfg: WorldConfig, layout: ArenaLayout, palette: Palette, facade: Material, facadeDouble: Material) {
    this.group.name = 'Arenas';
    const glowMat = (color: Color, double = false) => {
      const m = new MeshBasicMaterial({ color, side: double ? DoubleSide : FrontSide });
      this.owned.push(m);
      return m;
    };
    const keep = <T extends { dispose(): void }>(g: T) => (this.owned.push(g), g);

    // ---------- Setor B: arena circular ----------
    const ca = cfg.sectors.combatArena;
    const ring = layout.combatRing;
    const thetaStart = ring.gateHalfWidth; // θ = 0 aponta para +Z (portão) na CylinderGeometry
    const thetaLength = Math.PI * 2 - ring.gateHalfWidth * 2;
    const arena = new Group();
    arena.position.set(ca.center[0], 0, ca.center[1]);

    const wallH = ring.height;
    for (const r of [ring.outerRadius, ring.innerRadius]) {
      const wall = new Mesh(keep(new CylinderGeometry(r, r, wallH, 128, 1, true, thetaStart, thetaLength)), facadeDouble);
      wall.position.y = wallH / 2;
      arena.add(wall);
      // Filetes: topo (ciano) e base.
      for (const [y, h, k] of [[wallH - 0.3, 0.6, 3.2], [0.4, 0.5, 2.0]] as const) {
        const band = new Mesh(
          keep(new CylinderGeometry(r * 1.002, r * 1.002, h, 128, 1, true, thetaStart, thetaLength)),
          glowMat(palette.glow('cyan', k), true),
        );
        band.position.y = y;
        arena.add(band);
      }
    }
    // Faixa laranja externa a meia altura.
    const midBand = new Mesh(
      keep(new CylinderGeometry(ring.outerRadius * 1.003, ring.outerRadius * 1.003, 0.8, 128, 1, true, thetaStart, thetaLength)),
      glowMat(palette.glow('orange', 2.4), true),
    );
    midBand.position.y = wallH * 0.45;
    arena.add(midBand);

    // Tampa superior do anel (RingGeometry rotacionada: θ = 3π/2 aponta para +Z).
    const cap = new Mesh(
      keep(new RingGeometry(ring.innerRadius, ring.outerRadius, 128, 1, Math.PI * 1.5 + ring.gateHalfWidth, thetaLength)),
      facade,
    );
    cap.rotation.x = -Math.PI / 2;
    cap.position.y = wallH;
    arena.add(cap);

    // Holograma flutuante no centro da arena.
    this.hologram = new Mesh(keep(new TorusGeometry(26, 0.35, 8, 96)), glowMat(palette.glow('cyan', 3.5)));
    this.hologram.position.y = 42;
    arena.add(this.hologram);
    const inner = new Mesh(keep(new TorusGeometry(16, 0.25, 8, 64)), glowMat(palette.glow('orange', 3)));
    inner.rotation.x = Math.PI / 2;
    this.hologram.add(inner);
    this.group.add(arena);

    // ---------- Setor C: arena de veículos ----------
    const bodies = new InstanceBatch();
    const strips = new InstanceBatch();
    const orange = palette.orange;
    for (const w of layout.vehicleWalls) {
      const sx = w.maxX - w.minX, sy = w.maxY - w.minY, sz = w.maxZ - w.minZ;
      const cx = (w.minX + w.maxX) / 2, cz = (w.minZ + w.maxZ) / 2;
      bodies.add(cx, sy / 2, cz, sx, sy, sz, orange);
      strips.add(cx, sy - 0.3, cz, sx + 0.4, 0.6, sz + 0.4, palette.glow('orange', 3));
      strips.add(cx, sy * 0.5, cz, sx + 0.3, 0.35, sz + 0.3, palette.glow('cyan', 1.8));
      strips.add(cx, 0.35, cz, sx + 0.3, 0.5, sz + 0.3, palette.glow('orange', 2.2));
    }

    // Estruturas sólidas das duas arenas (pilares, obeliscos, torres, pórtico).
    for (const s of layout.structures) {
      const b = s.box;
      const sx = b.maxX - b.minX, sy = b.maxY - b.minY, sz = b.maxZ - b.minZ;
      const cx = (b.minX + b.maxX) / 2, cz = (b.minZ + b.maxZ) / 2, cy = (b.minY + b.maxY) / 2;
      const accent = palette.accent(s.accent);
      bodies.add(cx, cy, cz, sx, sy, sz, accent);
      const k = 2.6;
      for (const [kx, kz] of [[-1, -1], [1, -1], [-1, 1], [1, 1]] as const) {
        strips.add(cx + (kx * sx) / 2, cy, cz + (kz * sz) / 2, 0.4, sy, 0.4, accent.clone().multiplyScalar(k));
      }
      if (s.beacon) strips.add(cx, b.maxY + 1, cz, 1.4, 2, 1.4, palette.glow('white', 6));
    }

    const box = keep(new BoxGeometry(1, 1, 1));
    this.group.add(bodies.build(box, facade, 'ArenaBodies'), strips.build(box, glowMat(new Color(0xffffff)), 'ArenaStrips'));
  }

  update(time: number): void {
    this.hologram.rotation.y = time * 0.25;
    this.hologram.rotation.z = Math.sin(time * 0.4) * 0.25;
    this.hologram.position.y = 42 + Math.sin(time * 0.8) * 1.5;
  }

  dispose(): void {
    this.group.removeFromParent();
    for (const o of this.owned) o.dispose();
  }
}
