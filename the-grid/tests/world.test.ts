import { describe, expect, it } from 'vitest';
import { cameraConfig, worldConfig } from '../src/config';
import { aabb, aabbFromFootprint, aabbOverlap } from '../src/physics/Colliders';
import { CollisionWorld } from '../src/physics/CollisionWorld';
import { CinematicController } from '../src/camera/CinematicController';
import { FreeFlyController } from '../src/camera/FreeFlyController';
import { Vector3 } from 'three';
import { CityGenerator } from '../src/world/CityGenerator';
import { SectorMap } from '../src/world/SectorMap';
import { TrafficSystem } from '../src/world/TrafficSystem';
import { World } from '../src/world/World';

const layout = new CityGenerator(worldConfig.city, worldConfig.seed).generate();

describe('CityGenerator', () => {
  it('é determinístico', () => {
    const again = new CityGenerator(worldConfig.city, worldConfig.seed).generate();
    expect(again).toEqual(layout);
    const other = new CityGenerator(worldConfig.city, worldConfig.seed + 1).generate();
    expect(other.towers).not.toEqual(layout.towers);
  });

  it('gera uma cidade densa com pontes e plataformas', () => {
    expect(layout.towers.length).toBeGreaterThan(150);
    expect(layout.bridges.length).toBeGreaterThan(10);
    expect(layout.platforms.length).toBeGreaterThan(0);
    expect(layout.towers.filter((t) => t.monument)).toHaveLength(4);
  });

  it('mantém o boulevard central em x = 0 e z = 0', () => {
    expect(layout.avenuesX[layout.boulevardX]).toBeCloseTo(0);
    expect(layout.avenuesZ[layout.boulevardZ]).toBeCloseTo(0);
  });

  it('nenhuma torre invade avenidas nem se sobrepõe a outra', () => {
    const half = layout.avenueWidth / 2;
    const boxes = layout.towers.map((t) => aabbFromFootprint(t.x, t.z, t.tiers[0]!.width, t.tiers[0]!.depth, t.height));
    for (const b of boxes) {
      for (const ax of layout.avenuesX) expect(b.maxX <= ax - half || b.minX >= ax + half).toBe(true);
      for (const az of layout.avenuesZ) expect(b.maxZ <= az - half || b.minZ >= az + half).toBe(true);
    }
    for (let i = 0; i < boxes.length; i++) {
      for (let j = i + 1; j < boxes.length; j++) expect(aabbOverlap(boxes[i]!, boxes[j]!)).toBe(false);
    }
  });

  it('níveis superiores cabem dentro do nível inferior', () => {
    for (const t of layout.towers) {
      for (let i = 1; i < t.tiers.length; i++) {
        expect(t.tiers[i]!.width).toBeLessThanOrEqual(t.tiers[i - 1]!.width);
        expect(t.tiers[i]!.baseY).toBeCloseTo(t.tiers[i - 1]!.baseY + t.tiers[i - 1]!.height);
      }
      const last = t.tiers[t.tiers.length - 1]!;
      expect(last.baseY + last.height).toBeCloseTo(t.height);
    }
  });

  it('pontes nunca cruzam os boulevards e ficam acima da altura mínima', () => {
    const half = layout.avenueWidth / 2;
    for (const b of layout.bridges) {
      expect(b.y).toBeGreaterThanOrEqual(worldConfig.city.bridgeMinY);
      if (b.axis === 'x') expect(b.x - b.length / 2 > half || b.x + b.length / 2 < -half).toBe(true);
      if (b.axis === 'z') expect(b.z - b.length / 2 > half || b.z + b.length / 2 < -half).toBe(true);
    }
  });
});

describe('SectorMap', () => {
  const map = new SectorMap(worldConfig);
  it('classifica os três setores e as vias de conexão', () => {
    expect(map.sectorAt(0, 0)).toBe('city');
    expect(map.sectorAt(...worldConfig.sectors.combatArena.center)).toBe('combatArena');
    expect(map.sectorAt(...worldConfig.sectors.vehicleArena.center)).toBe('vehicleArena');
    expect(map.sectorAt(0, -380)).toBe('transit');
  });
});

describe('Mundo e câmeras', () => {
  const world = new World(worldConfig);

  it('o ponto de entrada do jogador está livre', () => {
    const [x, y, z] = cameraConfig.spawn.position;
    expect(world.collision.overlapsSphere({ x, y, z }, cameraConfig.free.collisionRadius)).toBe(false);
  });

  it('a rota cinematográfica não atravessa nenhuma construção', () => {
    const cine = new CinematicController(cameraConfig.cinematic);
    const blocked = cine.samplePoints(1500).filter((p) => world.collision.overlapsSphere(p, 1.5) || p.y < 2);
    expect(blocked.map((p) => p.toArray().map(Math.round))).toEqual([]);
  });

  it('a câmera livre para na face de uma construção', () => {
    const collision = new CollisionWorld();
    collision.add(aabb(-10, 0, -60, 10, 80, -40)); // face sul em z = -40
    const fly = new FreeFlyController(cameraConfig.free, collision);
    fly.setPose({ position: new Vector3(0, 10, 0), yaw: 0, pitch: 0 }); // olhando para -Z
    const intent = { move: new Vector3(0, 0, 1), lookDeltaX: 0, lookDeltaY: 0, boost: true };
    for (let i = 0; i < 600; i++) fly.update(1 / 60, intent);
    const p = fly.pose.position;
    expect(p.z).toBeGreaterThanOrEqual(-40 + cameraConfig.free.collisionRadius - 0.05);
    expect(p.z).toBeLessThan(-35);
    expect(collision.overlapsSphere(p, cameraConfig.free.collisionRadius - 0.01)).toBe(false);
  });

  it('voando pelo boulevard a partir do ponto de entrada, a câmera atravessa a cidade', () => {
    const fly = new FreeFlyController(cameraConfig.free, world.collision);
    const [x, y, z] = cameraConfig.spawn.position;
    fly.setPose({ position: new Vector3(x, y, z), yaw: 0, pitch: 0 });
    const intent = { move: new Vector3(0, 0, 1), lookDeltaX: 0, lookDeltaY: 0, boost: false };
    for (let i = 0; i < 60 * 10; i++) fly.update(1 / 60, intent);
    expect(fly.pose.position.z).toBeLessThan(z - 300);
    expect(Math.abs(fly.pose.position.x)).toBeLessThan(0.01);
  });

  it('o tráfego se move e permanece nas faixas', () => {
    const traffic = new TrafficSystem(world.city, worldConfig);
    const before = traffic.runners.map((r) => r.pos);
    for (let i = 0; i < 600; i++) traffic.fixedUpdate(1 / 60);
    traffic.runners.forEach((r, i) => {
      expect(r.pos).not.toBe(before[i]);
      expect(r.pos).toBeGreaterThanOrEqual(r.min);
      expect(r.pos).toBeLessThanOrEqual(r.max);
    });
  });
});
