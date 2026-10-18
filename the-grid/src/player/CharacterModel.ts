import {
  BoxGeometry, CapsuleGeometry, Color, CylinderGeometry, Group, Mesh, MeshBasicMaterial, MeshStandardMaterial,
  Object3D, SphereGeometry, TorusGeometry, type BufferGeometry, type Material,
} from 'three';
import { addRimLight } from '../rendering/RimLight';

/** Articulações animáveis do personagem (nomes estáveis: contrato com o animador). */
export interface CharacterJoints {
  /** Pivô de rolagem (esquiva); fica na altura do quadril. */
  body: Object3D;
  hips: Object3D;
  spine: Object3D;
  chest: Object3D;
  neck: Object3D;
  head: Object3D;
  upperArmL: Object3D; forearmL: Object3D;
  upperArmR: Object3D; forearmR: Object3D;
  thighL: Object3D; shinL: Object3D; footL: Object3D;
  thighR: Object3D; shinR: Object3D; footR: Object3D;
}

export interface CharacterPalette {
  armor: Color;
  suit: Color;
  glow: Color;
  /** Cor do contorno (fresnel) que destaca a silhueta no escuro. */
  rim: Color;
}

/** Medidas do esqueleto (m). Personagem com ~1,85 m. */
export const SKELETON = {
  bodyPivotY: 0.55,
  hipsY: 0.95,
  spine: 0.12,
  chest: 0.24,
  neck: 0.25,
  shoulderX: 0.215,
  shoulderY: 0.2,
  upperArm: 0.29,
  forearm: 0.27,
  hipX: 0.1,
  thigh: 0.45,
  shin: 0.44,
};

/**
 * Personagem humanoide provisório construído com primitivas: armadura
 * preta, circuitos luminosos e disco de energia nas costas.
 *
 * É um placeholder bem-acabado: a hierarquia de articulações segue a de um
 * esqueleto humanoide padrão (quadril → coluna → peito → pescoço → cabeça;
 * ombros → braços; quadril → coxas → canelas → pés), de modo que um modelo
 * glTF rigado possa substituí-lo implementando o mesmo `CharacterView`.
 */
export class CharacterModel {
  readonly root = new Group();
  readonly joints: CharacterJoints;
  /** Disco nas costas (o Marco 3 o destaca para arremesso). */
  readonly backDisc: Group;
  private readonly owned: { dispose(): void }[] = [];
  private readonly armor: MeshStandardMaterial;
  private readonly suit: MeshStandardMaterial;
  private readonly glow: MeshBasicMaterial;

  constructor(palette: CharacterPalette) {
    this.root.name = 'Character';
    this.armor = this.keep(addRimLight(
      new MeshStandardMaterial({ color: palette.armor, metalness: 0.85, roughness: 0.25, envMapIntensity: 2.2 }),
      palette.rim, 2.6, 0.55,
    ));
    this.suit = this.keep(addRimLight(
      new MeshStandardMaterial({ color: palette.suit, metalness: 0.4, roughness: 0.55, envMapIntensity: 1.4 }),
      palette.rim, 3, 0.3,
    ));
    this.glow = this.keep(new MeshBasicMaterial({ color: palette.glow }));

    const S = SKELETON;
    const joint = (name: string, parent: Object3D, x: number, y: number, z = 0) => {
      const o = new Object3D();
      o.name = name;
      o.position.set(x, y, z);
      parent.add(o);
      return o;
    };

    const body = joint('body', this.root, 0, S.bodyPivotY);
    const hips = joint('hips', body, 0, S.hipsY - S.bodyPivotY);
    const spine = joint('spine', hips, 0, S.spine);
    const chest = joint('chest', spine, 0, S.chest);
    const neck = joint('neck', chest, 0, S.neck);
    const head = joint('head', neck, 0, 0.07);
    const upperArmL = joint('upperArmL', chest, -S.shoulderX, S.shoulderY);
    const upperArmR = joint('upperArmR', chest, S.shoulderX, S.shoulderY);
    const forearmL = joint('forearmL', upperArmL, 0, -S.upperArm);
    const forearmR = joint('forearmR', upperArmR, 0, -S.upperArm);
    const thighL = joint('thighL', hips, -S.hipX, -0.04);
    const thighR = joint('thighR', hips, S.hipX, -0.04);
    const shinL = joint('shinL', thighL, 0, -S.thigh);
    const shinR = joint('shinR', thighR, 0, -S.thigh);
    const footL = joint('footL', shinL, 0, -S.shin);
    const footR = joint('footR', shinR, 0, -S.shin);
    this.joints = { body, hips, spine, chest, neck, head, upperArmL, forearmL, upperArmR, forearmR, thighL, shinL, footL, thighR, shinR, footR };

    // ---------- Tronco ----------
    this.mesh(new CapsuleGeometry(0.13, 0.08, 4, 12), this.suit, spine, 0, 0.08, 0, 1.2, 1, 0.85); // abdômen
    this.mesh(new BoxGeometry(0.4, 0.3, 0.23), this.armor, chest, 0, 0.08, 0);                      // couraça
    this.mesh(new BoxGeometry(0.3, 0.12, 0.2), this.armor, chest, 0, -0.1, 0);                      // base da couraça
    this.mesh(new CapsuleGeometry(0.12, 0.06, 4, 12), this.armor, hips, 0, 0, 0, 1.25, 0.8, 0.9);  // pélvis
    // Circuitos do peito: núcleo + linhas em V.
    this.mesh(new TorusGeometry(0.045, 0.008, 6, 24), this.glow, chest, 0, 0.1, -0.118);
    this.strip(chest, 0.012, 0.2, -0.09, 0.02, -0.117, 0.55);
    this.strip(chest, 0.012, 0.2, 0.09, 0.02, -0.117, -0.55);
    this.strip(chest, 0.36, 0.012, 0, -0.05, -0.117);
    this.strip(chest, 0.012, 0.25, -0.205, 0.06, 0);
    this.strip(chest, 0.012, 0.25, 0.205, 0.06, 0);
    this.mesh(new TorusGeometry(0.15, 0.008, 6, 32), this.glow, hips, 0, 0.04, 0, 1, 1, 0.75, Math.PI / 2); // cinto
    // Ombreiras.
    for (const [arm, side] of [[upperArmL, -1], [upperArmR, 1]] as const) {
      this.mesh(new SphereGeometry(0.085, 14, 10), this.armor, arm, side * 0.01, 0, 0, 1.1, 0.9, 1);
      this.strip(arm, 0.012, 0.012, side * 0.07, 0.03, -0.05, 0, 0.11);
    }

    // ---------- Cabeça (capacete com visor) ----------
    this.mesh(new CylinderGeometry(0.045, 0.055, 0.1, 10), this.suit, neck, 0, 0.0, 0);
    this.mesh(new SphereGeometry(0.115, 20, 16), this.armor, head, 0, 0.1, 0.005, 0.92, 1.08, 1.02);
    const visor = this.mesh(new TorusGeometry(0.108, 0.009, 6, 28, Math.PI * 0.9), this.glow, head, 0, 0.1, 0, 1, 1, 1, Math.PI / 2);
    visor.rotation.z = Math.PI + Math.PI * 0.05; // arco voltado para a frente (-Z)
    this.strip(head, 0.012, 0.012, 0, 0.215, 0.02, 0, 0.18); // crista

    // ---------- Braços ----------
    for (const [upper, fore, side] of [[upperArmL, forearmL, -1], [upperArmR, forearmR, 1]] as const) {
      this.limb(upper, S.upperArm, 0.052, this.suit);
      this.mesh(new BoxGeometry(0.09, S.upperArm * 0.55, 0.09), this.armor, upper, 0, -S.upperArm * 0.45, 0);
      this.strip(upper, 0.012, S.upperArm * 0.7, side * 0.047, -S.upperArm * 0.5, 0);
      this.limb(fore, S.forearm, 0.045, this.suit);
      this.mesh(new BoxGeometry(0.085, S.forearm * 0.6, 0.085), this.armor, fore, 0, -S.forearm * 0.42, 0);
      this.strip(fore, 0.012, S.forearm * 0.6, side * 0.044, -S.forearm * 0.42, 0);
      this.mesh(new BoxGeometry(0.07, 0.09, 0.05), this.armor, fore, 0, -S.forearm - 0.04, 0); // mão
    }

    // ---------- Pernas ----------
    for (const [thigh, shin, foot, side] of [[thighL, shinL, footL, -1], [thighR, shinR, footR, 1]] as const) {
      this.limb(thigh, S.thigh, 0.075, this.suit);
      this.mesh(new BoxGeometry(0.13, S.thigh * 0.6, 0.13), this.armor, thigh, 0, -S.thigh * 0.4, 0);
      this.strip(thigh, 0.012, S.thigh * 0.75, side * 0.068, -S.thigh * 0.45, 0);
      this.limb(shin, S.shin, 0.058, this.suit);
      this.mesh(new BoxGeometry(0.11, S.shin * 0.7, 0.12), this.armor, shin, 0, -S.shin * 0.45, -0.005);
      this.strip(shin, 0.012, S.shin * 0.65, 0, -S.shin * 0.45, -0.066);
      this.mesh(new BoxGeometry(0.1, 0.07, 0.25), this.armor, foot, 0, -0.03, -0.05);
      this.strip(foot, 0.1, 0.01, 0, -0.064, -0.05, 0, 0.26);
    }

    // ---------- Disco de energia nas costas ----------
    this.backDisc = new Group();
    this.backDisc.name = 'BackDisc';
    this.backDisc.position.set(0, 0.06, 0.135);
    chest.add(this.backDisc);
    this.mesh(new CylinderGeometry(0.16, 0.16, 0.022, 32), this.armor, this.backDisc, 0, 0, 0, 1, 1, 1, Math.PI / 2);
    this.mesh(new TorusGeometry(0.16, 0.012, 8, 40), this.glow, this.backDisc, 0, 0, 0.004);
    this.mesh(new TorusGeometry(0.07, 0.007, 6, 28), this.glow, this.backDisc, 0, 0, 0.012);
  }

  /** Muda a cor dos circuitos (ex.: laranja para programas hostis). */
  setGlowColor(color: Color): void {
    this.glow.color.copy(color);
  }

  dispose(): void {
    this.root.removeFromParent();
    for (const o of this.owned) o.dispose();
  }

  private keep<T extends { dispose(): void }>(o: T): T {
    this.owned.push(o);
    return o;
  }

  private mesh(
    geo: BufferGeometry, mat: Material, parent: Object3D,
    x: number, y: number, z: number, sx = 1, sy = 1, sz = 1, rotX = 0,
  ): Mesh {
    const m = new Mesh(this.keep(geo), mat);
    m.position.set(x, y, z);
    m.scale.set(sx, sy, sz);
    m.rotation.x = rotX;
    parent.add(m);
    return m;
  }

  /** Segmento de membro (cápsula) pendurado a partir da articulação. */
  private limb(parent: Object3D, length: number, radius: number, mat: Material): void {
    this.mesh(new CapsuleGeometry(radius, Math.max(0.01, length - radius * 2), 4, 10), mat, parent, 0, -length / 2, 0);
  }

  /** Filete luminoso: caixa fina de dimensões (w, h, depth). */
  private strip(parent: Object3D, w: number, h: number, x: number, y: number, z: number, rotZ = 0, depth = 0.012): void {
    const m = this.mesh(new BoxGeometry(w, h, depth), this.glow, parent, x, y, z);
    m.rotation.z = rotZ;
  }
}
