/** Keep the screen on while a game is open, so it does not go dark while the player thinks or the computers move. */
type Sentinel = { release(): Promise<void> };
export function keepAwake(): () => void {
  const wl = (navigator as unknown as { wakeLock?: { request(t: 'screen'): Promise<Sentinel> } }).wakeLock;
  if (!wl) return () => {};
  let lock: Sentinel | null = null;
  let stopped = false;
  const take = async () => {
    if (stopped || document.hidden || lock) return;
    try {
      lock = await wl.request('screen');
      // the browser releases the lock itself when the tab is hidden; ask again on return
      (lock as unknown as EventTarget).addEventListener?.('release', () => { lock = null; });
    } catch { /* refused: low battery, or no permission */ }
  };
  const onVisible = () => { if (!document.hidden) void take(); };
  document.addEventListener('visibilitychange', onVisible);
  void take();
  return () => {
    stopped = true;
    document.removeEventListener('visibilitychange', onVisible);
    void lock?.release().catch(() => undefined);
    lock = null;
  };
}
