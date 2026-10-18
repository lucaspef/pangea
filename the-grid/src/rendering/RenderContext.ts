import {
  ACESFilmicToneMapping, HalfFloatType, SRGBColorSpace, Vector2, WebGLRenderTarget, WebGLRenderer,
  type PerspectiveCamera, type Scene,
} from 'three';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { FXAAPass } from 'three/examples/jsm/postprocessing/FXAAPass.js';
import { OutputPass } from 'three/examples/jsm/postprocessing/OutputPass.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';
import type { QualityPreset, RenderingConfig } from '../config/types';

/**
 * Renderer WebGL + cadeia de pós-processamento:
 *   cena (HDR, half-float) → bloom → tone mapping ACES + sRGB → FXAA.
 * Equivalente Unity: URP com Bloom e Tonemapping (ACES) num Volume global.
 */
export class RenderContext {
  readonly renderer: WebGLRenderer;
  private composer!: EffectComposer;
  private bloom!: UnrealBloomPass;
  private fxaa!: FXAAPass;
  private preset: QualityPreset;
  private bloomEnabled = true;

  constructor(
    private readonly container: HTMLElement,
    private readonly scene: Scene,
    private readonly camera: PerspectiveCamera,
    private readonly cfg: RenderingConfig,
    preset: QualityPreset,
  ) {
    this.preset = preset;
    this.renderer = new WebGLRenderer({ antialias: false, powerPreference: 'high-performance', stencil: false });
    this.renderer.outputColorSpace = SRGBColorSpace;
    this.renderer.toneMapping = ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = cfg.toneMappingExposure;
    this.renderer.setClearColor(0x000000, 1);
    // Zeramos as estatísticas manualmente: assim elas somam todas as passadas do quadro.
    this.renderer.info.autoReset = false;
    this.renderer.domElement.id = 'game-canvas';
    this.renderer.domElement.tabIndex = 0;
    container.appendChild(this.renderer.domElement);
    this.buildComposer();
    this.resize();
  }

  get canvas(): HTMLCanvasElement {
    return this.renderer.domElement;
  }

  get pixelRatio(): number {
    return Math.min(window.devicePixelRatio || 1, this.preset.pixelRatioCap);
  }

  /** Tamanho do buffer de desenho em pixels físicos. */
  get drawingSize(): Vector2 {
    return this.renderer.getDrawingBufferSize(new Vector2());
  }

  applyQuality(preset: QualityPreset, bloomEnabled: boolean): void {
    const msaaChanged = preset.msaa !== this.preset.msaa;
    this.preset = preset;
    this.bloomEnabled = bloomEnabled;
    if (msaaChanged) this.buildComposer();
    this.bloom.enabled = bloomEnabled;
    this.resize();
  }

  resize(): void {
    const w = Math.max(1, this.container.clientWidth);
    const h = Math.max(1, this.container.clientHeight);
    const pr = this.pixelRatio;
    this.renderer.setPixelRatio(pr);
    this.renderer.setSize(w, h);
    this.composer.setPixelRatio(pr);
    this.composer.setSize(w, h);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  render(): void {
    this.renderer.info.reset();
    this.composer.render();
  }

  dispose(): void {
    this.composer.dispose();
    this.renderer.dispose();
    this.renderer.domElement.remove();
  }

  private buildComposer(): void {
    this.composer?.dispose();
    const target = new WebGLRenderTarget(1, 1, { type: HalfFloatType, samples: this.preset.msaa });
    this.composer = new EffectComposer(this.renderer, target);
    this.composer.addPass(new RenderPass(this.scene, this.camera));
    const b = this.cfg.bloom;
    this.bloom = new UnrealBloomPass(new Vector2(256, 256), b.strength, b.radius, b.threshold);
    // Peso por nível de mip (fino → difuso). Favorecer os níveis finos dá o halo
    // justo de neon; os difusos em excesso enchem a tela de névoa colorida.
    const factors = this.bloom.compositeMaterial.uniforms.bloomFactors!.value as number[];
    b.mipWeights.forEach((w, i) => (factors[i] = w));
    this.bloom.enabled = this.bloomEnabled;
    this.composer.addPass(this.bloom);
    this.composer.addPass(new OutputPass());
    this.fxaa = new FXAAPass();
    // Com MSAA o FXAA é dispensável.
    this.fxaa.enabled = this.preset.msaa === 0;
    this.composer.addPass(this.fxaa);
  }
}
