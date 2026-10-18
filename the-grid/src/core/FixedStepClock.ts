/**
 * Acumulador de passo fixo (padrão "Fix Your Timestep").
 *
 * A simulação roda sempre com `fixedDt` (equivalente ao FixedUpdate da Unity),
 * independente da taxa de quadros. `alpha` é a fração restante para
 * interpolação visual entre dois passos.
 */
export class FixedStepClock {
  private accumulator = 0;

  constructor(
    readonly fixedDt: number,
    /** Limite de passos por quadro: evita "espiral da morte" após travamentos. */
    readonly maxStepsPerFrame = 5,
  ) {
    if (fixedDt <= 0) throw new Error('fixedDt deve ser positivo');
  }

  advance(frameDt: number): { steps: number; alpha: number } {
    this.accumulator += Math.max(0, frameDt);
    let steps = Math.floor(this.accumulator / this.fixedDt);
    if (steps > this.maxStepsPerFrame) {
      steps = this.maxStepsPerFrame;
      this.accumulator = 0; // descarta o atraso em vez de tentar recuperá-lo
    } else {
      this.accumulator -= steps * this.fixedDt;
    }
    return { steps, alpha: this.accumulator / this.fixedDt };
  }

  reset(): void {
    this.accumulator = 0;
  }
}
