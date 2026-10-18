import type { RenderingConfig } from '../config/types';
import type { PlayerSettings, SettingsStore } from '../core/Settings';
import { el } from './Screens';

/** Liga o formulário de configurações ao SettingsStore (mudanças aplicam na hora). */
export class SettingsPanel {
  private readonly form = el<HTMLFormElement>('settings-form');
  private readonly info = el('settings-info');

  constructor(private readonly store: SettingsStore, private readonly rendering: RenderingConfig) {
    this.form.addEventListener('input', this.onInput);
    this.form.addEventListener('change', this.onInput);
    this.refresh(store.get());
  }

  /** Reflete as configurações atuais nos campos. */
  refresh(s: Readonly<PlayerSettings>): void {
    const f = this.form.elements;
    (f.namedItem('quality') as HTMLSelectElement).value = s.quality;
    (f.namedItem('bloom') as HTMLInputElement).checked = s.bloom;
    (f.namedItem('mouseSensitivity') as HTMLInputElement).value = String(s.mouseSensitivity);
    (f.namedItem('invertY') as HTMLInputElement).checked = s.invertY;
    (f.namedItem('fov') as HTMLInputElement).value = String(s.fov);
    (f.namedItem('showFps') as HTMLInputElement).checked = s.showFps;
    (f.namedItem('mouseSensitivityOut') as HTMLOutputElement).value = `${s.mouseSensitivity.toFixed(1)}×`;
    (f.namedItem('fovOut') as HTMLOutputElement).value = `${s.fov}°`;
    const p = this.rendering.presets[s.quality];
    this.info.textContent =
      `Preset ${p.label}: escala ${p.pixelRatioCap}× · reflexo ${p.reflection ? `${Math.round(p.reflectionScale * 100)}%` : 'desligado'}` +
      ` · ${p.particles} partículas · ${p.msaa ? `MSAA ${p.msaa}×` : 'FXAA'}`;
  }

  private readonly onInput = (): void => {
    const f = this.form.elements;
    this.store.update({
      quality: (f.namedItem('quality') as HTMLSelectElement).value as PlayerSettings['quality'],
      bloom: (f.namedItem('bloom') as HTMLInputElement).checked,
      mouseSensitivity: Number((f.namedItem('mouseSensitivity') as HTMLInputElement).value),
      invertY: (f.namedItem('invertY') as HTMLInputElement).checked,
      fov: Number((f.namedItem('fov') as HTMLInputElement).value),
      showFps: (f.namedItem('showFps') as HTMLInputElement).checked,
    });
    this.refresh(this.store.get());
  };
}
