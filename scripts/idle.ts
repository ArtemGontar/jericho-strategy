import { Game } from '../src/shared/game';
// A passive human (founds a capital, builds warriors/walls, never attacks) against three AIs. Does the AI ever threaten them?
const results: string[] = [];
for (let seed = 1; seed <= Number(process.argv[2] ?? 6); seed++) {
  const g = Game.create({ seed: seed * 17, size: 'medium', players: [{ name: 'Idle', ai: false }, { name: 'A', ai: true, aiLevel: 'normal' }, { name: 'B', ai: true, aiLevel: 'normal' }, { name: 'C', ai: true, aiLevel: 'hard' }], turnLimit: 150 });
  for (const t of g.tiles) void t;
  let lostTurn = -1;
  let battles = 0;
  const me = g.players[0];
  for (let turn = 0; turn < 150 && !g.s.over; turn++) {
    // idle human behaviour
    for (const u of g.playerUnits(0).slice()) {
      if (u.type === 'settler') g.exec(0, { t: 'found', unit: u.id });
    }
    for (const c of g.playerCities(0)) {
      if (!c.queue.length) {
        const kind = g.turn % 3 === 0 ? 'walls' : 'warrior';
        if (kind === 'walls' && g.allowedItem(me, c, 'building', 'walls')) g.exec(0, { t: 'queue', city: c.id, op: 'set', kind: 'building', id: 'walls' });
        else g.exec(0, { t: 'queue', city: c.id, op: 'set', kind: 'unit', id: 'warrior' });
      }
    }
    if (!me.researching) { const a = g.exec(0, { t: 'research', tech: 'bronze' }); if (!a.ok) g.exec(0, { t: 'research', tech: 'pottery' }); }
    for (const u of g.playerUnits(0)) if (u.type === 'warrior' && !u.fortified) g.exec(0, { t: 'order', unit: u.id, order: 'fortify' });
    g.exec(0, { t: 'end' });
    for (const e of g.events) if (e.kind === 'attack' && (e.defOwner === 0 || e.owner === 0)) battles++;
    g.events.length = 0;
    if (!me.alive && lostTurn < 0) lostTurn = g.turn;
    if (g.playerCities(0).length === 0 && lostTurn < 0) lostTurn = g.turn;
  }
  results.push(`seed ${seed}: turn ${g.turn} winner=${g.s.winner} via ${g.s.victory}; idle human alive=${me.alive} cities=${g.playerCities(0).length} attacksInvolvingHuman=${battles} lostAt=${lostTurn}`);
}
console.log(results.join('\n'));
