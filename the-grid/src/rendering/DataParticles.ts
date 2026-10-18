import {
  AdditiveBlending, BufferAttribute, BufferGeometry, Points, ShaderMaterial, Vector3,
  type IUniform,
} from 'three';
import { Rng } from '../core/Rng';
import type { Palette } from './Palette';

const RANGE = 170;
const HEIGHT = 170;

/**
 * Partículas digitais suspensas no ar. O campo é "infinito": as posições são
 * dobradas (mod) em torno da câmera dentro do shader, sem custo de CPU.
 */
export class DataParticles {
  readonly points: Points;
  private readonly material: ShaderMaterial;
  private readonly center = new Vector3();

  constructor(count: number, palette: Palette, time: IUniform<number>, pixelRatio: number) {
    const rng = new Rng(9001);
    const pos = new Float32Array(count * 3);
    const seed = new Float32Array(count);
    for (let i = 0; i < count; i++) {
      pos[i * 3] = rng.range(-RANGE, RANGE);
      pos[i * 3 + 1] = rng.range(0, HEIGHT);
      pos[i * 3 + 2] = rng.range(-RANGE, RANGE);
      seed[i] = rng.next();
    }
    const geo = new BufferGeometry();
    geo.setAttribute('position', new BufferAttribute(pos, 3));
    geo.setAttribute('aSeed', new BufferAttribute(seed, 1));

    this.material = new ShaderMaterial({
      name: 'DataParticles',
      transparent: true,
      depthWrite: false,
      blending: AdditiveBlending,
      fog: false,
      uniforms: {
        uTime: time,
        uCenter: { value: this.center },
        uPixelRatio: { value: pixelRatio },
        uCyan: { value: palette.cyan.clone().multiplyScalar(1.4) },
        uOrange: { value: palette.orange.clone().multiplyScalar(1.4) },
      },
      vertexShader: /* glsl */ `
        uniform float uTime;
        uniform vec3 uCenter;
        uniform float uPixelRatio;
        attribute float aSeed;
        varying float vAlpha;
        varying float vSeed;
        void main() {
          vec3 p = position;
          p.y += uTime * (0.8 + aSeed * 2.2);
          p.x += sin(uTime * 0.21 + aSeed * 31.0) * 3.0;
          vec2 rel = mod(p.xz - uCenter.xz + ${RANGE.toFixed(1)}, ${(RANGE * 2).toFixed(1)}) - ${RANGE.toFixed(1)};
          vec3 wp = vec3(uCenter.x + rel.x, mod(p.y, ${HEIGHT.toFixed(1)}) + 0.3, uCenter.z + rel.y);
          vec4 mv = viewMatrix * vec4(wp, 1.0);
          gl_Position = projectionMatrix * mv;
          float depth = max(1.0, -mv.z);
          gl_PointSize = clamp((1.2 + aSeed * 1.8) * uPixelRatio * (70.0 / depth), 0.0, 6.0 * uPixelRatio);
          float edge = 1.0 - smoothstep(${(RANGE * 0.55).toFixed(1)}, ${RANGE.toFixed(1)}, length(rel));
          vAlpha = edge * (0.35 + 0.65 * (0.5 + 0.5 * sin(uTime * (1.0 + aSeed * 2.0) + aSeed * 50.0)));
          vSeed = aSeed;
        }
      `,
      fragmentShader: /* glsl */ `
        uniform vec3 uCyan;
        uniform vec3 uOrange;
        varying float vAlpha;
        varying float vSeed;
        void main() {
          vec2 c = abs(gl_PointCoord - 0.5);
          float a = 1.0 - smoothstep(0.25, 0.5, max(c.x, c.y)); // "pixel" quadrado suave
          vec3 col = vSeed > 0.92 ? uOrange : uCyan;
          gl_FragColor = vec4(col * a * vAlpha, 1.0);
        }
      `,
    });
    this.points = new Points(geo, this.material);
    this.points.name = 'DataParticles';
    this.points.frustumCulled = false;
  }

  follow(cameraPosition: Vector3): void {
    this.center.copy(cameraPosition);
  }

  setPixelRatio(pr: number): void {
    this.material.uniforms.uPixelRatio!.value = pr;
  }

  dispose(): void {
    this.points.removeFromParent();
    this.points.geometry.dispose();
    this.material.dispose();
  }
}
