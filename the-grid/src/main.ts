import './ui/styles.css';
import { Game } from './core/Game';

const viewport = document.getElementById('viewport');
if (!viewport) throw new Error('#viewport ausente no index.html');

const game = new Game(viewport);

// API de diagnóstico (console do navegador / testes automatizados).
(window as unknown as { __THE_GRID__: unknown }).__THE_GRID__ = {
  snapshot: () => game.debugSnapshot(),
  teleport: (x: number, y: number, z: number, yawDeg?: number, pitchDeg?: number) => game.debugTeleport(x, y, z, yawDeg, pitchDeg),
  teleportPlayer: (x: number, y: number, z: number, yawDeg?: number) => game.debugTeleportPlayer(x, y, z, yawDeg),
  settings: game.settings,
  scene: game.scene,
  events: game.events,
};

game.init().catch((err) => {
  console.error('[THE GRID] falha na inicialização', err);
});

if (import.meta.hot) {
  import.meta.hot.dispose(() => game.dispose());
}
