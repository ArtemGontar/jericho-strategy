/**
 * "A new turn is ready" while the game is in a background tab or the phone is locked-but-awake: the tab title gets a
 * marker, the phone buzzes, and a system notification appears when the player allowed one. This works only while the
 * page is still alive; a browser that has frozen the tab cannot be reached without a push service.
 */
const KEY = 'jericho.alerts';
let baseTitle = '';
let marked = false;

export function alertsOn(): boolean {
  try { return localStorage.getItem(KEY) !== 'off'; } catch { return true; }
}
export function setAlerts(on: boolean): void {
  try { localStorage.setItem(KEY, on ? 'on' : 'off'); } catch { /* ignore */ }
  if (!on) clearAlert();
}

/** Ask the browser for permission to show notifications. Resolves to whether they will be shown. */
export async function enableNotifications(): Promise<boolean> {
  if (typeof Notification === 'undefined') return false;
  if (Notification.permission === 'granted') return true;
  if (Notification.permission === 'denied') return false;
  try {
    return (await Notification.requestPermission()) === 'granted';
  } catch {
    return false;
  }
}

export function turnAlert(turn: number): void {
  if (!alertsOn() || typeof document === 'undefined' || !document.hidden) return;
  if (!marked) {
    baseTitle = document.title;
    marked = true;
  }
  document.title = `● Turn ${turn} · JERICHO`;
  try { navigator.vibrate?.([80, 60, 80]); } catch { /* ignore */ }
  try {
    if (typeof Notification !== 'undefined' && Notification.permission === 'granted') new Notification('JERICHO', { body: `Turn ${turn} is ready.`, tag: 'jericho-turn', icon: 'icons/icon-192.png' });
  } catch { /* some mobile browsers only allow notifications from a service worker */ }
}

export function clearAlert(): void {
  if (marked) document.title = baseTitle;
  marked = false;
}

/** Remove the marker as soon as the player is back. Returns the cleanup function. */
export function watchVisibility(): () => void {
  const on = () => { if (!document.hidden) clearAlert(); };
  document.addEventListener('visibilitychange', on);
  return () => {
    document.removeEventListener('visibilitychange', on);
    clearAlert();
  };
}
