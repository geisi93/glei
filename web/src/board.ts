import type { Departure } from './api';

/** Identifies one line in one direction at a stop, e.g. "3|inward". */
export const groupKey = (d: Pick<Departure, 'line' | 'direction' | 'destination'>) =>
  `${d.line}|${d.direction ?? d.destination}`;

export const effectiveTime = (d: Departure) => new Date(d.estimatedTime ?? d.plannedTime).getTime();

/** A departure stays on the board this long after its time ("jetzt"). */
const GRACE_MS = 30_000;

export function upcoming(deps: Departure[], now: number): Departure[] {
  return deps.filter(d => effectiveTime(d) >= now - GRACE_MS).sort((a, b) => effectiveTime(a) - effectiveTime(b));
}

export function minutesUntil(d: Departure, now: number): number {
  // Rounded down like the stop displays: 2:50 left shows 2, under a minute shows jetzt.
  return Math.max(0, Math.floor((effectiveTime(d) - now) / 60_000));
}

export interface LineGroup { key: string; line: string; mode: string | null; label: string; next: Departure[] }

/** Numeric lines first by number (3, 4, 23, 64E), then lettered ones (N5). */
export function compareLines(a: string, b: string): number {
  const na = /^\d+/.exec(a)?.[0], nb = /^\d+/.exec(b)?.[0];
  if (na && nb && na !== nb) return Number(na) - Number(nb);
  if (na && !nb) return -1;
  if (!na && nb) return 1;
  return a.localeCompare(b, 'de', { numeric: true });
}

/** All departures grouped by line + direction, labelled with the most frequent destination. */
export function groupByLine(deps: Departure[], now: number): LineGroup[] {
  const map = new Map<string, Departure[]>();
  for (const d of upcoming(deps, now)) {
    const k = groupKey(d);
    map.set(k, [...(map.get(k) ?? []), d]);
  }
  return [...map.entries()].map(([key, list]) => {
    const counts = new Map<string, number>();
    for (const d of list) counts.set(d.destination, (counts.get(d.destination) ?? 0) + 1);
    const label = [...counts.entries()].sort((a, b) => b[1] - a[1])[0][0];
    return { key, line: list[0].line, mode: list[0].mode, label, next: list.filter(d => !d.cancelled).slice(0, 3) };
  }).sort((a, b) => compareLines(a.line, b.line) || a.label.localeCompare(b.label, 'de'));
}

/** Short time like "17:05" in local time. */
export const clock = (t: number | Date) =>
  new Date(t).toLocaleTimeString('de-AT', { hour: '2-digit', minute: '2-digit' });
