import type { QualityLevel } from '../config/types';

/** Preferências do jogador (persistidas entre sessões). */
export interface PlayerSettings {
  quality: QualityLevel;
  bloom: boolean;
  /** Multiplicador sobre a sensibilidade base de `camera.json`. */
  mouseSensitivity: number;
  invertY: boolean;
  fov: number;
  showFps: boolean;
}

export const DEFAULT_SETTINGS: PlayerSettings = {
  quality: 'medium',
  bloom: true,
  mouseSensitivity: 1,
  invertY: false,
  fov: 62,
  showFps: true,
};

/** Abstração mínima de armazenamento (localStorage no navegador, PlayerPrefs na Unity). */
export interface KeyValueStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

const STORAGE_KEY = 'thegrid.settings.v1';
const QUALITY_LEVELS: QualityLevel[] = ['low', 'medium', 'high'];

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v));

/** Normaliza dados vindos do armazenamento (podem estar corrompidos ou antigos). */
export function sanitizeSettings(raw: unknown): PlayerSettings {
  const s = { ...DEFAULT_SETTINGS };
  if (typeof raw !== 'object' || raw === null) return s;
  const r = raw as Record<string, unknown>;
  if (typeof r.quality === 'string' && QUALITY_LEVELS.includes(r.quality as QualityLevel)) {
    s.quality = r.quality as QualityLevel;
  }
  if (typeof r.bloom === 'boolean') s.bloom = r.bloom;
  if (typeof r.invertY === 'boolean') s.invertY = r.invertY;
  if (typeof r.showFps === 'boolean') s.showFps = r.showFps;
  if (typeof r.mouseSensitivity === 'number' && Number.isFinite(r.mouseSensitivity)) {
    s.mouseSensitivity = clamp(r.mouseSensitivity, 0.2, 3);
  }
  if (typeof r.fov === 'number' && Number.isFinite(r.fov)) s.fov = clamp(Math.round(r.fov), 50, 90);
  return s;
}

export class SettingsStore {
  private current: PlayerSettings;

  constructor(
    private readonly storage: KeyValueStorage | null,
    private readonly onChange: (settings: PlayerSettings) => void = () => {},
  ) {
    this.current = this.load();
  }

  get(): Readonly<PlayerSettings> {
    return this.current;
  }

  update(patch: Partial<PlayerSettings>): void {
    this.current = sanitizeSettings({ ...this.current, ...patch });
    this.save();
    this.onChange(this.current);
  }

  reset(): void {
    this.update({ ...DEFAULT_SETTINGS });
  }

  private load(): PlayerSettings {
    try {
      const raw = this.storage?.getItem(STORAGE_KEY);
      return sanitizeSettings(raw ? JSON.parse(raw) : null);
    } catch {
      return { ...DEFAULT_SETTINGS };
    }
  }

  private save(): void {
    try {
      this.storage?.setItem(STORAGE_KEY, JSON.stringify(this.current));
    } catch {
      // Armazenamento indisponível (modo privado, cota): segue só em memória.
    }
  }
}

/** Retorna o localStorage se acessível, senão null. */
export function browserStorage(): KeyValueStorage | null {
  try {
    const ls = window.localStorage;
    const probe = '__thegrid_probe__';
    ls.setItem(probe, '1');
    ls.removeItem(probe);
    return ls;
  } catch {
    return null;
  }
}
