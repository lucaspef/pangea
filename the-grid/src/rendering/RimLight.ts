import type { Color, MeshStandardMaterial } from 'three';

/**
 * Luz de contorno (fresnel) somada à emissão de um material PBR.
 * A cena não tem luzes diretas — tudo é emissivo ou reflexo de ambiente —,
 * então personagens precisam de um contorno para que a silhueta da
 * armadura preta seja legível contra o fundo escuro.
 */
export function addRimLight(material: MeshStandardMaterial, color: Color, power = 3, strength = 1): MeshStandardMaterial {
  const previous = material.onBeforeCompile;
  material.onBeforeCompile = (shader, renderer) => {
    previous.call(material, shader, renderer);
    shader.uniforms.uRimColor = { value: color };
    shader.uniforms.uRimPower = { value: power };
    shader.uniforms.uRimStrength = { value: strength };
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <common>', '#include <common>\nuniform vec3 uRimColor;\nuniform float uRimPower;\nuniform float uRimStrength;')
      .replace(
        '#include <emissivemap_fragment>',
        `#include <emissivemap_fragment>
        {
          // vViewPosition e normal estão em espaço de câmera.
          float rim = 1.0 - clamp(dot(normalize(normal), normalize(vViewPosition)), 0.0, 1.0);
          totalEmissiveRadiance += uRimColor * pow(rim, uRimPower) * uRimStrength;
        }`,
      );
  };
  material.customProgramCacheKey = () => `rim-${power}-${strength}`;
  return material;
}
