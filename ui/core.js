// Pulse — shared runtime for widget and settings pages: host bridge, state, formatting,
// colour science, the spring animation loop and rolling digits.
'use strict';

window.__errs = [];
window.addEventListener('error', e => window.__errs.push(`${e.message} @${(e.filename || '').split('/').pop()}:${e.lineno}`));
window.addEventListener('unhandledrejection', e => window.__errs.push('promise: ' + (e.reason && e.reason.message || e.reason)));

const P = (() => {
  const host = window.chrome && window.chrome.webview;
  const handlers = {};
  const state = {
    role: null, id: null, settings: null, info: {}, sys: {}, icons: {}, tick: null,
    geo: null, wall: null, margin: 26,
    perf: 'balanced', perfReason: '', paused: false,
    anom: [], anomLearned: 1, anomLearnedSec: 0, anomNeed: 7200, anomJournal: null,
  };

  function on(type, fn) { (handlers[type] = handlers[type] || []).push(fn); }
  function emit(type, msg) { (handlers[type] || []).forEach(f => { try { f(msg); } catch (e) { console.error(e); } }); }
  function send(cmd, data) { if (host) host.postMessage(Object.assign({ cmd }, data || {})); }

  if (host) host.addEventListener('message', e => {
    const m = e.data;
    if (!m || !m.type) return;
    switch (m.type) {
      case 'init':
        Object.assign(state, { role: m.role, id: m.id, settings: m.settings, info: m.info || {}, sys: m.sys || {}, margin: m.margin || 26 });
        Object.assign(state.icons, m.icons || {});
        applyLook();
        break;
      case 'settings': state.settings = m.settings; applyLook(); break;
      case 'info': state.info = m.info || {}; break;
      case 'sys': state.sys = m.sys || {}; break;
      case 'history': H.load(m); break;
      case 'tick':
        state.tick = m;
        if (m.icons) Object.assign(state.icons, m.icons);
        H.push(m);
        break;
      case 'geo': state.geo = m; break;
      case 'wall': state.wall = m; break;
      case 'perf': state.perf = m.mode || 'balanced'; state.perfReason = m.reason || ''; state.weak = !!m.weak; applyLook(); break;
      case 'pause': setPaused(!!m.on); break;
      case 'anom': case 'anomJournal':
        state.anom = m.active || []; state.anomLearned = m.learned ?? 1; state.anomLearnedSec = m.learnedSec || 0; state.anomNeed = m.learnNeed || 7200;
        if (m.type === 'anomJournal') state.anomJournal = m.events || [];
        break;
    }
    emit(m.type, m);
  });

  // ------------------------------------------------------------ pause: nobody can see the page
  // Running animations freeze (the last frame stays on screen), charts stop streaming and ticks
  // only update the history; resuming catches up from the latest tick.
  const frozen = new Set();
  function setPaused(on) {
    if (on === state.paused) return;
    state.paused = on;
    document.documentElement.toggleAttribute('data-paused', on);
    if (on) {
      for (const a of document.getAnimations()) if (a.playState === 'running') { a.pause(); frozen.add(a); }
    } else {
      for (const a of frozen) { try { a.play(); } catch (_) { } }
      frozen.clear();
    }
  }

  // ------------------------------------------------------------ settings helpers
  const g = () => (state.settings && state.settings.global) || {};
  const widget = id => ((state.settings && state.settings.widgets) || []).find(w => w.id === (id || state.id));
  const lang = () => g().lang || 'ru';
  const t = k => (I18N[lang()] && I18N[lang()][k]) || I18N.ru[k] || k;
  const u = k => (UNITS[lang()] || UNITS.ru)[k];
  const set = (path, value) => send('set', { path, value });
  const action = (name, data) => send('action', Object.assign({ name }, data || {}));

  // ------------------------------------------------------------ look
  const darkMq = matchMedia('(prefers-color-scheme: dark)');
  darkMq.addEventListener('change', () => applyLook());
  function applyLook() {
    const s = g();
    const root = document.documentElement;
    const theme = s.theme === 'light' || s.theme === 'dark' ? s.theme : (darkMq.matches ? 'dark' : 'light');
    root.dataset.theme = theme;
    root.dataset.glass = s.glass || 'frosted';
    const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
    // Value changes animate every frame for about half a second after each tick, which is where
    // nearly all of Pulse's cost goes; eco therefore shows new values at once.
    root.dataset.anim = state.perf === 'eco' ? 'off' : reduce && (s.animations || 'full') === 'full' ? 'reduced' : (s.animations || 'full');
    root.dataset.perf = state.perf;
    root.lang = lang();
    root.style.setProperty('--tint', s.glass === 'clear' ? Math.min(.85, (s.tint ?? .5) * .9) : (s.tint ?? .5));
    root.style.setProperty('--blur', (s.blur ?? 34) + 'px');
    root.style.setProperty('--radius', (s.radius ?? 26) + 'px');
    const c = s.colors || {};
    for (const k of ['cpu', 'gpu', 'ram', 'disk', 'net', 'llm', 'fans', 'power', 'battery']) if (c[k]) root.style.setProperty('--' + k, c[k]);
    emit('look', s);
  }
  const anim = () => document.documentElement.dataset.anim || 'full';
  const perf = () => state.perf;

  // ------------------------------------------------------------ formatting
  const nf = {};
  function num(v, d) {
    const key = lang() + d;
    if (!nf[key]) nf[key] = new Intl.NumberFormat(lang() === 'en' ? 'en-US' : lang() === 'uz' ? 'uz-Latn-UZ' : 'ru-RU', { minimumFractionDigits: d, maximumFractionDigits: d });
    return nf[key].format(v);
  }
  const ok = v => v !== null && v !== undefined && !Number.isNaN(v);
  const fmt = {
    num,
    pct: v => ok(v) ? Math.round(v) + '' : '–',
    temp: c => {
      if (!ok(c)) return '–';
      return Math.round(g().tempUnit === 'F' ? c * 9 / 5 + 32 : c) + '°';
    },
    tempUnit: () => g().tempUnit === 'F' ? '°F' : '°C',
    clock: mhz => !ok(mhz) || mhz <= 0 ? '–' : mhz >= 1000 ? num(mhz / 1000, 2) + ' ' + u('ghz') : Math.round(mhz) + ' ' + u('mhz'),
    power: w => ok(w) ? num(w, w < 10 ? 1 : 0) + ' ' + u('w') : '–',
    gb: v => ok(v) ? num(v, v >= 100 ? 0 : 1) + ' ' + u('gb') : '–',
    gbPair: (a, b) => ok(a) && ok(b) ? num(a, 1) + ' / ' + num(b, b >= 100 ? 0 : 1) + ' ' + u('gb') : '–',
    mb: v => ok(v) ? (v >= 1024 ? num(v / 1024, 1) + ' ' + u('gb') : Math.round(v) + ' ' + u('mb')) : '–',
    size: gbv => !ok(gbv) ? '–' : gbv >= 1000 ? num(gbv / 1024, 1) + ' ' + u('tb') : num(gbv, gbv >= 100 ? 0 : 1) + ' ' + u('gb'),
    rpm: v => ok(v) ? (v < 1 ? t('fanStopped') : num(Math.round(v), 0) + ' ' + u('rpm')) : '–',
    speed: (bps, compact) => {
      if (!ok(bps)) return '–';
      if (g().netUnit === 'bits') {
        const b = bps * 8;
        if (b >= 1e9) return num(b / 1e9, 2) + ' ' + u('gbit');
        if (b >= 1e6) return num(b / 1e6, b >= 1e8 ? 0 : 1) + ' ' + u('mbit');
        return num(b / 1e3, 0) + ' ' + u('kbit');
      }
      const ps = u('ps');
      if (bps >= 1073741824) return num(bps / 1073741824, 2) + ' ' + u('gb') + ps;
      if (bps >= 1048576) return num(bps / 1048576, bps >= 104857600 ? 0 : 1) + ' ' + u('mb') + ps;
      if (bps >= 1024 || compact) return num(bps / 1024, 0) + ' ' + u('kb') + ps;
      return Math.round(bps) + ' ' + u('b') + ps;
    },
    uptime: s => {
      if (!ok(s)) return '–';
      const d = Math.floor(s / 86400), h = Math.floor(s % 86400 / 3600), m = Math.floor(s % 3600 / 60);
      return d > 0 ? `${d} ${u('d')} ${h} ${u('h')}` : h > 0 ? `${h} ${u('h')} ${m} ${u('m')}` : `${m} ${u('m')}`;
    },
  };

  // ------------------------------------------------------------ colour
  function rgb(hex) {
    const m = /^rgba?\(([^)]+)\)/.exec(hex || '');
    if (m) return m[1].split(',').slice(0, 3).map(Number);
    hex = (hex || '#888').replace('#', '');
    if (hex.length === 3) hex = hex.split('').map(c => c + c).join('');
    const n = parseInt(hex, 16);
    return [n >> 16 & 255, n >> 8 & 255, n & 255];
  }
  const css = (c, a = 1) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a})`;
  const mix = (a, b, k) => [a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k];
  function lighten(hex, k) { return css(mix(rgb(hex), [255, 255, 255], k)); }
  const HEAT = [[0, '#64D2FF'], [.55, '#64D2FF'], [.68, '#30D158'], [.8, '#FFD60A'], [.9, '#FF9F0A'], [1, '#FF453A']].map(([s, c]) => [s, rgb(c)]);
  /** Temperature → colour, scaled to the alert threshold so "red" always means "at the limit". */
  function heat(temp, warn) {
    if (!ok(temp)) return 'rgba(140,140,150,.6)';
    const f = Math.max(0, Math.min(1, temp / (warn || 90)));
    for (let i = 1; i < HEAT.length; i++) {
      if (f <= HEAT[i][0]) {
        const k = (f - HEAT[i - 1][0]) / (HEAT[i][0] - HEAT[i - 1][0]);
        return css(mix(HEAT[i - 1][1], HEAT[i][1], k));
      }
    }
    return css(HEAT[HEAT.length - 1][1]);
  }
  function moduleColor(id) {
    return (g().colors || {})[id] || getComputedStyle(document.documentElement).getPropertyValue('--' + id).trim() || '#0A84FF';
  }

  return { state, on, emit, send, set, action, g, widget, t, u, lang, fmt, ok, rgb, css, mix, lighten, heat, moduleColor, applyLook, anim, perf };
})();

// ------------------------------------------------------------------ history (shared by every chart)
const H = (() => {
  const MAP = {
    cpu: m => m.cpu && m.cpu.load, cpuT: m => m.cpu && m.cpu.temp, cpuP: m => m.cpu && m.cpu.power,
    gpu: m => m.gpu && m.gpu.load, gpuT: m => m.gpu && m.gpu.temp, gpuP: m => m.gpu && m.gpu.power, vram: m => m.gpu && m.gpu.vram,
    ram: m => m.ram && m.ram.load, ramT: m => m.ram && m.ram.temp,
    disk: m => m.disk && m.disk.act, diskT: m => m.disk && m.disk.temp, diskR: m => m.disk && m.disk.read, diskW: m => m.disk && m.disk.write,
    netD: m => m.net && m.net.down, netU: m => m.net && m.net.up,
    llm: m => m.llm && m.llm.tps,
    fan: m => m.fans && m.fans.max,
    pwr: m => m.power && m.power.wall,
    bat: m => m.battery && m.battery.pct, batW: m => m.battery && m.battery.rate,
    ping: m => { const q = m.net && m.net.q; const s = q && (q.target === 'isp' ? q.isp : q.inet); return s ? s.rtt : null; },
  };
  const CAP = 3600;
  let times = [];
  const data = {};
  for (const k in MAP) data[k] = [];
  let lastArrival = 0, interval = 1000;

  function load(m) {
    times = (m.t || []).slice(-CAP);
    for (const k in MAP) data[k] = ((m.data && m.data[k]) || []).slice(-CAP);
    while (data.cpu.length < times.length) for (const k in MAP) data[k].unshift(null);
    lastArrival = performance.now();
  }
  function push(m) {
    if (times.length && m.t <= times[times.length - 1]) return;
    times.push(m.t);
    for (const k in MAP) { const v = MAP[k](m); data[k].push(v === undefined ? null : v); }
    if (times.length > CAP) { times.shift(); for (const k in MAP) data[k].shift(); }
    lastArrival = performance.now();
    interval = m.iv || interval;
  }
  /**
   * Display clock for streaming charts: it trails the newest sample by one interval and
   * advances continuously between samples, so lines glide instead of jumping once a second.
   */
  function clock(smooth) {
    const last = times[times.length - 1];
    if (!last) return Date.now();
    if (!smooth) return last;
    const e = Math.min(performance.now() - lastArrival, interval * 1.5);
    return last - interval + e;
  }
  return { load, push, clock, times: () => times, series: k => data[k] || [], get interval() { return interval; } };
})();

// ------------------------------------------------------------------ animation loop & springs
const Anim = (() => {
  const active = new Set();
  let raf = 0, last = 0;
  function frame(now) {
    // balanced steps animations at ~30 fps: frames without DOM changes cost the compositor nothing
    if (P.perf() !== 'max' && now - last < 28) { raf = requestAnimationFrame(frame); return; }
    const dt = Math.min(1 / 20, (now - last) / 1000 || 1 / 60);
    last = now;
    for (const a of [...active]) {
      let keep = false;
      try { keep = a(dt, now); } catch (e) { console.error(e); }
      if (!keep) active.delete(a);
    }
    raf = active.size ? requestAnimationFrame(frame) : 0;
  }
  function add(fn) {
    active.add(fn);
    if (!raf) { last = performance.now(); raf = requestAnimationFrame(frame); }
  }
  return { add };
})();

/** Critically-ish damped spring. Values settle smoothly and never jump, even mid-flight. */
class Spring {
  constructor(v = 0, onChange, { k = 120, c = 22 } = {}) {
    this.v = v; this.target = v; this.vel = 0; this.k = k; this.c = c; this.onChange = onChange;
    this.running = false;
    this.step = this.step.bind(this);
  }
  set(target) {
    if (target === null || target === undefined || Number.isNaN(target)) return;
    this.target = target;
    const mode = P.anim();
    if (mode === 'off') { this.jump(target); return; }
    // below max, changes too small to see on screen land at once instead of animating
    if (!this.running && P.perf() !== 'max' && Math.abs(this.v - target) < 0.004 * Math.max(1, Math.abs(target))) { this.jump(target); return; }
    if (!this.running && Math.abs(this.v - target) > 1e-4) { this.running = true; Anim.add(this.step); }
  }
  jump(v) { this.v = this.target = v; this.vel = 0; if (this.onChange) this.onChange(v); }
  step(dt) {
    const reduced = P.anim() === 'reduced';
    // balanced settles about 1.7× sooner (same damping ratio), so each tick animates for less time
    const f = P.perf() === 'max' ? 1 : 3, k = this.k * f;
    const c = (reduced ? 2 * Math.sqrt(this.k) * 1.15 : this.c) * Math.sqrt(f); // no overshoot when reduced
    const a = -k * (this.v - this.target) - c * this.vel;
    this.vel += a * dt;
    this.v += this.vel * dt;
    const done = Math.abs(this.vel) < 1e-3 && Math.abs(this.v - this.target) < 1e-3 * Math.max(1, Math.abs(this.target));
    if (done) { this.v = this.target; this.vel = 0; }
    if (this.onChange) this.onChange(this.v);
    this.running = !done;
    return !done;
  }
}

/**
 * Numeric text transition (like iOS .numericText): only the characters that changed move —
 * the old glyph slides out and fades while the new one slides in from the side the value
 * moved toward. Static characters never flicker, so fast-changing values stay readable.
 */
class Odo {
  constructor(el) {
    this.el = el;
    this.el.classList.add('ntx');
    this.text = null;
  }
  set(text) {
    text = String(text);
    if (text === this.text) return;
    const old = this.text;
    this.text = text;
    const animate = old !== null && P.anim() !== 'off';
    const num = s => parseFloat(String(s).replace(/[^\d.,-]/g, '').replace(',', '.')) || 0;
    const dir = animate && num(text) < num(old) ? -1 : 1;
    const reduced = P.anim() === 'reduced';
    this.el.textContent = '';
    const n = text.length, m = old ? old.length : 0;
    for (let i = 0; i < n; i++) {
      const ch = text[i];
      const oc = old && m - (n - i) >= 0 ? old[m - (n - i)] : '';
      const cell = document.createElement('span');
      cell.className = 'ntx-c';
      const inn = document.createElement('span');
      inn.className = 'i';
      inn.textContent = ch;
      cell.appendChild(inn);
      this.el.appendChild(cell);
      if (!animate || oc === ch) continue;
      const dy = reduced ? .2 : .55;
      inn.animate([
        { transform: `translateY(${dir * dy}em) scale(.92)`, opacity: 0, filter: reduced ? 'none' : 'blur(2px)' },
        { transform: 'none', opacity: 1, filter: 'none' },
      ], { duration: 460, easing: 'cubic-bezier(.16,1,.3,1)' });
      if (oc && oc !== ' ') {
        const out = document.createElement('span');
        out.className = 'o';
        out.textContent = oc;
        cell.appendChild(out);
        out.animate([
          { transform: 'none', opacity: 1, filter: 'none' },
          { transform: `translateY(${-dir * dy}em) scale(.92)`, opacity: 0, filter: reduced ? 'none' : 'blur(2px)' },
        ], { duration: 300, easing: 'cubic-bezier(.4,0,1,1)', fill: 'forwards' }).onfinish = () => out.remove();
      }
    }
  }
}

const $ = (sel, root = document) => root.querySelector(sel);
const h = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html !== undefined) e.innerHTML = html; return e; };
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
// ------------------------------------------------------------------ vendor logos (official vector marks)
// Brands with a vector file get the real logo; the rest fall back to a clean wordmark.
const BRAND_FILES = {
  ryzen: { dark: 'logos/ryzen-white.svg', light: 'logos/ryzen-black.svg' },
  nvidia: { dark: 'logos/nvidia-white.svg', light: 'logos/nvidia.svg' },
  intel: { dark: 'logos/intel.svg', light: 'logos/intel.svg' },
  gskill: { dark: 'logos/gskill.png', light: 'logos/gskill.png' },
  samsung: { dark: 'logos/samsung-white.svg', light: 'logos/samsung.svg' },
  msi: { dark: 'logos/msi-white.svg', light: 'logos/msi.svg' },
  realtek: { dark: 'logos/realtek-light.svg', light: 'logos/realtek.svg' },
  qwen: { dark: 'logos/qwen.png', light: 'logos/qwen.png' },
};
// Marks bundled in ui/logos/b so every listed brand has a logo offline (Simple Icons CC0, Wikimedia
// Commons artwork, or a typeset wordmark); "mono" ones show white on dark and near-black on light.
const BRAND_BUNDLED = {
  acer: 'mono',
  adata: 'mono-png',
  aerocool: 'word',
  alphacool: 'word',
  apple: 'mono',
  arctic: 'word',
  asrock: 'mono',
  asus: 'mono',
  bequiet: 'mono',
  broadcom: 'mono',
  chieftec: 'mono-png',
  chuwi: 'word',
  colorful: 'word',
  coolermaster: 'mono',
  corsair: 'mono',
  cougar: 'word',
  crucial: 'word',
  deepcool: 'mono',
  deepseek: 'mono',
  dell: 'mono',
  dynabook: 'mono-png',
  ek: 'word',
  endorfy: 'word',
  enermax: 'mono-png',
  evga: 'mono',
  fractal: 'mono-png',
  framework: 'mono',
  fsp: 'mono-png',
  fujitsu: 'mono-png',
  gainward: 'word',
  galax: 'word',
  gemma: 'mono',
  gigabyte: 'mono',
  gpd: 'mono-png',
  highpower: 'word',
  honor: 'mono',
  hp: 'mono',
  huawei: 'mono-png',
  idcooling: 'word',
  infinix: 'mono',
  inno3d: 'mono',
  jonsbo: 'word',
  kingston: 'mono',
  lenovo: 'mono',
  lexar: 'mono-png',
  lg: 'mono',
  lianli: 'mono',
  llama: 'mono',
  machenike: 'word',
  maibenben: 'word',
  mediatek: 'mono',
  medion: 'mono-png',
  micron: 'mono',
  microsoft: 'color',
  mistral: 'mono',
  montech: 'word',
  noctua: 'mono-png',
  nzxt: 'mono',
  palit: 'mono',
  panasonic: 'mono',
  patriot: 'mono',
  phanteks: 'mono-png',
  phi: 'color',
  pny: 'mono',
  powercolor: 'mono',
  qualcomm: 'mono',
  radeon: 'mono',
  razer: 'mono',
  sandisk: 'mono',
  sapphire: 'word',
  scythe: 'word',
  seagate: 'mono',
  seasonic: 'mono-png',
  silverstone: 'word',
  skhynix: 'mono',
  solidigm: 'word',
  superflower: 'word',
  teamgroup: 'word',
  tecno: 'mono',
  thermalright: 'mono',
  thermaltake: 'mono-png',
  thunderobot: 'word',
  vaio: 'mono-png',
  wd: 'mono',
  xfx: 'mono',
  xiaomi: 'mono',
  xpg: 'word',
  zalman: 'mono-png',
  zotac: 'mono',
};
const BRAND_RULES = [
  ['qwen', /qwen/], ['gemma', /gemma/], ['deepseek', /deepseek/], ['mistral', /mistral|mixtral/], ['llama', /\bllama[-_ ]?\d|meta-llama/], ['phi', /\bphi[-_]?\d/],
  ['ryzen', /ryzen|threadripper/], ['nvidia', /nvidia|geforce|\brtx\b|\bgtx\b|quadro/], ['radeon', /radeon/],
  ['intel', /intel|core\s?(i\d|ultra)|\barc\b|xeon/],
  ['gskill', /g\.?\s?skill|^f[2-5]-\d/], ['corsair', /corsair|^cm[ktwhsg]\d/], ['kingston', /kingston|^kf\d|^khx|^kvr|^ksm|^sa400|^snv/],
  ['crucial', /crucial|^ct\d|^bl\d/], ['samsung', /samsung|^m[34]\d\d[a-z]|^mz[a-z]/], ['skhynix', /hynix|^hm[ac]/], ['micron', /micron|^mt\d/],
  ['teamgroup', /team\s?group|^tf\d|^tlz/], ['adata', /adata|xpg|^ax\d/], ['patriot', /patriot|^pv[ebs]\d/], ['lexar', /lexar/],
  ['wd', /western\s?digital|^wdc|^wd[_ ]?/], ['seagate', /seagate|^st\d{3,}/], ['sandisk', /sandisk/], ['solidigm', /solidigm/],
  ['mediatek', /mediatek/], ['realtek', /realtek/], ['qualcomm', /qualcomm|killer/], ['broadcom', /broadcom/],
  ['msi', /micro-star|^msi\b/], ['asus', /\basus/], ['gigabyte', /gigabyte|aorus/], ['asrock', /asrock/],
];
const BRAND_WORDS = {
  ryzen: 'AMD Ryzen', nvidia: 'NVIDIA', radeon: 'AMD Radeon', intel: 'intel', gskill: 'G.SKILL', corsair: 'CORSAIR', kingston: 'Kingston',
  crucial: 'Crucial', samsung: 'SAMSUNG', skhynix: 'SK hynix', micron: 'Micron', teamgroup: 'TEAMGROUP', adata: 'ADATA', patriot: 'PATRIOT',
  lexar: 'Lexar', wd: 'WD', seagate: 'SEAGATE', sandisk: 'SanDisk', solidigm: 'Solidigm', mediatek: 'MediaTek', realtek: 'Realtek',
  qwen: 'Qwen', gemma: 'Gemma', deepseek: 'DeepSeek', mistral: 'Mistral', llama: 'Llama', phi: 'Phi', qualcomm: 'Qualcomm', broadcom: 'Broadcom', msi: 'MSI', asus: 'ASUS', gigabyte: 'GIGABYTE', asrock: 'ASRock',
};
// Official sites: when a brand has no bundled logo, its site icon is fetched once by the host.
const BRAND_SITES = {
  radeon: 'amd.com', corsair: 'corsair.com', kingston: 'kingston.com', crucial: 'crucial.com', skhynix: 'skhynix.com', micron: 'micron.com',
  teamgroup: 'teamgroupinc.com', adata: 'adata.com', patriot: 'patriotmemory.com', lexar: 'lexar.com', wd: 'westerndigital.com', seagate: 'seagate.com',
  sandisk: 'sandisk.com', solidigm: 'solidigm.com', mediatek: 'mediatek.com', qualcomm: 'qualcomm.com', broadcom: 'broadcom.com',
  asus: 'asus.com', gigabyte: 'gigabyte.com', asrock: 'asrock.com', gemma: 'deepmind.google', deepseek: 'deepseek.com', mistral: 'mistral.ai', llama: 'llama.com',
  palit: 'palit.com', gainward: 'gainward.com', zotac: 'zotac.com', evga: 'evga.com', pny: 'pny.com', galax: 'galax.com', colorful: 'colorful.cn',
  xfx: 'xfxforce.com', powercolor: 'powercolor.com', sapphire: 'sapphiretech.com', inno3d: 'inno3d.com',
  noctua: 'noctua.at', arctic: 'arctic.de', bequiet: 'bequiet.com', lianli: 'lian-li.com', nzxt: 'nzxt.com', deepcool: 'deepcool.com',
  thermalright: 'thermalright.com', coolermaster: 'coolermaster.com', phanteks: 'phanteks.com', fractal: 'fractal-design.com',
  thermaltake: 'thermaltake.com', idcooling: 'idcooling.com', scythe: 'scythe-eu.com', ek: 'ekwb.com', alphacool: 'alphacool.com',
  montech: 'montechpc.com', silverstone: 'silverstonetek.com', endorfy: 'endorfy.com', jonsbo: 'jonsbo.com',
  seasonic: 'seasonic.com', fsp: 'fsplifestyle.com', superflower: 'super-flower.com.tw', chieftec: 'chieftec.eu', xpg: 'xpg.com',
  zalman: 'zalman.com', aerocool: 'aerocool.io', cougar: 'cougargaming.com', enermax: 'enermax.com', highpower: 'highpowertech.com',
};
Object.assign(BRAND_WORDS, {
  palit: 'Palit', gainward: 'Gainward', zotac: 'ZOTAC', evga: 'EVGA', pny: 'PNY', galax: 'GALAX', colorful: 'Colorful', xfx: 'XFX',
  powercolor: 'PowerColor', sapphire: 'SAPPHIRE', inno3d: 'INNO3D',
  noctua: 'Noctua', arctic: 'ARCTIC', bequiet: 'be quiet!', lianli: 'Lian Li', nzxt: 'NZXT', deepcool: 'DeepCool', thermalright: 'Thermalright',
  coolermaster: 'Cooler Master', phanteks: 'Phanteks', fractal: 'Fractal Design', thermaltake: 'Thermaltake', idcooling: 'ID-COOLING',
  scythe: 'Scythe', ek: 'EK', alphacool: 'Alphacool', montech: 'Montech', silverstone: 'SilverStone', endorfy: 'Endorfy', jonsbo: 'JONSBO',
  seasonic: 'Seasonic', fsp: 'FSP', superflower: 'Super Flower', chieftec: 'Chieftec', xpg: 'XPG', zalman: 'Zalman', aerocool: 'Aerocool',
  cougar: 'COUGAR', enermax: 'Enermax', highpower: 'High Power',
});
BRAND_RULES.push(
  ['palit', /palit/], ['gainward', /gainward/], ['zotac', /zotac/], ['evga', /evga/], ['pny', /\bpny\b/], ['galax', /galax|kfa2/],
  ['colorful', /colorful/], ['xfx', /\bxfx\b/], ['powercolor', /powercolor/], ['sapphire', /sapphire/], ['inno3d', /inno3d/]);
// Laptop makers (the battery module and the about page show the computer's model).
Object.assign(BRAND_SITES, {
  lenovo: 'lenovo.com', hp: 'hp.com', dell: 'dell.com', acer: 'acer.com', apple: 'apple.com', huawei: 'huawei.com', honor: 'honor.com',
  xiaomi: 'mi.com', microsoft: 'microsoft.com', razer: 'razer.com', lg: 'lg.com', dynabook: 'dynabook.com', fujitsu: 'fujitsu.com',
  chuwi: 'chuwi.com', infinix: 'infinixmobility.com', tecno: 'tecno-mobile.com', machenike: 'machenike.com', thunderobot: 'thunderobot.com',
  maibenben: 'maibenben.com', medion: 'medion.com', framework: 'frame.work', gpd: 'gpd.hk', vaio: 'vaio.com', panasonic: 'panasonic.com',
});
Object.assign(BRAND_WORDS, {
  lenovo: 'Lenovo', hp: 'HP', dell: 'DELL', acer: 'acer', apple: 'Apple', huawei: 'HUAWEI', honor: 'HONOR', xiaomi: 'Xiaomi',
  microsoft: 'Microsoft', razer: 'RAZER', lg: 'LG', dynabook: 'dynabook', fujitsu: 'Fujitsu', chuwi: 'CHUWI', infinix: 'Infinix',
  tecno: 'TECNO', machenike: 'Machenike', thunderobot: 'Thunderobot', maibenben: 'MAIBENBEN', medion: 'MEDION', framework: 'Framework',
  gpd: 'GPD', vaio: 'VAIO', panasonic: 'Panasonic',
});
BRAND_RULES.push(
  ['lenovo', /lenovo|thinkpad|ideapad|legion|yoga/], ['hp', /^hp\b|hewlett|omen|victus|pavilion|elitebook|probook|spectre|envy/],
  ['dell', /\bdell\b|alienware|latitude|inspiron|vostro|xps/], ['acer', /\bacer\b|aspire|nitro|predator|swift/], ['apple', /\bapple\b|macbook/],
  ['huawei', /huawei|matebook/], ['honor', /\bhonor\b|magicbook/], ['xiaomi', /xiaomi|redmibook|timi/], ['microsoft', /microsoft|surface/],
  ['razer', /razer/], ['lg', /^lg\b|lg electronics/], ['dynabook', /dynabook|toshiba/], ['fujitsu', /fujitsu/], ['chuwi', /chuwi/],
  ['infinix', /infinix/], ['tecno', /tecno/], ['machenike', /machenike/], ['thunderobot', /thunderobot/], ['maibenben', /maibenben/],
  ['medion', /medion/], ['framework', /^framework/], ['gpd', /^gpd\b/], ['vaio', /vaio|sony/], ['panasonic', /panasonic|toughbook/]);
/** Power supply makers offered in settings. */
const PSU_BRANDS = ['seasonic', 'corsair', 'bequiet', 'msi', 'asus', 'coolermaster', 'thermaltake', 'fsp', 'superflower', 'chieftec', 'deepcool',
  'nzxt', 'evga', 'xpg', 'gigabyte', 'silverstone', 'fractal', 'phanteks', 'lianli', 'montech', 'zalman', 'aerocool', 'cougar', 'enermax', 'highpower'];
/** Fan makers offered in settings (fans cannot report their brand electrically). */
const FAN_BRANDS = ['noctua', 'arctic', 'bequiet', 'corsair', 'lianli', 'nzxt', 'deepcool', 'thermalright', 'coolermaster', 'phanteks',
  'fractal', 'thermaltake', 'msi', 'asus', 'gigabyte', 'idcooling', 'scythe', 'ek', 'alphacool', 'montech', 'silverstone', 'endorfy', 'jonsbo'];

function brandOf(text) {
  const n = String(text || '').toLowerCase().trim();
  if (!n || n === 'unknown') return null;
  if (BRAND_WORDS[n]) return n; // already a brand key (user-picked fan / PSU makers)
  for (const [k, re] of BRAND_RULES) if (re.test(n)) return k;
  return null;
}
const brandRequested = new Set();
/** Logo markup: bundled vector/PNG, else the vendor's site icon (fetched once), else a wordmark chip. */
function logoFor(brand, cls) {
  if (!brand) return '';
  // official artwork first, then the marks bundled in ui/logos/b (work offline), then the site icon, then a wordmark
  const bundled = BRAND_BUNDLED[brand];
  const file = bundled ? `logos/b/${brand}.${bundled.endsWith('png') ? 'png' : 'svg'}` : null;
  const f = BRAND_FILES[brand] || (bundled ? { dark: file, light: file, mono: bundled !== 'color' } : null);
  if (f) return `<span class="logo img b-${brand} ${cls || ''}" title="${BRAND_WORDS[brand] || ''}"><img class="lg-d${f.mono ? ' mono' : ''}" src="${f.dark}" alt=""><img class="lg-l${f.mono ? ' monol' : ''}" src="${f.light}" alt=""></span>`;
  const icons = (P.state.info && P.state.info.brandLogos) || {};
  const word = esc(BRAND_WORDS[brand] || brand);
  if (icons[brand]) return `<span class="logo site b-${brand} ${cls || ''}"><img src="${esc(icons[brand])}" alt=""><b>${word}</b></span>`;
  if (!(brand in icons) && BRAND_SITES[brand] && !brandRequested.has(brand)) {
    brandRequested.add(brand);
    P.action('brandLogo', { key: brand, domain: BRAND_SITES[brand] });
  }
  return `<span class="logo txt b-${brand} ${cls || ''}">${word}</span>`;
}const logoHtml = (name, cls) => logoFor(brandOf(name), cls);
/** Aligns a blurred copy of the wallpaper inside a glass element with the real desktop. */
function placeWall(glass, opts) {
  const st = P.state, w = st.wall, geo = st.geo;
  const wall = glass.querySelector('.wall');
  if (w && w.avg) glass.style.setProperty('--wall-avg', w.avg);
  if (!wall || !w || !w.url || !geo || !w.iw) { if (wall) wall.style.display = 'none'; return; }
  wall.style.display = '';
  if (wall.dataset.url !== w.url) { wall.style.backgroundImage = `url("${w.url}")`; wall.dataset.url = w.url; }
  const mon = w.mode === 'span' ? w.virt : w.mon;
  let dw, dh;
  if (w.mode === 'stretch') { dw = mon.w; dh = mon.h; }
  else if (w.mode === 'center') { dw = w.iw; dh = w.ih; }
  else if (w.mode === 'fit') { const k = Math.min(mon.w / w.iw, mon.h / w.ih); dw = w.iw * k; dh = w.ih * k; }
  else { const k = Math.max(mon.w / w.iw, mon.h / w.ih); dw = w.iw * k; dh = w.ih * k; }
  const dx = mon.x + (mon.w - dw) / 2, dy = mon.y + (mon.h - dh) / 2;
  const s = geo.scale || 1;
  const off = (opts && opts.offset) || { x: st.margin, y: st.margin };
  wall.style.width = dw / s + 'px';
  wall.style.height = dh / s + 'px';
  wall.style.transform = `translate3d(${(dx - geo.x) / s - off.x}px, ${(dy - geo.y) / s - off.y}px, 0)`;
}

// ------------------------------------------------------------------ anomalies (widget badges + settings journal)
const fill = (s, ...a) => String(s).replace(/\{(\d)\}/g, (_, i) => a[+i] ?? '');
const exeName = s => /^(vmmem|vmwp)/i.test(String(s || '')) ? 'Docker / WSL' : String(s || '').replace(/\.exe$/i, '');
const AN_ICON = '<svg viewBox="0 0 16 16"><path d="M7.13 2.5a1 1 0 0 1 1.74 0l5.6 9.9a1 1 0 0 1-.87 1.5H2.4a1 1 0 0 1-.87-1.5z" fill="currentColor"/><path d="M8 6.2v3.2M8 11.3v.1" stroke="#000" stroke-opacity=".55" stroke-width="1.6" stroke-linecap="round"/></svg>';
/** Title, sentence and advice for one anomaly, in the current language. */
function anomInfo(a) {
  const t = P.t, p = a.p || {}, f = P.fmt;
  const title = t('an.' + a.kind);
  let text = '', advice = '';
  switch (a.kind) {
    case 'hot':
      text = fill(t('anT.hot'), t('n.' + a.mod), f.temp(p.temp), f.temp(p.norm), fill(t('anBy.' + (p.by || 'power')), p.x), f.temp(p.peak || p.temp));
      advice = t('anA.hot'); break;
    case 'fanStop': text = fill(t('anT.fanStop'), p.fan, p.norm); advice = t('anA.fanStop'); break;
    case 'fanSlow': text = fill(t('anT.fanSlow'), p.fan, p.rpm, p.norm); advice = t('anA.fanSlow'); break;
    case 'idleCpu': case 'idleGpu':
      text = fill(t('anT.' + a.kind), exeName(p.exe), p.proc, p.idle); advice = t(p.upkeep || a.sev === 'info' ? 'anA.upkeep' : 'anA.idle'); break;
    case 'leak': text = fill(t('anT.leak'), exeName(p.exe), f.num(p.gb, 1), f.num(p.rate, 1), p.min); advice = t('anA.leak'); break;
    case 'rail': text = fill(t('anT.rail'), p.rail, f.num(p.v, 2), (p.dev > 0 ? '+' : '') + f.num(p.dev, 1), (p.norm > 0 ? '+' : '') + f.num(p.norm, 1)); advice = t('anA.rail'); break;
    case 'netBad': text = fill(t('anT.netBad'), p.rtt, p.norm, f.num(p.loss, 1)); advice = t('anA.netBad'); break;
    case 'offline': text = t('anT.offline'); advice = t('anA.offline'); break;
    case 'batDrain': text = fill(t('anT.batDrain'), f.num(p.rate, 1), f.num(p.norm, 1)) + (p.exe ? fill(t('anT.batDrainBy'), exeName(p.exe)) : ''); advice = t('anA.batDrain'); break;
  }
  return { title, text, advice };
}

// ------------------------------------------------------------------ metrics a module can headline (widget.js METRICS has the values)
const METRIC_KEYS = {
  cpu: ['load', 'temp', 'clock', 'power'], gpu: ['load', 'temp', 'vram', 'hot', 'memT', 'clock', 'power'], ram: ['load', 'used', 'avail', 'temp'],
  disk: ['act', 'read', 'write', 'temp'], net: ['down', 'up', 'ping'], llm: ['tps', 'vram'], fans: ['max', 'rpm'],
  power: ['wall', 'pcpu', 'pgpu', 'today'], battery: ['pct', 'rate', 'time', 'health'],
};
const METRIC_BOTH = ['cpu', 'gpu', 'ram', 'disk'];
