/** Tiny synthesised sound effects — no asset files needed. */
let ctx: AudioContext | null = null;
let muted = false;
try { muted = localStorage.getItem('jericho.muted') === '1'; } catch { /* ignore */ }

export function isMuted(): boolean {
  return muted;
}
export function setMuted(m: boolean): void {
  muted = m;
  try { localStorage.setItem('jericho.muted', m ? '1' : '0'); } catch { /* ignore */ }
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
  g.gain.exponentialRampToValueAtTime(vol, t0 + 0.012);
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
  g.gain.value = vol;
  s.connect(f).connect(g).connect(c.destination);
  s.start(t0);
}

export function sfx(name: string): void {
  switch (name) {
    case 'click': tone(540, 0.05, 'triangle', 0.05); break;
    case 'error': tone(160, 0.18, 'sawtooth', 0.06, 0, -60); break;
    case 'move': tone(300, 0.04, 'triangle', 0.03); break;
    case 'hit': noise(0.14, 0.14, 0, 300); tone(120, 0.12, 'square', 0.07, 0, -50); break;
    case 'shoot': noise(0.08, 0.07, 0, 2000); tone(900, 0.12, 'triangle', 0.04, 0, -500); break;
    case 'found': [392, 523, 659].forEach((f, i) => tone(f, 0.25, 'triangle', 0.08, i * 0.09)); break;
    case 'capture': [330, 247, 330, 440].forEach((f, i) => tone(f, 0.22, 'sawtooth', 0.06, i * 0.1)); break;
    case 'turn': tone(440, 0.08, 'sine', 0.05); tone(660, 0.12, 'sine', 0.05, 0.07); break;
    case 'tech': [523, 659, 784, 1046].forEach((f, i) => tone(f, 0.2, 'sine', 0.07, i * 0.07)); break;
    case 'wonder': [392, 494, 587, 784, 988].forEach((f, i) => tone(f, 0.35, 'triangle', 0.08, i * 0.1)); break;
    case 'build': tone(660, 0.1, 'triangle', 0.06); tone(880, 0.14, 'triangle', 0.06, 0.08); break;
    case 'notify': tone(740, 0.08, 'sine', 0.04); break;
    case 'victory': [523, 659, 784, 1046, 784, 1046, 1318].forEach((f, i) => tone(f, 0.3, 'triangle', 0.09, i * 0.14)); break;
    case 'defeat': [392, 349, 311, 262].forEach((f, i) => tone(f, 0.4, 'sawtooth', 0.06, i * 0.2)); break;
  }
}
