import './moments.css';
import { h } from './dom';
import type { CineMode, Clip } from '../cinematics';

/** The DOM half of a cinematic moment: letterbox bars, a vignette and a title card. Pointer events pass through. */
export class MomentsUI {
  private el: HTMLElement | null = null;
  private timer = 0;

  constructor(private root: HTMLElement) {}

  show(c: Clip, ms: number, mode: CineMode, still: boolean): void {
    this.hide();
    const bars = c.bars !== false && mode === 'full' && !still;
    const el = h('div', { class: `cine ${c.tone ?? 'gold'} ${mode === 'short' ? 'short' : ''} ${still ? 'still' : ''}`, 'aria-hidden': 'true' },
      bars ? h('div', { class: 'bar top' }) : null,
      bars ? h('div', { class: 'bar bot' }) : null,
      h('div', { class: 'wash' }),
      c.title ? h('div', { class: 'card' }, c.sub ? h('small', null, c.sub) : null, h('b', null, c.title), h('i')) : null);
    el.style.setProperty('--ms', `${Math.round(ms)}ms`);
    this.root.append(el);
    this.el = el;
    this.timer = window.setTimeout(() => this.hide(), ms + 80);
  }

  hide(): void {
    window.clearTimeout(this.timer);
    this.el?.remove();
    this.el = null;
  }

  tech(name: string): void {
    const el = h('div', { class: 'techcard', 'aria-hidden': 'true' }, h('small', null, 'Discovery'), h('b', null, name));
    this.root.append(el);
    setTimeout(() => el.remove(), 3300);
  }
}
