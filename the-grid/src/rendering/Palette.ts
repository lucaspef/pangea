import { Color } from 'three';
import type { RenderingConfig } from '../config/types';
import type { AccentColor } from '../world/CityLayout';

/**
 * Paleta central. Cores emissivas são multiplicadas acima de 1.0 (HDR):
 * só o que ultrapassa o limiar do bloom brilha, sem precisar de camadas extras.
 */
export class Palette {
  readonly cyan: Color;
  readonly blue: Color;
  readonly orange: Color;
  readonly white: Color;
  readonly body: Color;
  readonly floor: Color;
  readonly fog: Color;

  constructor(cfg: RenderingConfig) {
    this.cyan = new Color(cfg.palette.cyan);
    this.blue = new Color(cfg.palette.blue);
    this.orange = new Color(cfg.palette.orange);
    this.white = new Color(cfg.palette.white);
    this.body = new Color(cfg.palette.body);
    this.floor = new Color(cfg.palette.floor);
    this.fog = new Color(cfg.fog.color);
  }

  accent(name: AccentColor): Color {
    return name === 'orange' ? this.orange : name === 'white' ? this.white : this.cyan;
  }

  /** Cor emissiva HDR (nova instância). */
  glow(name: AccentColor | 'blue', intensity: number): Color {
    const base = name === 'blue' ? this.blue : this.accent(name);
    return base.clone().multiplyScalar(intensity);
  }
}
