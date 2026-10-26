import { BUILDINGS, CIV_BY_ID, ERA_NAMES, TECHS, TECH_BY_ID, UNITS } from '../../shared/data';
import type { GameScreen } from '../gamescreen';
import { isMuted, setMuted, sfx } from '../audio';
import { btn, h, turnsLeft } from './dom';

function modal(title: string, body: HTMLElement | HTMLElement[], onClose: () => void, narrow = false, extra: HTMLElement[] = []): HTMLElement {
  return h('div', { class: `modal ${narrow ? 'narrow' : ''}` },
    h('header', null, h('h3', { style: { flex: '1' } }, title), ...extra, btn('✕', onClose, 'small ghost', 'Close (Esc)')),
    h('div', { class: 'body' }, body),
  );
}

// ------------------------------------------------------------------ technology
export function showTechTree(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const cols = h('div', { class: 'tech-cols' });
  for (let era = 0; era < 4; era++) {
    const col = h('div', { class: 'tech-col' }, h('h4', null, ERA_NAMES[era]));
    for (const t of TECHS.filter((x) => x.era === era)) {
      const known = v.techs.includes(t.id);
      const avail = v.availableTechs.includes(t.id);
      const cur = v.researching === t.id;
      const cls = known ? 'known' : cur ? 'avail cur' : avail ? 'avail' : 'locked';
      const missing = t.pre.filter((p) => !v.techs.includes(p)).map((p) => TECH_BY_ID[p].name);
      const cost = v.techCosts[t.id];
      const sub = known ? '✔ Known' : cur ? `Researching · ${turnsLeft(v.researchCost - v.research, v.income.science)} turns` : avail ? `${cost} 🔬 · ${turnsLeft(cost, v.income.science)} turns` : `Needs ${missing.join(', ')}`;
      const el = h('button', { class: `tech ${cls}`, type: 'button', title: t.pre.length ? `Requires: ${t.pre.map((p) => TECH_BY_ID[p].name).join(', ')}` : '' },
        h('b', null, t.name), h('span', null, `Unlocks: ${t.desc}`), h('span', { class: avail || cur ? 'pos' : '' }, sub));
      if (avail) {
        el.addEventListener('click', async () => {
          if (await gs.cmd({ t: 'research', tech: t.id })) {
            sfx('click');
            gs.closeModal();
          }
        });
      }
      col.append(el);
    }
    cols.append(col);
  }
  const researching = v.researching ? TECH_BY_ID[v.researching] : null;
  const head = h('div', { class: 'row' },
    h('span', { class: 'pill' }, `🔬 +${v.income.science} science / turn`),
    researching ? h('span', { class: 'pill' }, `Researching ${researching.name}`) : h('span', { class: 'pill neg' }, 'Pick a technology!'),
    h('span', { class: 'muted' }, 'Reach Enlightenment first for a science victory.'),
  );
  return modal('Technology', [head, h('div', { style: { overflowX: 'auto' } }, cols)], () => gs.closeModal());
}

// ------------------------------------------------------------------ diplomacy
export function showDiplomacy(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const me = v.players[v.me];
  const body: HTMLElement[] = [];
  const mine = v.proposals.filter((p) => p.to === v.me);
  for (const p of mine) {
    body.push(h('div', { class: 'panel row' },
      h('span', null, `🕊 ${v.players[p.from].name} proposes peace.`), h('span', { class: 'spacer' }),
      btn('Accept', async () => { await gs.cmd({ t: 'accept', proposal: p.id, accept: true }); }, 'small primary'),
      btn('Decline', async () => { await gs.cmd({ t: 'accept', proposal: p.id, accept: false }); }, 'small')));
  }
  const rows = v.players.map((p) => {
    const civ = CIV_BY_ID[p.civ];
    const rel = p.id === v.me ? 'You' : p.relation === 'war' ? '⚔ War' : p.relation === 'peace' ? `🕊 Peace (${p.relationAge}t)` : 'Not met';
    const relCls = p.relation === 'war' ? 'rel-war' : p.relation === 'peace' ? 'rel-peace' : 'rel-unmet';
    const strength = p.id === v.me || p.relation === 'unmet' ? '' : p.power > me.power * 1.3 ? 'Stronger' : p.power < me.power * 0.7 ? 'Weaker' : 'Even';
    const actions = h('div', { class: 'row' });
    if (p.id !== v.me && p.alive) {
      if (p.relation === 'war') {
        const pending = v.proposals.some((q) => q.from === v.me && q.to === p.id);
        const b = btn(pending ? 'Peace offered…' : '🕊 Propose peace', async () => { await gs.cmd({ t: 'peace', to: p.id }); }, 'small');
        b.disabled = pending;
        actions.append(b);
      } else if (p.relation === 'peace') {
        const can = p.relationAge >= 10;
        const b = btn('⚔ Declare war', async () => { if (confirm(`Declare war on ${p.name}?`)) await gs.cmd({ t: 'war', to: p.id }); }, 'small danger', can ? '' : `The treaty must hold for 10 turns (${p.relationAge} so far)`);
        b.disabled = !can;
        actions.append(b);
      }
      if (!p.ai && !p.connected && gs.opts.hostActions) actions.append(btn('🤖 Give to AI', () => gs.opts.hostActions!.takeover(p.id), 'small', 'Replace this disconnected player with the computer'));
    }
    return h('tr', null,
      h('td', null, h('span', { class: 'dot', style: { background: civ.color } }), ' ', h('b', null, p.name), h('div', { class: 'muted', style: { fontSize: '11px' } }, `${civ.name}${p.ai ? ' · AI' : ''}${!p.alive ? ' · eliminated' : ''}`)),
      h('td', { class: relCls }, rel),
      h('td', null, `${p.score}`), h('td', null, `${p.cityCount}`), h('td', null, `${p.techCount}`), h('td', { class: 'muted' }, strength),
      h('td', null, actions));
  });
  body.push(h('table', { class: 'table' },
    h('thead', null, h('tr', null, ...['Civilisation', 'Relation', 'Score', 'Cities', 'Techs', 'Army', ''].map((t) => h('th', null, t)))),
    h('tbody', null, ...rows)));
  body.push(h('div', { class: 'muted', style: { fontSize: '12px' } }, 'Civilisations are at war when they first meet. Peace treaties close borders to each other and must hold for 10 turns before you can break them.'));
  return modal('Diplomacy', body, () => gs.closeModal());
}

// ------------------------------------------------------------------ menu
export function showMenu(gs: GameScreen): HTMLElement {
  const rows: HTMLElement[] = [];
  rows.push(btn('▶ Resume', () => gs.closeModal(), 'primary'));
  rows.push(btn('📖 How to play', () => gs.openModal('help', showHelp(gs))));
  rows.push(btn(`🔊 Sound: ${isMuted() ? 'off' : 'on'}`, () => { setMuted(!isMuted()); gs.openModal('menu', showMenu(gs)); }));
  rows.push(btn(`🌾 Tile yields: ${gs.showYields ? 'on' : 'off'}`, () => { gs.togglePref('yields'); gs.openModal('menu', showMenu(gs)); }));
  rows.push(btn(`▦ Grid lines: ${gs.showGrid ? 'on' : 'off'}`, () => { gs.togglePref('grid'); gs.openModal('menu', showMenu(gs)); }));
  if (gs.opts.sendChat) rows.push(btn('💬 Chat', () => { gs.closeModal(); gs.toggleChat(); }));
  const invite = gs.opts.invite;
  if (invite) {
    const link = `${location.origin}${location.pathname}?room=${invite.code}`;
    rows.push(h('div', { class: 'panel' }, h('div', { class: 'muted' }, 'Room code'), h('div', { class: 'code', style: { fontSize: '28px' } }, invite.code),
      btn('📋 Copy invite link', () => { void navigator.clipboard?.writeText(link); gs.toast('Invite link copied'); }, 'small')));
  }
  rows.push(btn('🚪 Leave game', () => { if (confirm(gs.transport.kind === 'local' ? 'Leave to the main menu? Your game is saved automatically each turn.' : 'Leave the game? You can rejoin with the room code.')) gs.opts.onExit(); }, 'danger'));
  return modal('Menu', h('div', { style: { display: 'grid', gap: '8px' } }, ...rows), () => gs.closeModal(), true);
}

// ------------------------------------------------------------------ help
export function showHelp(gs: GameScreen): HTMLElement {
  const body = h('div', { class: 'help-grid' },
    h('div', null, h('h4', null, '🏕 Getting started'),
      h('p', null, 'Select your ', h('b', null, 'Settler'), ' and press ', h('kbd', null, 'B'), ' (or click “Found city”) to build your capital — ideally near rivers of food and some hills. Your Warrior protects it, the Scout explores, the Worker improves land.'),
      h('p', null, 'Cities grow on food, build things with production, and fuel your treasury and research.')),
    h('div', null, h('h4', null, '🏙 Cities'),
      h('p', null, 'Click a city (or press ', h('kbd', null, 'C'), ') to choose what it builds. Queue warriors for defence, workers and settlers to expand, then libraries, markets and wonders. Spend gold to buy items.'),
      h('p', null, 'Cities must be 3 tiles apart. Citizens work the best nearby tiles — change the focus to steer them.')),
    h('div', null, h('h4', null, '🔬 Research'),
      h('p', null, 'Press ', h('kbd', null, 'T'), ' to pick a technology. Techs unlock units, buildings, wonders and tile improvements. Discover ', h('b', null, 'Enlightenment'), ' first to win by science.')),
    h('div', null, h('h4', null, '⚔ War & peace'),
      h('p', null, 'You are at war with civilisations you meet. Click an enemy next to your unit to attack; hover to preview damage. Melee units fight back, ranged units hit from 2 tiles away without retaliation.'),
      h('p', null, 'Reduce a city to 0 HP and walk a melee unit in to capture it. Capture every rival capital — or propose peace in Diplomacy (', h('kbd', null, 'D'), ').')),
    h('div', null, h('h4', null, '🏆 Winning'),
      h('p', null, h('b', null, 'Conquest:'), ' be the last civilisation standing. ', h('b', null, 'Science:'), ' discover Enlightenment. ', h('b', null, 'Score:'), ` the highest score when the turn limit (${gs.view.settings.turnLimit}) ends — cities, population, techs, wonders, culture and land all count.`)),
    h('div', null, h('h4', null, '⌨ Controls'),
      h('ul', { style: { paddingLeft: '18px', margin: '0' } },
        h('li', null, h('kbd', null, 'Click'), ' select · move · attack  (right-click always commands)'),
        h('li', null, h('kbd', null, 'Drag'), ' / arrows to pan · wheel / pinch to zoom'),
        h('li', null, h('kbd', null, 'Enter'), ' next action / end turn · ', h('kbd', null, 'N'), ' next unit'),
        h('li', null, h('kbd', null, 'F'), ' fortify · ', h('kbd', null, 'Z'), ' sleep · ', h('kbd', null, 'A'), ' auto/explore · ', h('kbd', null, 'Space'), ' skip'),
        h('li', null, h('kbd', null, 'Y'), ' tile yields · ', h('kbd', null, 'G'), ' grid · ', h('kbd', null, 'M'), ' mute · ', h('kbd', null, 'Esc'), ' menu'),
        h('li', null, 'On touch: tap a destination, then tap it again to confirm.'))),
  );
  void BUILDINGS;
  void UNITS;
  return modal('How to play', body, () => gs.closeModal());
}

// ------------------------------------------------------------------ victory
export function showVictory(gs: GameScreen): HTMLElement {
  const v = gs.view;
  const winner = v.winner !== null ? v.players[v.winner] : null;
  const won = winner && winner.id === v.me;
  const how: Record<string, string> = { science: 'a scientific victory (Enlightenment)', score: 'the highest score', domination: 'domination', 'last-standing': 'conquest — the last civilisation standing' };
  const rows = v.players.slice().sort((a, b) => b.score - a.score).map((p, i) =>
    h('tr', null, h('td', null, String(i + 1)), h('td', null, h('span', { class: 'dot', style: { background: CIV_BY_ID[p.civ].color } }), ' ', p.name, p.id === v.winner ? ' 🏆' : ''), h('td', null, `${p.score}`), h('td', null, `${p.cityCount} cities`), h('td', null, `${p.techCount} techs`)));
  return modal(won ? 'Victory!' : 'Game over', [
    h('div', { class: 'winner' },
      h('h2', null, won ? '🏆 You win!' : winner ? `${winner.name} wins` : 'Nobody wins'),
      h('p', { class: 'muted' }, winner ? `${CIV_BY_ID[winner.civ].name} triumphs through ${how[v.victory ?? ''] ?? v.victory}.` : '')),
    h('table', { class: 'table' }, h('thead', null, h('tr', null, ...['#', 'Civilisation', 'Score', '', ''].map((t) => h('th', null, t)))), h('tbody', null, ...rows)),
    h('div', { class: 'row' }, btn('Back to main menu', () => gs.opts.onExit(), 'primary'), btn('Keep looking at the map', () => gs.closeModal())),
  ], () => gs.closeModal(), true);
}
