import { BoxGeometry, Group, MeshBasicMaterial, type Material } from 'three';
import type { PropDef } from '../world/Props';
import { InstanceBatch } from './InstanceBatch';
import type { Palette } from './Palette';

/** Blocos montados à mão (praça de entrada, percursos, coberturas da arena). */
export class PropRenderer {
  readonly group = new Group();
  private readonly geometry = new BoxGeometry(1, 1, 1);
  private readonly glow = new MeshBasicMaterial({ color: 0xffffff });

  constructor(props: readonly PropDef[], palette: Palette, facade: Material) {
    this.group.name = 'Props';
    const bodies = new InstanceBatch();
    const strips = new InstanceBatch();
    const s = 0.12;
    for (const p of props) {
      const b = p.box;
      const w = b.maxX - b.minX, h = b.maxY - b.minY, d = b.maxZ - b.minZ;
      const cx = (b.minX + b.maxX) / 2, cz = (b.minZ + b.maxZ) / 2;
      const accent = palette.accent(p.accent);
      bodies.add(cx, b.minY + h / 2, cz, w, h, d, accent);
      // Contorno superior bem visível: mostra ao jogador onde pisar.
      const top = b.maxY - s / 2 + 0.01;
      const rim = accent.clone().multiplyScalar(1.7);
      strips.add(cx, top, b.minZ, w + s, s, s, rim);
      strips.add(cx, top, b.maxZ, w + s, s, s, rim);
      strips.add(b.minX, top, cz, s, s, d + s, rim);
      strips.add(b.maxX, top, cz, s, s, d + s, rim);
      if (h > 1.5) {
        const edge = accent.clone().multiplyScalar(1.4);
        for (const [x, z] of [[b.minX, b.minZ], [b.maxX, b.minZ], [b.minX, b.maxZ], [b.maxX, b.maxZ]] as const) {
          strips.add(x, b.minY + h / 2, z, s, h, s, edge);
        }
      }
      if (b.minY > 0.5) {
        // Peça suspensa (viga): contorno inferior também.
        const bottom = b.minY + s / 2 - 0.01;
        strips.add(cx, bottom, b.minZ, w + s, s, s, rim);
        strips.add(cx, bottom, b.maxZ, w + s, s, s, rim);
      }
      if (p.beacon) strips.add(cx, b.maxY + 0.6, cz, 0.5, 1.2, 0.5, palette.glow('white', 5));
    }
    this.group.add(bodies.build(this.geometry, facade, 'PropBodies'), strips.build(this.geometry, this.glow, 'PropStrips'));
  }

  dispose(): void {
    this.group.removeFromParent();
    this.geometry.dispose();
    this.glow.dispose();
  }
}
