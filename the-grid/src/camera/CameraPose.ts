import { MathUtils, Vector3 } from 'three';

/**
 * Pose de câmera independente do grafo de cena: posição + yaw/pitch (rad).
 * Convenção: yaw = 0 olha para -Z; pitch > 0 olha para cima.
 * (Os tipos matemáticos do Three.js são usados apenas como álgebra;
 *  na Unity o equivalente é Vector3 + Quaternion.Euler.)
 */
export interface CameraPose {
  position: Vector3;
  yaw: number;
  pitch: number;
}

export const PITCH_LIMIT = MathUtils.degToRad(85);

export function clonePose(p: CameraPose): CameraPose {
  return { position: p.position.clone(), yaw: p.yaw, pitch: p.pitch };
}

/** Direção de visão unitária da pose. */
export function forwardFromAngles(yaw: number, pitch: number, out = new Vector3()): Vector3 {
  const cp = Math.cos(pitch);
  return out.set(-Math.sin(yaw) * cp, Math.sin(pitch), -Math.cos(yaw) * cp);
}

/** Yaw/pitch que apontam na direção dada. */
export function anglesFromDirection(dir: Vector3): { yaw: number; pitch: number } {
  const len = dir.length() || 1;
  return {
    yaw: Math.atan2(-dir.x, -dir.z),
    pitch: Math.asin(MathUtils.clamp(dir.y / len, -1, 1)),
  };
}

/** Diferença angular no intervalo (-π, π]. */
export function angleDelta(from: number, to: number): number {
  return Math.atan2(Math.sin(to - from), Math.cos(to - from));
}

/** Fator de suavização exponencial independente de taxa de quadros. */
export function damp(rate: number, dt: number): number {
  return 1 - Math.exp(-rate * dt);
}
