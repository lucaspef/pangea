import { describe, expect, it } from 'vitest';
import { aabb, type RingWall } from '../src/physics/Colliders';
import { CollisionWorld, sphereAabbPenetration, sphereRingPush } from '../src/physics/CollisionWorld';

describe('CollisionWorld', () => {
  const box = aabb(-5, 0, -5, 5, 20, 5);

  it('empurra uma esfera encostada para fora da face', () => {
    const p = { x: 6, y: 10, z: 0 };
    const push = sphereAabbPenetration(p, 2, box)!;
    expect(push.x).toBeCloseTo(1);
    expect(push.y).toBe(0);
  });

  it('tira o centro de dentro da caixa pela face mais próxima', () => {
    const w = new CollisionWorld();
    w.add(box);
    const p = { x: 4.5, y: 10, z: 0 };
    expect(w.resolveSphere(p, 1)).toBe(true);
    expect(p.x).toBeCloseTo(6);
    expect(w.overlapsSphere(p, 0.99)).toBe(false);
  });

  it('não corrige quando não há contato', () => {
    const w = new CollisionWorld();
    w.add(box);
    const p = { x: 20, y: 10, z: 20 };
    expect(w.resolveSphere(p, 2)).toBe(false);
    expect(p).toEqual({ x: 20, y: 10, z: 20 });
  });

  it('encontra AABBs que atravessam várias células do hash', () => {
    const w = new CollisionWorld(16);
    w.add(aabb(-100, 0, -2, 100, 5, 2));
    expect(w.queryAabbs(90, -1, 91, 1)).toHaveLength(1);
    expect(w.queryAabbs(-95, -1, -94, 1)).toHaveLength(1);
  });

  describe('parede anelar com portão', () => {
    const ring: RingWall = {
      kind: 'ring', cx: 0, cz: 0, innerRadius: 100, outerRadius: 110, height: 30,
      gateAngle: Math.PI / 2, gateHalfWidth: 0.2,
    };
    it('bloqueia por fora e por dentro', () => {
      expect(sphereRingPush({ x: 111, y: 5, z: 0 }, 2, ring)!.x).toBeCloseTo(1);
      expect(sphereRingPush({ x: 99, y: 5, z: 0 }, 2, ring)!.x).toBeCloseTo(-1);
    });
    it('deixa passar pelo portão (+Z) e por cima do muro', () => {
      expect(sphereRingPush({ x: 0, y: 5, z: 105 }, 2, ring)).toBeNull();
      expect(sphereRingPush({ x: 105, y: 40, z: 0 }, 2, ring)).toBeNull();
    });
  });
});
