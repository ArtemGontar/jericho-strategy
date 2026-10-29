/** Graphics settings and the WebGL capability check. Kept free of three.js so the menus can use it without loading the 3D renderer. */

/** Camera pitch of the 3D map in degrees (90 would be straight down, like the flat map). */
export const PITCH = 52;

export type Graphics = 'auto' | 'high' | 'medium' | 'low' | 'flat';
export const GRAPHICS_LABEL: Record<Graphics, string> = { auto: 'Automatic', high: 'High', medium: 'Medium', low: 'Low', flat: 'Flat 2D' };
export function graphicsPref(): Graphics {
  try {
    const v = localStorage.getItem('jericho.gfx');
    if (v === 'high' || v === 'medium' || v === 'low' || v === 'flat') return v;
  } catch { /* ignore */ }
  return 'auto';
}
export function setGraphicsPref(g: Graphics): void {
  try { localStorage.setItem('jericho.gfx', g); } catch { /* ignore */ }
}

/** WebGL2 present, not switched off, and not a software rasteriser (which would crawl). `?gl` forces it on. */
export function glSupported(): boolean {
  try {
    const q = new URLSearchParams(location.search);
    if (q.has('flat') || localStorage.getItem('jericho.flat') === '1' || graphicsPref() === 'flat') return false;
    const c = document.createElement('canvas');
    const gl = c.getContext('webgl2') as WebGL2RenderingContext | null;
    if (!gl) return false;
    if (!q.has('gl') && localStorage.getItem('jericho.gl') !== '1' && graphicsPref() === 'auto') {
      const ext = gl.getExtension('WEBGL_debug_renderer_info');
      const name = ext ? String(gl.getParameter(ext.UNMASKED_RENDERER_WEBGL)) : '';
      if (/swiftshader|llvmpipe|software|softpipe/i.test(name)) return false;
    }
    gl.getExtension('WEBGL_lose_context')?.loseContext();
    return true;
  } catch {
    return false;
  }
}
