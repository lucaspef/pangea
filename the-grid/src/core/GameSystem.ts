/**
 * Contrato de ciclo de vida de um sistema do jogo.
 *
 * Ordem por quadro: earlyUpdate → fixedUpdate (0..N) → update → lateUpdate → render.
 * Mapeamento para Unity (MonoBehaviour):
 *   earlyUpdate → leitura de entrada (Input System, antes do FixedUpdate),
 *   fixedUpdate → FixedUpdate, update → Update, lateUpdate → LateUpdate,
 *   dispose → OnDestroy.
 *
 * `alpha` (0..1) é a fração do passo fixo ainda não simulado: use-a para
 * interpolar posições visuais entre o estado anterior e o atual.
 */
export interface GameSystem {
  readonly name: string;
  earlyUpdate?(dt: number): void;
  fixedUpdate?(dt: number): void;
  update?(dt: number, alpha: number): void;
  lateUpdate?(dt: number, alpha: number): void;
  dispose?(): void;
}
