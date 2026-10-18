import {
  AdditiveBlending, Color, DoubleSide, Group, Mesh, MeshBasicMaterial, PlaneGeometry, RingGeometry, ShaderMaterial,
  type Scene,
} from 'three';
import type { PlayerMovementConfig } from '../config/types';
import type { Palette } from '../rendering/Palette';
import { CharacterModel } from './CharacterModel';
import type { AnimationInput } from './PlayerState';
import { ProceduralAnimator } from './ProceduralAnimator';

/** Contrato da camada visual de um personagem (procedural hoje, glTF/Unity amanhã). */
export interface CharacterView {
  /** Posição dos pés (interpolada), altura do chão sob o personagem e animação. */
  update(x: number, y: number, z: number, groundY: number, anim: AnimationInput, dt: number): void;
  /** Efeito de impacto no chão (aterrissagem, esquiva). */
  pulse(x: number, y: number, z: number, strength: number): void;
  setVisible(visible: boolean): void;
  dispose(): void;
}

interface Shockwave { mesh: Mesh; material: MeshBasicMaterial; age: number; life: number; strength: number }

/**
 * Visual do jogador: modelo + animador procedural + sombra de contato
 * (a cena não usa shadow maps) + ondas de choque luminosas no chão.
 */
export class PlayerView implements CharacterView {
  private readonly group = new Group();
  private readonly model: CharacterModel;
  private readonly animator: ProceduralAnimator;
  private readonly shadow: Mesh;
  private readonly shadowMaterial: ShaderMaterial;
  private readonly waves: Shockwave[] = [];
  private readonly ringGeometry = new RingGeometry(0.85, 1, 48);

  constructor(scene: Scene, palette: Palette, move: PlayerMovementConfig) {
    this.group.name = 'Player';
    this.model = new CharacterModel({
      armor: new Color(0x0a0d11),
      suit: new Color(0x12171d),
      glow: palette.glow('cyan', 3.2),
      rim: palette.glow('blue', 0.9),
    });
    this.animator = new ProceduralAnimator(this.model.joints, move);
    this.group.add(this.model.root);

    this.shadowMaterial = new ShaderMaterial({
      transparent: true,
      depthWrite: false,
      uniforms: { uOpacity: { value: 0.75 } },
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: 'uniform float uOpacity; varying vec2 vUv; void main(){ float d = length(vUv - 0.5) * 2.0; float a = (1.0 - smoothstep(0.2, 1.0, d)) * uOpacity; gl_FragColor = vec4(0.0, 0.0, 0.0, a); }',
    });
    this.shadow = new Mesh(new PlaneGeometry(1.6, 1.6), this.shadowMaterial);
    this.shadow.rotation.x = -Math.PI / 2;
    this.shadow.renderOrder = 1;
    this.group.add(this.shadow);

    for (let i = 0; i < 4; i++) {
      const material = new MeshBasicMaterial({
        color: palette.glow('cyan', 2.5), transparent: true, blending: AdditiveBlending, depthWrite: false, side: DoubleSide,
      });
      const mesh = new Mesh(this.ringGeometry, material);
      mesh.rotation.x = -Math.PI / 2;
      mesh.visible = false;
      this.group.add(mesh);
      this.waves.push({ mesh, material, age: 0, life: 0.55, strength: 0 });
    }
    scene.add(this.group);
  }

  update(x: number, y: number, z: number, groundY: number, anim: AnimationInput, dt: number): void {
    const root = this.model.root;
    root.position.set(x, y, z);
    root.rotation.y = anim.facing;
    this.animator.update(anim, dt);

    // Sombra: fica no chão, encolhe e esmaece com a altura.
    const h = Math.max(0, y - groundY);
    const s = Math.max(0.35, 1 - h * 0.12);
    this.shadow.position.set(x, groundY + 0.02, z);
    this.shadow.scale.setScalar(s);
    this.shadowMaterial.uniforms.uOpacity!.value = 0.75 * Math.max(0.15, 1 - h * 0.15);

    for (const w of this.waves) {
      if (!w.mesh.visible) continue;
      w.age += dt;
      const t = w.age / w.life;
      if (t >= 1) { w.mesh.visible = false; continue; }
      w.mesh.scale.setScalar(0.4 + t * (1.5 + 2.5 * w.strength));
      w.material.opacity = (1 - t) * (1 - t) * (0.4 + 0.6 * w.strength);
    }
  }

  pulse(x: number, y: number, z: number, strength: number): void {
    const w = this.waves.find((v) => !v.mesh.visible) ?? this.waves.reduce((a, b) => (a.age > b.age ? a : b));
    w.mesh.visible = true;
    w.mesh.position.set(x, y + 0.03, z);
    w.age = 0;
    w.strength = Math.min(1, Math.max(0, strength));
  }

  setVisible(visible: boolean): void {
    this.group.visible = visible;
  }

  dispose(): void {
    this.group.removeFromParent();
    this.model.dispose();
    this.shadow.geometry.dispose();
    this.shadowMaterial.dispose();
    this.ringGeometry.dispose();
    for (const w of this.waves) w.material.dispose();
  }
}
