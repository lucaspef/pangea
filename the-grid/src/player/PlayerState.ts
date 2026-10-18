/** Estados de locomoção do jogador (lógica). A animação é derivada deles. */
export type LocomotionState = 'idle' | 'walk' | 'run' | 'sprint' | 'jump' | 'fall' | 'land' | 'dodge';

/** Direção de movimento desejada em espaço de mundo (já relativa à câmera). */
export interface MoveIntent {
  /** Componentes no plano XZ; o comprimento (0..1) é a intensidade. */
  x: number;
  z: number;
  sprint: boolean;
}

/**
 * Tudo o que a camada visual precisa para animar o personagem.
 * Contrato entre lógica e apresentação: a animação procedural atual e um
 * futuro Animator (glTF/Unity) consomem exatamente estes dados.
 */
export interface AnimationInput {
  state: LocomotionState;
  /** Tempo no estado atual (s). */
  stateTime: number;
  /** Velocidade horizontal (m/s). */
  speed: number;
  verticalSpeed: number;
  grounded: boolean;
  /** Progresso da esquiva (0..1); 0 fora da esquiva. */
  dodgeProgress: number;
  /** Intensidade da última aterrissagem (0..1). */
  landImpact: number;
  /** Direção do corpo (yaw, rad; 0 = -Z). */
  facing: number;
}
