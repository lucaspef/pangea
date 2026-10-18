import {
  BackSide, BoxGeometry, ConeGeometry, EdgesGeometry, Group, LineBasicMaterial, LineSegments, Matrix4, Mesh,
  MeshBasicMaterial, ShaderMaterial, SphereGeometry, TorusGeometry, type BufferGeometry, type IUniform, type Vector3,
} from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { Rng } from '../core/Rng';
import type { Palette } from './Palette';

const skyVertex = /* glsl */ `
  varying vec3 vDir;
  void main() {
    vDir = position;
    vec4 p = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
    gl_Position = p.xyww; // sempre no plano distante
  }
`;

const skyFragment = /* glsl */ `
  uniform vec3 uHorizon;
  uniform vec3 uZenith;
  uniform vec3 uGlow;
  uniform vec3 uAccent;
  uniform float uTime;
  varying vec3 vDir;

  float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

  void main() {
    vec3 d = normalize(vDir);
    float h = d.y;
    float az = atan(d.z, d.x);
    vec3 col = mix(uHorizon, uZenith, smoothstep(-0.02, 0.55, h));
    // Faixa luminosa do horizonte e camadas de "nuvem digital" horizontais.
    col += uGlow * exp(-abs(h) * 16.0) * 0.55;
    float layers = smoothstep(0.6, 1.0, sin(h * 140.0 + sin(az * 3.0 + uTime * 0.05) * 3.0)) * exp(-h * 6.0) * step(0.0, h);
    col += uGlow * layers * 0.05;
    // Pontos de dados distantes, piscando.
    vec2 sp = vec2(az * 90.0, h * 180.0);
    vec2 cell = floor(sp);
    float r = hash(cell);
    vec2 f = fract(sp) - 0.5;
    float star = step(0.986, r) * (1.0 - smoothstep(0.05, 0.16, max(abs(f.x), abs(f.y))))
               * smoothstep(0.06, 0.35, h) * (0.55 + 0.45 * sin(uTime * (1.0 + r * 3.0) + r * 60.0));
    col += mix(uAccent, vec3(1.0), 0.5) * star * 0.9;
    gl_FragColor = vec4(col, 1.0);
  }
`;

/**
 * Céu e horizonte: cúpula com degradê, pontos de dados e estruturas
 * colossais distantes (cordilheiras geométricas e um anel orbital).
 * Tudo acompanha a câmera — o horizonte nunca é alcançado.
 */
export class SkyDome {
  readonly group = new Group();
  private readonly owned: { dispose(): void }[] = [];
  private readonly orbitalRing: Mesh;

  constructor(palette: Palette, time: IUniform<number>) {
    this.group.name = 'Sky';
    const keep = <T extends { dispose(): void }>(o: T) => (this.owned.push(o), o);

    const sky = new Mesh(
      keep(new SphereGeometry(4500, 48, 24)),
      keep(new ShaderMaterial({
        name: 'SkyDome',
        vertexShader: skyVertex,
        fragmentShader: skyFragment,
        side: BackSide,
        depthWrite: false,
        fog: false,
        uniforms: {
          uHorizon: { value: palette.fog.clone().multiplyScalar(1.6) },
          uZenith: { value: palette.fog.clone().multiplyScalar(0.15) },
          uGlow: { value: palette.blue.clone().multiplyScalar(0.18) },
          uAccent: { value: palette.cyan },
          uTime: time,
        },
      })),
    );
    sky.renderOrder = -10;
    sky.frustumCulled = false;
    this.group.add(sky);

    // Silhuetas distantes: corpo escuro + arestas luminosas tênues. Todas são
    // fundidas em 3 geometrias (corpos, arestas ciano, arestas laranja): 3 draw calls.
    const rng = new Rng(4242);
    const bodies: BufferGeometry[] = [];
    const edgesCyan: BufferGeometry[] = [];
    const edgesOrange: BufferGeometry[] = [];
    const placement = new Matrix4();
    const rotation = new Matrix4();
    const addSilhouette = (geo: BufferGeometry, x: number, y: number, z: number, rotY: number, orange: boolean) => {
      placement.makeTranslation(x, y, z).multiply(rotation.makeRotationY(rotY));
      const edges = new EdgesGeometry(geo, 20);
      geo.applyMatrix4(placement);
      edges.applyMatrix4(placement);
      bodies.push(geo.index ? geo.toNonIndexed() : geo);
      (orange ? edgesOrange : edgesCyan).push(edges);
    };
    for (let i = 0; i < 46; i++) {
      const a = (i / 46) * Math.PI * 2 + rng.range(-0.05, 0.05);
      const r = rng.range(2600, 3600);
      const x = Math.cos(a) * r, z = Math.sin(a) * r;
      if (rng.chance(0.55)) {
        // Cordilheira: pirâmides baixas e largas.
        const h = rng.range(180, 520);
        addSilhouette(new ConeGeometry(rng.range(300, 650), h, 4, 1), x, h / 2 - 5, z, rng.range(0, Math.PI), false);
      } else {
        // Megaestrutura: torre colossal.
        const h = rng.range(400, 1100);
        const w = rng.range(60, 180);
        addSilhouette(new BoxGeometry(w, h, w * rng.range(0.5, 1.5)), x, h / 2 - 5, z, rng.range(0, Math.PI), rng.chance(0.2));
      }
    }
    const merge = (list: BufferGeometry[]) => {
      // Só a posição importa (materiais sem iluminação): descarta normais/UVs para fundir.
      for (const g of list) for (const name of Object.keys(g.attributes)) if (name !== 'position') g.deleteAttribute(name);
      const merged = keep(mergeGeometries(list)!);
      for (const g of list) g.dispose();
      return merged;
    };
    const bodyMat = keep(new MeshBasicMaterial({ color: palette.fog.clone().multiplyScalar(1.15), fog: false }));
    this.group.add(
      new Mesh(merge(bodies), bodyMat),
      new LineSegments(merge(edgesCyan), keep(new LineBasicMaterial({ color: palette.cyan.clone().multiplyScalar(0.18), fog: false }))),
    );
    if (edgesOrange.length) {
      this.group.add(new LineSegments(merge(edgesOrange), keep(new LineBasicMaterial({ color: palette.orange.clone().multiplyScalar(0.16), fog: false }))));
    }

    // Anel orbital luminoso no céu.
    this.orbitalRing = new Mesh(
      keep(new TorusGeometry(1300, 5, 8, 200)),
      keep(new MeshBasicMaterial({ color: palette.white.clone().multiplyScalar(1.8), fog: false })),
    );
    this.orbitalRing.position.set(-900, 1500, -3200);
    this.orbitalRing.rotation.set(1.2, 0.3, 0.2);
    this.group.add(this.orbitalRing);
  }

  /** Mantém o céu centrado na câmera (sem paralaxe: horizonte infinito). */
  follow(cameraPosition: Vector3): void {
    this.group.position.set(cameraPosition.x, 0, cameraPosition.z);
  }

  update(time: number): void {
    this.orbitalRing.rotation.z = 0.2 + time * 0.004;
  }

  dispose(): void {
    this.group.removeFromParent();
    for (const o of this.owned) o.dispose();
  }
}
