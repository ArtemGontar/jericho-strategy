/** A short buzz for the moments that matter, on phones that can vibrate. Its own setting, separate from sound. */
const KEY = 'jericho.haptics';
const PATTERNS: Record<string, number | number[]> = {
  found: [30, 40, 60], capture: [60, 50, 90], hit: 25, clash: 25, death: 45, error: [20, 40, 20],
  tech: [25, 30, 25], wonder: [40, 40, 70], victory: [60, 60, 60, 60, 120], turn: 18, promote: [20, 30, 20], grow: 15,
};

export const hapticsAvailable = (): boolean => typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function';
export function hapticsOn(): boolean {
  try { return localStorage.getItem(KEY) !== 'off'; } catch { return true; }
}
export function setHaptics(on: boolean): void {
  try { localStorage.setItem(KEY, on ? 'on' : 'off'); } catch { /* ignore */ }
  if (on) buzz(30);
}
function buzz(p: number | number[]): void {
  try { navigator.vibrate(p); } catch { /* ignore */ }
}
export function haptic(name: string): void {
  const p = PATTERNS[name];
  if (p !== undefined && hapticsAvailable() && hapticsOn()) buzz(p);
}
