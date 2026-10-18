/**
 * Ponto único de acesso às configurações de gameplay e renderização.
 * Os JSON são empacotados pelo Vite; a validação mínima abaixo falha cedo
 * (com mensagem clara) se algum arquivo for editado de forma inconsistente.
 */
import worldJson from './world.json';
import renderingJson from './rendering.json';
import cameraJson from './camera.json';
import inputJson from './input.json';
import type { CameraConfig, InputConfig, RenderingConfig, WorldConfig } from './types';

function assert(condition: boolean, message: string): asserts condition {
  if (!condition) throw new Error(`[config] ${message}`);
}

export const worldConfig = worldJson as WorldConfig;
export const renderingConfig = renderingJson as RenderingConfig;
export const cameraConfig = cameraJson as unknown as CameraConfig;
export const inputConfig = inputJson as InputConfig;

assert(worldConfig.city.blocksX % 2 === 0 && worldConfig.city.blocksZ % 2 === 0,
  'blocksX/blocksZ devem ser pares para existir uma avenida central (boulevard).');
assert(worldConfig.city.heightMin < worldConfig.city.heightMax, 'heightMin deve ser menor que heightMax.');
assert(cameraConfig.cinematic.waypoints.length >= 4, 'a câmera cinemática precisa de ao menos 4 waypoints.');
assert(worldConfig.sectors.combatArena.innerRadius < worldConfig.sectors.combatArena.outerRadius,
  'innerRadius da arena deve ser menor que outerRadius.');
