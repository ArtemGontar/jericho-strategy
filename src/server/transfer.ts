/**
 * Hand a saved game to another device with a short code, with no account.
 *
 *   POST /transfer        body: the save (JSON)   ->  { code }
 *   GET  /transfer/CODE                           ->  the save, once
 *
 * A code works once and for an hour. The server keeps the text in memory only, never reads into it beyond checking that
 * it looks like a save, and refuses large bodies, too many uploads from one address, and too many wrong codes.
 */
import type { IncomingMessage, ServerResponse } from 'node:http';
import { randomInt } from 'node:crypto';

export const TRANSFER_MAX_BYTES = 3 * 1024 * 1024;
export const TRANSFER_TTL_MS = 60 * 60 * 1000;
export const TRANSFER_MAX_PENDING = 200;
const UPLOADS_PER_HOUR = 12;
const WRONG_PER_10_MIN = 12;
/** no letters that look like digits */
const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
const CODE_LENGTH = 8;

interface Held {
  body: string;
  at: number;
}
const held = new Map<string, Held>();
const uploads = new Map<string, number[]>();
const wrong = new Map<string, number[]>();

const recent = (list: number[] | undefined, windowMs: number, now: number): number[] => (list ?? []).filter((t) => now - t < windowMs);
const newCode = (): string => {
  for (;;) {
    let c = '';
    for (let i = 0; i < CODE_LENGTH; i++) c += ALPHABET[randomInt(ALPHABET.length)];
    if (!held.has(c)) return c;
  }
};

export function resetTransfers(): void {
  held.clear();
  uploads.clear();
  wrong.clear();
}
export function sweepTransfers(now = Date.now()): void {
  for (const [c, h] of held) if (now - h.at > TRANSFER_TTL_MS) held.delete(c);
  for (const [ip, l] of uploads) if (!recent(l, 3600_000, now).length) uploads.delete(ip);
  for (const [ip, l] of wrong) if (!recent(l, 600_000, now).length) wrong.delete(ip);
}

function send(res: ServerResponse, status: number, body: string, cors: Record<string, string>, type = 'application/json'): void {
  res.writeHead(status, { 'content-type': type, 'cache-control': 'no-store', ...cors });
  res.end(body);
}

/** Does this text look like a saved game? Nothing deeper: the player's browser checks it properly. */
export function looksLikeSave(text: string): boolean {
  try {
    const v = JSON.parse(text) as { state?: { players?: unknown; tiles?: unknown } };
    return !!v && typeof v === 'object' && !!v.state && Array.isArray(v.state.players) && Array.isArray(v.state.tiles);
  } catch {
    return false;
  }
}

/** Handles /transfer requests. Returns false for any other path, so the caller can carry on. */
export function handleTransfer(req: IncomingMessage, res: ServerResponse, ip: string, allowedOrigins: string[]): boolean {
  const url = new URL(req.url ?? '/', 'http://x');
  if (url.pathname !== '/transfer' && !url.pathname.startsWith('/transfer/')) return false;
  const origin = String(req.headers.origin ?? '');
  // a page on another site only gets an answer when the server was told to trust it
  const trusted = !origin || !allowedOrigins.length || allowedOrigins.includes(origin);
  const cors: Record<string, string> = trusted ? { 'access-control-allow-origin': allowedOrigins.length && origin ? origin : '*', 'access-control-allow-methods': 'GET, POST, OPTIONS', 'access-control-allow-headers': 'content-type', vary: 'Origin' } : {};
  if (!trusted) return send(res, 403, '{"error":"This site may not use this server."}', cors), true;
  if (req.method === 'OPTIONS') return send(res, 204, '', cors, 'text/plain'), true;
  const now = Date.now();

  if (req.method === 'POST' && url.pathname === '/transfer') {
    const mine = recent(uploads.get(ip), 3600_000, now);
    if (mine.length >= UPLOADS_PER_HOUR) return send(res, 429, '{"error":"Too many saves sent recently. Try again later."}', cors), true;
    if (held.size >= TRANSFER_MAX_PENDING) sweepTransfers(now);
    if (held.size >= TRANSFER_MAX_PENDING) return send(res, 503, '{"error":"The server is busy. Try again in a few minutes."}', cors), true;
    const declared = Number(req.headers['content-length'] ?? 0);
    if (declared > TRANSFER_MAX_BYTES) return send(res, 413, '{"error":"That save is too large to send."}', cors), true;
    const chunks: Buffer[] = [];
    let size = 0;
    let dead = false;
    req.on('data', (c: Buffer) => {
      if (dead) return;
      size += c.length;
      if (size > TRANSFER_MAX_BYTES) {
        dead = true;
        send(res, 413, '{"error":"That save is too large to send."}', cors);
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on('end', () => {
      if (dead) return;
      const body = Buffer.concat(chunks).toString('utf8');
      if (!looksLikeSave(body)) return send(res, 400, '{"error":"That does not look like a saved game."}', cors);
      mine.push(Date.now());
      uploads.set(ip, mine);
      const code = newCode();
      held.set(code, { body, at: Date.now() });
      send(res, 200, JSON.stringify({ code, minutes: Math.round(TRANSFER_TTL_MS / 60000) }), cors);
    });
    req.on('error', () => { if (!dead) { dead = true; if (!res.headersSent) send(res, 400, '{"error":"The upload was interrupted."}', cors); } });
    return true;
  }

  if (req.method === 'GET' && url.pathname.startsWith('/transfer/')) {
    const bad = recent(wrong.get(ip), 600_000, now);
    if (bad.length >= WRONG_PER_10_MIN) return send(res, 429, '{"error":"Too many wrong codes. Wait a few minutes."}', cors), true;
    const code = decodeURIComponent(url.pathname.slice('/transfer/'.length)).toUpperCase().replace(/[^A-Z0-9]/g, '');
    const h = held.get(code);
    if (!h || now - h.at > TRANSFER_TTL_MS) {
      held.delete(code);
      bad.push(now);
      wrong.set(ip, bad);
      return send(res, 404, '{"error":"No saved game with that code. Codes work once and for an hour."}', cors), true;
    }
    held.delete(code); // it works once
    return send(res, 200, h.body, cors), true;
  }
  return send(res, 405, '{"error":"Not allowed."}', cors), true;
}
