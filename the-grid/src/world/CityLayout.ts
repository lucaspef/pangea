/**
 * Dados puros que descrevem a cidade. Nada aqui depende de Three.js:
 * o renderizador lê estes dados e constrói malhas; a física cria colisores.
 * Na Unity, um "CityBuilder" consumiria o mesmo layout para instanciar prefabs.
 */
export type TowerShape = 'block' | 'tiered' | 'octagon' | 'spire' | 'slab';
export type AccentColor = 'cyan' | 'orange' | 'white';
/** Padrão das linhas luminosas na fachada. */
export type StripStyle = 'corners' | 'bands' | 'crown';

export interface TowerTier {
  width: number;
  depth: number;
  baseY: number;
  height: number;
}

export interface TowerDef {
  id: number;
  shape: TowerShape;
  /** Centro da base no plano XZ. */
  x: number;
  z: number;
  /** Altura total (topo do último nível). */
  height: number;
  /** Níveis empilhados, de baixo para cima. O primeiro define a pegada. */
  tiers: TowerTier[];
  accent: AccentColor;
  style: StripStyle;
  block: [number, number];
  monument: boolean;
}

export interface BridgeDef {
  /** Eixo ao longo do qual a ponte se estende. */
  axis: 'x' | 'z';
  /** Centro da ponte. */
  x: number;
  y: number;
  z: number;
  length: number;
  width: number;
  thickness: number;
  accent: AccentColor;
}

export interface PlatformDef {
  x: number;
  y: number;
  z: number;
  width: number;
  depth: number;
  thickness: number;
}

export interface CityLayout {
  halfX: number;
  halfZ: number;
  avenueWidth: number;
  blockSize: number;
  /** Centros das avenidas em X (linhas paralelas a Z) e em Z. */
  avenuesX: number[];
  avenuesZ: number[];
  /** Índices das avenidas centrais (boulevards) que conectam os setores. */
  boulevardX: number;
  boulevardZ: number;
  towers: TowerDef[];
  bridges: BridgeDef[];
  platforms: PlatformDef[];
}
