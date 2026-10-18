/** Utilitários de DOM para telas e elementos obrigatórios. */
export function el<T extends HTMLElement = HTMLElement>(id: string): T {
  const node = document.getElementById(id);
  if (!node) throw new Error(`[ui] elemento #${id} não encontrado`);
  return node as T;
}

export type ScreenId = 'loading' | 'error' | 'menu' | 'pause' | 'settings' | 'controls' | 'none';

export class ScreenStack {
  private readonly screens = new Map<Exclude<ScreenId, 'none'>, HTMLElement>();
  private currentId: ScreenId = 'loading';

  constructor() {
    for (const id of ['loading', 'error', 'menu', 'pause', 'settings', 'controls'] as const) {
      this.screens.set(id, el(`screen-${id}`));
    }
  }

  get current(): ScreenId {
    return this.currentId;
  }

  show(id: ScreenId): void {
    this.currentId = id;
    for (const [key, node] of this.screens) node.classList.toggle('visible', key === id);
    // Foco no primeiro botão: navegação por teclado (Tab/Enter) funciona nos menus.
    if (id !== 'none') {
      const first = this.screens.get(id)?.querySelector<HTMLElement>('button, select, input');
      first?.focus({ preventScroll: true });
    }
  }
}
