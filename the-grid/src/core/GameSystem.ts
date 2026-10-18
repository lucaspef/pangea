/**
 * Contrato de ciclo de vida de um sistema do jogo.
 *
 * Mapeamento para Unity (MonoBehaviour):
 *   fixedUpdate → FixedUpdate, update → Update, lateUpdate → LateUpdate,
 *   dispose → OnDestroy.
 */
export interface GameSystem {
  readonly name: string;
  fixedUpdate?(dt: number): void;
  update?(dt: number): void;
  lateUpdate?(dt: number): void;
  dispose?(): void;
}
