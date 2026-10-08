'use strict';

/* ---------- Downloads: point the buttons at the newest release files ---------- */

(() => {
  const REPO = 'ni3ra5/tool-wheel';
  const mac = document.getElementById('dl-mac');
  const win = document.getElementById('dl-win');

  // Light up the button for the visitor's system and put it first.
  const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
  const ua = navigator.userAgent;
  const os = /win/i.test(platform) || /Windows/.test(ua) ? 'win'
    : /mac/i.test(platform) || /Mac OS X/.test(ua) ? 'mac' : null;
  if (os) (os === 'mac' ? mac : win).classList.add('recommended');

  const version = (tag) => tag.replace(/^[a-z]+-/, '');

  fetch(`https://api.github.com/repos/${REPO}/releases?per_page=50`)
    .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
    .then((releases) => {
      // Newest release whose tag matches that has a matching file.
      const pick = (tag, match) => {
        for (const release of releases) {
          if (release.draft || !tag.test(release.tag_name)) continue;
          const asset = release.assets.find((a) => match.test(a.name));
          if (asset) return { release, asset };
        }
        return null;
      };
      const set = (button, found) => {
        if (!found) return;
        button.href = found.asset.browser_download_url;
        button.querySelector('[data-meta]').textContent = `Download ${version(found.release.tag_name)}`;
      };
      set(mac, pick(/^(mac-)?v\d/, /\.zip$/)); // Mac tags were plain v0.x before mac-v*
      set(win, pick(/^windows-v/, /win-x64\.zip$/));
    })
    .catch(() => { /* buttons keep pointing at the Releases page */ });
})();

/* ---------- The wheel demo ----------
   Same geometry as the apps (windows/WheelWindow.cs, mac/Sources/ToolWheel/main.swift), in a 520-unit
   viewBox so the Settings-style pills fit outside the rim. */

(() => {
  const svg = document.getElementById('wheel');
  if (!svg) return;

  const SIZE = 520, C = SIZE / 2;
  const OUTER = 172, CENTER = 56, GAP = 2, KNOB_GAP = 1.5, CORNER = 6, DIAL_RIM = 5;
  const GEAR_OFFSET = 26, GEAR_HIT = 14, RUNNING_DOT = 82, ICON_R = (CENTER + OUTER) / 2 + 4, ICON = 44;
  const PILL_W = 58, PILL_H = 26, MAX_TOOLS = 12;
  const TAU = Math.PI * 2, DETENT = TAU / 36; // 10° per click
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;

  /** Point `r` out from the centre at `angle` (radians, clockwise from 12 o'clock). */
  const at = (angle, r) => [C + Math.sin(angle) * r, C - Math.cos(angle) * r];
  /** The short way round: like .NET's Math.IEEERemainder(a, 2π). */
  const wrap = (a) => a - TAU * Math.round(a / TAU);
  const f2 = (n) => n.toFixed(2);

  /* ----- Apps (generic icons drawn here; coordinates span -22…22) ----- */

  const today = new Date();
  const APPS = {
    browser: { name: 'Browser', c: ['#4DB2FF', '#1660E0'], g:
      '<g fill="none" stroke="#fff" stroke-width="2"><circle r="12"/><ellipse rx="5.2" ry="12"/><path d="M-12 0H12M-10.4-6h20.8M-10.4 6h20.8" stroke-width="1.6"/></g>' },
    mail: { name: 'Mail', c: ['#6AD0FF', '#1E7FE0'], g:
      '<rect x="-13" y="-9" width="26" height="18" rx="3" fill="#fff"/><path d="M-11.5-7 0 2l11.5-9" fill="none" stroke="#1E7FE0" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>' },
    notes: { name: 'Notes', c: ['#FFE680', '#F5C02C'], g:
      '<path d="M-11-6h22M-11 0h22M-11 6h14" stroke="rgba(92,62,0,.55)" stroke-width="2.4" stroke-linecap="round"/>' },
    terminal: { name: 'Terminal', c: ['#3A3A3C', '#151517'], g:
      '<path d="M-11-6-5 0l-6 6" fill="none" stroke="#5BE37D" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/><path d="M-1 7h10" stroke="#fff" stroke-width="2.4" stroke-linecap="round"/>' },
    music: { name: 'Music', c: ['#FF7088', '#EE2A4C'], g:
      '<path d="M-3.5 9V-8.5L10-12V6" fill="none" stroke="#fff" stroke-width="2.4" stroke-linejoin="round"/><circle cx="-7" cy="9" r="3.6" fill="#fff"/><circle cx="6.5" cy="6" r="3.6" fill="#fff"/>' },
    photos: { name: 'Photos', c: ['#FFB43B', '#FF5A6A'], g:
      '<circle cx="-6" cy="-6" r="4" fill="#fff"/><path d="M-14 12-5 1l6 6 5-5 8 10z" fill="#fff"/>' },
    calendar: { name: 'Calendar', c: ['#FFFFFF', '#ECECEC'], g:
      `<text y="-9" text-anchor="middle" font-size="7.5" font-weight="700" fill="#FF3B30" letter-spacing=".5">${today.toLocaleDateString('en', { weekday: 'short' }).toUpperCase()}</text>` +
      `<text y="12" text-anchor="middle" font-size="20" font-weight="500" fill="#1C1C1E">${today.getDate()}</text>` },
    calculator: { name: 'Calculator', c: ['#707074', '#3A3A3C'], g:
      '<rect x="-12" y="-14" width="24" height="7" rx="1.5" fill="#CFD8CF"/>' +
      [-4, 3, 10].map((y) => [-12, -3, 6].map((x, i) =>
        `<rect x="${x}" y="${y}" width="6" height="5" rx="1.3" fill="${i === 2 ? '#FF9500' : '#D8D8DC'}"/>`).join('')).join('') },
    maps: { name: 'Maps', c: ['#86DF5E', '#2FA24B'], g:
      '<path d="M0 13S-9 3-9-3a9 9 0 0 1 18 0c0 6-9 16-9 16z" fill="#fff"/><circle cy="-3" r="3.3" fill="#2FA24B"/>' },
    code: { name: 'Code', c: ['#5B84FF', '#2A47D0'], g:
      '<path d="M-6-8-13 0l7 8M6-8l7 8-7 8M3-10-3 10" fill="none" stroke="#fff" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>' },
    chat: { name: 'Chat', c: ['#B48CFF', '#6E3BEA'], g:
      '<path d="M-12-9h24a3 3 0 0 1 3 3V6a3 3 0 0 1-3 3H-3l-7 6V9h-2a3 3 0 0 1-3-3V-6a3 3 0 0 1 3-3z" fill="#fff"/>' },
    files: { name: 'Files', c: ['#7FC8FF', '#2F86EA'], g:
      '<path d="M-14-9a2 2 0 0 1 2-2h8l3 4h13a2 2 0 0 1 2 2V8a2 2 0 0 1-2 2h-24a2 2 0 0 1-2-2z" fill="#fff"/>' },
    camera: { name: 'Camera', c: ['#909096', '#46464A'], g:
      '<rect x="-5" y="-12" width="10" height="5" rx="1.5" fill="#fff"/><rect x="-14" y="-8" width="28" height="18" rx="4" fill="#fff"/><circle cy="1" r="6" fill="#46464A"/><circle cy="1" r="3" fill="#909096"/>' },
    weather: { name: 'Weather', c: ['#56C6FA', '#1D79D6'], g:
      '<circle cx="-5" cy="-5" r="6" fill="#FFD54A"/><g fill="#fff"><circle cx="-4" cy="5" r="5"/><circle cx="4" cy="1" r="7"/><circle cx="11" cy="6" r="4"/><rect x="-4" y="5" width="15" height="5"/></g>' },
    design: { name: 'Design', c: ['#2E2E30', '#141416'], g:
      '<circle cx="-5.5" cy="-5" r="6" fill="#FF7262"/><rect x="1" y="-11" width="11" height="11" rx="2" fill="#A259FF"/><path d="M-1 13 5.5 2 12 13z" fill="#1ABCFE"/>' },
    podcasts: { name: 'Podcasts', c: ['#CF86FF', '#8A33DE'], g:
      '<g fill="none" stroke="#fff" stroke-width="2.2" stroke-linecap="round"><path d="M-6.5 3.5a9.2 9.2 0 1 1 13 0M-9.9 6.9a14 14 0 1 1 19.8 0"/><path d="M0 3v10" stroke-width="3"/></g><circle cy="-3" r="3.2" fill="#fff"/>' },
  };
  const DEFAULT = ['files', 'calculator', 'chat', 'design', 'code', 'music', 'browser', 'mail'];
  const RUNNING = ['files', 'chat', 'design', 'code', 'browser'];

  // Each icon's tile gradient, defined once for the page.
  const defs = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  defs.setAttribute('width', '0');
  defs.setAttribute('height', '0');
  defs.setAttribute('aria-hidden', 'true');
  defs.style.position = 'absolute';
  defs.innerHTML = '<defs>' + Object.entries(APPS).map(([key, app]) =>
    `<linearGradient id="ig-${key}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${app.c[0]}"/><stop offset="1" stop-color="${app.c[1]}"/></linearGradient>`).join('') + '</defs>';
  document.body.prepend(defs);

  /** Rounded tile, glyph and a hairline outline (no shadow, as in the apps). */
  const iconMarkup = (key) =>
    `<rect x="-22" y="-22" width="44" height="44" rx="10" fill="url(#ig-${key})"/>${APPS[key].g}` +
    '<rect x="-21.75" y="-21.75" width="43.5" height="43.5" rx="9.8" fill="none" stroke="rgba(0,0,0,.14)" stroke-width=".5"/>';

  /* ----- Static parts of the wheel ----- */

  const ticks = Array.from({ length: 72 }, (_, i) => { // engraved scale: a tick every 5°, a longer one every 30°
    const a = i * TAU / 72, major = i % 6 === 0, r0 = CENTER - DIAL_RIM - 3, r1 = r0 - (major ? 5 : 3);
    const [x1, y1] = at(a, r0), [x2, y2] = at(a, r1);
    return `<line x1="${f2(x1)}" y1="${f2(y1)}" x2="${f2(x2)}" y2="${f2(y2)}" stroke="rgba(0,0,0,${major ? 0.16 : 0.09})" stroke-width="${major ? 0.9 : 0.6}"/>`;
  }).join('');

  const gear = '<circle r="4.3" fill="none" stroke="currentColor" stroke-width="2.3"/>' +
    Array.from({ length: 8 }, (_, k) => `<rect x="-1.3" y="-7" width="2.6" height="3" rx=".5" fill="currentColor" transform="rotate(${k * 45})"/>`).join('');

  svg.innerHTML = `
    <defs>
      <linearGradient id="plastic" gradientUnits="userSpaceOnUse" x1="0" y1="${C - 200}" x2="0" y2="${C + 200}">
        <stop offset="0" stop-color="#fff"/><stop offset="1" stop-color="#eee"/>
      </linearGradient>
      <pattern id="grille" width="7" height="7" patternUnits="userSpaceOnUse"><circle cx="3.5" cy="3.5" r=".7" fill="rgba(0,0,0,.06)"/></pattern>
      <linearGradient id="knobRing" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff"/><stop offset="1" stop-color="#e0e0e0"/></linearGradient>
      <linearGradient id="knobFace" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fefefe"/><stop offset="1" stop-color="#ededed"/></linearGradient>
      <linearGradient id="knobEdge" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#d1d1d1"/><stop offset="1" stop-color="#fff"/></linearGradient>
      <filter id="bodyShadow" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="5" stdDeviation="4.7" flood-color="#000" flood-opacity=".16"/></filter>
      <filter id="liftShadow" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="5" stdDeviation="5.3" flood-color="#000" flood-opacity=".18"/></filter>
      <filter id="knobShadow" x="-50%" y="-50%" width="200%" height="200%"><feDropShadow dx="0" dy="7" stdDeviation="6" flood-color="#334047" flood-opacity=".42"/></filter>
      <filter id="glow" filterUnits="userSpaceOnUse" x="${C - 12}" y="${C - CENTER - 10}" width="24" height="30"><feDropShadow dx="0" dy="0" stdDeviation="1.7" flood-color="#FF3C00" flood-opacity=".6"/></filter>
    </defs>
    <g id="slices" filter="url(#bodyShadow)"></g>
    <g id="dotsLayer"></g>
    <g id="facesLayer"></g>
    <g id="knob">
      <circle cx="${C}" cy="${C}" r="${CENTER}" fill="url(#knobRing)" filter="url(#knobShadow)"/>
      <circle cx="${C}" cy="${C}" r="${CENTER - DIAL_RIM}" fill="url(#knobFace)" stroke="url(#knobEdge)" stroke-width=".75"/>
      <g id="turning">${ticks}<rect id="pointer" x="${C - 1.25}" y="${C - CENTER + 1.5}" width="2.5" height="9" rx="1.25" fill="#FF3C00" filter="url(#glow)"/></g>
      <text id="label" x="${C}" y="${C - 4}" text-anchor="middle" dominant-baseline="middle"></text>
      <g id="gear" transform="translate(${C} ${C + GEAR_OFFSET})"><circle r="${GEAR_HIT}" fill="transparent"/>${gear}</g>
      <text id="removeText" x="${C}" y="${C + GEAR_OFFSET}" text-anchor="middle" dominant-baseline="middle">Remove</text>
    </g>
    <g id="pillsLayer"></g>`;

  const $ = (id) => svg.querySelector('#' + id);
  const slicesLayer = $('slices'), dotsLayer = $('dotsLayer'), facesLayer = $('facesLayer'), pillsLayer = $('pillsLayer');
  const knobEl = $('knob'), turning = $('turning'), labelEl = $('label'), gearEl = $('gear');
  const stage = document.getElementById('stage');
  const list = document.getElementById('list');
  const search = document.getElementById('search');
  const countEl = document.getElementById('count');
  const statusEl = document.getElementById('status');

  const el = (tag, attrs = {}) => {
    const node = document.createElementNS('http://www.w3.org/2000/svg', tag);
    for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
    return node;
  };

  /* ----- Tweens (one rAF loop for everything that moves) ----- */

  /** WPF's BackEase (EaseOut) with the given amplitude, so overshoots match the Windows app. */
  const backOut = (amp) => (t) => { const u = 1 - t; return 1 - (u * u * u - u * amp * Math.sin(Math.PI * u)); };
  const cubicOut = (t) => 1 - (1 - t) ** 3;

  const moving = new Set();
  let frameId = 0;

  class Value {
    constructor(v) { this.v = v; }
    to(target, ms, ease) {
      if (reduceMotion) return this.set(target);
      Object.assign(this, { a: this.v, b: target, t0: performance.now(), ms, ease });
      moving.add(this);
      kick();
    }
    set(v) { this.v = v; moving.delete(this); kick(); }
    step(now) {
      const t = Math.min(1, (now - this.t0) / this.ms);
      this.v = this.a + (this.b - this.a) * this.ease(t);
      if (t >= 1) moving.delete(this);
    }
  }

  function kick() { if (!frameId) frameId = requestAnimationFrame(frame); }
  function frame(now) {
    frameId = 0;
    for (const v of [...moving]) v.step(now);
    render();
    if (moving.size) kick();
  }

  /* ----- Detents: no sound on the site, just a tiny buzz on phones that support it ----- */

  let lastDetent = 0, touching = false;

  function detent() {
    const now = performance.now();
    if (now - lastDetent < 50) return; // at most one per 50 ms, like the apps' click
    lastDetent = now;
    if (touching && navigator.vibrate) navigator.vibrate(4);
  }

  /* ----- State ----- */

  let tools = [];
  const faces = new Map(); // app key -> { icon, dot, pill, angle, lift, scale, liftTarget }
  let slices = []; // per position: { g, lift, target }
  let hovered = null, trashHovered = null, gearHovered = false, pressed = null;
  let knobAngle = 0; // unwrapped, so it always turns the short way round
  const knob = new Value(0);
  let drag = null;

  const indexOf = (key) => tools.findIndex((t) => t.key === key);
  const stepAngle = () => TAU / Math.max(tools.length, 1);

  /* ----- Geometry ----- */

  function arc(r, from, to) {
    const n = Math.max(8, Math.ceil(Math.abs(to - from) / (TAU / 180)));
    return Array.from({ length: n + 1 }, (_, i) => at(from + (to - from) * i / n, r));
  }

  /** Ring segment whose edges run parallel to its radial lines, so every gap is the same width. Drawn shrunk
   *  by CORNER; a round-joined stroke of twice that grows it back with rounded corners. */
  function wedge(start, end) {
    const inner = CENTER + KNOB_GAP + CORNER, outer = OUTER - CORNER, half = GAP / 2 + CORNER;
    const points = arc(outer, start + Math.asin(half / outer), end - Math.asin(half / outer))
      .concat(arc(inner, end - Math.asin(half / inner), start + Math.asin(half / inner)));
    return 'M' + points.map(([x, y]) => f2(x) + ' ' + f2(y)).join('L') + 'Z';
  }

  /** Slice under an offset from the centre, or null over the knob or beyond the rim. */
  function sliceAt(dx, dy, ring = true) {
    const n = tools.length, d = Math.hypot(dx, dy);
    if (!n || (ring && (d <= CENTER || d > OUTER))) return null;
    let a = Math.atan2(dx, -dy);
    if (a < 0) a += TAU;
    return Math.round(a / stepAngle()) % n;
  }

  /* ----- Building ----- */

  function buildSlices(n) {
    slicesLayer.replaceChildren();
    slices = Array.from({ length: n }, (_, i) => {
      const step = TAU / n, d = wedge(i * step - step / 2, i * step + step / 2);
      const g = el('g', { class: 'slice' });
      g.append(
        el('path', { d, fill: 'url(#plastic)', stroke: 'url(#plastic)', 'stroke-width': CORNER * 2, 'stroke-linejoin': 'round' }),
        el('path', { d, fill: 'url(#grille)' }),
      );
      slicesLayer.append(g);
      return { g, lift: new Value(0), target: 0 };
    });
  }

  function makeFace(key) {
    const icon = el('g', { class: 'face' });
    icon.innerHTML = iconMarkup(key);
    const dot = el('circle', { class: 'run-dot', r: 1.75 });
    const pill = el('g', { class: 'pill' });
    pill.innerHTML =
      `<rect class="pill-bg" x="${-PILL_W / 2}" y="${-PILL_H / 2}" width="${PILL_W}" height="${PILL_H}" rx="${PILL_H / 2}"/>` +
      `<g class="pill-trash" data-act="trash" data-key="${key}"><title>Remove ${APPS[key].name}</title>` +
        `<rect x="${-PILL_W / 2}" y="${-PILL_H / 2}" width="${PILL_W / 2}" height="${PILL_H}" fill="transparent"/>` +
        '<path transform="translate(-12.5 0.5)" d="M-4.5-3.5h9M-1.8-3.5v-1.3a.8.8 0 0 1 .8-.8h2a.8.8 0 0 1 .8.8v1.3M-3.4-3.5l.5 7.6a1 1 0 0 0 1 .9h3.8a1 1 0 0 0 1-.9l.5-7.6" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></g>' +
      `<g class="pill-grip" data-act="grip" data-key="${key}"><title>Drag to reorder</title>` +
        `<rect x="0" y="${-PILL_H / 2}" width="${PILL_W / 2}" height="${PILL_H}" fill="transparent"/>` +
        [-4, 0, 4].map((y) => [10.5, 15.5].map((x) => `<circle cx="${x}" cy="${y}" r="1.15" fill="currentColor"/>`).join('')).join('') +
      '</g>';
    facesLayer.append(icon);
    dotsLayer.append(dot);
    pillsLayer.append(pill);
    return { icon, dot, pill, angle: new Value(0), lift: new Value(0), scale: new Value(1), liftTarget: 0 };
  }

  const fadeIn = (node) => {
    node.style.opacity = '0';
    requestAnimationFrame(() => requestAnimationFrame(() => { node.style.opacity = ''; }));
  };

  /** Lays out `tools`. With `glide`, icons already on the wheel travel along the arc to their new places. */
  function layout(glide) {
    const n = tools.length, step = stepAngle();
    if (slices.length !== n) buildSlices(n);
    const keep = new Set();
    tools.forEach((tool, i) => {
      keep.add(tool.key);
      let face = faces.get(tool.key);
      if (!face) {
        face = makeFace(tool.key);
        faces.set(tool.key, face);
        face.angle.set(i * step);
        if (glide) {
          face.scale.set(0.5);
          face.scale.to(1, 280, backOut(0.6));
          [face.icon, face.pill].forEach(fadeIn);
        }
      } else if (!(drag && drag.moved && drag.key === tool.key)) {
        const target = face.angle.v + wrap(i * step - face.angle.v);
        if (glide) face.angle.to(target, 350, backOut(0.25));
        else face.angle.set(target);
      }
      face.dot.classList.toggle('on', tool.running);
    });
    for (const [key, face] of faces) {
      if (keep.has(key)) continue;
      faces.delete(key);
      [face.icon, face.dot, face.pill].forEach((node) => {
        node.classList.add('gone');
        node.style.pointerEvents = 'none';
        setTimeout(() => node.remove(), 220);
      });
    }
    restyle();
    renderList();
  }

  /* ----- Styling for the current hover / drag state ----- */

  function restyle() {
    const lifted = trashHovered ?? hovered;
    slices.forEach((s, i) => {
      const on = lifted === i;
      const target = on ? (pressed === i ? -1 : 3) : 0; // hovered slices nudge outward; pressed ones sink
      if (s.target !== target) { s.target = target; s.lift.to(target, 160, backOut(0.4)); }
      if (on && !s.g.classList.contains('on')) slicesLayer.append(s.g); // lifted slice on top
      s.g.classList.toggle('on', on);
    });
    tools.forEach((tool, i) => {
      const face = faces.get(tool.key);
      const dragged = drag && drag.moved && drag.key === tool.key;
      const target = dragged ? 10 : lifted === i ? (pressed === i ? -1 : 3) : 0;
      if (face.liftTarget !== target) { face.liftTarget = target; face.lift.to(target, 160, backOut(0.4)); }
      face.pill.classList.toggle('active', dragged || (!drag && lifted === i));
      face.pill.classList.toggle('trash', trashHovered === i);
    });
    knobEl.classList.toggle('active', lifted != null);
    knobEl.classList.toggle('trash', trashHovered != null);
    gearEl.classList.toggle('hover', gearHovered);
    labelEl.textContent = gearHovered ? 'Settings'
      : lifted != null ? APPS[tools[lifted].key].name
      : tools.length ? '' : 'Add an app';
    svg.style.cursor = drag && drag.moved ? 'grabbing' : hovered != null || gearHovered ? 'pointer' : '';
    kick();
  }

  function render() {
    const step = stepAngle();
    slices.forEach((s, i) => {
      s.g.setAttribute('transform', `translate(${f2(Math.sin(i * step) * s.lift.v)} ${f2(-Math.cos(i * step) * s.lift.v)})`);
    });
    for (const face of faces.values()) {
      const a = face.angle.v, lift = face.lift.v;
      const [x, y] = at(a, ICON_R + lift);
      face.icon.setAttribute('transform', `translate(${f2(x)} ${f2(y)}) scale(${face.scale.v.toFixed(3)})`);
      const [dx, dy] = at(a, RUNNING_DOT + lift);
      face.dot.setAttribute('cx', f2(dx));
      face.dot.setAttribute('cy', f2(dy));
      // Pills are wider than tall, so at 3 and 9 o'clock they sit further out to keep the same clearance.
      const [px, py] = at(a, OUTER + 10 + PILL_H / 2 + Math.abs(Math.sin(a)) * (PILL_W - PILL_H) / 2);
      face.pill.setAttribute('transform', `translate(${f2(px)} ${f2(py)})`);
    }
    turning.setAttribute('transform', `rotate(${(knob.v * 180 / Math.PI).toFixed(2)} ${C} ${C})`);
  }

  /* ----- Knob ----- */

  /** Cursor offset from the centre. Turns the knob toward it in 10° detents. */
  function track(dx, dy) {
    const h = sliceAt(dx, dy);
    const g = Math.hypot(dx, dy - GEAR_OFFSET) < GEAR_HIT;
    if (h !== hovered || g !== gearHovered) { hovered = h; gearHovered = g; restyle(); }
    if (h == null) return;
    const toward = knobAngle + wrap(Math.atan2(dx, -dy) - knobAngle);
    const snapped = Math.round(toward / DETENT) * DETENT;
    if (Math.abs(snapped - knobAngle) <= DETENT / 2) return;
    knobAngle = snapped;
    knob.set(knobAngle); // no animation: each detent lands at once
    detent();
  }

  /** Turns the knob to face slice `i`, the short way round (when there's no cursor on the ring to follow). */
  function pointAt(i) {
    knobAngle += wrap(i * stepAngle() - knobAngle);
    knob.to(knobAngle, 250, cubicOut);
  }

  /* ----- Actions ----- */

  let statusTimer = 0;
  function say(text) {
    statusEl.textContent = text;
    clearTimeout(statusTimer);
    statusTimer = setTimeout(() => { statusEl.textContent = ''; }, 2600);
  }

  function open(i) {
    const tool = tools[i];
    if (!tool) return;
    const wasRunning = tool.running;
    tool.running = true;
    faces.get(tool.key).dot.classList.add('on');
    say(wasRunning ? `${APPS[tool.key].name} brought forward` : `Opened ${APPS[tool.key].name}`);
  }

  function add(key) {
    if (tools.length >= MAX_TOOLS) return say(`The wheel holds up to ${MAX_TOOLS} apps here`);
    tools.push({ key, running: false });
    hovered = trashHovered = null;
    layout(true);
    pointAt(tools.length - 1);
    say(`Added ${APPS[key].name}`);
  }

  function remove(key) {
    const i = indexOf(key);
    if (i < 0) return;
    tools.splice(i, 1);
    hovered = trashHovered = null;
    layout(true);
    say(`Removed ${APPS[key].name}`);
  }

  /* ----- Pointer ----- */

  function local(e) {
    const r = svg.getBoundingClientRect(), s = SIZE / r.width;
    return [(e.clientX - r.left) * s - C, (e.clientY - r.top) * s - C];
  }

  svg.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    touching = e.pointerType !== 'mouse';
    const [dx, dy] = local(e);
    const act = e.target.closest('[data-act]');
    if (act && act.dataset.act === 'trash') return; // handled on click
    let mode = null, key = null;
    if (act && act.dataset.act === 'grip') {
      mode = 'grip';
      key = act.dataset.key;
      hovered = indexOf(key);
      pointAt(hovered);
    } else if (Math.hypot(dx, dy - GEAR_OFFSET) < GEAR_HIT) {
      search.focus({ preventScroll: true });
      return;
    } else if (Math.hypot(dx, dy) <= CENTER) {
      mode = 'scrub'; // press the knob and slide out to a slice, then let go to open it
    } else {
      const i = sliceAt(dx, dy);
      if (i == null) return;
      mode = 'slice';
      key = tools[i].key;
      hovered = pressed = i;
      track(dx, dy);
    }
    e.preventDefault();
    drag = { mode, key, x0: e.clientX, y0: e.clientY, moved: false };
    svg.setPointerCapture(e.pointerId);
    restyle();
  });

  svg.addEventListener('pointermove', (e) => {
    const [dx, dy] = local(e);
    if (drag && drag.mode !== 'scrub') {
      if (!drag.moved && Math.hypot(e.clientX - drag.x0, e.clientY - drag.y0) > 5) {
        drag.moved = true;
        pressed = null;
        svg.classList.add('dragging');
      }
      if (drag.moved) dragTo(dx, dy);
      return;
    }
    if (!drag && e.pointerType !== 'mouse') return;
    const trash = e.target.closest && e.target.closest('[data-act="trash"]');
    const t = trash ? indexOf(trash.dataset.key) : null;
    if (t !== trashHovered) {
      trashHovered = t;
      if (t != null) pointAt(t);
      restyle();
    }
    if (t == null) track(dx, dy);
    else if (hovered != null || gearHovered) { hovered = null; gearHovered = false; restyle(); }
  });

  /** The dragged icon follows the pointer round the wheel; the others glide out of its way. */
  function dragTo(dx, dy) {
    const face = faces.get(drag.key);
    face.angle.set(face.angle.v + wrap(Math.atan2(dx, -dy) - face.angle.v));
    const target = sliceAt(dx, dy, false), from = indexOf(drag.key);
    if (target != null && target !== from) {
      tools.splice(target, 0, ...tools.splice(from, 1));
      hovered = target;
      layout(true);
      pointAt(target);
      detent();
    } else if (hovered !== from) {
      hovered = from;
      restyle();
    }
  }

  function endDrag(e, cancelled) {
    if (!drag) return;
    const d = drag;
    drag = null;
    pressed = null;
    svg.classList.remove('dragging');
    if (d.moved) {
      layout(true); // the dragged icon glides into its slot
      if (!cancelled) say(`Moved ${APPS[d.key].name}`);
    } else if (!cancelled && d.mode === 'slice' && hovered != null) {
      open(hovered);
    } else if (!cancelled && d.mode === 'scrub' && hovered != null) {
      open(hovered);
    }
    if (e.pointerType === 'mouse' && !cancelled) {
      hovered = null;
      const [dx, dy] = local(e);
      track(dx, dy);
    } else {
      hovered = trashHovered = null;
      gearHovered = false;
    }
    restyle();
  }

  svg.addEventListener('pointerup', (e) => endDrag(e, false));
  svg.addEventListener('pointercancel', (e) => endDrag(e, true));
  svg.addEventListener('pointerleave', () => {
    if (drag) return;
    hovered = trashHovered = null;
    gearHovered = false;
    restyle();
  });
  svg.addEventListener('click', (e) => {
    const trash = e.target.closest('[data-act="trash"]');
    if (trash) remove(trash.dataset.key);
  });

  /* ----- App list ----- */

  const sorted = Object.keys(APPS).sort((a, b) => APPS[a].name.localeCompare(APPS[b].name));

  function renderList() {
    const q = search.value.trim().toLowerCase();
    const focused = document.activeElement && document.activeElement.dataset && document.activeElement.dataset.key;
    const full = tools.length >= MAX_TOOLS;
    const items = sorted.filter((key) => !q || APPS[key].name.toLowerCase().includes(q));
    list.replaceChildren(...items.map((key) => {
      const on = indexOf(key) >= 0;
      const li = document.createElement('li');
      const row = document.createElement('button');
      row.type = 'button';
      row.className = 'row';
      row.dataset.key = key;
      row.setAttribute('aria-pressed', String(on));
      row.disabled = full && !on;
      row.innerHTML = `<svg viewBox="-22 -22 44 44" aria-hidden="true">${iconMarkup(key)}</svg>` +
        `<span>${APPS[key].name}</span><span class="state">${on ? 'On wheel' : 'Add'}</span>`;
      li.append(row);
      return li;
    }));
    if (!items.length) {
      const li = document.createElement('li');
      li.className = 'empty';
      li.textContent = 'No apps match.';
      list.append(li);
    }
    if (focused) list.querySelector(`[data-key="${focused}"]`)?.focus();
    countEl.textContent = `${tools.length} / ${MAX_TOOLS}`;
  }

  list.addEventListener('click', (e) => {
    const row = e.target.closest('.row');
    if (!row || row.disabled) return;
    if (indexOf(row.dataset.key) >= 0) remove(row.dataset.key);
    else add(row.dataset.key);
  });
  search.addEventListener('input', renderList);

  function reset() {
    tools = DEFAULT.map((key) => ({ key, running: RUNNING.includes(key) }));
    hovered = trashHovered = null;
    layout(true);
  }
  document.getElementById('reset').addEventListener('click', () => { reset(); say('Back to the default wheel'); });

  /* ----- Start: build, then open the wheel when it scrolls into view ----- */

  reset();
  render();
  if (reduceMotion || !('IntersectionObserver' in window)) stage.classList.add('open');
  else {
    const io = new IntersectionObserver((entries) => {
      if (!entries[0].isIntersecting) return;
      stage.classList.add('open');
      io.disconnect();
    }, { threshold: 0.35 });
    io.observe(stage);
  }
})();
