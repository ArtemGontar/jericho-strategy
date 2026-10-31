/**
 * Display options for players who need them.
 *  - colour-blind palette: civilisation colours from the Okabe-Ito set, and blue/orange instead of green/red for good/bad
 *  - high contrast: brighter secondary text and stronger lines
 * Both are read once when the page opens (the palette is applied to the shared civilisation table in this browser only).
 */
import { CIVS } from '../shared/data';

const CB_KEY = 'jericho.cb';
const HC_KEY = 'jericho.contrast';

/** Colours that stay apart for the common kinds of colour blindness. */
const CB_COLORS: Record<string, string> = {
  jericho: '#f0e442', sumer: '#56b4e9', egypt: '#e69f00', indus: '#009e73', rome: '#cc79a7', persia: '#0072b2', maya: '#d55e00', greece: '#e6e6ee',
};

function read(key: string): boolean {
  try { return localStorage.getItem(key) === 'on'; } catch { return false; }
}
function write(key: string, on: boolean): void {
  try { localStorage.setItem(key, on ? 'on' : 'off'); } catch { /* ignore */ }
}

export const colourBlindOn = (): boolean => read(CB_KEY);
export const setColourBlind = (on: boolean): void => write(CB_KEY, on);
export const highContrastOn = (): boolean => read(HC_KEY);
export function setHighContrast(on: boolean): void {
  write(HC_KEY, on);
  applyContrast();
}

function applyContrast(): void {
  if (highContrastOn()) document.documentElement.dataset.contrast = 'high';
  else delete document.documentElement.dataset.contrast;
}

/** Call once before any screen is drawn. */
export function applyAccessibility(): void {
  applyContrast();
  if (!colourBlindOn()) return;
  document.documentElement.dataset.cb = 'on';
  for (const c of CIVS) if (CB_COLORS[c.id]) c.color = CB_COLORS[c.id];
}
