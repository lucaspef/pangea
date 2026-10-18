import {
  Mesh, PlaneGeometry, ShaderMaterial, Vector2, Vector3, Vector4, type Color, type IUniform, type Object3D,
} from 'three';
import { Reflector } from 'three/examples/jsm/objects/Reflector.js';
import type { WorldConfig } from '../config/types';
import type { CityLayout } from '../world/CityLayout';
import type { Palette } from './Palette';

/**
 * Piso do Grid: superfície escura e polida com reflexo planar real (qualidade
 * média/alta) ou apenas brilho simulado (baixa). Todas as marcações — grade
 * fina, avenidas, boulevards com pulsos de luz, círculos da arena e o
 * traçado da arena de veículos — são desenhadas no shader, sem texturas.
 */
const vertexShader = /* glsl */ `
  #ifdef USE_REFLECTION
    uniform mat4 textureMatrix;
    varying vec4 vReflUv;
  #endif
  varying vec3 vWorld;
  void main() {
    vec4 wp = modelMatrix * vec4(position, 1.0);
    vWorld = wp.xyz;
    #ifdef USE_REFLECTION
      vReflUv = textureMatrix * vec4(position, 1.0);
    #endif
    gl_Position = projectionMatrix * viewMatrix * wp;
  }
`;

const fragmentShader = /* glsl */ `
  #ifdef USE_REFLECTION
    uniform sampler2D tDiffuse;
    uniform vec2 uReflTexel;
    varying vec4 vReflUv;
  #endif
  uniform vec3 color;
  uniform float uTime;
  uniform float uReflectivity;
  uniform vec3 uFogColor;
  uniform float uFogDensity;
  uniform vec3 uCyan;
  uniform vec3 uOrange;
  uniform vec3 uWhite;
  uniform vec2 uCityHalf;
  uniform vec2 uFirstAvenue;
  uniform float uPitch;
  uniform float uAvenueWidth;
  uniform vec3 uArena;        // cx, cz, raio externo
  uniform vec4 uVehicleArena; // cx, cz, meia-largura x, meia-largura z
  varying vec3 vWorld;

  float lineAA(float d, float w) {
    float fw = fwidth(d);
    return 1.0 - smoothstep(w, w + fw * 1.5, d);
  }
  float gridDist(float c, float spacing) {
    return abs(fract(c / spacing + 0.5) - 0.5) * spacing;
  }

  void main() {
    vec2 p = vWorld.xz;
    float dist = length(cameraPosition - vWorld);
    float near = 1.0 - smoothstep(80.0, 520.0, dist);
    float hw = uAvenueWidth * 0.5;

    // Grade fina por todo o mundo.
    float fine = max(lineAA(gridDist(p.x, 6.0), 0.035), lineAA(gridDist(p.y, 6.0), 0.035)) * near;
    float coarse = max(lineAA(gridDist(p.x, 66.0), 0.08), lineAA(gridDist(p.y, 66.0), 0.08));

    // Avenidas da cidade: bordas contínuas + faixa central tracejada.
    float inCity = step(abs(p.x), uCityHalf.x) * step(abs(p.y), uCityHalf.y);
    vec2 rel = p - uFirstAvenue;
    vec2 av = abs(fract(rel / uPitch + 0.5) - 0.5) * uPitch; // distância ao eixo da avenida mais próxima
    float edgeNS = lineAA(abs(av.x - hw), 0.14) * step(hw, av.y);
    float edgeEW = lineAA(abs(av.y - hw), 0.14) * step(hw, av.x);
    float dashNS = lineAA(av.x, 0.09) * step(0.55, fract(p.y / 7.0)) * step(hw, av.y);
    float dashEW = lineAA(av.y, 0.09) * step(0.55, fract(p.x / 7.0)) * step(hw, av.x);
    float avenues = (edgeNS + edgeEW) * inCity;
    float dashes = (dashNS + dashEW) * inCity * near;

    // Calçadas dos quarteirões: borda interna sutil.
    float blockEdge = (lineAA(abs(av.x - hw - 2.5), 0.05) * step(hw + 2.5, av.y)
                     + lineAA(abs(av.y - hw - 2.5), 0.05) * step(hw + 2.5, av.x)) * inCity * near;

    // Boulevards: norte-sul (x≈0) até a arena e leste-oeste (z≈0) até a arena de veículos.
    float nsSpan = step(uArena.y + uArena.z, p.y) * step(p.y, uCityHalf.y + 120.0);
    float ewSpan = step(-uCityHalf.x - 120.0, p.x) * step(p.x, uVehicleArena.x - uVehicleArena.z);
    float boulevardNS = nsSpan * (lineAA(abs(abs(p.x) - hw), 0.22) + lineAA(abs(p.x), 0.35) * 0.6);
    float boulevardEW = ewSpan * (lineAA(abs(abs(p.y) - hw), 0.22) + lineAA(abs(p.y), 0.35) * 0.6);
    // Pacotes de energia: traços curtos e finos correndo no meio de cada faixa.
    float laneNS = lineAA(abs(abs(p.x) - hw * 0.5), 0.18);
    float laneEW = lineAA(abs(abs(p.y) - hw * 0.5), 0.18);
    float packetNS = 1.0 - smoothstep(0.0, 6.0, abs(mod(p.y + uTime * 70.0 * sign(p.x), 90.0) - 45.0));
    float packetEW = 1.0 - smoothstep(0.0, 6.0, abs(mod(p.x - uTime * 70.0 * sign(p.y), 90.0) - 45.0));
    float pulseNS = nsSpan * laneNS * packetNS;
    float pulseEW = ewSpan * laneEW * packetEW;

    // Arena de combate: anéis concêntricos e raios.
    vec2 ac = p - uArena.xy;
    float ar = length(ac);
    float inArena = step(ar, uArena.z);
    float rings = (lineAA(gridDist(ar, 24.0), 0.12) + lineAA(abs(ar - 30.0), 0.3) * 1.5) * inArena;
    float ang = atan(ac.y, ac.x);
    float spokes = lineAA(gridDist(ang * ar, 2.0 * 3.14159265 * ar / 16.0), 0.1) * step(30.0, ar) * inArena * 0.6;

    // Arena de veículos: borda e grade larga laranja.
    vec2 vr = abs(p - uVehicleArena.xy);
    float inVehicle = step(vr.x, uVehicleArena.z) * step(vr.y, uVehicleArena.w);
    float vGrid = max(lineAA(gridDist(p.x - uVehicleArena.x, 24.0), 0.12), lineAA(gridDist(p.y - uVehicleArena.y, 24.0), 0.12)) * inVehicle;
    float vBorder = lineAA(min(abs(vr.x - uVehicleArena.z + 14.0), abs(vr.y - uVehicleArena.w + 14.0)), 0.3)
                  * step(vr.x, uVehicleArena.z - 14.0 + 0.5) * step(vr.y, uVehicleArena.w - 14.0 + 0.5);

    vec3 col = color;

    #ifdef USE_REFLECTION
      vec2 ruv = vReflUv.xy / vReflUv.w;
      // Pequeno desfoque em cruz: o piso parece polido, não espelho perfeito.
      vec2 o = uReflTexel * 1.5;
      vec3 refl = texture2D(tDiffuse, ruv).rgb * 0.36
                + texture2D(tDiffuse, ruv + vec2(o.x, 0.0)).rgb * 0.16
                + texture2D(tDiffuse, ruv - vec2(o.x, 0.0)).rgb * 0.16
                + texture2D(tDiffuse, ruv + vec2(0.0, o.y * 2.0)).rgb * 0.16
                + texture2D(tDiffuse, ruv - vec2(0.0, o.y * 2.0)).rgb * 0.16;
      vec3 viewDir = normalize(cameraPosition - vWorld);
      float fresnel = mix(0.95, 0.22, pow(clamp(viewDir.y, 0.0, 1.0), 0.5));
      col += refl * uReflectivity * fresnel;
    #else
      // Sem reflexo: brilho difuso próximo às avenidas simula a luz rebatida.
      col += uCyan * 0.025 * (1.0 - smoothstep(0.0, hw, min(av.x, av.y))) * inCity;
    #endif

    col += uCyan * (fine * 0.16 + coarse * 0.08 + avenues * 1.5 + dashes * 0.55 + blockEdge * 0.25);
    col += uWhite * (boulevardNS + boulevardEW) * 1.6;
    col += uCyan * (pulseNS + pulseEW) * 2.2;
    col += uCyan * (rings * 1.1 + spokes * 0.5);
    col += uOrange * (vGrid * 0.45 + vBorder * 2.0);

    float fogFactor = 1.0 - exp(-pow(uFogDensity * dist, 2.0));
    col = mix(col, uFogColor, fogFactor);
    gl_FragColor = vec4(col, 1.0);
  }
`;

export interface FloorOptions {
  reflection: boolean;
  /** Tamanho da textura de reflexo em pixels. */
  reflectionWidth: number;
  reflectionHeight: number;
}

export class GridFloor {
  readonly mesh: Mesh;
  private readonly material: ShaderMaterial;
  private readonly reflector: Reflector | null;

  constructor(
    private readonly size: number,
    city: CityLayout,
    world: WorldConfig,
    palette: Palette,
    fog: { color: Color; density: number },
    time: IUniform<number>,
    options: FloorOptions,
  ) {
    const geometry = new PlaneGeometry(size, size);
    const ca = world.sectors.combatArena;
    const va = world.sectors.vehicleArena;
    const uniforms = {
      color: { value: palette.floor.clone() },
      tDiffuse: { value: null },
      textureMatrix: { value: null },
      uReflTexel: { value: new Vector2(1 / Math.max(1, options.reflectionWidth), 1 / Math.max(1, options.reflectionHeight)) },
      uTime: time,
      uReflectivity: { value: 0.42 },
      uFogColor: { value: fog.color },
      uFogDensity: { value: fog.density },
      uCyan: { value: palette.cyan },
      uOrange: { value: palette.orange },
      uWhite: { value: palette.white },
      uCityHalf: { value: new Vector2(city.halfX, city.halfZ) },
      uFirstAvenue: { value: new Vector2(city.avenuesX[0], city.avenuesZ[0]) },
      uPitch: { value: city.blockSize + city.avenueWidth },
      uAvenueWidth: { value: city.avenueWidth },
      uArena: { value: new Vector3(ca.center[0], ca.center[1], ca.outerRadius) },
      uVehicleArena: { value: new Vector4(va.center[0], va.center[1], va.halfSize[0], va.halfSize[1]) },
    };

    if (options.reflection) {
      this.reflector = new Reflector(geometry, {
        textureWidth: options.reflectionWidth,
        textureHeight: options.reflectionHeight,
        clipBias: 0.003,
        // Sem isto o Reflector usa cinza 50% como cor base do piso.
        color: palette.floor.clone(),
        shader: { name: 'GridFloorReflector', uniforms, vertexShader, fragmentShader },
      });
      this.mesh = this.reflector;
      this.material = this.reflector.material as ShaderMaterial;
      // O Reflector clona os uniforms: religa os que devem ser compartilhados.
      this.material.uniforms.uTime = time;
      this.material.uniforms.uFogColor!.value = fog.color;
      this.material.defines = { USE_REFLECTION: '' };
      this.material.needsUpdate = true;
    } else {
      this.reflector = null;
      this.material = new ShaderMaterial({
        name: 'GridFloor', uniforms, vertexShader, fragmentShader,
      });
      this.mesh = new Mesh(geometry, this.material);
    }
    this.mesh.rotation.x = -Math.PI / 2;
    this.mesh.name = 'GridFloor';
    this.mesh.renderOrder = -1;
  }

  get hasReflection(): boolean {
    return this.reflector !== null;
  }

  get worldSize(): number {
    return this.size;
  }

  setFogDensity(density: number): void {
    this.material.uniforms.uFogDensity!.value = density;
  }

  /** Redimensiona a textura de reflexo (chamado no resize da janela). */
  setReflectionSize(width: number, height: number): void {
    if (!this.reflector) return;
    const w = Math.max(1, Math.floor(width)), h = Math.max(1, Math.floor(height));
    this.reflector.getRenderTarget().setSize(w, h);
    (this.material.uniforms.uReflTexel!.value as Vector2).set(1 / w, 1 / h);
  }

  addTo(parent: Object3D): void {
    parent.add(this.mesh);
  }

  dispose(): void {
    this.mesh.removeFromParent();
    this.mesh.geometry.dispose();
    this.reflector?.dispose();
    this.material.dispose();
  }
}
