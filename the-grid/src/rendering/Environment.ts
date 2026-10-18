import {
  BackSide, BoxGeometry, Mesh, MeshBasicMaterial, PMREMGenerator, Scene, SphereGeometry,
  type Texture, type WebGLRenderer,
} from 'three';
import type { Palette } from './Palette';

/**
 * Mapa de ambiente para os reflexos metálicos: uma "sala" escura com faixas
 * de luz ciano/laranja, pré-filtrada com PMREM. Dá às superfícies pretas os
 * reflexos de luz característicos sem iluminação dinâmica cara.
 */
export function createEnvironment(renderer: WebGLRenderer, palette: Palette): Texture {
  const scene = new Scene();
  const disposables: { dispose(): void }[] = [];
  const add = (geo: BoxGeometry | SphereGeometry, mat: MeshBasicMaterial, x: number, y: number, z: number) => {
    const m = new Mesh(geo, mat);
    m.position.set(x, y, z);
    scene.add(m);
    disposables.push(geo, mat);
    return m;
  };

  add(new SphereGeometry(50, 32, 16), new MeshBasicMaterial({ color: 0x020408, side: BackSide }), 0, 0, 0);
  // Faixas horizontais distantes (horizonte luminoso).
  const cyan = new MeshBasicMaterial({ color: palette.cyan.clone().multiplyScalar(1.3) });
  const blue = new MeshBasicMaterial({ color: palette.blue.clone().multiplyScalar(1.2) });
  const orange = new MeshBasicMaterial({ color: palette.orange.clone().multiplyScalar(1.6) });
  const white = new MeshBasicMaterial({ color: palette.white.clone().multiplyScalar(0.8) });
  add(new BoxGeometry(80, 0.6, 1), cyan, 0, 2, -30);
  add(new BoxGeometry(80, 0.6, 1), cyan, 0, 2, 30);
  add(new BoxGeometry(1, 0.6, 80), blue, 30, 6, 0);
  add(new BoxGeometry(1, 0.4, 80), orange, -30, 1, 0);
  add(new BoxGeometry(20, 0.3, 20), white, 0, 28, 0);
  add(new BoxGeometry(1, 30, 1), cyan, 20, 0, -20);
  add(new BoxGeometry(1, 30, 1), cyan, -20, 0, 20);

  const pmrem = new PMREMGenerator(renderer);
  const target = pmrem.fromScene(scene, 0.02);
  pmrem.dispose();
  for (const d of disposables) d.dispose();
  return target.texture;
}
