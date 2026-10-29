// Converts the generated artwork archived in legacy/ into the web-sized WebP files under src/client/assets/art.
// Needs sharp, which is not a project dependency: `npm i --no-save sharp && node scripts/optimize-art.mjs`.
import { mkdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const art = join(root, 'legacy/Assets/Game/Runtime/Client/Resources/Art');
const out = join(root, 'src/client/assets/art');
const pub = join(root, 'public');

const ensure = (p) => mkdirSync(dirname(p), { recursive: true });

/** Scene backdrops: scaled down to fit `max` pixels on the longer side. */
const SCENES = {
  menu: 'Menu_Background', campaign: 'Campaign_Background', settings: 'Settings_Background', tutorial: 'Tutorial_Background',
  diplomacy: 'Diplomacy_Background', research: 'Research_Background', city: 'City_Background', victory: 'Victory_Background',
  defeat: 'Defeat_Background', era: 'Era_Transition_Background', events: 'Events_Background', archive: 'Archive_Background',
};
for (const [name, src] of Object.entries(SCENES)) {
  const dst = join(out, 'scenes', `${name}.webp`);
  ensure(dst);
  await sharp(join(art, 'Backgrounds', `${src}.png`)).resize({ width: 1600, height: 1600, fit: 'inside', withoutEnlargement: true }).webp({ quality: 68, effort: 5 }).toFile(dst);
}

/**
 * Atlas cells carry slivers of their neighbours along the edges. Along each axis, pixels are grouped into runs
 * separated by gaps of empty lines, and only the run with the most opaque pixels is kept.
 */
async function dropNeighbourBleed(buf) {
  const { data, info } = await sharp(buf).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width: w, height: h } = info;
  const GAP = 3;
  const mass = (axis) => {
    const n = axis === 'row' ? h : w;
    const m = new Array(n).fill(0);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) if (data[(y * w + x) * 4 + 3] > 24) m[axis === 'row' ? y : x]++;
    return m;
  };
  for (const axis of ['row', 'col']) {
    const m = mass(axis);
    const runs = [];
    let from = -1;
    let blank = 0;
    m.forEach((v, i) => {
      if (v > 0) {
        if (from < 0) from = i;
        blank = 0;
      } else if (from >= 0 && ++blank >= GAP) {
        runs.push([from, i - blank]);
        from = -1;
      }
    });
    if (from >= 0) runs.push([from, m.length - 1]);
    if (runs.length < 2) continue;
    const weight = ([a, b]) => m.slice(a, b + 1).reduce((s, v) => s + v, 0);
    const [keepFrom, keepTo] = runs.reduce((best, r) => (weight(r) > weight(best) ? r : best));
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const at = axis === 'row' ? y : x;
        if (at < keepFrom || at > keepTo) data[(y * w + x) * 4 + 3] = 0;
      }
    }
  }
  return sharp(data, { raw: info }).png().toBuffer();
}

/** Cut-out figures: cropped to their visible pixels, then fitted into a square with a little margin. */
async function cutout(srcFile, dst, size, region) {
  ensure(dst);
  // extract and trim run as two passes: sharp applies trim before extract inside a single pipeline
  const cropped = region ? await dropNeighbourBleed(await sharp(srcFile).extract(region).toBuffer()) : srcFile;
  const trimmed = await sharp(cropped).trim({ threshold: 8 }).toBuffer();
  await sharp(trimmed).resize({ width: size, height: size, fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } }).webp({ quality: 82, alphaQuality: 90, effort: 5 }).toFile(dst);
}

for (const civ of ['jericho', 'sumer', 'egypt', 'indus']) {
  const name = civ[0].toUpperCase() + civ.slice(1);
  await cutout(join(art, 'Leaders', `Leader_${name}.png`), join(out, 'leaders', `${civ}.webp`), 448);
}
await cutout(join(art, 'Jericho_Logo.png'), join(out, 'crest.webp'), 512);

/** Unity stores each atlas slice in its .meta file, with y counted from the bottom. */
function slices(file) {
  const text = readFileSync(`${file}.meta`, 'utf8');
  const found = [];
  const re = /name: (\S+)\s+rect:\s+serializedVersion: 2\s+x: (\d+)\s+y: (\d+)\s+width: (\d+)\s+height: (\d+)/g;
  for (let m; (m = re.exec(text)); ) found.push({ name: m[1], x: +m[2], y: +m[3], w: +m[4], h: +m[5] });
  return found;
}

async function atlas(file, prefix, pick, dir) {
  const full = join(art, file);
  const { height } = await sharp(full).metadata();
  for (const s of slices(full)) {
    const key = pick(s.name);
    if (!key) continue;
    await cutout(full, join(out, 'sprites', dir, `${key}.webp`), 160, { left: s.x, top: height - s.y - s.h, width: s.w, height: s.h });
  }
}

// units are named by era and role; the game's unit ids are mapped onto them in src/client/artwork.ts
await atlas('UnitRosterAtlas.png', 'UnitRoster_', (n) => n.replace('UnitRoster_', '').toLowerCase(), 'units');
await atlas('UnitAtlas.png', 'Unit_', (n) => (n === 'Unit_Settler' ? 'settler' : null), 'units');
await atlas('Animations/WorkerActivities_Rural.png', 'Activity_', (n) => (n === 'Activity_Farmer_3' ? 'worker' : null), 'units');
await atlas('BuildingAtlas.png', 'Building_', (n) => n.replace('Building_', '').toLowerCase(), 'buildings');

// the install icons
for (const size of [180, 192, 512]) {
  const dst = join(pub, 'icons', `icon-${size}.png`);
  ensure(dst);
  await sharp(join(art, 'AppIcon_iOS.png')).resize(size, size).png({ compressionLevel: 9 }).toFile(dst);
}
console.log('art written to', out);
