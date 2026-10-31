import type { GameScreen } from '../gamescreen';
import { icon } from '../icons';
import { clear, h, isCompact } from './dom';

interface Step {
  title: string;
  hint: string;
  /** the same advice for a touch screen, which has no keys */
  touch: string;
  done: (gs: GameScreen) => boolean;
}

const STEPS: Step[] = [
  { title: 'Found your capital', touch: 'Tap the Settler, then the flag button.', hint: 'Select the Settler and press B (or its flag button).', done: (g) => g.myCities().length > 0 },
  { title: 'Choose what it builds', touch: 'Tap your city, then pick a Warrior, Worker or building.', hint: 'Press C to open the city and pick a Warrior, Worker or building.', done: (g) => g.myCities().some((c) => (c.queue?.length ?? 0) > 0) },
  { title: 'Choose research', touch: 'Tap the flask at the top left and pick a technology.', hint: 'Press T and pick a technology. Each one unlocks something.', done: (g) => !!g.view.researching },
  { title: 'Explore', touch: 'Tap a unit, then a tile twice to send it there, or use its explore button.', hint: 'Select a unit and click a tile, or press A to explore automatically.', done: (g) => g.moved || g.myUnits().some((u) => u.auto === 'explore') },
  { title: 'End your turn', touch: 'Tap the round button at the bottom right.', hint: 'Press Enter, or the round button at the bottom right.', done: (g) => g.view.turn >= 2 },
  { title: 'Found a second city', touch: 'Build a Settler (the city needs 2 population) and walk it three or more tiles away.', hint: 'Build a Settler (the city needs 2 population) and walk it three or more tiles away.', done: (g) => g.myCities().length >= 2 },
  { title: 'Meet a neighbour', touch: 'Explore until you find another civilisation, then open Diplomacy in the top bar.', hint: 'Explore until you find another civilisation, then open Diplomacy (D).', done: (g) => g.view.players.some((p) => p.id !== g.view.me && p.relation !== 'unmet') },
];

const KEY = 'jericho.guide.v1';
const off = (): boolean => {
  try { return localStorage.getItem(KEY) === 'off'; } catch { return false; }
};
const setOff = (): void => {
  try { localStorage.setItem(KEY, 'off'); } catch { /* ignore */ }
};

/** Show the checklist again; a new game starts with it. */
export function resetGuide(): void {
  try { localStorage.removeItem(KEY); } catch { /* ignore */ }
}

/** A short checklist for a first game: it ticks itself off as the player does each thing, then goes away. */
export function renderGuide(gs: GameScreen, el: HTMLElement): void {
  clear(el);
  if (off() || !gs.view || gs.view.over) {
    el.classList.add('hidden');
    return;
  }
  const done = STEPS.map((s) => s.done(gs));
  const current = done.findIndex((d) => !d);
  if (current === -1) {
    // everything done: say so once, then never show again
    if (!el.dataset.finished) {
      el.dataset.finished = '1';
      setTimeout(() => { setOff(); el.classList.add('hidden'); }, 7000);
    }
    el.classList.remove('hidden');
    el.append(h('div', { class: 'eyebrow' }, 'Getting started'), h('p', { class: 'cheer' }, 'You have the basics. Good luck, and have fun.'));
    return;
  }
  el.classList.remove('hidden');
  const touch = typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches;
  const close = h('button', { class: 'round flat', type: 'button', title: 'Hide these tips', 'aria-label': 'Hide these tips' }, icon('close', 14));
  close.addEventListener('click', (e) => { e.stopPropagation(); setOff(); el.classList.add('hidden'); });
  const step = STEPS[current];
  // on a small screen the list shrinks to the one thing to do next
  if (isCompact()) {
    const hide = h('button', { class: 'round flat', type: 'button', title: 'Hide these tips', 'aria-label': 'Hide these tips' }, icon('close', 14));
    hide.addEventListener('click', (e) => { e.stopPropagation(); setOff(); el.classList.add('hidden'); });
    el.append(h('div', { class: 'guide-chip' },
      h('span', { class: 'mark' }, String(current + 1)),
      h('div', { class: 'what' }, h('b', null, step.title), h('small', null, touch ? step.touch : step.hint)),
      h('span', { class: 'of' }, `${current + 1}/${STEPS.length}`),
      hide));
    return;
  }
  el.append(
    h('div', { class: 'head' }, h('div', { class: 'eyebrow' }, 'Getting started'), close),
    h('ol', null, ...STEPS.map((s, i) => h('li', { class: done[i] ? 'done' : i === current ? 'now' : '' },
      h('span', { class: 'mark' }, done[i] ? icon('check', 12) : String(i + 1)),
      h('div', null, h('b', null, s.title), i === current ? h('small', null, touch ? s.touch : s.hint) : null)))));
}
