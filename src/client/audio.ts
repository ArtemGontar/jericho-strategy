import { haptic } from './haptics';
/** Tiny synthesised sound effects — no asset files needed. */
let ctx: AudioContext | null = null;
let muted = false;
try { muted = localStorage.getItem('jericho.muted') === '1'; } catch { /* ignore */ }

export type Level = 'off' | 'low' | 'medium' | 'high';
export const LEVELS: Level[] = ['off', 'low', 'medium', 'high'];
const LEVEL_GAIN: Record<Level, number> = { off: 0, low: 0.35, medium: 0.7, high: 1 };
const loadLevel = (key: string, fallback: Level): Level => {
  try {
    const v = localStorage.getItem(key);
    if (v && (LEVELS as string[]).includes(v)) return v as Level;
  } catch { /* ignore */ }
  return fallback;
};
let musicLevel: Level = loadLevel('jericho.music', 'medium');
let sfxLevel: Level = loadLevel('jericho.sfx', 'high');
export function getLevel(kind: 'music' | 'sfx'): Level {
  return kind === 'music' ? musicLevel : sfxLevel;
}
export function setLevel(kind: 'music' | 'sfx', l: Level): void {
  if (kind === 'music') musicLevel = l;
  else sfxLevel = l;
  try { localStorage.setItem(kind === 'music' ? 'jericho.music' : 'jericho.sfx', l); } catch { /* ignore */ }
  if (kind === 'music' && amb) amb.master.gain.value = LEVEL_GAIN[l];
}

export function isMuted(): boolean {
  return muted;
}
export function setMuted(m: boolean): void {
  muted = m;
  try { localStorage.setItem('jericho.muted', m ? '1' : '0'); } catch { /* ignore */ }
  if (m) stopAmbience();
  else if (wantAmbience) startAmbience();
}

function ac(): AudioContext | null {
  if (muted) return null;
  try {
    if (!ctx) ctx = new (window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)();
    if (ctx.state === 'suspended') void ctx.resume();
    return ctx;
  } catch {
    return null;
  }
}

function tone(freq: number, dur: number, type: OscillatorType = 'sine', vol = 0.08, delay = 0, slide = 0): void {
  const c = ac();
  if (!c) return;
  const t0 = c.currentTime + delay;
  const o = c.createOscillator();
  const g = c.createGain();
  o.type = type;
  o.frequency.setValueAtTime(freq, t0);
  if (slide) o.frequency.exponentialRampToValueAtTime(Math.max(30, freq + slide), t0 + dur);
  g.gain.setValueAtTime(0.0001, t0);
  g.gain.exponentialRampToValueAtTime(Math.max(0.0002, vol * LEVEL_GAIN[sfxLevel]), t0 + 0.012);
  g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
  o.connect(g).connect(c.destination);
  o.start(t0);
  o.stop(t0 + dur + 0.02);
}

function noise(dur: number, vol = 0.1, delay = 0, hp = 800): void {
  const c = ac();
  if (!c) return;
  const t0 = c.currentTime + delay;
  const buf = c.createBuffer(1, Math.floor(c.sampleRate * dur), c.sampleRate);
  const d = buf.getChannelData(0);
  for (let i = 0; i < d.length; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / d.length);
  const s = c.createBufferSource();
  s.buffer = buf;
  const f = c.createBiquadFilter();
  f.type = 'highpass';
  f.frequency.value = hp;
  const g = c.createGain();
  g.gain.value = vol * LEVEL_GAIN[sfxLevel];
  s.connect(f).connect(g).connect(c.destination);
  s.start(t0);
}

export function sfx(name: string): void {
  haptic(name); // independent of the sound setting
  if (sfxLevel === 'off') return;
  switch (name) {
    case 'click': tone(540, 0.05, 'triangle', 0.05); break;
    case 'error': tone(160, 0.18, 'sawtooth', 0.06, 0, -60); break;
    case 'move': tone(300, 0.04, 'triangle', 0.03); break;
    case 'cannon': noise(0.5, 0.22, 0, 80); tone(90, 0.4, 'sine', 0.16, 0, -60); noise(0.25, 0.08, 0.05, 1200); break;
    case 'catapult': tone(110, 0.22, 'sawtooth', 0.05, 0, 40); noise(0.18, 0.1, 0.18, 200); tone(70, 0.25, 'sine', 0.1, 0.2, -30); break;
    case 'clash': noise(0.1, 0.1, 0, 2500); tone(1400, 0.16, 'triangle', 0.035, 0, -300); tone(2100, 0.1, 'sine', 0.02, 0.01); break;
    case 'death': tone(300, 0.35, 'triangle', 0.05, 0, -200); break;
    case 'hit': noise(0.14, 0.14, 0, 300); tone(120, 0.12, 'square', 0.07, 0, -50); break;
    case 'shoot': noise(0.08, 0.07, 0, 2000); tone(900, 0.12, 'triangle', 0.04, 0, -500); break;
    case 'found': [392, 523, 659].forEach((f, i) => tone(f, 0.25, 'triangle', 0.08, i * 0.09)); break;
    case 'capture': [330, 247, 330, 440].forEach((f, i) => tone(f, 0.22, 'sawtooth', 0.06, i * 0.1)); break;
    case 'turn': tone(440, 0.08, 'sine', 0.05); tone(660, 0.12, 'sine', 0.05, 0.07); break;
    case 'tech': [523, 659, 784, 1046].forEach((f, i) => tone(f, 0.2, 'sine', 0.07, i * 0.07)); break;
    case 'wonder': [392, 494, 587, 784, 988].forEach((f, i) => tone(f, 0.35, 'triangle', 0.08, i * 0.1)); break;
    case 'build': tone(660, 0.1, 'triangle', 0.06); tone(880, 0.14, 'triangle', 0.06, 0.08); break;
    case 'notify': tone(740, 0.08, 'sine', 0.04); break;
    case 'whoosh': noise(0.45, 0.07, 0, 500); tone(220, 0.5, 'sine', 0.035, 0, 260); break;
    case 'column': [262, 392, 523, 784].forEach((f, i) => tone(f, 0.9, 'sine', 0.045, i * 0.12)); noise(0.8, 0.03, 0.1, 3000); break;
    case 'drums': [0, 0.28, 0.56].forEach((d) => { tone(70, 0.3, 'sine', 0.16, d, -30); noise(0.12, 0.05, d, 200); }); tone(110, 1.1, 'sawtooth', 0.025, 0.7, -40); break;
    case 'splash': noise(0.35, 0.09, 0, 900); tone(180, 0.3, 'sine', 0.05, 0, -90); break;
    case 'promote': [784, 988, 1319].forEach((f, i) => tone(f, 0.16, 'triangle', 0.05, i * 0.06)); break;
    case 'grow': tone(587, 0.12, 'sine', 0.045); tone(784, 0.16, 'sine', 0.045, 0.08); break;
    case 'victory': [523, 659, 784, 1046, 784, 1046, 1318].forEach((f, i) => tone(f, 0.3, 'triangle', 0.09, i * 0.14)); break;
    case 'defeat': [392, 349, 311, 262].forEach((f, i) => tone(f, 0.4, 'sawtooth', 0.06, i * 0.2)); break;
  }
}


// ---------------------------------------------------------------- ambience and music
// A quiet bed of wind and surf, now and then a bird, and a slow generative melody over soft pads.
let wantAmbience = false;
let amb: { master: GainNode; timers: number[]; nodes: AudioNode[] } | null = null;

const SCALE = [293.66, 329.63, 369.99, 440, 493.88, 587.33, 659.25, 739.99, 880]; // D major pentatonic
const CHORDS: number[][] = [
  [146.83, 220, 293.66, 369.99],
  [123.47, 185, 246.94, 369.99],
  [98, 146.83, 196, 293.66],
  [110, 164.81, 220, 329.63],
];

function loopNoise(c: AudioContext, seconds: number): AudioBufferSourceNode {
  const buf = c.createBuffer(1, Math.floor(c.sampleRate * seconds), c.sampleRate);
  const d = buf.getChannelData(0);
  let last = 0;
  for (let i = 0; i < d.length; i++) {
    const w = Math.random() * 2 - 1;
    last = (last + 0.02 * w) / 1.02; // brown-ish noise
    d[i] = last * 3.5;
  }
  const s = c.createBufferSource();
  s.buffer = buf;
  s.loop = true;
  return s;
}

export function startAmbience(): void {
  wantAmbience = true;
  if (amb) return;
  const c = ac();
  if (!c) return;
  const master = c.createGain();
  master.gain.value = 0;
  master.gain.linearRampToValueAtTime(LEVEL_GAIN[musicLevel], c.currentTime + 4);
  master.connect(c.destination);
  const nodes: AudioNode[] = [master];
  const timers: number[] = [];

  // wind
  const wind = loopNoise(c, 4);
  const wf = c.createBiquadFilter();
  wf.type = 'lowpass';
  wf.frequency.value = 420;
  const wg = c.createGain();
  wg.gain.value = 0.05;
  const lfo = c.createOscillator();
  lfo.frequency.value = 0.11;
  const lg = c.createGain();
  lg.gain.value = 0.03;
  lfo.connect(lg).connect(wg.gain);
  wind.connect(wf).connect(wg).connect(master);
  wind.start();
  lfo.start();
  // surf
  const surf = loopNoise(c, 5);
  const sf = c.createBiquadFilter();
  sf.type = 'bandpass';
  sf.frequency.value = 700;
  sf.Q.value = 0.6;
  const sg = c.createGain();
  sg.gain.value = 0.035;
  const lfo2 = c.createOscillator();
  lfo2.frequency.value = 0.15;
  const lg2 = c.createGain();
  lg2.gain.value = 0.028;
  lfo2.connect(lg2).connect(sg.gain);
  surf.connect(sf).connect(sg).connect(master);
  surf.start();
  lfo2.start();
  nodes.push(wind, wf, wg, lfo, lg, surf, sf, sg, lfo2, lg2);

  // music bus with a soft echo
  const bus = c.createGain();
  bus.gain.value = 0.9;
  const echo = c.createDelay(1.5);
  echo.delayTime.value = 0.42;
  const fb = c.createGain();
  fb.gain.value = 0.38;
  const ef = c.createBiquadFilter();
  ef.type = 'lowpass';
  ef.frequency.value = 1800;
  bus.connect(master);
  bus.connect(echo);
  echo.connect(ef).connect(fb).connect(echo);
  ef.connect(master);
  nodes.push(bus, echo, fb, ef);

  const pluck = (f: number, t: number, vol: number) => {
    const o = c.createOscillator();
    const g = c.createGain();
    o.type = 'triangle';
    o.frequency.value = f;
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(vol, t + 0.01);
    g.gain.exponentialRampToValueAtTime(0.0001, t + 1.6);
    o.connect(g).connect(bus);
    o.start(t);
    o.stop(t + 1.7);
  };
  const pad = (freqs: number[], t: number, dur: number) => {
    for (const f of freqs) {
      const o = c.createOscillator();
      const g = c.createGain();
      o.type = 'sine';
      o.frequency.value = f * (1 + (Math.random() - 0.5) * 0.004);
      g.gain.setValueAtTime(0.0001, t);
      g.gain.linearRampToValueAtTime(0.012, t + dur * 0.4);
      g.gain.linearRampToValueAtTime(0.0001, t + dur);
      o.connect(g).connect(bus);
      o.start(t);
      o.stop(t + dur + 0.05);
    }
  };
  const beat = 60 / 58;
  let next = c.currentTime + 1.5;
  let step = 0;
  let last = 4;
  const tick = () => {
    if (!amb) return;
    while (next < c.currentTime + 2.5) {
      if (step % 8 === 0) pad(CHORDS[(step / 8) % CHORDS.length | 0], next, beat * 8.5);
      if (Math.random() < 0.5) {
        last = Math.max(0, Math.min(SCALE.length - 1, last + Math.round((Math.random() - 0.5) * 4)));
        pluck(SCALE[last], next + (Math.random() < 0.3 ? beat / 2 : 0), 0.02 + Math.random() * 0.012);
      }
      next += beat;
      step++;
      if (step % 32 === 0) step = 0;
    }
  };
  timers.push(window.setInterval(tick, 700));
  // birds
  const chirp = () => {
    if (!amb) return;
    const n = 2 + Math.floor(Math.random() * 3);
    const base = 2600 + Math.random() * 1800;
    for (let i = 0; i < n; i++) {
      const t = c.currentTime + i * 0.11;
      const o = c.createOscillator();
      const g = c.createGain();
      o.type = 'sine';
      o.frequency.setValueAtTime(base, t);
      o.frequency.exponentialRampToValueAtTime(base * 1.35, t + 0.06);
      g.gain.setValueAtTime(0.0001, t);
      g.gain.exponentialRampToValueAtTime(0.012, t + 0.01);
      g.gain.exponentialRampToValueAtTime(0.0001, t + 0.08);
      o.connect(g).connect(master);
      o.start(t);
      o.stop(t + 0.1);
    }
    timers.push(window.setTimeout(chirp, 7000 + Math.random() * 12000));
  };
  timers.push(window.setTimeout(chirp, 4000));
  amb = { master, timers, nodes };
  tick();
}

export function stopAmbience(): void {
  const a = amb;
  if (!a) return;
  amb = null;
  for (const t of a.timers) {
    clearInterval(t);
    clearTimeout(t);
  }
  try {
    a.master.gain.cancelScheduledValues(0);
    a.master.gain.value = 0;
    for (const n of a.nodes) {
      try { (n as OscillatorNode | AudioBufferSourceNode).stop?.(); } catch { /* already stopped */ }
      n.disconnect();
    }
  } catch { /* ignore */ }
}
