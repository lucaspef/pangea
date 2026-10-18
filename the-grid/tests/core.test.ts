import { describe, expect, it } from 'vitest';
import { EventBus } from '../src/core/EventBus';
import { FixedStepClock } from '../src/core/FixedStepClock';
import { Rng } from '../src/core/Rng';
import { DEFAULT_SETTINGS, SettingsStore, sanitizeSettings, type KeyValueStorage } from '../src/core/Settings';

describe('Rng', () => {
  it('é determinístico para a mesma seed', () => {
    const a = new Rng(42), b = new Rng(42);
    for (let i = 0; i < 100; i++) expect(a.next()).toBe(b.next());
  });
  it('produz valores em [0, 1) e inteiros inclusivos', () => {
    const r = new Rng(7);
    const ints = new Set<number>();
    for (let i = 0; i < 2000; i++) {
      const v = r.next();
      expect(v).toBeGreaterThanOrEqual(0);
      expect(v).toBeLessThan(1);
      ints.add(r.int(1, 3));
    }
    expect([...ints].sort()).toEqual([1, 2, 3]);
  });
});

describe('FixedStepClock', () => {
  it('acumula frações e executa o número correto de passos', () => {
    const c = new FixedStepClock(1 / 60);
    expect(c.advance(1 / 120).steps).toBe(0);
    expect(c.advance(1 / 120).steps).toBe(1);
    expect(c.advance(0.05).steps).toBe(3);
  });
  it('limita passos após um travamento longo', () => {
    const c = new FixedStepClock(1 / 60, 5);
    expect(c.advance(2).steps).toBe(5);
    expect(c.advance(0).steps).toBe(0); // atraso descartado
  });
});

describe('EventBus', () => {
  it('entrega eventos e permite cancelar a inscrição', () => {
    const bus = new EventBus<{ ping: { n: number } }>();
    const got: number[] = [];
    const off = bus.on('ping', (p) => got.push(p.n));
    bus.emit('ping', { n: 1 });
    off();
    bus.emit('ping', { n: 2 });
    expect(got).toEqual([1]);
  });
});

describe('Settings', () => {
  class MemoryStorage implements KeyValueStorage {
    data = new Map<string, string>();
    getItem(k: string) { return this.data.get(k) ?? null; }
    setItem(k: string, v: string) { this.data.set(k, v); }
  }

  it('saneia valores inválidos', () => {
    const s = sanitizeSettings({ quality: 'ultra', fov: 500, mouseSensitivity: -3, bloom: 'sim' });
    expect(s.quality).toBe(DEFAULT_SETTINGS.quality);
    expect(s.fov).toBe(90);
    expect(s.mouseSensitivity).toBe(0.2);
    expect(s.bloom).toBe(DEFAULT_SETTINGS.bloom);
  });

  it('persiste e recarrega', () => {
    const storage = new MemoryStorage();
    const changes: string[] = [];
    const store = new SettingsStore(storage, (s) => changes.push(s.quality));
    store.update({ quality: 'high', invertY: true });
    const reloaded = new SettingsStore(storage);
    expect(reloaded.get().quality).toBe('high');
    expect(reloaded.get().invertY).toBe(true);
    expect(changes).toEqual(['high']);
  });

  it('tolera armazenamento corrompido', () => {
    const storage = new MemoryStorage();
    storage.setItem('thegrid.settings.v1', '{nao-e-json');
    expect(new SettingsStore(storage).get()).toEqual(DEFAULT_SETTINGS);
  });
});
