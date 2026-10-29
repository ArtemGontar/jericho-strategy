import { Color, Vector2, type IUniform, type Material, type WebGLProgramParametersWithUniforms } from 'three';
import { GLSL_NOISE } from './noise';

/** Uniforms every lit material shares: the fog-of-war texture, its placement, and the clock. */
export interface SharedUniforms {
  [k: string]: IUniform;
  uFog: IUniform;
  uFogOrg: IUniform<Vector2>;
  uFogExt: IUniform<Vector2>;
  uVoid: IUniform<Color>;
  uTime: IUniform<number>;
}

export const FOG_FRAGMENT = /* glsl */ `
  {
    vec2 fuv = (vWorldF.xz - uFogOrg) / uFogExt;
    float fv = texture2D(uFog, fuv).r;
    float expl = smoothstep(0.1, 0.28, fv);
    float vis = smoothstep(0.55, 0.85, fv);
    vec3 col = gl_FragColor.rgb;
    float g = dot(col, vec3(0.3, 0.59, 0.11));
    vec3 fogged = mix(col, vec3(g), 0.5) * vec3(0.5, 0.57, 0.74);
    col = mix(fogged, col, vis);
    col = mix(uVoid, col, expl);
    gl_FragColor = vec4(col, gl_FragColor.a);
  }
`;

/** Brush-stroke mottle and a touch more colour in the albedo, so flat vertex colours read as painted. */
function paintAlbedo(freq: number): string {
  return `{
    vec2 pp = vec2(vLoc.x + vLoc.z * 0.6, vLoc.y * 1.3 + vLoc.z * 0.45) * ${freq.toFixed(1)};
    float pn = vnoise(pp) * 0.65 + vnoise(pp * 2.7 + 9.1) * 0.35;
    diffuseColor.rgb *= 0.86 + pn * 0.3;
    float pl = dot(diffuseColor.rgb, vec3(0.3, 0.59, 0.11));
    diffuseColor.rgb = clamp(mix(vec3(pl), diffuseColor.rgb, 1.2), 0.0, 1.0);
  }`;
}

/** Warm, slightly violet shadows and a soft warm rim on the silhouette, like the hand-painted legacy art. */
const PAINT_GRADE = /* glsl */ `
  {
    vec3 pc = gl_FragColor.rgb;
    float plum = dot(pc, vec3(0.3, 0.59, 0.11));
    float pshade = 1.0 - smoothstep(0.08, 0.5, plum);
    pc = mix(pc, pc * vec3(1.12, 0.92, 1.0) + vec3(0.025, 0.004, 0.03), pshade * 0.55);
    float prim = pow(1.0 - clamp(normal.z, 0.0, 1.0), 2.4);
    pc += vec3(1.0, 0.82, 0.55) * prim * 0.1;
    gl_FragColor.rgb = pc;
  }
`;

/**
 * Teach a standard material about fog of war, and optionally wind sway for foliage and cloud shadows for the ground.
 * Works for plain meshes and instanced meshes alike.
 */
export function patchMaterial(mat: Material, u: SharedUniforms, opts: { sway?: number; clouds?: boolean; paint?: number; outline?: number } = {}): void {
  mat.onBeforeCompile = (shader: WebGLProgramParametersWithUniforms) => {
    Object.assign(shader.uniforms, u);
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', `#include <common>\nvarying vec3 vWorldF;\nvarying vec3 vLoc;\nuniform float uTime;${opts.outline ? '\nattribute vec3 onrm;' : ''}`)
      .replace(
        '#include <begin_vertex>',
        `#include <begin_vertex>
        vLoc = position;
        ${opts.outline ? `transformed += normalize(onrm) * ${opts.outline.toFixed(4)};` : ''}
        ${
          opts.sway
            ? `{
          #ifdef USE_INSTANCING
            vec3 root = vec3(instanceMatrix[3][0], 0.0, instanceMatrix[3][2]);
          #else
            vec3 root = vec3(modelMatrix[3][0], 0.0, modelMatrix[3][2]);
          #endif
          float ph = root.x * 1.7 + root.z * 2.3;
          float k = max(position.y, 0.0);
          transformed.x += sin(uTime * 1.7 + ph) * k * k * ${opts.sway.toFixed(3)};
          transformed.z += cos(uTime * 1.3 + ph * 1.3) * k * k * ${(opts.sway * 0.6).toFixed(3)};
        }`
            : ''
        }`,
      )
      .replace(
        '#include <project_vertex>',
        `#include <project_vertex>
        #ifdef USE_INSTANCING
          vWorldF = (modelMatrix * instanceMatrix * vec4(transformed, 1.0)).xyz;
        #else
          vWorldF = (modelMatrix * vec4(transformed, 1.0)).xyz;
        #endif`,
      );
    shader.fragmentShader = shader.fragmentShader
      .replace(
        '#include <common>',
        `#include <common>
        varying vec3 vWorldF;
        varying vec3 vLoc;
        uniform sampler2D uFog; uniform vec2 uFogOrg; uniform vec2 uFogExt; uniform vec3 uVoid; uniform float uTime;
        ${GLSL_NOISE}`,
      )
      .replace(
        '#include <color_fragment>',
        `#include <color_fragment>
        ${opts.paint ? paintAlbedo(opts.paint) : ''}`,
      )
      .replace(
        '#include <dithering_fragment>',
        `#include <dithering_fragment>
        ${opts.paint ? PAINT_GRADE : ''}
        ${
          opts.clouds
            ? `{
          float cl = smoothstep(0.48, 0.78, fbm3(vWorldF.xz * 0.16 + vec2(uTime * 0.035, uTime * 0.012)));
          gl_FragColor.rgb *= 1.0 - cl * 0.24;
        }`
            : ''
        }
        ${FOG_FRAGMENT}`,
      );
  };
  mat.customProgramCacheKey = () => `fog:${opts.sway ?? 0}:${opts.clouds ? 1 : 0}:${opts.paint ?? 0}:${opts.outline ?? 0}`;
  mat.needsUpdate = true;
}
