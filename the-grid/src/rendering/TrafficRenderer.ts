import { BoxGeometry, InstancedMesh, MeshBasicMaterial, Object3D } from 'three';
import type { TrafficSystem } from '../world/TrafficSystem';
import { InstanceBatch } from './InstanceBatch';
import type { Palette } from './Palette';

/** Desenha os corredores de luz do TrafficSystem (só leitura do estado). */
export class TrafficRenderer {
  readonly mesh: InstancedMesh;
  private readonly dummy = new Object3D();
  private readonly geometry = new BoxGeometry(1, 1, 1);
  private readonly material = new MeshBasicMaterial({ color: 0xffffff });

  constructor(private readonly traffic: TrafficSystem, palette: Palette) {
    const batch = new InstanceBatch();
    for (const r of traffic.runners) {
      batch.add(0, r.y, 0, 1, 1, 1, palette.glow(r.orange ? 'orange' : 'cyan', 4));
    }
    this.mesh = batch.build(this.geometry, this.material, 'Traffic', true);
    this.mesh.frustumCulled = false; // instâncias se movem; a esfera estática não serve
    this.sync();
  }

  sync(): void {
    const d = this.dummy;
    this.traffic.runners.forEach((r, i) => {
      if (r.axis === 'x') {
        d.position.set(r.pos, r.y, r.lane);
        d.scale.set(r.length, 0.35, 0.9);
      } else {
        d.position.set(r.lane, r.y, r.pos);
        d.scale.set(0.9, 0.35, r.length);
      }
      d.updateMatrix();
      this.mesh.setMatrixAt(i, d.matrix);
    });
    this.mesh.instanceMatrix.needsUpdate = true;
  }

  dispose(): void {
    this.mesh.removeFromParent();
    this.geometry.dispose();
    this.material.dispose();
  }
}
