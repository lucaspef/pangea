import type { PlayerSettings } from './Settings';
import type { SectorId } from '../world/SectorMap';

export type GameState = 'loading' | 'menu' | 'playing' | 'paused';
export type CameraMode = 'free' | 'cinematic';

/** Catálogo de eventos globais do jogo. */
export interface GameEvents {
  'state:changed': { state: GameState; previous: GameState };
  'settings:changed': { settings: PlayerSettings };
  'sector:changed': { sector: SectorId };
  'camera:mode': { mode: CameraMode };
}
