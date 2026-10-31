import type { GameScreen } from '../gamescreen';
import { icon } from '../icons';
import { clear, h } from './dom';

/**
 * The coach: for the first turns of a first game, a ring around the very control to use and a few words beside it,
 * with the key word in bold and a picture. It points; it does not lecture. Each step ends by itself when the thing is
 * done, and the whole coach goes away when the basics are, or at once with the cross.
 */
interface Step {
  id: string;
  /** a picture from the icon set */
  art: string;
  /** a few words; **bold** marks the word to look for */
  say: (gs: GameScreen) => string;
  /** is the player already past this? */
  done: (gs: GameScreen) => boolean;
  /** is it time for this one? */
  when?: (gs: GameScreen) => boolean;
  /** what to circle on screen, if it is there to be circled */
  target: (gs: GameScreen) => HTMLElement | null;
  /** do something helpful when the step begins, once (for example pick the scout) */
  begin?: (gs: GameScreen) => void;
}

const q = (gs: GameScreen, sel: string): HTMLElement | null => gs.root.querySelector<HTMLElement>(sel);
const visible = (el: HTMLElement | null): HTMLElement | null => (el && el.getClientRects().length && getComputedStyle(el).visibility !== 'hidden' ? el : null);

const STEPS: Step[] = [
  {
    id: 'build', art: 'production',
    say: () => 'Pick what to **build**',
    done: (g) => g.myCities().some((c) => (c.queue?.length ?? 0) > 0),
    when: (g) => g.myCities().length > 0,
    target: (g) => visible(q(g, '.drawer:not(.hidden) .btn.primary')) ?? visible(q(g, '.drawer:not(.hidden) .item.pick')) ?? visible(q(g, '.nextturn')),
  },
  {
    id: 'research', art: 'science',
    say: () => 'Choose **research**',
    done: (g) => !!g.view.researching || g.view.techs.length > 0,
    target: (g) => visible(q(g, '.medallions .medal')),
  },
  {
    id: 'explore', art: 'explore',
    say: () => 'Send the scout to **explore**',
    done: (g) => g.moved || g.myUnits().some((u) => u.auto === 'explore') || g.view.turn >= 4,
    when: (g) => g.myUnits().some((u) => u.type === 'scout'),
    begin: (g) => { const s = g.myUnits().find((u) => u.type === 'scout'); if (s) g.selectUnit(s.id, true); },
    target: (g) => visible(q(g, '.unitactions [aria-label^="Explore"]')) ?? visible(q(g, '.unitbar [aria-label^="Explore"]')),
  },
  {
    id: 'end', art: 'skip',
    say: () => '**End** the turn',
    done: (g) => g.view.turn >= 2,
    target: (g) => visible(q(g, '.nextturn')),
  },
  {
    id: 'settle', art: 'settler',
    say: (g) => (g.myUnits().some((u) => u.type === 'settler') ? 'Found a **second city**' : 'Build a **settler**'),
    done: (g) => g.myCities().length >= 2,
    when: (g) => g.view.turn >= 3,
    begin: (g) => { const s = g.myUnits().find((u) => u.type === 'settler'); if (s) g.selectUnit(s.id, true); },
    target: (g) => {
      if (g.myUnits().some((u) => u.type === 'settler')) return visible(q(g, '.unitactions [aria-label^="Found"]')) ?? visible(q(g, '.unitbar [aria-label^="Found"]'));
      const row = [...g.root.querySelectorAll<HTMLElement>('.drawer:not(.hidden) .item.pick')].find((r) => /Settler/.test(r.textContent ?? ''));
      return visible(row ?? null) ?? visible(q(g, '.nextturn'));
    },
  },
  {
    id: 'trade', art: 'trade',
    say: () => 'Sign a **trade** deal',
    done: (g) => g.view.players.some((p) => p.deal) || g.view.turn > 70,
    when: (g) => g.view.turn >= 6 && g.view.players.some((p) => p.id !== g.view.me && p.alive && p.relation === 'peace'),
    target: (g) => visible(q(g, '.topbar [aria-label^="Diplomacy"]')),
  },
];

const KEY = 'jericho.guide.v2';
const state = (): string | null => { try { return localStorage.getItem(KEY); } catch { return null; } };
const set = (v: string): void => { try { localStorage.setItem(KEY, v); } catch { /* ignore */ } };

/** A new game shows the coach again, unless the basics were already learned once. */
export function resetGuide(): void {
  if (state() === 'learned') return;
  try { localStorage.removeItem(KEY); } catch { /* ignore */ }
  lastIndex = -1;
  started.clear();
}
/** Show it again, for the "How to play" screen. */
export function restartGuide(): void {
  try { localStorage.removeItem(KEY); } catch { /* ignore */ }
  lastIndex = -1;
  started.clear();
}

let lastIndex = -1;
const started = new Set<string>();
let follow: ReturnType<typeof setInterval> | null = null;

const bold = (text: string): Array<Node | string> =>
  text.split(/(\*\*[^*]+\*\*)/).filter(Boolean).map((part) => (part.startsWith('**') ? h('b', null, part.slice(2, -2)) : part));

/** Draw (or hide) the coach. Called whenever the screen is redrawn. */
export function renderGuide(gs: GameScreen, el: HTMLElement): void {
  if (follow) { clearInterval(follow); follow = null; }
  clear(el);
  const off = state();
  if (off === 'off' || off === 'learned' || !gs.view || gs.view.over || gs.myCities().length + gs.myUnits().length === 0) {
    el.classList.add('hidden');
    return;
  }
  const todo = STEPS.map((s, i) => ({ s, i })).filter(({ s }) => !s.done(gs));
  const active = todo.find(({ s }) => !s.when || s.when(gs));
  if (!todo.length || gs.view.turn > 60) {
    set('learned');
    el.classList.add('hidden');
    return;
  }
  if (!active) {
    el.classList.add('hidden');
    return;
  }
  const { s, i } = active;
  el.classList.remove('hidden');
  if (!started.has(s.id)) {
    started.add(s.id);
    s.begin?.(gs);
  }
  const advanced = lastIndex >= 0 && i > lastIndex;
  lastIndex = i;
  const skip = h('button', { class: 'coach-x', type: 'button', title: 'Skip the tips', 'aria-label': 'Skip the tips' }, icon('close', 14));
  skip.addEventListener('click', (e) => { e.stopPropagation(); set('off'); el.classList.add('hidden'); });
  const dots = h('span', { class: 'coach-dots', 'aria-hidden': 'true' }, ...STEPS.map((_x, k) => h('i', { class: STEPS[k].done(gs) ? 'done' : k === i ? 'now' : '' })));
  const ring = h('div', { class: 'coach-ring' });
  const tip = h('div', { class: `coach-tip ${advanced ? 'next' : ''}` }, h('span', { class: 'coach-art' }, icon(s.art, 26)), h('span', { class: 'coach-say' }, ...bold(s.say(gs))), dots, skip);
  el.append(ring, tip);

  const place = () => {
    if (!el.isConnected || el.classList.contains('hidden')) return;
    const root = gs.root.getBoundingClientRect();
    const target = s.target(gs);
    const t = target?.getBoundingClientRect();
    const tw = tip.offsetWidth, th = tip.offsetHeight;
    // a control that has scrolled out of its panel is brought back into view before it is circled
    if (t && target && (t.bottom > root.bottom - 6 || t.top < root.top + 40) && target.closest('.drawer, .overlay')) {
      target.scrollIntoView({ block: 'center', inline: 'nearest' });
      return;
    }
    if (t && target) {
      const pad = 6;
      const left = t.left - root.left - pad, top = t.top - root.top - pad;
      Object.assign(ring.style, { display: 'block', left: `${left}px`, top: `${top}px`, width: `${t.width + pad * 2}px`, height: `${t.height + pad * 2}px`, borderRadius: getComputedStyle(target).borderRadius.includes('%') || t.width === t.height ? '50%' : '14px' });
      // the tip sits below the control when it is in the upper half of the screen, above it otherwise
      const below = t.top - root.top < root.height * 0.5;
      let x = t.left - root.left + t.width / 2 - tw / 2;
      x = Math.max(10, Math.min(root.width - tw - 10, x));
      const y = below ? t.bottom - root.top + pad + 12 : t.top - root.top - pad - th - 12;
      tip.dataset.side = below ? 'below' : 'above';
      tip.style.setProperty('--arrow', `${Math.max(14, Math.min(tw - 14, t.left - root.left + t.width / 2 - x))}px`);
      Object.assign(tip.style, { left: `${x}px`, top: `${Math.max(48, Math.min(root.height - th - 8, y))}px` });
    } else {
      // nothing to circle yet: the tip waits under the top bar
      ring.style.display = 'none';
      tip.dataset.side = 'none';
      Object.assign(tip.style, { left: `${Math.max(10, (root.width - tw) / 2)}px`, top: `${54 + parseFloat(getComputedStyle(gs.root).getPropertyValue('--sat') || '0')}px` });
    }
  };
  place();
  // panels slide in and the map moves: keep the ring on its control
  follow = setInterval(place, 250);
}
