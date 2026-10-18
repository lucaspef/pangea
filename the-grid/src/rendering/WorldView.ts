import { DoubleSide, FogExp2, type MeshStandardMaterial, type PerspectiveCamera, type Scene, type Texture } from 'three';
import type { QualityPreset, RenderingConfig } from '../config/types';
import type { World } from '../world/World';
import { ArenaRenderer } from './ArenaRenderer';
import { CityRenderer } from './CityRenderer';
import { DataParticles } from './DataParticles';
import { createFacadeMaterial } from './FacadeMaterial';
import { GridFloor } from './GridFloor';
import type { Palette } from './Palette';
import { PropRenderer } from './PropRenderer';
import { SkyDome } from './SkyDome';
import { TrafficRenderer } from './TrafficRenderer';

/**
 * Representação visual do World. Recria apenas o que depende da qualidade
 * (piso com/sem reflexo, número de partículas) quando o preset muda.
 */
export class WorldView {
  private readonly time = { value: 0 };
  private readonly city: CityRenderer;
  private readonly arenas: ArenaRenderer;
  private readonly sky: SkyDome;
  private readonly traffic: TrafficRenderer;
  private readonly props: PropRenderer;
  private floor: GridFloor | null = null;
  private particles: DataParticles | null = null;
  private readonly fog: FogExp2;
  private readonly materials: MeshStandardMaterial[];

  constructor(
    private readonly scene: Scene,
    private readonly world: World,
    private readonly palette: Palette,
    private readonly cfg: RenderingConfig,
    environment: Texture,
  ) {
    scene.environment = environment;
    this.fog = new FogExp2(palette.fog.getHex(), cfg.fog.density);
    // Mantém a cor da névoa em espaço linear idêntica à do piso/céu.
    this.fog.color.copy(palette.fog);
    scene.fog = this.fog;
    scene.background = palette.fog.clone().multiplyScalar(0.3);

    const facade = createFacadeMaterial(palette.body, this.time);
    const facadeDouble = createFacadeMaterial(palette.body, this.time);
    facadeDouble.side = DoubleSide;
    this.materials = [facade, facadeDouble];

    this.sky = new SkyDome(palette, this.time);
    this.city = new CityRenderer(world.city, palette, facade);
    this.arenas = new ArenaRenderer(world.config, world.arenas, palette, facade, facadeDouble);
    this.traffic = new TrafficRenderer(world.traffic, palette);
    this.props = new PropRenderer(world.props, palette, facade);
    scene.add(this.sky.group, this.city.group, this.arenas.group, this.traffic.mesh, this.props.group);
  }

  get stats(): { bodies: number; strips: number; reflection: boolean; particles: number } {
    return {
      ...this.city.stats,
      reflection: this.floor?.hasReflection ?? false,
      particles: this.particles ? (this.particles.points.geometry.getAttribute('position').count) : 0,
    };
  }

  /** (Re)cria os elementos dependentes de qualidade. */
  applyQuality(preset: QualityPreset, drawingWidth: number, drawingHeight: number, pixelRatio: number): void {
    const wantReflection = preset.reflection;
    if (!this.floor || this.floor.hasReflection !== wantReflection) {
      this.floor?.dispose();
      this.floor = new GridFloor(
        this.world.config.worldHalfExtent * 2 * 2.2,
        this.world.city,
        this.world.config,
        this.palette,
        { color: this.fog.color, density: this.cfg.fog.density },
        this.time,
        {
          reflection: wantReflection,
          reflectionWidth: drawingWidth * preset.reflectionScale,
          reflectionHeight: drawingHeight * preset.reflectionScale,
        },
      );
      this.floor.addTo(this.scene);
    }
    this.resize(drawingWidth, drawingHeight, preset);

    const currentCount = this.particles?.points.geometry.getAttribute('position').count ?? -1;
    if (currentCount !== preset.particles) {
      this.particles?.dispose();
      this.particles = new DataParticles(preset.particles, this.palette, this.time, pixelRatio);
      this.scene.add(this.particles.points);
    } else {
      this.particles?.setPixelRatio(pixelRatio);
    }
  }

  resize(drawingWidth: number, drawingHeight: number, preset: QualityPreset): void {
    this.floor?.setReflectionSize(drawingWidth * preset.reflectionScale, drawingHeight * preset.reflectionScale);
  }

  /** Atualização visual por quadro (após a simulação). */
  update(elapsed: number, camera: PerspectiveCamera): void {
    this.time.value = elapsed;
    this.traffic.sync();
    this.arenas.update(elapsed);
    this.sky.update(elapsed);
    this.sky.follow(camera.position);
    this.particles?.follow(camera.position);
  }

  dispose(): void {
    this.city.dispose();
    this.arenas.dispose();
    this.sky.dispose();
    this.traffic.dispose();
    this.props.dispose();
    this.floor?.dispose();
    this.particles?.dispose();
    for (const m of this.materials) m.dispose();
  }
}
