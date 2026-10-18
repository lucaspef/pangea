import type { RenderingConfig } from '../config/types';
import type { SettingsStore } from '../core/Settings';
import { Hud } from './Hud';
import { ScreenStack, el, type ScreenId } from './Screens';
import { SettingsPanel } from './SettingsPanel';

export interface UIActions {
  start(): void;
  resume(): void;
  mainMenu(): void;
  toggleCinematic(): void;
}

/**
 * Coordena telas e HUD. Não contém regras de jogo: cada botão chama uma
 * ação do Game (via UIActions); o estado do jogo decide o que mostrar.
 */
export class UIManager {
  readonly hud = new Hud();
  readonly screens = new ScreenStack();
  readonly settings: SettingsPanel;
  /** Tela para onde "Voltar" retorna a partir de Configurações/Controles. */
  private returnTo: ScreenId = 'menu';

  constructor(private readonly actions: UIActions, private readonly store: SettingsStore, rendering: RenderingConfig) {
    this.settings = new SettingsPanel(store, rendering);
    el('app').addEventListener('click', this.onClick);
  }

  showLoading(progress: number, text: string): void {
    el('loading-fill').style.width = `${Math.round(progress * 100)}%`;
    el('loading-text').textContent = text;
  }

  showError(message: string): void {
    el('error-text').textContent = message;
    this.screens.show('error');
  }

  /** Esc dentro de um submenu: volta um nível. Retorna true se tratou. */
  back(): boolean {
    const cur = this.screens.current;
    if (cur === 'settings' || cur === 'controls') {
      this.screens.show(this.returnTo);
      return true;
    }
    return false;
  }

  fade(on: boolean): void {
    el('fade').classList.toggle('on', on);
  }

  private readonly onClick = (e: MouseEvent): void => {
    const button = (e.target as HTMLElement).closest<HTMLElement>('[data-action]');
    if (!button) return;
    switch (button.dataset.action) {
      case 'start': this.actions.start(); break;
      case 'resume': this.actions.resume(); break;
      case 'main-menu': this.actions.mainMenu(); break;
      case 'toggle-cinematic': this.actions.toggleCinematic(); this.actions.resume(); break;
      case 'open-settings': this.openSub('settings'); break;
      case 'open-controls': this.openSub('controls'); break;
      case 'back': this.back(); break;
      case 'reset-settings': this.resetSettings(); break;
    }
  };

  private resetSettings(): void {
    this.store.reset();
    this.settings.refresh(this.store.get());
  }

  private openSub(id: 'settings' | 'controls'): void {
    const cur = this.screens.current;
    if (cur === 'menu' || cur === 'pause') this.returnTo = cur;
    this.screens.show(id);
  }
}
