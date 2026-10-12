// Pulse — visual components. Every value change goes through a spring or a CSS spring
// transition, so nothing ever jumps.
'use strict';

const SVGNS = 'http://www.w3.org/2000/svg';
const sv = (tag, attrs, parent) => {
  const e = document.createElementNS(SVGNS, tag);
  for (const k in attrs || {}) e.setAttribute(k, attrs[k]);
  if (parent) parent.appendChild(e);
  return e;
};
let vizUid = 0;
const clamp01 = v => Math.max(0, Math.min(1, v || 0));

/** Concentric activity rings (outer = load, inner = temperature or a second metric). */
class RingViz {
  constructor(rings) {
    this.el = h('div', 'viz ring');
    const svg = sv('svg', { viewBox: '0 0 100 100' }, this.el);
    const defs = sv('defs', {}, svg);
    this.rings = [];
    let r = 50 - rings[0].width / 2 - 2;
    rings.forEach((ring, i) => {
      const id = 'rg' + (++vizUid);
      const grad = sv('linearGradient', { id, x1: '0', y1: '0', x2: '1', y2: '1' }, defs);
      const s1 = sv('stop', { offset: '0' }, grad), s2 = sv('stop', { offset: '1' }, grad);
      const C = 2 * Math.PI * r;
      const trk = sv('circle', { cx: 50, cy: 50, r, class: 'trk', 'stroke-width': ring.width }, svg);
      const val = sv('circle', {
        cx: 50, cy: 50, r, class: 'val', 'stroke-width': ring.width, stroke: `url(#${id})`,
        'stroke-dasharray': `${C} ${C}`, 'stroke-dashoffset': C, transform: 'rotate(-90 50 50)',
      }, svg);
      const st = { r, C, val, trk, s1, s2 };
      st.spring = new Spring(0, v => val.setAttribute('stroke-dashoffset', C * (1 - Math.max(0.0001, Math.min(1, v)))), { k: 90, c: 17 });
      this.rings.push(st);
      this.color(i, ring.color, ring.color2);
      if (i < rings.length - 1) r -= ring.width / 2 + (ring.gap ?? 2.6) + rings[i + 1].width / 2;
    });
    this.center = h('div', 'ring-center');
    this.el.appendChild(this.center);
  }
  set(i, frac) { if (this.rings[i] && P.ok(frac)) this.rings[i].spring.set(clamp01(frac)); }
  color(i, c1, c2) {
    const r = this.rings[i];
    if (!r || (r.c1 === c1 && r.c2 === c2)) return;
    r.c1 = c1; r.c2 = c2;
    r.s1.setAttribute('stop-color', c1);
    r.s2.setAttribute('stop-color', c2 || c1);
  }
  dim(i, hide) {
    const r = this.rings[i];
    if (!r || r.hidden === hide) return;
    r.hidden = hide;
    r.val.style.opacity = r.trk.style.opacity = hide ? 0 : '';
  }
}

/** 240° gauge with tick marks and a glowing indicator. */
class GaugeViz {
  constructor(color) {
    this.el = h('div', 'viz gauge');
    const svg = sv('svg', { viewBox: '0 0 100 100' }, this.el);
    const defs = sv('defs', {}, svg);
    const id = 'gg' + (++vizUid);
    const grad = sv('linearGradient', { id, x1: '0', y1: '1', x2: '1', y2: '0' }, defs);
    this.s1 = sv('stop', { offset: '0' }, grad);
    this.s2 = sv('stop', { offset: '1' }, grad);
    const r = 40, C = 2 * Math.PI * r, L = C * 240 / 360;
    const rot = 'rotate(150 50 50)';
    sv('circle', { cx: 50, cy: 50, r, class: 'trk', 'stroke-width': 7, 'stroke-dasharray': `${L} ${C}`, transform: rot }, svg);
    this.val = sv('circle', { cx: 50, cy: 50, r, class: 'val', 'stroke-width': 7, stroke: `url(#${id})`, 'stroke-dasharray': `0 ${C}`, transform: rot }, svg);
    const ticks = sv('g', { class: 'ticks' }, svg);
    for (let i = 0; i <= 24; i++) {
      const a = (150 + i * 10) * Math.PI / 180, major = i % 6 === 0;
      const r1 = 47.5, r2 = major ? 44.5 : 45.8;
      sv('line', { x1: 50 + r1 * Math.cos(a), y1: 50 + r1 * Math.sin(a), x2: 50 + r2 * Math.cos(a), y2: 50 + r2 * Math.sin(a), class: major ? 'major' : '' }, ticks);
    }
    this.knob = sv('g', {}, svg);
    sv('circle', { cx: 50 + r, cy: 50, r: 5.2, class: 'knob-halo' }, this.knob);
    sv('circle', { cx: 50 + r, cy: 50, r: 3.4, class: 'knob' }, this.knob);
    this.spring = new Spring(0, v => {
      v = clamp01(v);
      this.val.setAttribute('stroke-dasharray', `${Math.max(0.01, L * v)} ${C}`);
      this.knob.setAttribute('transform', `rotate(${150 + 240 * v} 50 50)`);
    }, { k: 80, c: 15 });
    this.spring.jump(0);
    this.center = h('div', 'gauge-center');
    this.foot = h('div', 'gauge-foot');
    this.el.append(this.center, this.foot);
    this.color(color);
  }
  set(frac) { if (P.ok(frac)) this.spring.set(clamp01(frac)); }
  /** Thin inner arc for a secondary load (GPU video memory). */
  setAux(frac, color) {
    if (!P.ok(frac)) return;
    if (!this.aux) {
      const svg = this.el.querySelector('svg');
      const r = 32.5, C = 2 * Math.PI * r, L = C * 240 / 360, rot = 'rotate(150 50 50)';
      sv('circle', { cx: 50, cy: 50, r, class: 'trk', 'stroke-width': 3.4, 'stroke-dasharray': `${L} ${C}`, transform: rot }, svg);
      const arc = sv('circle', { cx: 50, cy: 50, r, class: 'val', 'stroke-width': 3.4, stroke: color, 'stroke-dasharray': `0 ${C}`, transform: rot }, svg);
      this.aux = new Spring(0, v => arc.setAttribute('stroke-dasharray', `${Math.max(0.01, L * clamp01(v))} ${C}`), { k: 80, c: 15 });
    }
    this.aux.set(clamp01(frac));
  }
  color(c1, c2) {
    if (this.c1 === c1) return;
    this.c1 = c1;
    this.s1.setAttribute('stop-color', c1);
    this.s2.setAttribute('stop-color', c2 || P.lighten(c1, .45));
    this.el.style.setProperty('--glow', c1);
  }
}

/**
 * Donut with springy segments. Segments are keyed, so a process that climbs the list grows
 * in place instead of the whole chart redrawing.
 */
class DonutViz {
  constructor(thickness = 13) {
    this.el = h('div', 'viz donut');
    this.svg = sv('svg', { viewBox: '0 0 100 100' }, this.el);
    this.r = 50 - thickness / 2 - 1.5;
    this.C = 2 * Math.PI * this.r;
    this.th = thickness;
    sv('circle', { cx: 50, cy: 50, r: this.r, class: 'trk', 'stroke-width': thickness }, this.svg);
    this.g = sv('g', { transform: 'rotate(-90 50 50)' }, this.svg);
    this.segs = new Map();
    this.order = [];
    this.center = h('div', 'donut-center');
    this.el.appendChild(this.center);
    this.queued = false;
    this.onHover = null;
  }
  set(list) {
    const keys = list.map(s => s.key);
    for (const s of list) {
      let seg = this.segs.get(s.key);
      if (!seg) {
        const el = sv('circle', { cx: 50, cy: 50, r: this.r, class: 'seg', 'stroke-width': this.th, 'stroke-dasharray': `0 ${this.C}` }, this.g);
        el.addEventListener('pointerenter', () => { el.classList.add('hl'); if (this.onHover) this.onHover(s.key); });
        el.addEventListener('pointerleave', () => { el.classList.remove('hl'); if (this.onHover) this.onHover(null); });
        seg = { el, spring: new Spring(0, () => this.queue(), { k: 70, c: 15 }) };
        this.segs.set(s.key, seg);
      }
      seg.dead = false;
      seg.label = s.label;
      if (seg.color !== s.color) { seg.color = s.color; seg.el.setAttribute('stroke', s.color); }
      seg.spring.set(clamp01(s.v));
    }
    for (const [k, seg] of this.segs) if (!keys.includes(k)) { seg.dead = true; seg.spring.set(0); }
    // Dying segments keep their slot so neighbours slide rather than jump.
    const merged = [];
    for (const k of this.order) if (!keys.includes(k) && this.segs.has(k)) merged.push(k);
    this.order = keys.concat(merged.filter(k => !keys.includes(k)));
    this.queue();
  }
  queue() {
    if (this.queued) return;
    this.queued = true;
    requestAnimationFrame(() => { this.queued = false; this.draw(); });
  }
  draw() {
    const gap = 1.6;
    let pos = 0;
    for (const k of this.order) {
      const seg = this.segs.get(k);
      if (!seg) continue;
      const len = seg.spring.v * this.C;
      const vis = Math.max(0, len - gap);
      seg.el.setAttribute('stroke-dasharray', `${vis} ${this.C}`);
      seg.el.setAttribute('stroke-dashoffset', -(pos + gap / 2));
      pos += len;
      if (seg.dead && seg.spring.v < 0.002 && !seg.spring.running) { seg.el.remove(); this.segs.delete(k); }
    }
    this.order = this.order.filter(k => this.segs.has(k));
  }
}

/**
 * Connection quality as signal bars (0–4). The highest lit bar gives a soft pop whenever a new
 * measurement arrives, so the indicator feels alive without showing any numbers.
 */
class SignalBars {
  constructor() {
    this.el = h('span', 'viz qbars');
    this.bars = [0.35, 0.55, 0.78, 1].map(k => { const b = h('i'); b.style.setProperty('--h', k); this.el.appendChild(b); return b; });
    this.score = -1;
  }
  set(score, label) {
    score = Math.max(0, Math.min(4, score | 0));
    if (label && this.el.title !== label) this.el.title = label;
    if (score !== this.score) {
      this.score = score;
      this.el.dataset.score = score;
      this.bars.forEach((b, i) => b.classList.toggle('on', i < score));
    }
    const top = this.bars[Math.max(0, score - 1)];
    if (score > 0 && P.anim() !== 'off') top.animate([{ transform: 'scaleY(1)' }, { transform: 'scaleY(1.18)' }, { transform: 'scaleY(1)' }], { duration: 420, easing: 'cubic-bezier(.16,1,.3,1)' });
  }
}

/** Fan rotor whose spin follows the real RPM (playback rate, so speed changes never jump). */
function fanRotor(size, color) {
  const wrap = h('span', 'rotor');
  const svg = sv('svg', { viewBox: '0 0 100 100' }, wrap);
  const g = sv('g', {}, svg);
  for (let i = 0; i < 7; i++)
    sv('path', { d: 'M50 37 C57 31 63 24 66 15 C72 22 72 33 58 44 Z', fill: color, opacity: .92, transform: `rotate(${i * 360 / 7} 50 50)` }, g);
  sv('circle', { cx: 50, cy: 50, r: 10, class: 'hub' }, svg);
  if (size) wrap.style.width = wrap.style.height = size;
  // The HTML wrapper spins, not the SVG group: a transform on an HTML element runs on the
  // compositor, while an SVG transform repaints the rotor every frame.
  const anim = wrap.animate([{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }], { duration: 1000, iterations: Infinity });
  anim.playbackRate = 0;
  return {
    el: wrap,
    spin(rpm) {
      const mode = P.anim(), max = P.perf() === 'max';
      // Below max the rotor turns in 20 steps per revolution at up to ~1.2 rev/s: the page then
      // repaints only when a step changes (measured: 17.5 → 6.6 % of a core) instead of every frame.
      const easing = max ? 'linear' : 'steps(20)';
      if (anim.effect.getTiming().easing !== easing) anim.effect.updateTiming({ easing });
      const rate = !rpm || mode === 'off' || P.perf() === 'eco' ? 0 : Math.max(.12, Math.min(max ? 3.2 : 1.2, rpm / 750)) * (mode === 'reduced' ? .3 : 1);
      if (Math.abs((anim.playbackRate || 0) - rate) > .01) anim.updatePlaybackRate(rate);
    },
  };
}

/** Duty-cycle ring around a spinning rotor, with the percentage on a hub disc. */
class FanViz {
  constructor(color) {
    this.ring = new RingViz([{ width: 7, color, color2: P.lighten(color, .45) }]);
    this.el = this.ring.el;
    this.el.classList.add('fan');
    this.rotor = fanRotor(null, P.lighten(color, .15));
    this.rotor.el.classList.add('big-rotor');
    this.el.insertBefore(this.rotor.el, this.ring.center);
    this.center = this.ring.center;
  }
  set(pct, rpm) { this.ring.set(0, (pct || 0) / 100); this.rotor.spin(rpm); }
}

/** Keyed list of fans: mini rotor, name, rpm and duty with a hairline bar. */
class FanList {
  constructor(color) { this.el = h('div', 'viz fanlist'); this.color = color; this.rows = new Map(); }
  set(items) {
    const keys = items.map(i => i.key);
    for (const [k, r] of this.rows) if (!keys.includes(k)) { r.el.remove(); this.rows.delete(k); }
    for (const it of items) {
      let r = this.rows.get(it.key);
      if (!r) {
        const el = h('div', 'fr');
        const rot = fanRotor('18px', it.color || this.color);
        el.appendChild(rot.el);
        el.insertAdjacentHTML('beforeend', '<span class="nm"></span><span class="rp num"></span><b class="num"></b><i class="lb"><b></b></i>');
        r = { el, rot, nm: el.querySelector('.nm'), rp: el.querySelector('.rp'), pc: el.querySelector(':scope > b'), bar: el.querySelector('.lb b') };
        this.rows.set(it.key, r);
      }
      this.el.appendChild(r.el);
      const nmHtml = (it.brand || '') + esc(it.label) + (it.model ? `<i class="md">${esc(it.model)}</i>` : '');
      if (r.nmHtml !== nmHtml) { r.nmHtml = nmHtml; r.nm.innerHTML = nmHtml; }
      r.rp.textContent = it.rpmText;
      r.pc.textContent = it.pctText;
      r.bar.style.transform = `scaleX(${clamp01((it.pct || 0) / 100)})`;
      r.el.style.setProperty('--c', it.color || this.color);
      r.rot.spin(it.rpm);
    }
  }
}

/** Energy ledger: period totals on top, one bar per day (today highlighted), springy heights. */
class EnergyViz {
  constructor(color) {
    this.el = h('div', 'viz energy');
    this.el.style.setProperty('--c', color);
    this.head = h('div', 'en-head');
    this.bars = h('div', 'en-bars');
    this.axis = h('div', 'en-axis');
    this.el.append(this.head, this.bars, this.axis);
    this.cols = [];
    this.key = '';
  }
  set(p, tariff, currency) {
    if (!p || !p.days) return;
    const kwh = P.t('kwh'), cost = v => tariff > 0 ? ' · ' + P.fmt.num(v * tariff, 0) + ' ' + esc(currency || '') : '';
    const cell = (label, v, strong) => `<div class="${strong ? 'on' : ''}"><span>${esc(label)}</span><b>${P.fmt.num(v || 0, v >= 10 ? 1 : 2)}</b></div>`;
    const head = cell(P.t('today'), p.today, true) + cell(P.t('yesterday'), p.yesterday) + cell(P.t('week'), p.week) + cell(P.t('month'), p.month) +
      `<div class="unit">${esc(kwh)}${cost(p.month)}</div>`;
    if (this.headHtml !== head) { this.headHtml = head; this.head.innerHTML = head; }
    const days = p.days, n = days.length;
    const from = new Date(p.daysFrom + 'T00:00:00');
    if (this.cols.length !== n) {
      this.bars.textContent = '';
      this.cols = days.map(() => { const c = h('i'); c.appendChild(h('b')); this.bars.appendChild(c); return c; });
    }
    const max = Math.max(0.05, ...days);
    const loc = P.lang() === 'en' ? 'en-US' : P.lang() === 'uz' ? 'uz-Latn-UZ' : 'ru-RU';
    days.forEach((d, i) => {
      const col = this.cols[i];
      const date = new Date(from.getTime() + i * 86400000);
      col.classList.toggle('today', i === n - 1);
      col.firstChild.style.transform = `scaleY(${Math.max(.02, (d || 0) / max)})`;
      col.title = date.toLocaleDateString(loc, { day: 'numeric', month: 'short' }) + ' — ' + P.fmt.num(d || 0, 2) + ' ' + kwh + cost(d || 0);
    });
    const axisKey = p.daysFrom + P.lang();
    if (this.key !== axisKey) {
      this.key = axisKey;
      const label = i => new Date(from.getTime() + i * 86400000).toLocaleDateString(loc, { day: 'numeric', month: 'short' });
      this.axis.innerHTML = `<span>${esc(label(0))}</span><span>${esc(label(Math.floor(n / 2)))}</span><span>${esc(P.t('today'))}</span>`;
    }
  }
}

/** Horizontal meter with a springy fill. */
class MeterViz {
  constructor(color) {
    this.el = h('div', 'viz meter');
    this.fill = h('i');
    this.el.appendChild(this.fill);
    this.color(color);
  }
  set(frac) { if (P.ok(frac)) this.fill.style.transform = `scaleX(${clamp01(frac)})`; }
  color(c) { this.el.style.setProperty('--c', c); }
}

/** Per-thread load equalizer. Wraps into two rows when bars would get too thin. */
class ThreadBars {
  constructor(n, color) {
    this.el = h('div', 'viz bars');
    this.n = n;
    this.bars = [];
    for (let i = 0; i < n; i++) {
      const b = h('i');
      b.appendChild(h('b'));
      this.el.appendChild(b);
      this.bars.push(b.firstChild);
    }
    this.color(color);
    new ResizeObserver(() => this.fit()).observe(this.el);
  }
  fit() {
    const w = this.el.clientWidth;
    const rows = this.n > 8 && w / this.n < 6 ? 2 : 1;
    this.el.style.setProperty('--cols', Math.ceil(this.n / rows));
    this.el.classList.toggle('two', rows === 2);
  }
  set(values) {
    if (!values) return;
    if (values.length !== this.n) return;
    for (let i = 0; i < this.n; i++) {
      const v = clamp01(values[i] / 100);
      const b = this.bars[i];
      b.style.transform = `scaleY(${Math.max(0.035, v)})`;
      b.style.opacity = .4 + .6 * v;
      b.classList.toggle('hot', v > .9);
    }
  }
  color(c) { this.el.style.setProperty('--c', c); this.el.style.setProperty('--c2', P.lighten(c, .4)); }
}

/** Keyed list (processes, drives) whose rows glide to their new rank. */
class ListViz {
  constructor() {
    this.el = h('div', 'viz list');
    this.rows = new Map();
  }
  set(items, max) {
    const first = new Map();
    for (const [k, r] of this.rows) first.set(k, r.el.getBoundingClientRect().top);
    const keys = items.map(i => i.key);
    for (const [k, r] of this.rows) if (!keys.includes(k)) { r.el.remove(); this.rows.delete(k); }
    items.forEach(it => {
      let r = this.rows.get(it.key);
      if (!r) {
        const el = h('div', 'li');
        el.innerHTML = '<span class="ic"></span><span class="nm"></span><span class="vl num"></span><i class="lb"><b></b></i>';
        r = { el, ic: el.children[0], nm: el.children[1], vl: el.children[2], bar: el.children[3].firstChild, isNew: true };
        this.rows.set(it.key, r);
      }
      this.el.appendChild(r.el); // re-append in order
      if (r.icon !== it.icon) {
        r.icon = it.icon;
        r.ic.innerHTML = it.icon ? `<img src="${it.icon}">` : `<i style="background:${it.color || 'var(--text-3)'}"></i>`;
      }
      const nmHtml = (it.brand || '') + esc(it.label) + (it.model ? `<i class="md">${esc(it.model)}</i>` : '');
      if (r.nmHtml !== nmHtml) { r.nmHtml = nmHtml; r.nm.innerHTML = nmHtml; }
      r.vl.textContent = it.text;
      r.bar.style.transform = `scaleX(${clamp01(it.v / (max || 1))})`;
      if (it.color) r.el.style.setProperty('--c', it.color);
    });
    // FLIP: rows slide from their previous position
    if (P.anim() === 'off') return;
    for (const [k, r] of this.rows) {
      const top = r.el.getBoundingClientRect().top;
      if (r.isNew) {
        r.isNew = false;
        r.el.animate([{ opacity: 0, transform: 'translateY(6px)' }, { opacity: 1, transform: 'none' }], { duration: 450, easing: 'cubic-bezier(.16,1,.3,1)' });
      } else if (first.has(k) && Math.abs(first.get(k) - top) > 1) {
        r.el.animate([{ transform: `translateY(${first.get(k) - top}px)` }, { transform: 'none' }], { duration: 650, easing: 'cubic-bezier(.16,1,.3,1)' });
      }
    }
  }
}

/**
 * Streaming area chart on canvas. Draws against H.clock(), which advances continuously, so
 * the chart scrolls smoothly and the newest segment "grows" instead of popping in.
 */
class ChartViz {
  constructor(series, opts = {}) {
    this.series = series; // [{key, color, max, min, fill, width, dash, axis, label}]
    this.opts = opts;
    this.el = h('div', 'viz chart');
    this.canvas = h('canvas');
    this.el.appendChild(this.canvas);
    this.ctx = this.canvas.getContext('2d');
    this.w = 0; this.h = 0;
    this.ro = new ResizeObserver(() => this.resize());
    this.ro.observe(this.el);
    ChartClock.add(this);
  }
  resize() {
    const r = this.el.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    this.w = r.width; this.h = r.height;
    this.canvas.width = Math.max(1, Math.round(r.width * dpr));
    this.canvas.height = Math.max(1, Math.round(r.height * dpr));
    this.dpr = dpr;
    this.draw();
  }
  destroy() { ChartClock.remove(this); this.ro.disconnect(); }
  draw() {
    const { ctx, w, h: hh } = this;
    if (!w || !hh || !this.el.isConnected) return;
    const dpr = this.dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, hh);
    const smooth = ChartClock.smooth();
    const T = H.clock(smooth);
    const span = (this.opts.window ? this.opts.window() : (P.g().history || 120)) * 1000;
    const t0 = T - span;
    const times = H.times();
    if (!times.length) return;
    let start = times.length - 1;
    while (start > 0 && times[start - 1] >= t0 - H.interval * 2) start--;
    const padTop = this.opts.padTop ?? 6, padBot = this.opts.padBot ?? 2;
    const plotH = hh - padTop - padBot;
    const X = t => (t - t0) / span * w;

    if (this.opts.grid !== false) {
      ctx.strokeStyle = ChartClock.colors.grid;
      ctx.lineWidth = 1;
      ctx.setLineDash([2, 4]);
      for (const f of [.25, .5, .75]) {
        const y = Math.round(padTop + plotH * f) + .5;
        ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke();
      }
      ctx.setLineDash([]);
    }

    // Shared auto-scale for series that ask for it (network, disk throughput).
    let autoMax = 0;
    for (const s of this.series) if (s.max === 'auto') {
      const d = H.series(s.key);
      for (let i = start; i < d.length; i++) if (d[i] > autoMax) autoMax = d[i];
    }
    autoMax = Math.max(autoMax * 1.18, this.opts.autoFloor || 1);
    if (!this.autoMax) this.autoMax = autoMax;
    this.autoMax += (autoMax - this.autoMax) * 0.12; // ease the scale too

    for (const s0 of this.series) {
      const s = typeof s0.color === 'function' ? Object.assign({}, s0, { color: s0.color() }) : s0;
      const d = H.series(s.key);
      const max = s.max === 'auto' ? this.autoMax : typeof s.max === 'function' ? s.max() : (s.max ?? 100);
      const min = s.min ?? 0;
      const Y = v => padTop + plotH * (1 - Math.max(0, Math.min(1, (v - min) / (max - min || 1))));
      const pts = [];
      for (let i = start; i < times.length; i++) {
        if (times[i] > T) {
          // Interpolate the head exactly at T so the line grows smoothly into the next sample.
          const pv = d[i - 1], nv = d[i];
          if (i > 0 && P.ok(pv) && P.ok(nv)) {
            const k = (T - times[i - 1]) / (times[i] - times[i - 1] || 1);
            pts.push([X(T), Y(pv + (nv - pv) * k)]);
          }
          break;
        }
        if (P.ok(d[i])) pts.push([X(times[i]), Y(d[i])]);
        else if (pts.length) { this.path(pts, s, hh); pts.length = 0; }
      }
      this.path(pts, s, hh);
    }
  }
  path(pts, s, hh) {
    if (pts.length < 2) return;
    const ctx = this.ctx;
    const trace = () => {
      // Monotone cubic (Fritsch–Carlson): smooth, but never overshoots the data.
      const n = pts.length, m = new Array(n), dx = [], dy = [], sl = [];
      for (let i = 0; i < n - 1; i++) { dx[i] = pts[i + 1][0] - pts[i][0]; dy[i] = pts[i + 1][1] - pts[i][1]; sl[i] = dy[i] / (dx[i] || 1e-6); }
      m[0] = sl[0]; m[n - 1] = sl[n - 2];
      for (let i = 1; i < n - 1; i++) m[i] = sl[i - 1] * sl[i] <= 0 ? 0 : (sl[i - 1] + sl[i]) / 2;
      for (let i = 0; i < n - 1; i++) {
        if (sl[i] === 0) { m[i] = m[i + 1] = 0; continue; }
        const a = m[i] / sl[i], b = m[i + 1] / sl[i], q = a * a + b * b;
        if (q > 9) { const tt = 3 / Math.sqrt(q); m[i] = tt * a * sl[i]; m[i + 1] = tt * b * sl[i]; }
      }
      ctx.moveTo(pts[0][0], pts[0][1]);
      for (let i = 0; i < n - 1; i++) {
        const d3 = dx[i] / 3;
        ctx.bezierCurveTo(pts[i][0] + d3, pts[i][1] + m[i] * d3, pts[i + 1][0] - d3, pts[i + 1][1] - m[i + 1] * d3, pts[i + 1][0], pts[i + 1][1]);
      }
    };
    const c = P.rgb(s.color);
    if (s.fill !== false) {
      const grad = ctx.createLinearGradient(0, 0, 0, hh);
      grad.addColorStop(0, P.css(c, s.fillAlpha ?? .34));
      grad.addColorStop(1, P.css(c, 0));
      ctx.beginPath();
      trace();
      ctx.lineTo(pts[pts.length - 1][0], hh);
      ctx.lineTo(pts[0][0], hh);
      ctx.closePath();
      ctx.fillStyle = grad;
      ctx.fill();
    }
    ctx.beginPath();
    trace();
    ctx.strokeStyle = P.css(c, s.alpha ?? 1);
    ctx.lineWidth = s.width ?? 1.7;
    ctx.lineJoin = 'round'; ctx.lineCap = 'round';
    ctx.setLineDash(s.dash || []);
    ctx.stroke();
    ctx.setLineDash([]);
    if (s.dot !== false) {
      const [x, y] = pts[pts.length - 1];
      ctx.beginPath(); ctx.arc(x, y, 5.5, 0, Math.PI * 2); ctx.fillStyle = P.css(c, .2); ctx.fill();
      ctx.beginPath(); ctx.arc(x, y, 2.4, 0, Math.PI * 2); ctx.fillStyle = P.css(P.mix(c, [255, 255, 255], .35)); ctx.fill();
    }
  }
}

/** Drives all charts from one timed frame loop while smooth scrolling is on. */
const ChartClock = (() => {
  const charts = new Set();
  let running = false;
  const colors = { grid: 'rgba(255,255,255,.07)' };
  // eco and paused pages redraw only when data arrives; balanced streams at ~12 fps (the scroll is a
  // few px per second, so that is still perfectly fluid), max at ~30 fps.
  const smooth = () => P.g().smoothCharts !== false && P.anim() !== 'off' && P.perf() !== 'eco' && !P.state.paused && document.visibilityState === 'visible';
  const period = () => P.perf() === 'max' ? 31 : 83;
  function refreshColors() {
    const dark = document.documentElement.dataset.theme !== 'light';
    colors.grid = dark ? 'rgba(255,255,255,.075)' : 'rgba(0,0,0,.08)';
  }
  function loop() {
    if (!smooth() || !charts.size) { running = false; return; }
    drawAll();
    // sleep between frames instead of spinning requestAnimationFrame at the display rate
    setTimeout(() => requestAnimationFrame(loop), Math.max(0, period() - 6));
  }
  function drawAll() { for (const c of charts) c.draw(); }
  function kick() {
    if (P.state.paused) return;
    if (smooth() && !running && charts.size) { running = true; requestAnimationFrame(loop); }
    else if (!smooth()) drawAll();
  }
  P.on('tick', kick);
  P.on('look', () => { refreshColors(); kick(); });
  document.addEventListener('visibilitychange', kick);
  P.on('perf', kick);
  P.on('pause', kick);
  return { add: c => { charts.add(c); kick(); }, remove: c => charts.delete(c), smooth, colors, drawAll };
})();
