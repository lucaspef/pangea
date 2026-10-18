import { MeshStandardMaterial, type Color, type IUniform } from 'three';

/**
 * Material das construções: metal preto com painéis, frestas de luz e
 * janelas acesas geradas proceduralmente no shader a partir da posição no
 * mundo (sem texturas, sem custo de memória).
 *
 * A cor por instância (instanceColor) é usada como cor de destaque das
 * linhas — o corpo continua sempre preto.
 */
export function createFacadeMaterial(body: Color, time: IUniform<number>): MeshStandardMaterial {
  const mat = new MeshStandardMaterial({
    color: body,
    metalness: 0.9,
    roughness: 0.38,
    envMapIntensity: 0.28,
  });

  mat.onBeforeCompile = (shader) => {
    shader.uniforms.uTime = time;

    shader.vertexShader = shader.vertexShader
      .replace(
        '#include <common>',
        `#include <common>
        varying vec3 vFacadePos;
        varying vec3 vFacadeNormal;
        flat varying float vFacadeSeed;`,
      )
      .replace(
        '#include <project_vertex>',
        `#include <project_vertex>
        vec4 facadeWorld = vec4(transformed, 1.0);
        mat3 facadeNormalMatrix = mat3(modelMatrix);
        vec4 facadeOrigin = vec4(0.0, 0.0, 0.0, 1.0);
        #ifdef USE_INSTANCING
          facadeWorld = instanceMatrix * facadeWorld;
          facadeNormalMatrix = facadeNormalMatrix * mat3(instanceMatrix);
          facadeOrigin = instanceMatrix * facadeOrigin;
        #endif
        facadeWorld = modelMatrix * facadeWorld;
        facadeOrigin = modelMatrix * facadeOrigin;
        vFacadePos = facadeWorld.xyz;
        vFacadeNormal = normalize(facadeNormalMatrix * objectNormal);
        vFacadeSeed = floor(fract(sin(dot(floor(facadeOrigin.xz), vec2(12.9898, 78.233))) * 43758.5453) * 997.0);`,
      );

    shader.fragmentShader = shader.fragmentShader
      .replace(
        '#include <common>',
        `#include <common>
        uniform float uTime;
        varying vec3 vFacadePos;
        varying vec3 vFacadeNormal;
        flat varying float vFacadeSeed;

        // Hash sem seno (estável em qualquer GPU, sem padrões de precisão).
        float facadeHash(vec2 p) {
          vec3 p3 = fract(vec3(p.xyx) * 0.1031);
          p3 += dot(p3, p3.yzx + 33.33);
          return fract((p3.x + p3.y) * p3.z);
        }
        // Linha antisserrilhada: distância em metros até a linha mais próxima.
        float facadeLine(float coord, float spacing, float width) {
          float d = abs(fract(coord / spacing + 0.5) - 0.5) * spacing;
          float w = fwidth(coord);
          return 1.0 - smoothstep(width, width + w * 1.5, d);
        }`,
      )
      // O corpo não é tingido pela cor da instância (ela é só o destaque).
      .replace('#include <color_fragment>', '')
      .replace(
        '#include <emissivemap_fragment>',
        `#include <emissivemap_fragment>
        {
          vec3 n = normalize(vFacadeNormal);
          float vertical = 1.0 - step(0.6, abs(n.y));
          float roof = step(0.6, n.y);
          float u = abs(n.x) > abs(n.z) ? vFacadePos.z : vFacadePos.x;
          float y = vFacadePos.y;
          float dist = length(vFacadePos - cameraPosition);
          float fade = 1.0 - smoothstep(220.0, 900.0, dist);

          #if defined( USE_COLOR ) || defined( USE_COLOR_ALPHA )
            vec3 accent = vColor.rgb;
          #else
            vec3 accent = vec3(0.2, 0.85, 1.0);
          #endif

          // Juntas de painéis.
          float seams = max(facadeLine(u, 6.0, 0.05), facadeLine(y, 4.0, 0.04)) * vertical;

          // Janelas: poucas células acesas, piscando devagar.
          vec2 cellCoord = vec2(u / 3.0, y / 4.0);
          vec2 cell = floor(cellCoord);
          vec2 f = fract(cellCoord);
          float h = facadeHash(cell + vec2(vFacadeSeed * 7.0, vFacadeSeed * 3.0));
          float window = step(0.2, f.x) * step(f.x, 0.8) * step(0.38, f.y) * step(f.y, 0.62);
          float lit = step(0.955, h) * window * vertical * step(6.0, y);
          float flicker = 0.7 + 0.3 * sin(uTime * (0.4 + h * 2.5) + h * 40.0);

          // Linha horizontal de dados que sobe pela fachada.
          float scan = facadeLine(y - mod(uTime * (6.0 + fract(vFacadeSeed * 0.013) * 10.0) + vFacadeSeed * 0.3, 300.0), 1e4, 0.25) * vertical;

          // Topo com grade fina; base iluminada pelo brilho do piso.
          float roofGrid = max(facadeLine(vFacadePos.x, 4.0, 0.05), facadeLine(vFacadePos.z, 4.0, 0.05)) * roof;
          float groundGlow = exp(-y * 0.3) * vertical;

          totalEmissiveRadiance += accent * (
            seams * 0.035 * fade +
            lit * 1.1 * flicker +
            scan * 0.5 * fade +
            roofGrid * 0.06 * fade +
            groundGlow * 0.12
          );
        }`,
      );
  };
  mat.customProgramCacheKey = () => 'thegrid-facade-v1';
  return mat;
}
