import {
  Color, DynamicDrawUsage, Euler, InstancedMesh, Matrix4, Quaternion, Vector3,
  type BufferGeometry, type Material,
} from 'three';

/**
 * Acumula instâncias (posição, escala, rotação Y, cor) e gera um único
 * InstancedMesh — milhares de peças em uma chamada de desenho.
 */
export class InstanceBatch {
  private readonly matrices: Matrix4[] = [];
  private readonly colors: Color[] = [];
  private readonly q = new Quaternion();
  private readonly e = new Euler();

  get count(): number {
    return this.matrices.length;
  }

  add(x: number, y: number, z: number, sx: number, sy: number, sz: number, color: Color, rotY = 0): void {
    this.q.setFromEuler(this.e.set(0, rotY, 0));
    this.matrices.push(new Matrix4().compose(new Vector3(x, y, z), this.q, new Vector3(sx, sy, sz)));
    this.colors.push(color.clone());
  }

  build(geometry: BufferGeometry, material: Material, name: string, dynamic = false): InstancedMesh {
    const mesh = new InstancedMesh(geometry, material, Math.max(1, this.count));
    mesh.name = name;
    if (dynamic) mesh.instanceMatrix.setUsage(DynamicDrawUsage);
    this.matrices.forEach((m, i) => {
      mesh.setMatrixAt(i, m);
      mesh.setColorAt(i, this.colors[i]!);
    });
    mesh.count = this.count;
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
    mesh.computeBoundingSphere();
    return mesh;
  }
}
