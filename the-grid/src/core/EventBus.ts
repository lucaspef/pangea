/**
 * Barramento de eventos tipado. Desacopla sistemas (UI, câmera, mundo) sem
 * referências diretas entre eles. Equivalente em Unity: eventos C# estáticos
 * ou um ScriptableObject "GameEvent channel".
 */
type Handler<T> = (payload: T) => void;

export class EventBus<Events extends object> {
  private readonly handlers = new Map<keyof Events, Set<Handler<never>>>();

  on<K extends keyof Events>(type: K, handler: Handler<Events[K]>): () => void {
    let set = this.handlers.get(type);
    if (!set) {
      set = new Set();
      this.handlers.set(type, set);
    }
    set.add(handler as Handler<never>);
    return () => set.delete(handler as Handler<never>);
  }

  emit<K extends keyof Events>(type: K, payload: Events[K]): void {
    const set = this.handlers.get(type);
    if (!set) return;
    for (const handler of [...set]) (handler as Handler<Events[K]>)(payload);
  }

  clear(): void {
    this.handlers.clear();
  }
}
