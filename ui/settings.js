// Pulse settings window.
'use strict';

const fmt = P.fmt, t = P.t;
const pane = $('#pane'), nav = $('#nav'), pop = $('#pop'), win = $('#win');
const S = { section: 'general', widget: null, binds: [], sig: '' };

const SVG = {
  general: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="2.3"/><path d="M8 1.8v1.6M8 12.6v1.6M1.8 8h1.6M12.6 8h1.6M3.6 3.6l1.1 1.1M11.3 11.3l1.1 1.1M3.6 12.4l1.1-1.1M11.3 4.7l1.1-1.1"/></svg>',
  widgets: '<svg viewBox="0 0 16 16"><rect x="2.2" y="2.2" width="5" height="5" rx="1.4"/><rect x="8.8" y="2.2" width="5" height="5" rx="1.4"/><rect x="2.2" y="8.8" width="11.6" height="5" rx="1.4"/></svg>',
  look: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.8"/><path d="M8 2.2v11.6"/><path d="M8 4.5c2 0 3.5 1.6 3.5 3.5S10 11.5 8 11.5"/></svg>',
  sensors: '<svg viewBox="0 0 16 16"><rect x="4" y="4" width="8" height="8" rx="1.6"/><path d="M6 1.8V4M10 1.8V4M6 12v2.2M10 12v2.2M1.8 6H4M1.8 10H4M12 6h2.2M12 10h2.2"/></svg>',
  games: '<svg viewBox="0 0 16 16"><path d="M4.6 4.5h6.8a3 3 0 0 1 2.9 2.3l.8 3.6a1.8 1.8 0 0 1-3 1.6L10.6 10.5H5.4l-1.5 1.5a1.8 1.8 0 0 1-3-1.6l.8-3.6a3 3 0 0 1 2.9-2.3z"/><path d="M5 6.8v2M4 7.8h2"/><circle cx="11" cy="7.4" r=".4"/></svg>',
  alerts: '<svg viewBox="0 0 16 16"><path d="M4 11.5V7.5a4 4 0 0 1 8 0v4l1.2 1.3H2.8z"/><path d="M6.6 14.2a1.6 1.6 0 0 0 2.8 0"/></svg>',
  anomaly: '<svg viewBox="0 0 16 16"><path d="M1.6 8.6h2.6l1.5-4.2 2.6 8 1.7-5.2 1 1.4h3.3"/></svg>',
  system: '<svg viewBox="0 0 16 16"><path d="M8 2v5.5"/><path d="M4.4 4.2a5.2 5.2 0 1 0 7.2 0"/></svg>',
  about: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.8"/><path d="M8 7.3v4"/><path d="M8 4.9v.1"/></svg>',
};
const NAV = [
  { id: 'general', ic: '#8E8E93' }, { id: 'widgets', ic: '#0A84FF' }, { id: 'look', ic: '#BF5AF2' },
  { id: 'sensors', ic: '#30D158' }, { id: 'games', ic: '#FF375F' }, { id: 'alerts', ic: '#FF453A' }, { id: 'anomaly', ic: '#FFB020' },
  { id: 'system', ic: '#FF9F0A' }, { id: 'about', ic: '#5E5CE6' },
];
const STYLE_ICONS = {
  auto: '<svg viewBox="0 0 16 16"><path d="M8 2l1.4 3.6L13 7l-3.6 1.4L8 12l-1.4-3.6L3 7l3.6-1.4z" fill="currentColor"/></svg>',
  ring: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.6" fill="none" stroke="currentColor" stroke-width="2" stroke-dasharray="26 40" stroke-linecap="round" transform="rotate(-90 8 8)"/><circle cx="8" cy="8" r="2.6" fill="none" stroke="currentColor" stroke-width="1.6" opacity=".55"/></svg>',
  gauge: '<svg viewBox="0 0 16 16"><path d="M3 12a5.8 5.8 0 1 1 10 0" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M8 9.5 10.6 6" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>',
  chart: '<svg viewBox="0 0 16 16"><path d="M1.8 11.5l3-4 3 2.5 3.4-5.5 3 3.5" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  pie: '<svg viewBox="0 0 16 16"><path d="M8 2.2A5.8 5.8 0 1 0 13.8 8H8z" fill="currentColor" opacity=".55"/><path d="M9.5 1.5a5.5 5.5 0 0 1 5 5h-5z" fill="currentColor"/></svg>',
  bars: '<svg viewBox="0 0 16 16"><rect x="2" y="7" width="2.6" height="7" rx="1" fill="currentColor"/><rect x="6.7" y="3" width="2.6" height="11" rx="1" fill="currentColor"/><rect x="11.4" y="9" width="2.6" height="5" rx="1" fill="currentColor"/></svg>',
  numbers: '<svg viewBox="0 0 16 16"><text x="8" y="12.2" text-anchor="middle" font-size="10.5" font-weight="700" fill="currentColor" font-family="Segoe UI">42</text></svg>',
};
const SWATCHES = ['#0A84FF', '#64D2FF', '#63E6E2', '#30D158', '#FFD60A', '#FF9F0A', '#FF453A', '#FF375F', '#BF5AF2', '#5E5CE6'];
const CHEVRON = '<svg viewBox="0 0 10 10"><path d="M2.5 3.8 5 6.3l2.5-2.5"/></svg>';
const CHECK = '<svg viewBox="0 0 20 20"><path d="M5 10.5l3.2 3.2L15 6.8"/></svg>';
const WARN = '<svg viewBox="0 0 20 20"><path d="M10 6v5"/><path d="M10 14v.1"/></svg>';

// ------------------------------------------------------------------ settings access (optimistic)
function get(path) {
  let cur = P.state.settings;
  for (const part of path.split('.')) {
    if (cur == null) return undefined;
    cur = Array.isArray(cur) ? cur.find(x => x && x.id === part) : cur[part];
  }
  return cur;
}
function putLocal(path, value) {
  const parts = path.split('.');
  let cur = P.state.settings;
  for (let i = 0; i < parts.length - 1; i++) {
    cur = Array.isArray(cur) ? cur.find(x => x && x.id === parts[i]) : (cur[parts[i]] = cur[parts[i]] || {});
    if (!cur) return;
  }
  cur[parts[parts.length - 1]] = value;
}
function put(path, value) { putLocal(path, value); P.set(path, value); if (path.startsWith('global.')) P.applyLook(); }
const throttled = {};
function putSoon(path, value) {
  putLocal(path, value);
  if (path.startsWith('global.')) P.applyLook();
  const th = throttled[path] = throttled[path] || { last: 0, timer: 0 };
  th.value = value;
  const now = performance.now();
  clearTimeout(th.timer);
  if (now - th.last > 60) { th.last = now; P.set(path, value); }
  else th.timer = setTimeout(() => { th.last = performance.now(); P.set(path, th.value); }, 70);
}
const bind = fn => S.binds.push(fn);

// ------------------------------------------------------------------ controls
function toggle(path, onChange) {
  const el = h('div', 'switch');
  const sync = () => el.classList.toggle('on', !!get(path));
  el.addEventListener('click', () => { const v = !get(path); put(path, v); sync(); if (onChange) onChange(v); });
  bind(sync); sync();
  return el;
}

function seg(path, opts, cls) {
  const el = h('div', 'seg' + (cls ? ' ' + cls : ''));
  const thumb = h('i', 'thumb');
  thumb.style.left = '0';
  el.appendChild(thumb);
  let first = true;
  const btns = opts.map(o => {
    const b = h('button', '', o.html || esc(o.label));
    if (o.title) b.title = o.title;
    b.addEventListener('click', () => { put(path, o.v); sync(); if (o.after) o.after(); });
    el.appendChild(b);
    return b;
  });
  function sync() {
    const cur = get(path);
    const idx = opts.findIndex(o => String(o.v) === String(cur));
    btns.forEach((b, i) => b.classList.toggle('on', i === idx));
    const b = btns[idx];
    if (!b || !b.offsetWidth) { thumb.style.opacity = b ? '' : 0; if (b) requestAnimationFrame(sync); return; }
    if (first) { thumb.style.transition = 'none'; }
    thumb.style.opacity = '';
    thumb.style.width = b.offsetWidth + 'px';
    thumb.style.transform = `translateX(${b.offsetLeft}px)`;
    if (first) { void thumb.offsetWidth; thumb.style.transition = ''; first = false; }
  }
  bind(sync);
  requestAnimationFrame(sync);
  return el;
}

/** Slider over [min,max]. `map` converts between the stored value and the displayed one. */
function slider(path, min, max, step, format, map) {
  map = map || { show: v => v, store: v => v };
  const wrap = h('div');
  wrap.style.cssText = 'display:flex;align-items:center;gap:10px';
  const el = h('div', 'slider');
  el.appendChild(h('i'));
  const val = h('span', 'val');
  let dragging = false;
  const show = v => { el.style.setProperty('--v', (v - min) / (max - min)); val.textContent = format(v); };
  const sync = () => { if (!dragging) { const raw = get(path); show(raw == null ? min : map.show(raw)); } };
  const from = e => {
    const r = el.getBoundingClientRect();
    let f = (e.clientX - r.left - 10) / (r.width - 20);
    f = Math.max(0, Math.min(1, f));
    let v = Math.round((min + f * (max - min)) / step) * step;
    v = +v.toFixed(4);
    show(v);
    putSoon(path, map.store(v));
  };
  el.addEventListener('pointerdown', e => { dragging = true; el.classList.add('drag'); el.setPointerCapture(e.pointerId); from(e); });
  el.addEventListener('pointermove', e => { if (dragging) from(e); });
  const end = () => { if (!dragging) return; dragging = false; el.classList.remove('drag'); const th = throttled[path]; if (th) { clearTimeout(th.timer); P.set(path, th.value); } };
  el.addEventListener('pointerup', end);
  el.addEventListener('pointercancel', end);
  wrap.append(el, val);
  bind(sync); sync();
  return wrap;
}

function select(path, optsFn, onPick) {
  const b = h('button', 'sel');
  const lab = h('span');
  b.append(lab);
  b.insertAdjacentHTML('beforeend', CHEVRON);
  const sync = () => {
    const cur = get(path);
    const o = optsFn().find(x => String(x.v) === String(cur ?? ''));
    lab.textContent = o ? o.label : String(cur ?? '');
  };
  b.addEventListener('click', e => {
    e.stopPropagation();
    openPop(b, optsFn(), get(path) ?? '', v => { put(path, v); sync(); if (onPick) onPick(v); });
  });
  bind(sync); sync();
  return b;
}

/** A brand in a picker: its logo, then its name (emblem-only logos need the word). */
const brandOption = k => logoFor(k, 'pop' + (BRAND_BUNDLED[k] === 'word' ? ' word' : '')) + (BRAND_BUNDLED[k] === 'word' ? '' : `<span class="bn">${esc(BRAND_WORDS[k] || k)}</span>`);

function openPop(anchor, opts, cur, pick) {
  pop.innerHTML = '';
  for (const o of opts) {
    const b = h('button', '', `<span class="ck">${String(o.v) === String(cur) ? '✓' : ''}</span><span class="pl">${o.html || esc(o.label)}</span>`);
    b.addEventListener('click', e => { e.stopPropagation(); closePop(); pick(o.v); });
    pop.appendChild(b);
  }
  const wr = win.getBoundingClientRect(), ar = anchor.getBoundingClientRect();
  pop.classList.remove('open');
  pop.style.display = 'block';
  const pw = pop.offsetWidth, ph = pop.offsetHeight;
  let left = ar.right - wr.left - pw, top = ar.bottom - wr.top + 6;
  if (top + ph > wr.height - 10) top = ar.top - wr.top - ph - 6;
  pop.style.left = Math.max(10, left) + 'px';
  pop.style.top = top + 'px';
  pop.style.display = '';
  pop.classList.add('open');
}
function closePop() { pop.classList.remove('open'); }
document.addEventListener('click', closePop);

function button(label, onClick, cls) {
  const b = h('button', 'btn' + (cls ? ' ' + cls : ''), esc(label));
  b.addEventListener('click', onClick);
  return b;
}
function confirmButton(label, confirmLabel, onConfirm) {
  let armed = 0;
  const b = button(label, () => {
    if (armed && performance.now() - armed < 4000) { onConfirm(); return; }
    armed = performance.now();
    b.textContent = confirmLabel;
    b.classList.add('primary');
    b.style.background = 'var(--red)';
    setTimeout(() => { armed = 0; b.textContent = label; b.classList.remove('primary'); b.style.background = ''; }, 4000);
  }, 'danger');
  return b;
}

function row(title, sub, control, cls) {
  const r = h('div', 'row' + (cls ? ' ' + cls : ''));
  const tx = h('div', 'tx', `<b>${esc(title)}</b>` + (sub ? `<span>${esc(sub)}</span>` : ''));
  r.appendChild(tx);
  if (control) r.appendChild(control);
  return r;
}
function group(title, rows, cls) {
  const g = h('div', 'group');
  if (title) g.appendChild(h('h3', '', esc(title)));
  const c = h('div', 'card' + (cls ? ' ' + cls : ''));
  rows.filter(Boolean).forEach(r => c.appendChild(r));
  g.appendChild(c);
  return g;
}
const pct = v => Math.round(v * 100) + '%';

/** Performance mode: the choice, what it does, and — for auto — what Pulse runs right now and why. */
function perfGroup() {
  const sub = h('span');
  const sync = () => {
    const pref = get('global.perf') || 'auto';
    let text = t('perfSub.' + pref);
    if (pref === 'auto') {
      const why = P.state.perfReason && P.state.perfReason !== 'manual' ? ' — ' + t('perfWhy.' + P.state.perfReason) : '';
      text += '. ' + t('perfNow') + ': ' + t('perf.' + P.state.perf) + why;
    }
    sub.textContent = text;
  };
  bind(sync); sync();
  const r = row(t('perf'), null, seg('global.perf', ['auto', 'max', 'balanced', 'eco'].map(v => ({ v, label: t('perf.' + v), after: sync }))), 'perfrow');
  r.querySelector('.tx').appendChild(sub);
  return group(null, [r, row(t('perfHidden'), null, null, 'note')]);
}
P.on('perf', () => { if (S.section === 'general' && !typing()) render(); });

// ------------------------------------------------------------------ sections
const SECTIONS = {
  general(root) {
    root.append(
      group(null, [
        row(t('language'), null, seg('global.lang', [{ v: 'ru', label: 'Русский' }, { v: 'uz', label: 'Oʻzbek' }, { v: 'en', label: 'English' }].map(o => Object.assign(o, { after: render })))),
        row(t('theme'), null, seg('global.theme', ['auto', 'dark', 'light'].map(v => ({ v, label: t('theme.' + v) })))),
      ]),
      group(null, [
        row(t('autostart'), t('autostartSub'), toggle('global.autostart')),
        row(t('hotkey'), P.state.sys.hotkeyOk === false ? t('hotkeyBusy') : t('hotkeySub'),
          select('global.hotkey', () => ['Ctrl+Alt+P', 'Ctrl+Shift+P', 'Ctrl+Alt+W', 'Win+Alt+P', 'Ctrl+Alt+Space'].map(v => ({ v, label: v.replace('Win', '⊞ Win') })).concat([{ v: 'none', label: t('none') }]))),
        row(t('hideFs'), t('hideFsSub'), toggle('global.hideFullscreen')),
      ]),
      perfGroup(),
      group(t('snap'), [
        row(t('snap'), t('snapSub'), toggle('global.snap')),
        row(t('snapGap'), null, slider('global.snapGap', 0, 40, 1, v => v + ' px')),
        row(t('snapForce'), null, slider('global.snapForce', 6, 48, 1, v => v + ' px')),
        row(t('snapWidgets'), null, toggle('global.snapWidgets')),
      ]),
    );
  },

  widgets(root) {
    const ws = (P.state.settings.widgets || []);
    if (!S.widget || !ws.find(w => w.id === S.widget)) S.widget = ws[0] && ws[0].id;
    const id = S.widget, base = 'widgets.' + id + '.';
    const pick = h('div', 'wpick');
    ws.forEach((w, i) => {
      const dots = (w.modules || []).filter(m => m.on).map(m => `<b style="--d:${P.moduleColor(m.id)}"></b>`).join('');
      const c = h('button', 'chip' + (w.id === id ? ' on' : '') + (w.hidden ? ' closed' : ''), `<i>${dots}</i>${esc(t('widget'))} ${i + 1}`);
      c.addEventListener('click', () => { S.widget = w.id; render(); });
      pick.appendChild(c);
    });
    const add = h('button', 'chip', '+ ' + esc(t('addWidget')));
    add.addEventListener('click', () => P.action('addWidget'));
    pick.appendChild(add);
    root.appendChild(pick);
    if (!id) return;

    const sizeChips = h('div', 'chips');
    ['s', 'm', 'l', 'wide', 'tall', 'strip'].forEach(z => {
      const c = h('button', 'chip', esc(t('z.' + z)));
      c.addEventListener('click', () => P.action('sizePreset', { id, preset: z }));
      sizeChips.appendChild(c);
    });
    const sizeVal = h('span', 'val');
    bind(() => { const w = get('widgets.' + id); if (w) sizeVal.textContent = Math.round(w.w) + ' × ' + Math.round(w.h); });

    root.append(
      ...((get(base.slice(0, -1)) || {}).hidden ? [group(null, [row(t('wClosed'), t('wClosedSub'), button(t('wShow'), () => P.action('widgetShow', { id })))], 'closedcard')] : []),
      group(null, [row(t('linkWidgets'), t('linkSub'), toggle('global.linkWidgets'))]),
      group(null, [
        row(t('mode'), null, seg(base + 'mode', [{ v: 'desktop', label: t('mode.desktop') }, { v: 'top', label: t('mode.top') }])),
        row(t('clickThrough'), t('clickThroughSub'), toggle(base + 'clickThrough')),
        row(t('ghost'), t('ghostSub'), toggle(base + 'ghost')),
        row(t('locked'), null, toggle(base + 'locked')),
      ]),
      group(null, [
        row(t('bgOpacity'), t('bgOpacitySub'), slider(base + 'bgOpacity', 0, 1, .01, pct, { show: v => 1 - v, store: v => +(1 - v).toFixed(2) })),
        row(t('opacity'), null, slider(base + 'opacity', .2, 1, .01, pct)),
        row(t('zoom'), null, slider(base + 'zoom', .6, 2, .05, pct)),
      ]),
      group(t('size'), [
        (() => { const r = row(t('size'), t('sizeSub'), sizeVal); return r; })(),
        (() => { const r = h('div', 'row'); r.appendChild(sizeChips); return r; })(),
      ]),
      modulesGroup(id),
    );
    const btns = h('div', 'btnrow');
    btns.append(button(t('duplicate'), () => P.action('duplicateWidget', { id })), button(t('resetPos'), () => P.action('resetPosition', { id })));
    if (ws.length > 1) btns.append(confirmButton(t('remove'), t('resetConfirm'), () => P.action('removeWidget', { id })));
    root.appendChild(btns);
    bind(() => {});
  },

  look(root) {
    const colorRows = ['cpu', 'gpu', 'ram', 'disk', 'net', 'llm', 'fans', 'power', 'battery'].map(m => {
      const sw = h('div', 'sw');
      const btns = SWATCHES.map(c => {
        const b = h('button');
        b.style.setProperty('--s', c);
        b.addEventListener('click', () => { put('global.colors.' + m, c); sync(); });
        sw.appendChild(b);
        return b;
      });
      const sync = () => { const cur = (get('global.colors.' + m) || '').toUpperCase(); btns.forEach((b, i) => b.classList.toggle('on', SWATCHES[i].toUpperCase() === cur)); };
      bind(sync); sync();
      return row(t('n.' + m), null, sw);
    });
    root.append(
      group(null, [
        row(t('glass'), null, seg('global.glass', ['frosted', 'clear', 'solid'].map(v => ({ v, label: t('glass.' + v) })))),
        row(t('tint'), null, slider('global.tint', .1, .95, .01, pct)),
        row(t('blur'), null, slider('global.blur', 0, 80, 1, v => v + ' px')),
        row(t('radius'), null, slider('global.radius', 8, 40, 1, v => v + ' px')),
      ]),
      group(null, [
        row(t('animations'), null, seg('global.animations', ['full', 'reduced', 'off'].map(v => ({ v, label: t('anim.' + v) })))),
        row(t('tilt'), null, toggle('global.tilt')),
        row(t('smoothCharts'), null, toggle('global.smoothCharts')),
        row(t('logos'), t('logosSub'), toggle('global.logos')),
      ]),
      group(t('colors'), colorRows),
    );
  },

  sensors(root) {
    const sys = P.state.sys, info = P.state.info;
    const good = !!info.cpuTemp;
    const st = h('div', 'card status ' + (good ? 'good' : 'need'));
    st.innerHTML = `<div class="badge">${good ? CHECK : WARN}</div><div class="tx"><b>${esc(t('cpuTemp'))}</b><p>${esc(good ? t('cpuTempOk') : t('cpuTempNo'))}</p></div>`;
    if (!good) {
      const tx = st.querySelector('.tx');
      const busy = sys.setup === 'running';
      const b = h('button', 'btn primary', busy ? `<span class="spinner"></span>${esc(t('enabling'))}` : esc(t('enable')));
      b.disabled = busy;
      b.addEventListener('click', () => { b.innerHTML = `<span class="spinner"></span>${esc(t('enabling'))}`; b.disabled = true; P.action('enableSensors'); });
      tx.appendChild(b);
      if (sys.setup === 'cancelled' || sys.setup === 'failed') tx.appendChild(h('p', '', esc(t(sys.setup))));
    }
    const sg = h('div', 'group');
    sg.appendChild(st);
    root.appendChild(sg);

    const gpus = () => [{ v: 'auto', label: t('theme.auto') + (info.gpu ? ' — ' + info.gpu : '') }].concat((info.gpus || []).map(g => ({ v: g.id, label: g.name })));
    root.append(
      group(null, [
        (info.gpus || []).length > 1 ? row(t('gpuPick'), null, select('global.gpu', gpus)) : null,
        row(t('interval'), null, seg('global.interval', [500, 1000, 2000, 5000].map(v => ({ v, label: fmt.num(v / 1000, v < 1000 ? 1 : 0) + ' ' + t('sec') })))),
        row(t('history'), null, seg('global.history', [60, 120, 300, 900].map(v => ({ v, label: v / 60 + ' ' + t('minutes') })))),
      ]),
      group(null, [
        row(t('tempUnit'), null, seg('global.tempUnit', [{ v: 'C', label: '°C' }, { v: 'F', label: '°F' }])),
        row(t('isp'), t('ispSub'), toggle('global.isp')),
        row(t('netQ'), t('netQSub'), toggle('global.netQuality')),
        row(t('llmOn'), t('llmSub'), toggle('global.llm')),
        row(t('netUnit'), null, seg('global.netUnit', [{ v: 'bytes', label: t('net.bytes') }, { v: 'bits', label: t('net.bits') }])),
        row(t('trayMetric'), null, seg('global.trayMetric', [{ v: 'cpu', label: 'CPU' }, { v: 'gpu', label: 'GPU' }, { v: 'ram', label: 'RAM' }, { v: 'none', label: t('none') }])),
      ]),
      fansGroup(),
      psuGroup(),
      liveTable(),
    );
  },

  games(root) {
    const sys = P.state.sys;
    const st = sys.fps || 'idle';
    const status = h('span', 'val', esc(t('fps.' + st)));
    status.style.cssText = 'min-width:0;text-align:right;white-space:nowrap;color:' + (st === 'running' ? 'var(--green)' : st === 'idle' ? 'var(--text-2)' : 'var(--orange)');
    const corners = [{ v: 'tl', label: '↖' }, { v: 'tr', label: '↗' }, { v: 'bl', label: '↙' }, { v: 'br', label: '↘' }];
    root.append(
      group(null, [
        row(t('fpsOn'), t('fpsSub'), toggle('global.fps.enabled')),
        row(t('fpsHotkey'), null, status),
      ]),
      group(null, [
        row(t('content'), null, seg('global.fps.content', ['fps', 'graph', 'sensors'].map(v => ({ v, label: t('content.' + v) })))),
        row(t('corner'), null, seg('global.fps.corner', corners)),
        row(t('scope'), null, seg('global.fps.scope', ['fullscreen', 'all'].map(v => ({ v, label: t('scope.' + v) })))),
        row(t('overlaySize'), null, slider('global.fps.zoom', .7, 1.6, .05, pct)),
        row(t('opacity'), null, slider('global.fps.opacity', .3, 1, .01, pct)),
      ]),
    );
    const note = h('div', 'credits', esc(t('fpsNote')));
    note.style.textAlign = 'left';
    root.appendChild(note);
  },

  alerts(root) {
    root.append(
      group(null, [
        row(t('alertsOn'), t('alertsSub'), toggle('global.alerts.enabled')),
        row(t('alertCpu'), null, slider('global.alerts.cpuTemp', 60, 100, 1, v => fmt.temp(v))),
        row(t('alertGpu'), null, slider('global.alerts.gpuTemp', 60, 95, 1, v => fmt.temp(v))),
        row(t('alertRam'), null, slider('global.alerts.ramLoad', 70, 99, 1, v => v + '%')),
      ]),
    );
    const b = h('div', 'btnrow');
    b.append(button(t('testAlert'), () => P.action('testAlert')));
    root.appendChild(b);
  },

  anomaly(root) {
    const on = get('global.anomaly.enabled') !== false;
    const st = P.state, active = st.anom || [];
    if (!st.anomJournal) P.action('anomJournal');
    const dur = s => fmt.uptime(Math.max(60, s));
    const when = ms => new Date(ms).toLocaleString(P.lang() === 'en' ? 'en-GB' : P.lang() === 'uz' ? 'uz-Latn-UZ' : 'ru-RU', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });

    // status: learning, all normal, or what is unusual right now
    if (on) {
      const card = h('div', 'card status an-status ' + (st.anomLearned < 1 ? 'learn' : active.some(a => a.sev === 'warn') ? 'warnc' : 'good'));
      if (st.anomLearned < 1) {
        const pct = Math.round(st.anomLearned * 100);
        card.innerHTML = `<div class="badge"><svg viewBox="0 0 20 20"><circle cx="10" cy="10" r="6.5" class="trk"/><circle cx="10" cy="10" r="6.5" stroke-dasharray="${(40.8 * st.anomLearned).toFixed(1)} 41" transform="rotate(-90 10 10)"/></svg></div>`
          + `<div class="tx"><b>${esc(t('anLearning'))} · ${pct}%</b><p>${esc(fill(t('anLearningSub'), dur(st.anomLearnedSec), dur(st.anomNeed)))}</p><div class="an-bar"><i style="width:${pct}%"></i></div></div>`;
      } else {
        const week = (st.anomJournal || []).filter(a => Date.now() - a.t0 < 7 * 86400000).length;
        card.innerHTML = `<div class="badge">${active.length ? WARN : CHECK}</div><div class="tx"><b>${esc(active.length ? t('anNowTitle') + ': ' + active.length : t('anAllGood'))}</b><p>${esc(week ? fill(t('anWeek'), week) : t('anQuiet'))}</p></div>`;
      }
      root.appendChild(card);
    }

    // what is unusual right now, with an explanation and what to do
    if (on && active.length) {
      const g = h('div', 'group');
      g.appendChild(h('h3', '', esc(t('anNowTitle'))));
      for (const a of active) {
        const i = anomInfo(a);
        const c = h('div', 'card an-card ' + a.sev);
        c.style.setProperty('--c', P.moduleColor(a.mod));
        c.innerHTML = `<div class="an-head"><span class="an-ic">${AN_ICON}</span><b>${esc(i.title)}</b><span class="an-mod"><i></i>${esc(t('n.' + a.mod))}</span><span class="an-time">${esc(fill(t('anOngoing')))} · ${esc(dur((Date.now() - a.t0) / 1000))}</span></div>`
          + `<p>${esc(i.text)}</p><p class="an-adv"><span>${esc(t('anAdvice'))}:</span> ${esc(i.advice)}</p>`;
        const b = button(t('anIgnore'), () => { P.action('anomIgnore', { key: a.key }); c.classList.add('gone'); });
        b.classList.add('small');
        c.appendChild(b);
        g.appendChild(c);
      }
      root.appendChild(g);
    }

    root.append(
      group(null, [
        row(t('anOn'), t('anOnSub'), toggle('global.anomaly.enabled', () => render())),
        on ? row(t('anNotify'), t('anNotifySub'), toggle('global.anomaly.notify')) : null,
        on ? row(t('anSens'), null, seg('global.anomaly.sensitivity', ['low', 'normal', 'high'].map(v => ({ v, label: t('anSens.' + v) })))) : null,
      ]),
    );
    if (!on) return;
    root.appendChild(group(t('anChecks'), ['hot', 'fans', 'idle', 'leak', 'rails', 'net', 'battery']
      .filter(d => d !== 'battery' || P.state.info.device === 'laptop')
      .map(d => row(t('anDet.' + d), t('anDetSub.' + d), toggle('global.anomaly.detectors.' + d)))));

    // journal
    const hist = (st.anomJournal || []).filter(a => a.t1);
    const jg = h('div', 'group');
    jg.appendChild(h('h3', '', esc(t('anHistory'))));
    const jc = h('div', 'card an-journal');
    if (!hist.length) jc.appendChild(h('div', 'an-empty', esc(t('anEmpty'))));
    for (const a of hist.slice(0, 40)) {
      const i = anomInfo(a);
      const r = h('div', 'an-row ' + a.sev);
      r.style.setProperty('--c', P.moduleColor(a.mod));
      r.innerHTML = `<i></i><div class="tx"><b>${esc(i.title)}</b><span>${esc(i.text)}</span></div><span class="when">${esc(when(a.t0))}<br>${esc(dur((a.t1 - a.t0) / 1000))}</span>`;
      jc.appendChild(r);
    }
    jg.appendChild(jc);
    root.appendChild(jg);

    // things the user said are normal
    const ign = get('global.anomaly.ignore') || [];
    if (ign.length) {
      root.appendChild(group(t('anIgnoredTitle'), ign.map(k => {
        const kind = String(k).split(':')[0];
        const label = k.startsWith('idle:') || k.startsWith('leak:') ? exeName(k.split(':').pop()) : k.replace(/^[^:]+:/, '');
        const b = button(t('anUnignore'), () => P.action('anomUnignore', { key: k }));
        b.classList.add('small');
        return row(label, t('an.' + ({ hot: 'hot', fanStop: 'fanStop', fanSlow: 'fanSlow', idle: k.includes(':gpu:') ? 'idleGpu' : 'idleCpu', leak: 'leak', rail: 'rail', net: 'netBad', bat: 'batDrain' }[kind] || kind)), b);
      })));
    }

    const btns = h('div', 'btnrow');
    btns.append(
      confirmButton(t('anReset'), t('anResetConfirm'), () => P.action('anomReset')),
      button(t('anClear'), () => P.action('anomClear')),
    );
    root.appendChild(btns);
    root.appendChild(h('p', 'hint', esc(t('anResetSub'))));
  },

  system(root) {
    root.append(
      group(null, [
        row(t('exportCsv'), t('exportSub'), button(t('exportCsv').split(' ')[0], () => P.action('exportCsv'))),
        row(t('openFolder'), null, button('↗', () => P.action('openDataFolder'))),
        row(t('openLog'), null, button('↗', () => P.action('openLog'))),
      ]),
      group(null, [
        row(t('reset'), null, confirmButton(t('reset').split(' ')[0], t('resetConfirm'), () => P.action('resetSettings'))),
        row(t('quit'), null, button(t('quit').split(' ')[0], () => P.action('quit'), 'danger')),
      ]),
    );
  },

  about(root) {
    const sys = P.state.sys, info = P.state.info;
    const hero = h('div', 'hero', `<img src="icon.png" alt=""><h2>Pulse</h2><div class="ver">${esc(t('version'))} ${esc(sys.version || '1.0')}</div><p>${esc(t('aboutText'))}</p>`);
    root.appendChild(hero);
    const kv = (k, v, logo) => { const val = h('span', 'v', (logo ? logoHtml(logo, 'about') : '') + esc(v || '–')); return row(k, null, val); };
    const mods = info.ramMods || [];
    const ramSrc = mods.length ? (brandOf(mods[0].mfr) ? mods[0].mfr : mods[0].part) : null;
    const ramText = mods.length ? mods.length + ' × ' + mods[0].gb + ' ' + P.u('gb') + (mods[0].type ? ' ' + mods[0].type + (mods[0].speed ? '-' + mods[0].speed : '') : '') + (mods[0].part ? ' · ' + mods[0].part : '') : (P.ok(info.ramTotal) ? fmt.gb(info.ramTotal) : '–');
    root.appendChild(group(null, [
      info.device ? (() => { const md = info.model || {}; const name = [md.vendor, md.model].filter(Boolean).join(' '); return kv(t('device'), t('device.' + info.device) + (name ? ' · ' + name : ''), md.vendor); })() : null,
      kv(t('n.cpu'), info.cpu + (info.threads ? ` · ${info.threads} ${t('threads').toLowerCase()}` : ''), info.cpu),
      kv(t('n.gpu'), info.gpu, info.gpu),
      kv(t('n.ram'), ramText, ramSrc),
      ...(info.disks || []).map(d => kv(t('n.disk'), d.name.replace(/^(SAMSUNG|Lexar|WDC|WD|Kingston|Crucial|Seagate)\s+/i, '') + (d.kind ? ' · ' + d.kind : '') + ' · ' + fmt.size(d.gb), d.name)),
      info.board ? kv(t('board'), info.board.replace(/\s*\(MS-\w+\)/, ''), info.boardMfr) : null,
      info.nicDesc ? kv(t('n.net'), info.nicDesc.replace(/\(R\)/g, ''), info.nicDesc) : null,
      info.isp ? (() => { const r = kv(t('provider'), info.isp.name + (info.isp.city ? ' · ' + info.isp.city : '')); if (info.isp.logo) r.querySelector('.v').insertAdjacentHTML('afterbegin', `<span class="logo isp about"><img src="${esc(info.isp.logo)}" alt=""></span>`); return r; })() : null,
      kv(t('os'), info.os),
      kv(t('computer'), info.machine),
      kv(t('elevated'), sys.elevated ? t('yes') : t('no')),
    ], 'kv'));
    root.appendChild(h('div', 'credits', esc(t('credits'))));
  },
};

function modulesGroup(id) {
  const base = 'widgets.' + id + '.modules';
  const wrap = h('div', 'group');
  wrap.appendChild(h('h3', '', esc(t('modules'))));
  const card = h('div', 'card mods');
  wrap.appendChild(card);
  const styles = ['auto', 'ring', 'gauge', 'chart', 'pie', 'bars', 'numbers'];
  const mods = () => (get(base) || []).map(m => Object.assign({}, m));
  const save = list => put(base, list);

  function build() {
    card.innerHTML = '';
    // the battery module only exists on machines that have one
    const hasBattery = P.state.info.device === 'laptop' || !!(P.state.tick && P.state.tick.battery);
    mods().filter(m => m.id !== 'battery' || hasBattery).forEach(m => {
      const r = h('div', 'row' + (m.on ? '' : ' off'));
      r.dataset.id = m.id;
      r.style.setProperty('--c', P.moduleColor(m.id));
      const handle = h('div', 'handle', '<svg viewBox="0 0 12 12"><path d="M2 3.5h8M2 6h8M2 8.5h8"/></svg>');
      const tx = h('div', 'tx', `<b>${esc(t('n.' + m.id))}</b>`);
      // what the module headlines here, and what it shows when the widget is minimized to a capsule
      if (METRIC_KEYS[m.id]) {
        const mp = 'widgets.' + id + '.modules.' + m.id + '.';
        const keys = METRIC_KEYS[m.id];
        const opts = extra => () => [{ v: '', label: t('mt.auto') }, ...keys.map(k => ({ v: k, label: t('mt.' + k) })), ...extra];
        const line = h('div', 'mopts');
        line.append(h('span', '', esc(t('mtShow'))), select(mp + 'main', opts([])), h('span', '', esc(t('mtCap'))),
          select(mp + 'cap', opts(METRIC_BOTH.includes(m.id) ? [{ v: 'both', label: t('mt.both') }] : [])));
        tx.appendChild(line);
      }
      const sg = h('div', 'seg icons');
      const thumb = h('i', 'thumb'); thumb.style.left = '0'; sg.appendChild(thumb);
      const btns = styles.map(s => {
        const b = h('button', '', STYLE_ICONS[s]);
        b.title = t('st.' + s);
        b.addEventListener('click', () => { const list = mods(); list.find(x => x.id === m.id).style = s; save(list); place(); });
        sg.appendChild(b);
        return b;
      });
      const place = () => {
        const cur = (mods().find(x => x.id === m.id) || {}).style || 'auto';
        const i = styles.indexOf(cur);
        btns.forEach((b, k) => b.classList.toggle('on', k === i));
        if (btns[i] && btns[i].offsetWidth) { thumb.style.width = btns[i].offsetWidth + 'px'; thumb.style.transform = `translateX(${btns[i].offsetLeft}px)`; }
        else requestAnimationFrame(place);
      };
      requestAnimationFrame(place);
      const sw = h('div', 'switch' + (m.on ? ' on' : ''));
      sw.addEventListener('click', () => {
        const list = mods();
        const it = list.find(x => x.id === m.id);
        if (it.on && list.filter(x => x.on).length <= 1) { sw.animate([{ transform: 'translateX(-4px)' }, { transform: 'translateX(4px)' }, { transform: 'none' }], { duration: 260 }); return; }
        it.on = !it.on;
        save(list);
        sw.classList.toggle('on', it.on);
        r.classList.toggle('off', !it.on);
      });
      r.append(handle, h('span', 'dot'), tx, sg, sw);
      card.appendChild(r);
      dragSort(handle, r);
    });
  }

  // Drag to reorder: the row follows the pointer, the others make room with a spring.
  function dragSort(handle, r) {
    handle.addEventListener('pointerdown', e => {
      e.preventDefault();
      handle.setPointerCapture(e.pointerId);
      const rows = [...card.children];
      const startY = e.clientY;
      const hgt = r.offsetHeight;
      const from = rows.indexOf(r);
      let to = from;
      r.classList.add('dragging');
      const move = ev => {
        const dy = ev.clientY - startY;
        r.style.transform = `translateY(${dy}px) scale(1.02)`;
        to = Math.max(0, Math.min(rows.length - 1, from + Math.round(dy / hgt)));
        rows.forEach((o, i) => {
          if (o === r) return;
          let shift = 0;
          if (from < to && i > from && i <= to) shift = -hgt;
          if (from > to && i < from && i >= to) shift = hgt;
          o.style.transition = 'transform .35s var(--spring)';
          o.style.transform = shift ? `translateY(${shift}px)` : '';
        });
      };
      const up = () => {
        handle.removeEventListener('pointermove', move);
        handle.removeEventListener('pointerup', up);
        handle.removeEventListener('pointercancel', up);
        rows.forEach(o => { o.style.transition = ''; o.style.transform = ''; });
        r.classList.remove('dragging');
        if (to !== from) {
          // by id: rows of hidden modules (no battery on this PC) are not on screen
          const list = mods();
          const fi = list.findIndex(x => x.id === rows[from].dataset.id), ti = list.findIndex(x => x.id === rows[to].dataset.id);
          const [it] = list.splice(fi, 1);
          list.splice(ti, 0, it);
          save(list);
          build();
        }
      };
      handle.addEventListener('pointermove', move);
      handle.addEventListener('pointerup', up);
      handle.addEventListener('pointercancel', up);
    });
  }

  build();
  bind(() => {
    const ids = mods().map(m => m.id + m.on).join();
    if (card.dataset.sig !== ids) { card.dataset.sig = ids; build(); }
  });
  card.dataset.sig = mods().map(m => m.id + m.on).join();
  return wrap;
}

/** Per-fan brand / model / name. Fans cannot report their maker, so the user tells us once. */
function fansGroup() {
  const tick = P.state.tick, list = (tick && tick.fans && tick.fans.list) || [];
  const g = h('div', 'group');
  g.appendChild(h('h3', '', esc(t('fanBrands'))));
  const card = h('div', 'card');
  g.appendChild(card);
  if (!list.length) { card.appendChild(row(t('noFans'), null, null)); return g; }
  const names = { cpu: 'fan.cpu', pump: 'fan.pump', sys: 'fan.sys', chipset: 'fan.chipset', gpu: 'fan.gpu' };
  const defName = n => {
    let x;
    if (/^CPU Fan/i.test(n)) return t('fan.cpu');
    if ((x = /^Pump Fan\s*#?(\d*)/i.exec(n))) return t('fan.pump') + (x[1] && x[1] !== '1' ? ' ' + x[1] : '');
    if ((x = /^System Fan\s*#?(\d+)/i.exec(n))) return t('fan.sys') + ' ' + x[1];
    if (/^Chipset/i.test(n)) return t('fan.chipset');
    if ((x = /^GPU Fan\s*#?(\d+)/i.exec(n))) return t('fan.gpu') + ' ' + x[1];
    return n;
  };
  for (const f of list) {
    const base = 'global.fanInfo.' + f.id + '.';
    const info = (P.g().fanInfo || {})[f.id] || {};
    const auto = f.src === 'gpu' ? brandOf(P.state.info.gpuVendor) : null;
    const r = h('div', 'row fanrow');
    const tx = h('div', 'tx', `<b>${esc(info.name || defName(f.n))}</b><span>${esc(defName(f.n))} · ${esc(fmt.rpm(f.rpm))}${P.ok(f.pct) ? ' · ' + fmt.pct(f.pct) + '%' : ''}</span>`);
    const brandBtn = h('button', 'sel');
    const paint = () => {
      const b = ((P.g().fanInfo || {})[f.id] || {}).brand || auto;
      brandBtn.innerHTML = (b ? logoFor(b, 'mini') + `<span>${esc(BRAND_WORDS[b] || b)}</span>` : `<span>${esc(t('brandNone'))}</span>`) + CHEVRON;
    };
    paint();
    brandBtn.addEventListener('click', e => {
      e.stopPropagation();
      const cur = ((P.g().fanInfo || {})[f.id] || {}).brand || '';
      const opts = [{ v: '', label: auto ? t('brandAuto') + ' — ' + (BRAND_WORDS[auto] || auto) : t('brandNone') }].concat(FAN_BRANDS.map(k => ({ v: k, label: BRAND_WORDS[k] || k, html: brandOption(k) })));
      openPop(brandBtn, opts, cur, v => { put(base + 'brand', v || null); paint(); });
    });
    const model = h('input', 'inp');
    model.placeholder = t('fanModel');
    model.value = info.model || '';
    model.addEventListener('change', () => put(base + 'model', model.value.trim() || null));
    const name = h('input', 'inp');
    name.placeholder = defName(f.n);
    name.value = info.name || '';
    name.addEventListener('change', () => { put(base + 'name', name.value.trim() || null); tx.querySelector('b').textContent = name.value.trim() || defName(f.n); });
    for (const el of [model, name]) el.addEventListener('keydown', e => { if (e.key === 'Enter') el.blur(); e.stopPropagation(); });
    r.append(tx, name, model, brandBtn);
    card.appendChild(r);
  }
  return g;
}

/** Power supply: brand, model, wattage and 80 PLUS rating feed the wall-power estimate. */
function psuGroup() {
  const base = 'global.psu.';
  const brandBtn = h('button', 'sel psu-brand');
  const paint = () => {
    const b = (P.g().psu || {}).brand;
    brandBtn.innerHTML = (b ? logoFor(b, 'mini') + `<span>${esc(BRAND_WORDS[b] || b)}</span>` : `<span>${esc(t('brandNone'))}</span>`) + CHEVRON;
  };
  paint();
  bind(paint);
  brandBtn.addEventListener('click', e => {
    e.stopPropagation();
    const opts = [{ v: '', label: t('brandNone') }].concat(PSU_BRANDS.map(k => ({ v: k, label: BRAND_WORDS[k] || k, html: brandOption(k) })));
    openPop(brandBtn, opts, (P.g().psu || {}).brand || '', v => { put(base + 'brand', v || null); paint(); });
  });
  const input = (key, ph, type, w) => {
    const el = h('input', 'inp');
    el.placeholder = ph;
    if (type) el.type = type;
    if (w) el.style.width = w;
    const sync = () => { if (document.activeElement !== el) { const v = (P.g().psu || {})[key]; el.value = v === null || v === undefined || v === 0 ? '' : v; } };
    sync(); bind(sync);
    el.addEventListener('change', () => {
      let v = el.value.trim();
      if (type === 'number') v = v === '' ? 0 : Math.max(0, Number(v.replace(',', '.')) || 0);
      put(base + key, v === '' ? null : v);
    });
    el.addEventListener('keydown', e => { if (e.key === 'Enter') el.blur(); e.stopPropagation(); });
    return el;
  };
  const g = group(t('psu'), [
    row(t('psu'), t('psuSub'), brandBtn),
    row(t('fanModel'), null, input('model', 'RM850x, Focus GX…', null, '180px')),
    row(t('psuWatt'), null, input('watt', '850', 'number', '90px')),
    row(t('psuRating'), null, seg(base + 'rating', ['bronze', 'gold', 'platinum', 'titanium'].map(v => ({ v, label: t('r.' + v) })))),
    row(t('tariff'), null, (() => { const d = h('div'); d.style.cssText = 'display:flex;gap:6px'; d.append(input('tariff', '0', 'number', '90px'), input('currency', 'сум', null, '64px')); return d; })()),
  ]);
  const note = h('div', 'credits', esc(t('noInput')));
  note.style.cssText = 'text-align:left;margin:-10px 4px 18px';
  const wrap = h('div');
  wrap.append(g, note);
  return wrap;
}

function liveTable() {
  const rows = [
    ['n.cpu', 'load', m => fmt.pct(m.cpu.load) + '%'], ['n.cpu', 'temp', m => fmt.temp(m.cpu.temp)],
    ['n.cpu', 'clock', m => fmt.clock(m.cpu.clock)], ['n.cpu', 'power', m => fmt.power(m.cpu.power)],
    ['n.gpu', 'load', m => fmt.pct(m.gpu.load) + '%'], ['n.gpu', 'temp', m => fmt.temp(m.gpu.temp)],
    ['n.gpu', 'memTemp', m => fmt.temp(m.gpu.memT)], ['n.gpu', 'power', m => fmt.power(m.gpu.power)],
    ['n.gpu', 'fan', m => fmt.rpm(m.gpu.fan)], ['n.gpu', 'vram', m => P.ok(m.gpu.vu) ? fmt.pct(m.gpu.vram) + '% · ' + fmt.gbPair(m.gpu.vu / 1024, m.gpu.vt / 1024) : '–'],
    ['n.ram', 'used', m => fmt.gbPair(m.ram.used, m.ram.total)], ['n.ram', 'temp', m => (m.ram.dimms || []).length ? m.ram.dimms.map(d => fmt.temp(d.t)).join(' · ') : fmt.temp(m.ram.temp)],
    ['n.cpu', 'ccd', m => (m.cpu.ccd || []).filter(P.ok).map(fmt.temp).join(' · ') || '–'],
    ['n.disk', 'read', m => fmt.speed(m.disk.read, true)], ['n.disk', 'write', m => fmt.speed(m.disk.write, true)],
    ['n.net', 'down', m => fmt.speed(m.net.down)], ['n.net', 'up', m => fmt.speed(m.net.up)],
  ];
  const els = [];
  const g = group(t('s.sensors'), rows.map(([a, b]) => {
    const v = h('span', 'v num', '–');
    els.push(v);
    return row(t(a) + ' · ' + t(b).toLowerCase(), null, v);
  }), 'kv');
  const upd = m => { if (!m) return; rows.forEach((r, i) => { const s = r[2](m); if (els[i].textContent !== s) els[i].textContent = s; }); };
  live.table = upd;
  upd(P.state.tick);
  return g;
}

// ------------------------------------------------------------------ sidebar & navigation
const live = { rings: {}, table: null };
function buildSidebar() {
  nav.querySelectorAll('button').forEach(b => b.remove());
  NAV.forEach(n => {
    const b = h('button', '', `<span class="ico" style="--ic:${n.ic}">${SVG[n.id]}</span><span>${esc(t('s.' + n.id))}</span>`);
    b.dataset.id = n.id;
    b.addEventListener('click', () => { S.section = n.id; render(); });
    nav.appendChild(b);
  });
  const lv = $('#live');
  lv.innerHTML = '';
  for (const id of ['cpu', 'gpu', 'ram']) {
    const c = P.moduleColor(id);
    const ring = new RingViz([{ width: 11, color: c, color2: P.lighten(c, .45) }]);
    ring.center.innerHTML = '<span class="big num"><span data-big></span><small>%</small></span>';
    const odo = new Odo(ring.center.querySelector('[data-big]'));
    const cell = h('div', 'cell');
    cell.style.setProperty('--c', c);
    cell.append(ring.el, h('span', 'lb', esc(t('m.' + id))));
    lv.appendChild(cell);
    live.rings[id] = { ring, odo };
  }
  $('#ver').textContent = 'v' + (P.state.sys.version || '1.0');
}
function placeNav() {
  const i = NAV.findIndex(n => n.id === S.section);
  nav.querySelector('.hl').style.transform = `translateY(${i * 34}px)`;
  nav.querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.id === S.section));
}

function render() {
  closePop();
  S.binds = [];
  live.table = null;
  $('#title').textContent = t('s.' + S.section);
  pane.innerHTML = '';
  const sect = h('div', 'sect');
  SECTIONS[S.section](sect);
  pane.appendChild(sect);
  pane.scrollTop = 0;
  if (S.sideLang !== P.lang() || S.sideColors !== JSON.stringify(P.g().colors)) {
    S.sideLang = P.lang();
    S.sideColors = JSON.stringify(P.g().colors);
    buildSidebar();
  }
  placeNav();
  S.sig = sigOf();
}
const sigOf = () => (P.state.settings.widgets || []).map(w => w.id).join() + '|' + P.lang();

// ------------------------------------------------------------------ window chrome
function close() {
  document.body.classList.add('closing');
  setTimeout(() => P.send('closed'), P.anim() === 'off' ? 0 : 250);
}
$('.cb.close').addEventListener('click', close);
$('.cb.min').addEventListener('click', () => P.send('minimize'));
P.on('init', () => { $('.cb.min').title = t('wcMin'); $('.cb.close').title = t('closeWin'); });
document.addEventListener('keydown', e => { if (e.key === 'Escape') { if (pop.classList.contains('open')) closePop(); else close(); } });
(() => {
  let down = null, dragging = false;
  win.addEventListener('pointerdown', e => {
    if (e.button !== 0 || !e.target.closest('[data-drag]') || e.target.closest('[data-nodrag]')) return;
    down = { x: e.screenX, y: e.screenY, id: e.pointerId };
    win.setPointerCapture(e.pointerId);
  });
  win.addEventListener('pointermove', e => {
    if (!down) return;
    if (!dragging && Math.hypot(e.screenX - down.x, e.screenY - down.y) > 3) { dragging = true; P.send('dragStart'); }
    if (dragging) P.send('dragMove');
  });
  const end = () => { if (!down) return; if (dragging) P.send('dragEnd'); dragging = false; try { win.releasePointerCapture(down.id); } catch (_) { } down = null; };
  win.addEventListener('pointerup', end);
  win.addEventListener('pointercancel', end);
})();

// ------------------------------------------------------------------ host messages
const glassEl = $('.glass');
P.on('init', m => {
  document.documentElement.style.setProperty('--m', m.margin + 'px');
  render();
  requestAnimationFrame(() => document.body.classList.add('ready'));
});
P.on('settings', () => {
  if (sigOf() !== S.sig) { render(); return; }
  S.binds.forEach(f => { try { f(); } catch (e) { console.error(e); } });
});
P.on('look', () => { if (S.sideColors && S.sideColors !== JSON.stringify(P.g().colors)) { S.sideColors = null; buildSidebar(); placeNav(); } });
const typing = () => document.activeElement && document.activeElement.tagName === 'INPUT';
P.on('sys', () => { if (['sensors', 'about', 'general', 'games'].includes(S.section) && !typing()) render(); });
// the anomaly page follows what is active; the journal is asked for when the page opens
P.on('anom', () => { if (S.section === 'anomaly' && !typing()) { P.action('anomJournal'); } });
P.on('anomJournal', () => { if (S.section === 'anomaly' && !typing()) render(); });
P.on('info', () => { if (['sensors', 'about'].includes(S.section) && !typing()) render(); });
P.on('nav', m => { if (m.section) S.section = m.section; if (m.widget) S.widget = m.widget; render(); });
P.on('tick', m => {
  for (const id in live.rings) {
    const r = live.rings[id], v = m[id] && m[id].load;
    if (P.ok(v)) { r.ring.set(0, v / 100); r.odo.set(fmt.pct(v)); }
  }
  if (live.table) live.table(m);
});
P.on('geo', () => placeWall(glassEl));
P.on('wall', () => placeWall(glassEl));

P.send('ready');
