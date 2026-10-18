/**
 * Contratos de dados de configuração.
 *
 * Estes tipos espelham os arquivos JSON em `src/config/`. Na Fase 2 (Unity)
 * cada interface vira um ScriptableObject com os mesmos campos, e os JSON
 * podem ser importados diretamente por um script de editor.
 */

export type Vec3Tuple = [number, number, number];
export type Vec2Tuple = [number, number];

export interface CityConfig {
  blocksX: number;
  blocksZ: number;
  blockSize: number;
  avenueWidth: number;
  lotMargin: number;
  heightMin: number;
  heightMax: number;
  /** Quanto a altura cai do centro para a borda (0 = uniforme, 1 = forte). */
  centerFalloff: number;
  plazaChance: number;
  monumentHeight: number;
  bridgeCount: number;
  bridgeMinY: number;
  platformCount: number;
}

export interface CombatArenaConfig {
  center: Vec2Tuple;
  innerRadius: number;
  outerRadius: number;
  wallHeight: number;
  /** Abertura do portão voltado para a cidade (+Z), em graus. */
  gateWidthDeg: number;
}

export interface VehicleArenaConfig {
  center: Vec2Tuple;
  halfSize: Vec2Tuple;
  wallHeight: number;
  wallThickness: number;
  /** Largura do portão na parede voltada para a cidade (-X). */
  gateWidth: number;
}

export interface TrafficConfig {
  count: number;
  speedMin: number;
  speedMax: number;
  lengthMin: number;
  lengthMax: number;
}

/** Bloco sólido posicionado à mão (praça de entrada, percursos, coberturas). */
export interface PropConfig {
  id: string;
  x: number;
  z: number;
  /** Dimensões em X (w), Z (d) e altura (h). */
  w: number;
  d: number;
  h: number;
  /** Altura da base (padrão 0: apoiado no piso). */
  y?: number;
  accent?: 'cyan' | 'orange' | 'white';
  beacon?: boolean;
}

export interface WorldConfig {
  seed: number;
  city: CityConfig;
  sectors: { combatArena: CombatArenaConfig; vehicleArena: VehicleArenaConfig };
  traffic: TrafficConfig;
  worldHalfExtent: number;
  props: PropConfig[];
}

export type QualityLevel = 'low' | 'medium' | 'high';

export interface QualityPreset {
  label: string;
  pixelRatioCap: number;
  reflection: boolean;
  /** Resolução do reflexo do piso relativa à tela. */
  reflectionScale: number;
  particles: number;
  msaa: number;
}

export interface RenderingConfig {
  toneMappingExposure: number;
  fog: { color: string; density: number };
  bloom: {
    strength: number;
    radius: number;
    threshold: number;
    /** Peso de cada um dos 5 níveis de mip do bloom (do mais fino ao mais difuso). */
    mipWeights: number[];
  };
  palette: Record<'cyan' | 'blue' | 'orange' | 'white' | 'body' | 'floor', string>;
  presets: Record<QualityLevel, QualityPreset>;
}

export interface FreeCameraConfig {
  speed: number;
  boostMultiplier: number;
  acceleration: number;
  lookSensitivity: number;
  lookSmoothing: number;
  collisionRadius: number;
  minHeight: number;
  maxHeight: number;
  boostFovKick: number;
}

export interface CinematicCameraConfig {
  loopSeconds: number;
  lookAhead: number;
  rotationSmoothing: number;
  waypoints: Vec3Tuple[];
}

export interface CameraConfig {
  fov: number;
  near: number;
  far: number;
  modeBlendSeconds: number;
  spawn: { position: Vec3Tuple; yawDeg: number; pitchDeg: number };
  free: FreeCameraConfig;
  cinematic: CinematicCameraConfig;
}

export type InputAction =
  | 'jump'
  | 'dodge'
  | 'sprint'
  | 'toggleWalk'
  | 'toggleDrone'
  | 'moveForward'
  | 'moveBack'
  | 'moveLeft'
  | 'moveRight'
  | 'moveUp'
  | 'moveDown'
  | 'boost'
  | 'toggleCinematic'
  | 'toggleHud'
  | 'pause';

export interface InputConfig {
  actions: Record<InputAction, string[]>;
}

export interface CapsuleConfig {
  radius: number;
  height: number;
  /** Degrau máximo que o personagem sobe sem pular. */
  stepHeight: number;
}

export interface PlayerMovementConfig {
  walkSpeed: number;
  runSpeed: number;
  sprintSpeed: number;
  acceleration: number;
  deceleration: number;
  /** Fração da aceleração disponível no ar (0..1). */
  airControl: number;
  /** Velocidade de giro do corpo em direção ao movimento (rad/s, suavização exponencial). */
  turnSpeed: number;
  gravity: number;
  jumpHeight: number;
  /** Tempo após sair de uma borda em que o pulo ainda é aceito. */
  coyoteTime: number;
  /** Tempo em que um pulo apertado antes de tocar o chão fica guardado. */
  jumpBuffer: number;
  maxFallSpeed: number;
  /** Velocidade de queda a partir da qual a aterrissagem é "pesada". */
  hardLandingSpeed: number;
}

export interface PlayerConfig {
  capsule: CapsuleConfig;
  movement: PlayerMovementConfig;
  dodge: { distance: number; duration: number; cooldown: number; energyCost: number };
  energy: { max: number; sprintDrainPerSec: number; regenPerSec: number; regenDelay: number; minToSprint: number };
  spawn: { position: Vec3Tuple; yawDeg: number };
  camera: ThirdPersonCameraConfig;
}

export interface ThirdPersonCameraConfig {
  distance: number;
  sprintDistanceAdd: number;
  minDistance: number;
  pivotHeight: number;
  shoulderOffset: number;
  pitchMinDeg: number;
  pitchMaxDeg: number;
  followSmoothing: number;
  zoomInSmoothing: number;
  zoomOutSmoothing: number;
  collisionPadding: number;
  sprintFovKick: number;
}
