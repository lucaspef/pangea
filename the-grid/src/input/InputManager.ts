import type { InputAction, InputConfig } from '../config/types';

/**
 * Converte teclado/mouse em ações abstratas definidas em `input.json`.
 * O restante do jogo consulta ações ("moveForward"), nunca teclas.
 * Equivalente Unity: Input System com um InputActionAsset.
 */
export class InputManager {
  private readonly down = new Set<string>();
  private readonly pressed = new Set<string>();
  private readonly codeToActions = new Map<string, InputAction[]>();
  private lookX = 0;
  private lookY = 0;
  private dragging = false;
  private enabled = true;
  /** Até quando ignorar o mouse (ms): o navegador emite um salto falso ao capturar o cursor. */
  private ignoreLookUntil = 0;

  constructor(private readonly target: HTMLElement, config: InputConfig) {
    for (const [action, codes] of Object.entries(config.actions) as [InputAction, string[]][]) {
      for (const code of codes) {
        const list = this.codeToActions.get(code) ?? [];
        list.push(action);
        this.codeToActions.set(code, list);
      }
    }
    window.addEventListener('keydown', this.onKeyDown);
    window.addEventListener('keyup', this.onKeyUp);
    window.addEventListener('blur', this.onBlur);
    document.addEventListener('mousemove', this.onMouseMove);
    target.addEventListener('mousedown', this.onMouseDown);
    window.addEventListener('mouseup', this.onMouseUp);
    document.addEventListener('pointerlockchange', this.onPointerLockChange);
  }

  /** Desabilitado = ignora movimento/olhar (ex.: com menus abertos). */
  setEnabled(value: boolean): void {
    this.enabled = value;
    if (!value) {
      this.down.clear();
      this.lookX = this.lookY = 0;
    }
  }

  isDown(action: InputAction): boolean {
    if (!this.enabled) return false;
    for (const code of this.down) if (this.codeToActions.get(code)?.includes(action)) return true;
    return false;
  }

  /** Verdadeiro apenas no quadro em que a ação foi acionada (funciona mesmo desabilitado). */
  wasPressed(action: InputAction): boolean {
    for (const code of this.pressed) if (this.codeToActions.get(code)?.includes(action)) return true;
    return false;
  }

  /** Eixo -1..1 a partir de duas ações opostas. */
  axis(negative: InputAction, positive: InputAction): number {
    return (this.isDown(positive) ? 1 : 0) - (this.isDown(negative) ? 1 : 0);
  }

  /** Delta de mouse acumulado desde a última leitura (pixels). */
  consumeLook(): { x: number; y: number } {
    const out = { x: this.lookX, y: this.lookY };
    this.lookX = this.lookY = 0;
    return out;
  }

  get pointerLocked(): boolean {
    return document.pointerLockElement === this.target;
  }

  requestPointerLock(): void {
    // Alguns contextos (iframes sem permissão, automação) negam o bloqueio:
    // nesse caso o olhar funciona arrastando com o botão do mouse.
    try {
      const result = this.target.requestPointerLock() as unknown;
      if (result instanceof Promise) result.catch(() => {});
    } catch {
      /* fallback: arrastar */
    }
  }

  exitPointerLock(): void {
    if (this.pointerLocked) document.exitPointerLock();
  }

  /** Chamado ao fim de cada quadro. */
  endFrame(): void {
    this.pressed.clear();
  }

  dispose(): void {
    window.removeEventListener('keydown', this.onKeyDown);
    window.removeEventListener('keyup', this.onKeyUp);
    window.removeEventListener('blur', this.onBlur);
    document.removeEventListener('mousemove', this.onMouseMove);
    this.target.removeEventListener('mousedown', this.onMouseDown);
    window.removeEventListener('mouseup', this.onMouseUp);
    document.removeEventListener('pointerlockchange', this.onPointerLockChange);
  }

  private readonly onPointerLockChange = (): void => {
    this.ignoreLookUntil = performance.now() + 80;
    this.lookX = this.lookY = 0;
  };

  private readonly onKeyDown = (e: KeyboardEvent): void => {
    if (!this.codeToActions.has(e.code)) return;
    // Evita rolagem da página com espaço/setas.
    if (e.target === document.body || e.target === this.target) e.preventDefault();
    if (!e.repeat) this.pressed.add(e.code);
    this.down.add(e.code);
  };

  private readonly onKeyUp = (e: KeyboardEvent): void => {
    this.down.delete(e.code);
  };

  private readonly onBlur = (): void => {
    this.down.clear();
    this.dragging = false;
  };

  private readonly onMouseDown = (e: MouseEvent): void => {
    if (e.button === 0 || e.button === 2) this.dragging = true;
  };

  private readonly onMouseUp = (): void => {
    this.dragging = false;
  };

  private readonly onMouseMove = (e: MouseEvent): void => {
    if (!this.enabled || performance.now() < this.ignoreLookUntil) return;
    // Picos impossíveis para um único evento (alguns navegadores os emitem ao travar o cursor).
    if (Math.abs(e.movementX) > window.innerWidth / 3 || Math.abs(e.movementY) > window.innerHeight / 3) return;
    if (this.pointerLocked || this.dragging) {
      this.lookX += e.movementX;
      this.lookY += e.movementY;
    }
  };
}
