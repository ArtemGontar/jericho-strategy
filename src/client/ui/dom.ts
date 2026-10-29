type Child = Node | string | number | null | undefined | false | Child[];
type Attrs = Record<string, unknown> & { class?: string; style?: Partial<CSSStyleDeclaration> | string; on?: Record<string, (e: Event) => void> };

export function h<K extends keyof HTMLElementTagNameMap>(tag: K, attrs?: Attrs | null, ...children: Child[]): HTMLElementTagNameMap[K] {
  const el = document.createElement(tag);
  if (attrs) {
    for (const [k, v] of Object.entries(attrs)) {
      if (v === undefined || v === null || v === false) continue;
      if (k === 'class') el.className = String(v);
      else if (k === 'style') {
        if (typeof v === 'string') el.setAttribute('style', v);
        else Object.assign(el.style, v);
      } else if (k === 'on') {
        for (const [ev, fn] of Object.entries(v as Record<string, (e: Event) => void>)) el.addEventListener(ev, fn);
      } else if (k in el && k !== 'list') (el as unknown as Record<string, unknown>)[k] = v;
      else el.setAttribute(k, String(v));
    }
  }
  append(el, children);
  // icon-only buttons carry their meaning in the title: expose it to screen readers too
  if (tag === 'button' && (el as HTMLButtonElement).title && !el.textContent?.trim() && !el.hasAttribute('aria-label')) el.setAttribute('aria-label', (el as HTMLButtonElement).title);
  return el;
}

function append(el: Node, children: Child[]): void {
  for (const c of children) {
    if (c === null || c === undefined || c === false) continue;
    if (Array.isArray(c)) append(el, c);
    else el.appendChild(typeof c === 'object' ? c : document.createTextNode(String(c)));
  }
}

export function clear(el: Element): void {
  while (el.firstChild) el.removeChild(el.firstChild);
}

export function btn(label: Child, onClick: (e: MouseEvent) => void, cls = '', title = ''): HTMLButtonElement {
  const b = h('button', { class: `btn ${cls}`, title, type: 'button' }, label);
  b.addEventListener('click', (e) => {
    e.stopPropagation();
    onClick(e);
  });
  return b;
}

export function el$<T extends HTMLElement = HTMLElement>(sel: string, root: ParentNode = document): T {
  const e = root.querySelector<T>(sel);
  if (!e) throw new Error(`missing ${sel}`);
  return e;
}

export function fmtSigned(n: number): string {
  return (n >= 0 ? '+' : '') + n;
}

export function turnsLeft(remaining: number, perTurn: number): string {
  if (remaining <= 0) return '0';
  if (perTurn <= 0) return '∞';
  return String(Math.ceil(remaining / perTurn));
}

/** Append children (nulls/false are skipped). */
export function put<T extends Element>(el: T, ...children: Child[]): T {
  append(el, children);
  return el;
}

/** In-page replacement for window.confirm (which embedded pages cannot show). */
export function askConfirm(message: string, okLabel = 'Yes'): Promise<boolean> {
  return new Promise((resolve) => {
    const done = (v: boolean) => {
      back.remove();
      resolve(v);
    };
    const back = h('div', { class: 'modal-back', style: { zIndex: '60' } },
      h('div', { class: 'modal narrow' },
        h('div', { class: 'body' }, h('p', { style: { margin: '0', fontSize: '16px' } }, message),
          h('div', { class: 'row', style: { justifyContent: 'flex-end' } }, btn('Cancel', () => done(false)), btn(okLabel, () => done(true), 'primary')))));
    (document.querySelector('.game') ?? document.body).append(back);
  });
}

const lastNumber = new Map<string, number>();

/** Show a number that counts up or down to its new value the first time it changes (not on first sight). */
export function tickNumber(el: HTMLElement, key: string, value: number, ms = 600): void {
  const from = lastNumber.get(key);
  lastNumber.set(key, value);
  el.textContent = String(value);
  if (from === undefined || from === value || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
  const t0 = performance.now();
  const step = (now: number) => {
    if (!el.isConnected) return;
    const k = Math.min(1, (now - t0) / ms);
    const e = 1 - Math.pow(1 - k, 3);
    el.textContent = String(Math.round(from + (value - from) * e));
    if (k < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}

/** Give each full-screen sheet a short entrance, without replaying it when its contents are redrawn. */
export function installMotion(): void {
  new MutationObserver((records) => {
    for (const r of records) {
      r.addedNodes.forEach((n) => {
        if (n instanceof HTMLElement && n.classList.contains('overlay')) {
          n.classList.add('enter');
          setTimeout(() => n.classList.remove('enter'), 500);
        }
      });
    }
  }).observe(document.body, { childList: true, subtree: true });
}
