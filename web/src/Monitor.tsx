import { useEffect, useRef, useState } from 'preact/hooks';
import { getDepartures, type Departure } from './api';
import { clock, effectiveTime, groupKey, minutesUntil, upcoming } from './board';
import { href, shortName } from './route';
import { loadMonitors, monitorId, type SavedMonitor } from './storage';

interface Props { stopId: string; name: string; keys: string[] }

const REFRESH_MS = 30_000;
const ROWS = 8;
/** Horizontal drag (px) that counts as a swipe to the next monitor. */
const SWIPE_PX = 60;

/** Which side the next monitor slides in from; set right before navigating. */
let enterFrom: 'left' | 'right' | null = null;

/** The display itself, styled after the dot-matrix monitors at Graz stops. */
export function Monitor({ stopId, name, keys }: Props) {
  const [deps, setDeps] = useState<Departure[] | null>(null);
  const [updated, setUpdated] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [now, setNow] = useState(Date.now());

  // Poll while the tab is visible; refresh immediately when it becomes visible again.
  useEffect(() => {
    let timer: number | undefined;
    let ctrl: AbortController | undefined;
    const load = async () => {
      clearTimeout(timer);
      ctrl?.abort();
      ctrl = new AbortController();
      try {
        const d = await getDepartures(stopId, ctrl.signal);
        setDeps(d); setUpdated(Date.now()); setError(null);
      } catch (e) {
        if ((e as Error).name === 'AbortError') return;
        setError((e as Error).message);
      }
      if (!document.hidden) timer = setTimeout(load, REFRESH_MS);
    };
    const onVisible = () => { if (!document.hidden) load(); else clearTimeout(timer); };
    load();
    document.addEventListener('visibilitychange', onVisible);
    return () => { clearTimeout(timer); ctrl?.abort(); document.removeEventListener('visibilitychange', onVisible); };
  }, [stopId]);

  // Minutes are recomputed locally from the absolute times.
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 5_000);
    return () => clearInterval(t);
  }, []);

  // Keep the screen on while the monitor is shown (e.g. on a tablet at the door).
  useEffect(() => {
    let lock: WakeLockSentinel | undefined;
    const request = () => navigator.wakeLock?.request('screen').then(l => { lock = l; }).catch(() => {});
    request();
    const onVisible = () => { if (!document.hidden) request(); };
    document.addEventListener('visibilitychange', onVisible);
    return () => { lock?.release(); document.removeEventListener('visibilitychange', onVisible); };
  }, []);

  const carousel = useCarousel(stopId, keys);

  const rows = deps
    ? upcoming(deps, now).filter(d => keys.length === 0 || keys.includes(groupKey(d))).slice(0, ROWS)
    : [];
  const stale = error && updated;

  const fullscreen = () => {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen?.().catch(() => {});
  };

  return (
    <main class="monitor-page">
      <nav class="monitor-tools">
        <a href="#/" title="Startseite">←</a>
        <a href={href('stop', stopId, name, keys)} title="Linien wählen">✎</a>
        <button onClick={fullscreen} title="Vollbild">⛶</button>
      </nav>

      <div class={`post ${carousel.enterClass}`} {...carousel.handlers}
        style={carousel.dx ? { transform: `translateX(${carousel.dx}px)`, transition: 'none' } : undefined}>
        <div class="frame">
          <div class={stale ? 'display stale' : 'display'}>
            <div class="d-head">
              <span class="d-stop">{shortName(name)}</span>
              <span class="d-clock">{clock(now)}</span>
            </div>
            <div class="d-rows">
              {!deps && !error && <div class="d-msg">Lade …</div>}
              {error && !deps && <div class="d-msg">{error}</div>}
              {deps && rows.length === 0 && <div class="d-msg">Derzeit keine Abfahrten</div>}
              {rows.map(d => <Row key={`${d.line}${d.plannedTime}${d.destination}`} d={d} now={now} />)}
            </div>
          </div>
        </div>
        <div class="signs">
          <span class="h-sign big" aria-hidden="true">H</span>
          <span class="sign-name">{shortName(name)}</span>
        </div>
      </div>

      {carousel.list.length > 1 && (
        <div class="carousel-dots" role="tablist" aria-label="Gespeicherte Monitore">
          {carousel.list.map((m, i) => (
            <button key={monitorId(m)} role="tab" aria-selected={i === carousel.index}
              class={i === carousel.index ? 'dot active' : 'dot'}
              title={`${shortName(m.stopName)}${m.summary ? ` · ${m.summary}` : ''}`}
              onClick={() => carousel.goTo(i)} />
          ))}
        </div>
      )}

      <p class="monitor-foot">
        {stale ? `offline · Stand ${clock(updated!)}` : updated ? `Stand ${clock(updated)} · aktualisiert alle 30 s` : ''}
        {' · '}<span class="rt-dot" /> Echtzeit
        {' · '}<a href="#/impressum">Impressum</a>
      </p>
    </main>
  );
}

/**
 * Swipe (or ←/→ keys, or the dots) between the saved monitors, wrapping around.
 * Only active when the current monitor is one of the saved ones.
 */
function useCarousel(stopId: string, keys: string[]) {
  const [list] = useState(loadMonitors);
  const index = list.findIndex(m => monitorId(m) === monitorId({ stopId, keys }));
  const enabled = index >= 0 && list.length > 1;
  const [dx, setDx] = useState(0);
  const start = useRef<{ x: number; y: number; id: number; horizontal: boolean | null } | null>(null);
  const [enterClass] = useState(() => {
    const c = enterFrom ? `enter-${enterFrom}` : '';
    enterFrom = null;
    return c;
  });

  const show = (m: SavedMonitor, from: 'left' | 'right') => {
    enterFrom = from;
    // replace: the back button leads home, not through every swiped monitor
    location.replace(href('monitor', m.stopId, m.stopName, m.keys));
  };
  const step = (delta: 1 | -1) => {
    if (!enabled) return;
    show(list[(index + delta + list.length) % list.length], delta > 0 ? 'right' : 'left');
  };
  const goTo = (i: number) => { if (enabled && i !== index) show(list[i], i > index ? 'right' : 'left'); };

  useEffect(() => {
    if (!enabled) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight') step(1);
      if (e.key === 'ArrowLeft') step(-1);
    };
    addEventListener('keydown', onKey);
    return () => removeEventListener('keydown', onKey);
  }, [enabled, index]);

  const handlers = enabled ? {
    onPointerDown: (e: PointerEvent) => {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      start.current = { x: e.clientX, y: e.clientY, id: e.pointerId, horizontal: null };
    },
    onPointerMove: (e: PointerEvent) => {
      const s = start.current;
      if (!s || s.id !== e.pointerId) return;
      const x = e.clientX - s.x, y = e.clientY - s.y;
      // Decide once per gesture whether it's a horizontal swipe or a vertical scroll.
      if (s.horizontal === null && Math.hypot(x, y) > 8) {
        s.horizontal = Math.abs(x) > Math.abs(y);
        if (s.horizontal) (e.currentTarget as Element).setPointerCapture(e.pointerId);
      }
      if (s.horizontal) setDx(x);
    },
    onPointerUp: (e: PointerEvent) => {
      const s = start.current;
      start.current = null;
      if (!s?.horizontal) return;
      const x = e.clientX - s.x;
      if (Math.abs(x) >= SWIPE_PX) step(x < 0 ? 1 : -1);
      else setDx(0);
    },
    onPointerCancel: () => { start.current = null; setDx(0); },
  } : {};

  return { list: enabled ? list : [], index, dx, enterClass, handlers, goTo };
}

function Row({ d, now }: { d: Departure; now: number }) {
  const min = minutesUntil(d, now);
  const realtime = d.estimatedTime !== null;
  const time = d.cancelled ? 'fällt aus'
    : min === 0 ? 'jetzt'
    : min >= 60 ? clock(effectiveTime(d))
    : `${min}'`;
  return (
    <div class={d.cancelled ? 'd-row cancelled' : 'd-row'}>
      <span class="d-line">{d.line}</span>
      <span class="d-dest">{d.destination}</span>
      <span class={min === 0 && !d.cancelled ? 'd-min blink' : 'd-min'}>
        {time}
        {/* Always reserve the dot's slot so minutes line up whether or not there's realtime data. */}
        <span class={realtime && !d.cancelled ? 'rt-dot' : 'rt-dot off'} title={realtime ? 'Echtzeit' : 'Fahrplanzeit'} />
      </span>
    </div>
  );
}
