import { describe, expect, it } from 'vitest';
import { playerConfig, worldConfig } from '../src/config';
import { aabb, type RingWall } from '../src/physics/Colliders';
import { CollisionWorld } from '../src/physics/CollisionWorld';
import { CharacterMotor } from '../src/physics/CharacterMotor';
import { PlayerController } from '../src/player/PlayerController';
import { World } from '../src/world/World';

const DT = 1 / 60;
const cap = playerConfig.capsule;

function run(ctrl: PlayerController, seconds: number, intent = { x: 0, z: 0, sprint: false }) {
  ctrl.intent = intent;
  const steps = Math.round(seconds / DT);
  for (let i = 0; i < steps; i++) ctrl.fixedUpdate(DT);
}

function makeController(world: CollisionWorld) {
  const c = new PlayerController(playerConfig, world, 1000);
  c.spawn(0, 0, 0, 0);
  return c;
}

describe('CharacterMotor', () => {
  it('cai por gravidade e assenta no piso', () => {
    const ctrl = new PlayerController(playerConfig, new CollisionWorld(), 1000);
    ctrl.spawn(0, 5, 0, 0);
    expect(ctrl.motor.grounded).toBe(false);
    run(ctrl, 1.5);
    expect(ctrl.motor.position.y).toBe(0);
    expect(ctrl.motor.grounded).toBe(true);
  });

  it('sobe degraus baixos automaticamente', () => {
    const w = new CollisionWorld();
    w.add(aabb(-5, 0, -40, 5, 0.4, -2)); // degrau de 0,4 m à frente (-Z)
    const ctrl = makeController(w);
    run(ctrl, 1, { x: 0, z: -1, sprint: false });
    expect(ctrl.motor.position.z).toBeLessThan(-2.5);
    expect(ctrl.motor.position.y).toBeCloseTo(0.4);
  });

  it('é bloqueado por paredes mais altas que o degrau e desliza ao longo delas', () => {
    const w = new CollisionWorld();
    w.add(aabb(-5, 0, -3, 5, 1.2, -2));
    const ctrl = makeController(w);
    run(ctrl, 1, { x: 0.5, z: -1, sprint: false });
    expect(ctrl.motor.position.z).toBeGreaterThanOrEqual(-2 + cap.radius - 0.01);
    expect(ctrl.motor.position.x).toBeGreaterThan(1); // deslizou para o lado
    expect(ctrl.motor.position.y).toBe(0);
  });

  it('pula para cima de um bloco de 1,2 m', () => {
    const w = new CollisionWorld();
    w.add(aabb(-5, 0, -40, 5, 1.2, -2));
    const ctrl = makeController(w);
    ctrl.intent = { x: 0, z: -1, sprint: false };
    for (let i = 0; i < 20; i++) ctrl.fixedUpdate(DT);
    ctrl.requestJump();
    run(ctrl, 1.2, { x: 0, z: -1, sprint: false });
    expect(ctrl.motor.position.y).toBeCloseTo(1.2);
    expect(ctrl.motor.grounded).toBe(true);
  });

  it('bate a cabeça em vigas e volta ao chão', () => {
    const w = new CollisionWorld();
    w.add(aabb(-2, 2.4, -2, 2, 3.4, 2)); // viga acima do personagem (1,85 m)
    const ctrl = makeController(w);
    ctrl.requestJump();
    let maxY = 0;
    for (let i = 0; i < 90; i++) { ctrl.fixedUpdate(DT); maxY = Math.max(maxY, ctrl.motor.position.y); }
    expect(maxY + cap.height).toBeLessThanOrEqual(2.4 + 1e-6);
    expect(ctrl.motor.grounded).toBe(true);
  });

  it('desce degraus sem decolar e cai de bordas altas', () => {
    const w = new CollisionWorld();
    w.add(aabb(-5, 0, -1, 5, 0.4, 1));   // degrau baixo sob o personagem
    w.add(aabb(-5, 0, 10, 5, 3, 14));    // bloco alto mais adiante (+Z)
    const low = new PlayerController(playerConfig, w, 1000);
    low.spawn(0, 0.4, 0, 0);
    low.intent = { x: 0, z: 1, sprint: false };
    let airborne = 0;
    for (let i = 0; i < 30; i++) { low.fixedUpdate(DT); if (!low.motor.grounded) airborne++; }
    expect(low.motor.position.y).toBe(0);
    expect(airborne).toBe(0);

    const high = new PlayerController(playerConfig, w, 1000);
    high.spawn(0, 3, 13, Math.PI);
    high.intent = { x: 0, z: 1, sprint: false };
    let fell = false;
    for (let i = 0; i < 120; i++) { high.fixedUpdate(DT); if (!high.motor.grounded) fell = true; }
    expect(fell).toBe(true);
    expect(high.motor.position.y).toBe(0);
  });

  it('respeita a parede anelar e passa pelo portão', () => {
    const w = new CollisionWorld();
    const ring: RingWall = { kind: 'ring', cx: 0, cz: 0, innerRadius: 20, outerRadius: 24, height: 10, gateAngle: Math.PI / 2, gateHalfWidth: 0.3 };
    w.add(ring);
    const outside = new CharacterMotor(w, cap, 1000);
    outside.teleport(30, 0, 0);
    outside.velocity.x = -8;
    for (let i = 0; i < 120; i++) outside.step(DT);
    expect(outside.position.x).toBeGreaterThanOrEqual(24 + cap.radius - 0.01);

    const gate = new CharacterMotor(w, cap, 1000);
    gate.teleport(0, 0, 30);
    gate.velocity.z = -8;
    for (let i = 0; i < 120; i++) gate.step(DT);
    expect(gate.position.z).toBeLessThan(15);
  });
});

describe('PlayerController', () => {
  it('acelera até a velocidade de corrida, de sprint e de caminhada', () => {
    const m = playerConfig.movement;
    const ctrl = makeController(new CollisionWorld());
    run(ctrl, 1.5, { x: 1, z: 0, sprint: false });
    expect(ctrl.horizontalSpeed).toBeCloseTo(m.runSpeed, 1);
    expect(ctrl.state).toBe('run');
    run(ctrl, 1.5, { x: 1, z: 0, sprint: true });
    expect(ctrl.horizontalSpeed).toBeCloseTo(m.sprintSpeed, 1);
    expect(ctrl.state).toBe('sprint');
    ctrl.toggleWalk();
    run(ctrl, 1.5, { x: 1, z: 0, sprint: true });
    expect(ctrl.horizontalSpeed).toBeCloseTo(m.walkSpeed, 1);
    expect(ctrl.state).toBe('walk');
    run(ctrl, 1.5);
    expect(ctrl.state).toBe('idle');
  });

  it('vira o corpo na direção do movimento', () => {
    const ctrl = makeController(new CollisionWorld());
    run(ctrl, 1, { x: 1, z: 0, sprint: false }); // +X → yaw = -π/2
    expect(ctrl.facing).toBeCloseTo(-Math.PI / 2, 2);
  });

  it('o pulo atinge a altura configurada e passa pelos estados jump → fall → land/idle', () => {
    const ctrl = makeController(new CollisionWorld());
    ctrl.requestJump();
    const seen = new Set<string>();
    let maxY = 0;
    for (let i = 0; i < 90; i++) { ctrl.fixedUpdate(DT); seen.add(ctrl.state); maxY = Math.max(maxY, ctrl.motor.position.y); }
    expect(maxY).toBeGreaterThan(playerConfig.movement.jumpHeight * 0.95);
    expect(maxY).toBeLessThan(playerConfig.movement.jumpHeight * 1.05);
    expect(seen.has('jump') && seen.has('fall')).toBe(true);
    expect(ctrl.motor.grounded).toBe(true);
  });

  it('aceita o pulo logo após sair da borda (coyote time) e guarda o pulo apertado antes de tocar o chão', () => {
    const w = new CollisionWorld();
    w.add(aabb(-5, 0, -5, 5, 3, 0.5));
    const ctrl = new PlayerController(playerConfig, w, 1000);
    ctrl.spawn(0, 3, 0, Math.PI);
    ctrl.intent = { x: 0, z: 1, sprint: false };
    while (ctrl.motor.grounded) ctrl.fixedUpdate(DT);
    ctrl.fixedUpdate(DT); // já fora da borda
    ctrl.requestJump();
    ctrl.fixedUpdate(DT);
    expect(ctrl.motor.velocity.y).toBeGreaterThan(0);

    const buffered = makeController(new CollisionWorld());
    buffered.spawn(0, 2, 0, 0);
    // Aperta o pulo pouco antes de tocar o chão (ainda no ar).
    while (buffered.motor.position.y > 0.3) buffered.fixedUpdate(DT);
    expect(buffered.motor.grounded).toBe(false);
    buffered.requestJump();
    let jumpedAfterLanding = false;
    for (let i = 0; i < 30; i++) { buffered.fixedUpdate(DT); if (buffered.motor.velocity.y > 1) jumpedAfterLanding = true; }
    expect(jumpedAfterLanding).toBe(true);
  });

  it('a esquiva percorre a distância configurada e gasta energia', () => {
    const d = playerConfig.dodge;
    const ctrl = makeController(new CollisionWorld());
    ctrl.intent = { x: 0, z: -1, sprint: false };
    ctrl.requestDodge();
    ctrl.fixedUpdate(DT);
    expect(ctrl.state).toBe('dodge');
    expect(ctrl.energy).toBeCloseTo(playerConfig.energy.max - d.energyCost, 0);
    ctrl.intent = { x: 0, z: 0, sprint: false };
    for (let i = 0; i < Math.round(d.duration / DT) + 1; i++) ctrl.fixedUpdate(DT);
    expect(-ctrl.motor.position.z).toBeGreaterThan(d.distance * 0.9);
    expect(-ctrl.motor.position.z).toBeLessThan(d.distance * 1.2);
  });

  it('sem energia não esquiva; o sprint drena e a energia regenera', () => {
    const ctrl = makeController(new CollisionWorld());
    ctrl.energy = 5;
    ctrl.requestDodge();
    ctrl.fixedUpdate(DT);
    expect(ctrl.state).not.toBe('dodge');

    ctrl.energy = 100;
    run(ctrl, 3, { x: 1, z: 0, sprint: true });
    expect(ctrl.energy).toBeLessThan(100 - playerConfig.energy.sprintDrainPerSec * 2);
    const low = ctrl.energy;
    run(ctrl, 3);
    expect(ctrl.energy).toBeGreaterThan(low + 20);
  });
});

describe('Mundo do Marco 2', () => {
  const world = new World(worldConfig);

  it('o jogador nasce sobre a praça de entrada, sem colidir', () => {
    const ctrl = new PlayerController(playerConfig, world.collision, worldConfig.worldHalfExtent);
    const [x, y, z] = playerConfig.spawn.position;
    ctrl.spawn(x, y, z, 0);
    run(ctrl, 0.5);
    expect(ctrl.motor.grounded).toBe(true);
    expect(ctrl.motor.position.y).toBeCloseTo(0.8);
  });

  it('a escada do percurso leste leva até a plataforma de 2,8 m só andando', () => {
    const ctrl = new PlayerController(playerConfig, world.collision, worldConfig.worldHalfExtent);
    ctrl.spawn(0, 0.8, 446, -Math.PI / 2);
    ctrl.intent = { x: 1, z: 0, sprint: false };
    let steps = 0;
    while (ctrl.motor.position.x < 50 && steps++ < 1200) ctrl.fixedUpdate(DT);
    expect(ctrl.motor.position.x).toBeGreaterThanOrEqual(50);
    expect(ctrl.motor.position.y).toBeCloseTo(2.8);
  });

  it('os saltos do percurso leste são possíveis (plataforma A → B → C → D)', () => {
    const ctrl = new PlayerController(playerConfig, world.collision, worldConfig.worldHalfExtent);
    ctrl.spawn(50, 2.8, 446, -Math.PI / 2);
    // [borda onde pular, altura esperada após o salto, x a alcançar]
    const legs: [number, number, number][] = [[57.4, 2.8, 62.5], [65.4, 4.0, 69.5], [72.4, 5.2, 76.5]];
    for (const [edge, height, target] of legs) {
      ctrl.intent = { x: 1, z: 0, sprint: true };
      let steps = 0, jumped = false;
      while (steps++ < 600) {
        if (!jumped && ctrl.motor.position.x >= edge) { ctrl.requestJump(); jumped = true; }
        ctrl.fixedUpdate(DT);
        if (jumped && ctrl.motor.grounded && ctrl.motor.position.x >= target) break;
      }
      // Para sobre o bloco antes do próximo salto.
      ctrl.intent = { x: 0, z: 0, sprint: false };
      for (let i = 0; i < 40; i++) ctrl.fixedUpdate(DT);
      expect(ctrl.motor.position.y, `salto até x=${target}`).toBeCloseTo(height);
    }
  });

  it('props não se sobrepõem a torres', () => {
    for (const p of world.props) {
      for (const t of world.city.towers) {
        const b = t.tiers[0]!;
        const overlap = Math.abs(p.box.minX + p.box.maxX - 2 * t.x) / 2 < (p.box.maxX - p.box.minX + b.width) / 2
          && Math.abs(p.box.minZ + p.box.maxZ - 2 * t.z) / 2 < (p.box.maxZ - p.box.minZ + b.depth) / 2;
        expect(overlap, `${p.id} sobrepõe torre ${t.id}`).toBe(false);
      }
    }
  });
});

describe('CollisionWorld.raycast', () => {
  it('encontra a primeira caixa ao longo do raio', () => {
    const w = new CollisionWorld();
    w.add(aabb(4, 0, -1, 6, 3, 1));
    expect(w.raycast({ x: 0, y: 1, z: 0 }, { x: 1, y: 0, z: 0 }, 10)).toBeCloseTo(4);
    expect(w.raycast({ x: 0, y: 5, z: 0 }, { x: 1, y: 0, z: 0 }, 10)).toBe(10);
    expect(w.raycast({ x: 0, y: 1, z: 0 }, { x: -1, y: 0, z: 0 }, 10)).toBe(10);
  });
});
