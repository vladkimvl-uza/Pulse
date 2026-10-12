// Pulse widget page: adaptive dashboard of modules (CPU, GPU, RAM, disks, network).
'use strict';

const stage = $('#stage'), card = $('#card'), content = $('#content'), glass = $('.glass');
const fmt = P.fmt, ok = P.ok, t = P.t;

const ICON = {
  lock: '<svg viewBox="0 0 16 16"><path d="M4.6 7V5.2a3.4 3.4 0 0 1 6.8 0V7" fill="none" stroke="currentColor" stroke-width="1.7"/><rect x="3" y="7" width="10" height="7.5" rx="2.2" fill="currentColor"/></svg>',
  back: '<svg viewBox="0 0 10 10"><path d="M6.4 1.6 3 5l3.4 3.4"/></svg>',
  up: '↑', down: '↓',
  bolt: '<svg viewBox="0 0 10 14"><path d="M6.2 .6.9 8h3.6l-.9 5.4L8.9 6H5.3z" fill="currentColor"/></svg>',
  plug: '<svg viewBox="0 0 14 14"><path d="M4.6 1.2v3M9.4 1.2v3M3 4.2h8v2.4a4 4 0 0 1-8 0zM7 10.6v2.4" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/></svg>',
};
const GREY = 'rgba(142,142,147,.5)';
const TMAX = { cpu: 100, gpu: 95, ram: 70, disk: 80 };
const warnOf = id => {
  const a = P.g().alerts || {};
  const tk = P.state.tick;
  // RAM uses the high limit reported by the DIMM's own thermal sensor (typically 55 °C).
  return id === 'cpu' ? (a.cpuTemp || 90) : id === 'gpu' ? (a.gpuTemp || 85) : id === 'ram' ? ((tk && tk.ram.warn) || 60) : id === 'disk' ? 70 : 90;
};
const color = id => P.moduleColor(id);
const shade = (c, i) => P.css(P.mix(P.rgb(c), [255, 255, 255], Math.min(.75, i * .17)));
const nameSansBrand = n => String(n || '').replace(/^(AMD\s+)?(Radeon\s+)?(Ryzen\s+)?/i, '').replace(/^Intel\s+/i, '').replace(/^GeForce\s+/i, 'GeForce ').trim();
function ramTitle() {
  const mods = P.state.info.ramMods || [];
  const total = P.state.info.ramTotal;
  if (!mods.length) return ok(total) ? fmt.gb(total) : '';
  const m0 = mods[0];
  const same = mods.every(x => x.gb === m0.gb);
  const size = same && mods.length > 1 ? mods.length + '×' + m0.gb + ' ' + P.u('gb') : fmt.gb(mods.reduce((a, x) => a + x.gb, 0));
  return [m0.type && m0.speed ? m0.type + '-' + m0.speed : m0.type, size].filter(Boolean).join(' · ');
}
function nicTitle() {
  const d = P.state.info.nicDesc || (P.state.tick && P.state.tick.net.name) || P.state.info.nic || '';
  return d.replace(/\(R\)|\(TM\)/gi, '').replace(/^(MediaTek|Realtek|Intel|Qualcomm|Killer|Broadcom)\s+/i, '')
    .replace(/\s*(Wireless LAN Card|Wireless Network Adapter|Network Adapter|Network Connection|Ethernet Controller|PCI\(e\)|Family Controller|Controller)\s*/gi, ' ').replace(/\s{2,}/g, ' ').trim();
}
const isp = () => (P.g().isp !== false && P.state.info.isp && P.state.info.isp.name) ? P.state.info.isp : null;
const ispLogo = cls => { const i = isp(); return i && i.logo ? `<span class="logo isp ${cls || ''}"><img src="${esc(i.logo)}" alt=""></span>` : ''; };
const PROC_NAMES = { vmwp: 'Docker / WSL', vmmem: 'Docker / WSL', vmmemWSL: 'Docker / WSL', chrome: 'Chrome', msedge: 'Edge', Code: 'VS Code', explorer: 'Explorer', 'Docker Desktop': 'Docker Desktop', dwm: 'DWM' };
function llmStatus(l) {
  if (!l) return '';
  const st = l.busy > 0 ? 'busy' : l.state;
  return t('llm.' + st);
}
/** Video memory split by process; the WSL/Docker VM is labelled with the AI model it hosts. */
function vramPie(m, highlight) {
  const c = color(highlight === 'vmwp' ? 'llm' : 'gpu'), vt = m.gpu.vt || 1, procs = m.gpu.procs || [];
  const l = m.llm;
  let sum = 0;
  const segs = procs.slice(0, 6).map((p, i) => {
    sum += p.v;
    const vm = p.n === 'vmwp' || p.n.startsWith('vmmem');
    const label = vm && l && l.model ? l.model + ' · Docker' : (PROC_NAMES[p.n] || p.n);
    return { key: p.n, v: p.v / vt, color: vm && highlight === 'vmwp' ? color('llm') : shade(color('gpu'), vm ? 0 : i + 1), label, text: fmt.mb(p.v), icon: P.state.icons[p.n + '.exe'] };
  });
  const used = m.gpu.vu || sum;
  if (used - sum > 64) segs.push({ key: '_other', v: (used - sum) / vt, color: GREY, label: t('other'), text: fmt.mb(used - sum) });
  segs.push({ key: '_free', v: Math.max(0, vt - used) / vt, color: 'transparent', label: t('free'), text: fmt.mb(vt - used) });
  return { segs, big: [fmt.num(used / 1024, 1), P.u('gb')], sub: 'VRAM ' + fmt.pct(m.gpu.vram) + '%' };
}
const psuCfg = () => P.g().psu || {};
const energyText = (kwh, tariff) => ok(kwh) ? fmt.num(kwh, kwh >= 10 ? 1 : 2) + ' ' + t('kwh') + (tariff > 0 ? ' · ' + fmt.num(kwh * tariff, 0) + ' ' + (psuCfg().currency || '') : '') : '–';
function psuTitle() {
  const p = psuCfg();
  return [p.model, p.watt ? p.watt + ' ' + P.u('w') : null, p.rating ? t('r.' + p.rating) : null].filter(Boolean).join(' · ');
}
const bat = m => (m || P.state.tick || {}).battery || null;
/** The computer's maker and model ("Lenovo ThinkPad X1 Carbon Gen 10"); the pack's own name is a detail. */
const pcModel = () => { const d = P.state.info.model || {}; return [d.vendor, d.model].filter(Boolean).join(' '); };
const wh = v => ok(v) ? fmt.num(v, 1) + ' ' + t('wh') : '–';
const batCharging = b => b && b.state === 'charging';
/** Charge colour: the module colour while there is plenty, amber then red as it runs low on battery. */
const batColor = b => !b || b.state !== 'discharging' || b.pct > 30 ? color('battery') : b.pct > 15 ? '#FF9F0A' : '#FF453A';
const healthColor = v => v >= 80 ? '#30D158' : v >= 60 ? '#FFD60A' : '#FF453A';
function batSub(b) {
  if (!b) return '';
  if (b.state === 'charging') return [t('bat.charging'), ok(b.rate) && b.rate > 0 ? fmt.power(b.rate) : null].filter(Boolean).join(' · ');
  if (b.state === 'discharging') return [ok(b.time) ? fmt.uptime(b.time) : null, ok(b.rate) && b.rate > 0 ? fmt.power(b.rate) : null].filter(Boolean).join(' · ') || t('bat.discharging');
  return t('bat.' + b.state) + ' · ' + t('onAc');
}
const fanList = m => { const f = (m || P.state.tick || {}).fans; return f && f.list ? f.list : []; };
/** The fan working hardest (by duty, then rpm) drives the big rotor. */
const topFan = m => fanList(m).slice().sort((a, b) => (b.pct || 0) - (a.pct || 0) || (b.rpm || 0) - (a.rpm || 0))[0];
/** User-assigned brand / model / name for a fan header (settings: global.fanInfo[id]). */
const fanInfo = f => ((P.g().fanInfo || {})[f.id]) || {};
function fanBrand(f) {
  const own = fanInfo(f).brand;
  if (own) return own;
  return f.src === 'gpu' ? brandOf(P.state.info.gpuVendor) : null;
}
const fanLabel = f => fanInfo(f).name || fanName(f.n);
function fanName(n) {
  const s = String(n || '');
  let x;
  if (/^CPU Fan/i.test(s)) return t('fan.cpu');
  if ((x = /^Pump Fan\s*#?(\d*)/i.exec(s))) return t('fan.pump') + (x[1] && x[1] !== '1' ? ' ' + x[1] : '');
  if ((x = /^System Fan\s*#?(\d+)/i.exec(s))) return t('fan.sys') + ' ' + x[1];
  if (/^Chipset/i.test(s)) return t('fan.chipset');
  if ((x = /^GPU Fan\s*#?(\d+)/i.exec(s))) return t('fan.gpu') + ' ' + x[1];
  return s.replace(/\s*Fan\b/i, '');
}
const maxOf = arr => (arr && arr.length ? Math.max.apply(null, arr) : 0);
let lastProcs = null;

/** Splits a formatted value like "12,3 МБ/с" into number and unit for the big display. */
function splitUnit(s) {
  const m = /^([-–\d\s  .,]+?)\s*([^\d\s  .,].*)?$/.exec(s);
  return m ? [m[1].trim(), m[2] || ''] : [s, ''];
}
function peakOf(key, floor) {
  const d = H.series(key), n = Math.max(0, d.length - (P.g().history || 120) / Math.max(.25, H.interval / 1000));
  let m = floor;
  for (let i = n | 0; i < d.length; i++) if (d[i] > m) m = d[i];
  return m;
}

// ------------------------------------------------------------------ module adapters
const MOD = {
  cpu: {
    name: () => P.state.info.cpu || '',
    brands: () => [P.state.info.cpu],
    foot: m => fmt.clock(m.cpu.clock),
    main: m => m.cpu.load,
    big: m => [fmt.pct(m.cpu.load), '%'],
    temp: m => m.cpu.temp,
    tempState: m => ok(m && m.cpu.temp) ? 'ok' : (P.state.sys.elevated && P.state.sys.pawnio ? 'none' : 'lock'),
    second: m => ok(m.cpu.temp) ? { frac: m.cpu.temp / TMAX.cpu, color: P.heat(m.cpu.temp, warnOf('cpu')) } : null,
    series: () => [
      { key: 'cpu', color: color('cpu'), max: 100 },
      { key: 'cpuT', color: () => P.heat(P.state.tick && P.state.tick.cpu.temp, warnOf('cpu')), min: 20, max: TMAX.cpu, fill: false, width: 1.3, dash: [3, 3], dot: false },
    ],
    stats: m => [
      ['clock', fmt.clock(m.cpu.clock)], ['power', fmt.power(m.cpu.power)],
      ['ccd', (m.cpu.ccd || []).filter(ok).map(fmt.temp).join(' · ') || '–'],
      ['cpuFan', (() => { const f = fanList(m).find(x => /^CPU Fan/i.test(x.n)); return f ? (ok(f.pct) ? fmt.pct(f.pct) + '% · ' : '') + fmt.rpm(f.rpm) : '–'; })()],
      ['threads', String((m.cpu.threads || []).length || P.state.info.threads || '–')], ['uptime', fmt.uptime(m.up)],
    ],
    sub: m => [fmt.clock(m.cpu.clock), ok(m.cpu.power) ? fmt.power(m.cpu.power) : null].filter(x => x && x !== '–').join(' · '),
    sig: m => (ok(m.cpu.temp) ? 't' : '') + (ok(m.cpu.power) ? 'p' : '') + (m.cpu.ccd || []).filter(ok).length + (fanList(m).some(x => /^CPU Fan/i.test(x.n)) ? 'f' : ''),
    pie(m) {
      const L = m.cpu.load || 0, c = color('cpu');
      const procs = lastProcs && lastProcs.cpu ? lastProcs.cpu.slice(0, 5) : [];
      let sum = 0;
      const segs = procs.map((p, i) => { sum += p.v; return { key: p.n, v: p.v / 100, color: shade(c, i), label: p.d, text: fmt.num(p.v, 1) + '%', icon: P.state.icons[p.n] }; });
      if (!segs.length) segs.push({ key: '_load', v: L / 100, color: c, label: t('load'), text: fmt.pct(L) + '%' });
      else segs.push({ key: '_other', v: Math.max(0, L - sum) / 100, color: GREY, label: t('other'), text: fmt.num(Math.max(0, L - sum), 1) + '%' });
      return { segs, big: [fmt.pct(L), '%'], sub: '' };
    },
    bars: null,
    extraA: 'threads', extraB: 'procCpu',
  },
  gpu: {
    name: () => P.state.info.gpu || '',
    brands: () => [P.state.info.gpu],
    foot: m => fmt.clock(m.gpu.clock),
    main: m => m.gpu.load,
    big: m => [fmt.pct(m.gpu.load), '%'],
    temp: m => m.gpu.temp,
    tempState: m => ok(m && m.gpu.temp) ? 'ok' : 'none',
    second: m => ok(m.gpu.temp) ? { frac: m.gpu.temp / TMAX.gpu, color: P.heat(m.gpu.temp, warnOf('gpu')) } : null,
    aux: m => ok(m.gpu.vram) ? { frac: m.gpu.vram / 100, label: 'VRAM', text: fmt.pct(m.gpu.vram) + '%' } : null,
    series: () => [
      { key: 'gpu', color: color('gpu'), max: 100 },
      { key: 'vram', color: P.lighten(color('gpu'), .55), max: 100, fill: false, width: 1.4, dot: false },
      { key: 'gpuT', color: () => P.heat(P.state.tick && P.state.tick.gpu.temp, warnOf('gpu')), min: 20, max: TMAX.gpu, fill: false, width: 1.3, dash: [3, 3], dot: false },
    ],
    stats: m => [
      ['clock', fmt.clock(m.gpu.clock)], ['power', fmt.power(m.gpu.power)],
      ['vram', ok(m.gpu.vu) && ok(m.gpu.vt) ? fmt.pct(m.gpu.vram) + '% · ' + fmt.gbPair(m.gpu.vu / 1024, m.gpu.vt / 1024) : '–'],
      ['fan', (ok(m.gpu.fanPct) ? fmt.pct(m.gpu.fanPct) + '% · ' : '') + fmt.rpm(m.gpu.fan)], ['memTemp', fmt.temp(m.gpu.memT)],
    ],
    sub: m => [ok(m.gpu.vram) ? 'VRAM ' + fmt.pct(m.gpu.vram) + '%' : null, fmt.clock(m.gpu.clock), ok(m.gpu.power) ? fmt.power(m.gpu.power) : null].filter(x => x && x !== '–').slice(0, 2).join(' · '),
    sig: m => (ok(m.gpu.temp) ? 't' : '') + (ok(m.gpu.power) ? 'p' : '') + (ok(m.gpu.memT) ? 'm' : '') + (ok(m.gpu.fan) ? 'f' : ''),
    pie(m) {
      const c = color('gpu');
      if (ok(m.gpu.vu) && ok(m.gpu.vt)) {
        const used = m.gpu.vu / m.gpu.vt;
        return {
          segs: [{ key: 'used', v: used, color: c, label: t('used'), text: fmt.mb(m.gpu.vu) }, { key: 'free', v: 1 - used, color: 'transparent', label: t('free'), text: fmt.mb(m.gpu.vt - m.gpu.vu) }],
          big: [fmt.pct(m.gpu.load), '%'], sub: 'VRAM ' + fmt.pct(m.gpu.vram) + '%',
        };
      }
      return { segs: [{ key: 'load', v: (m.gpu.load || 0) / 100, color: c, label: t('load') }], big: [fmt.pct(m.gpu.load), '%'], sub: '' };
    },
    bars: m => [
      { label: t('core'), frac: (m.gpu.load || 0) / 100, text: fmt.pct(m.gpu.load) + '%' },
      { label: t('vram'), frac: (m.gpu.vram || 0) / 100, text: fmt.pct(m.gpu.vram) + '%' },
      { label: t('memCtl'), frac: (m.gpu.mc || 0) / 100, text: fmt.pct(m.gpu.mc) + '%' },
      { label: t('video'), frac: (m.gpu.video || 0) / 100, text: fmt.pct(m.gpu.video) + '%' },
    ],
    extraA: 'vramProcs', extraB: 'gpuTemps',
  },
  ram: {
    name: () => ramTitle(),
    brands: () => (P.state.info.ramMods || []).map(x => brandOf(x.mfr) ? x.mfr : x.part),
    foot: m => fmt.gb(m.ram.used),
    main: m => m.ram.load,
    big: m => [fmt.pct(m.ram.load), '%'],
    temp: m => m.ram.temp,
    tempState: m => ok(m && m.ram.temp) ? 'ok' : 'hide',
    second: m => ok(m.ram.temp) ? { frac: m.ram.temp / TMAX.ram, color: P.heat(m.ram.temp, warnOf('ram')) } : null,
    series: () => [
      { key: 'ram', color: color('ram'), max: 100 },
      { key: 'ramT', color: () => P.heat(P.state.tick && P.state.tick.ram.temp, warnOf('ram')), min: 20, max: TMAX.ram, fill: false, width: 1.3, dash: [3, 3], dot: false },
    ],
    stats: m => [
      ['used', fmt.gbPair(m.ram.used, m.ram.total)],
      ...(m.ram.dimms || []).map(d => ['dimm' + d.n, fmt.temp(d.t), t('dimm') + ' ' + d.n]),
      ['avail', fmt.gb(m.ram.avail)],
      ['commit', ok(m.ram.vused) && ok(m.ram.vtotal) ? fmt.gbPair(m.ram.vused, m.ram.vtotal) : '–'],
    ],
    sub: m => fmt.gbPair(m.ram.used, m.ram.total),
    sig: m => (ok(m.ram.temp) ? 't' : '') + (m.ram.dimms || []).length,
    pie(m) {
      const c = color('ram'), total = m.ram.total || 1, used = m.ram.used || 0;
      const procs = lastProcs && lastProcs.mem ? lastProcs.mem.slice(0, 5) : [];
      let sum = 0;
      const segs = procs.map((p, i) => { sum += p.v; return { key: p.n, v: p.v / total, color: shade(c, i), label: p.d, text: fmt.gb(p.v), icon: P.state.icons[p.n] }; });
      segs.push({ key: '_sys', v: Math.max(0, used - sum) / total, color: GREY, label: t('system'), text: fmt.gb(Math.max(0, used - sum)) });
      return { segs, big: [fmt.pct(m.ram.load), '%'], sub: fmt.gb(used) };
    },
    bars: m => [
      { label: t('used'), frac: (m.ram.load || 0) / 100, text: fmt.gb(m.ram.used) },
      { label: t('commit'), frac: (m.ram.vload || 0) / 100, text: fmt.pct(m.ram.vload) + '%' },
    ],
    extraA: 'memPie', extraB: 'procMem',
  },
  disk: {
    name: () => (P.state.info.disks || []).map(d => (d.kind ? d.kind + ' ' : '') + fmt.size(d.gb)).join(' + '),
    brands: () => (P.state.info.disks || []).map(d => d.name),
    foot: m => ICON.down + ' ' + fmt.speed(m.disk.read, true),
    main: m => m.disk.act,
    big: m => [fmt.pct(m.disk.act), '%'],
    temp: m => m.disk.temp,
    tempState: m => ok(m && m.disk.temp) ? 'ok' : 'hide',
    second: m => ok(m.disk.temp) ? { frac: m.disk.temp / TMAX.disk, color: P.heat(m.disk.temp, warnOf('disk')) } : null,
    series: () => [
      { key: 'diskR', color: color('disk'), max: 'auto' },
      { key: 'diskW', color: P.lighten(color('disk'), .5), max: 'auto', fillAlpha: .14 },
    ],
    autoFloor: 1048576,
    stats: m => [['read', fmt.speed(m.disk.read, true)], ['write', fmt.speed(m.disk.write, true)], ['activity', fmt.pct(m.disk.act) + '%']],
    sub: m => ICON.down + ' ' + fmt.speed(m.disk.read, true) + '  ' + ICON.up + ' ' + fmt.speed(m.disk.write, true),
    sig: m => (ok(m.disk.temp) ? 't' : '') + (m.disk.drives || []).length,
    pie(m) {
      const c = color('disk'), drives = m.disk.drives || [];
      const total = drives.reduce((a, d) => a + d.total, 0) || 1, used = drives.reduce((a, d) => a + d.total - d.free, 0);
      const segs = drives.map((d, i) => ({ key: d.n, v: (d.total - d.free) / total, color: shade(c, i), label: d.n + (d.label ? ' ' + d.label : ''), text: fmt.size(d.total - d.free) }));
      return { segs, big: [fmt.pct(used / total * 100), '%'], sub: fmt.size(total - used) + ' ' + t('free').toLowerCase() };
    },
    bars: m => (m.disk.drives || []).slice(0, 4).map(d => ({ label: d.n, frac: 1 - d.free / d.total, text: fmt.size(d.total - d.free) })),
    extraA: 'drives', extraB: null,
  },
  net: {
    name: () => { const i = isp(); return i ? i.name + (i.city ? ' · ' + i.city : '') : nicTitle(); },
    brands: () => [],
    foot: () => '',
    main: m => m.net.down,
    big: m => splitUnit(fmt.speed(m.net.down)),
    temp: () => null,
    tempState: () => 'net',
    second: m => ({ frac: (m.net.up || 0) / peakOf('netU', 65536), color: P.lighten(color('net'), .5) }),
    mainFrac: m => (m.net.down || 0) / peakOf('netD', 262144),
    series: () => [
      { key: 'netD', color: color('net'), max: 'auto' },
      { key: 'netU', color: P.lighten(color('net'), .55), max: 'auto', fillAlpha: .12 },
    ],
    autoFloor: 131072,
    stats: m => [['down', fmt.speed(m.net.down)], ['up', fmt.speed(m.net.up)], ['adapter', nicTitle() || '–']],
    sub: m => ICON.up + ' ' + fmt.speed(m.net.up),
    sig: () => { const i = isp(); return i ? i.name + (i.logo || '') : ''; },
    pie(m) {
      const c = color('net'), d = m.net.down || 0, u = m.net.up || 0, s = d + u || 1;
      return {
        segs: [{ key: 'd', v: d / s, color: c, label: t('down'), text: fmt.speed(d) }, { key: 'u', v: u / s, color: P.lighten(c, .55), label: t('up'), text: fmt.speed(u) }],
        big: splitUnit(fmt.speed(d + u)), sub: '',
      };
    },
    bars: m => [
      { label: t('down'), frac: (m.net.down || 0) / peakOf('netD', 262144), text: fmt.speed(m.net.down) },
      { label: t('up'), frac: (m.net.up || 0) / peakOf('netU', 65536), text: fmt.speed(m.net.up) },
    ],
    extraA: null, extraB: null,
  },
  llm: {
    name: () => { const l = P.state.tick && P.state.tick.llm; return l && (l.title || l.model) ? (l.title || l.model) + (l.quant ? ' · ' + l.quant : '') : ''; },
    brands: () => { const l = P.state.tick && P.state.tick.llm; return l ? [l.file || l.model] : []; },
    foot: m => m.llm && ok(m.llm.vram) ? 'VRAM ' + fmt.mb(m.llm.vram) : '',
    main: m => m.llm && m.llm.busy > 0 ? m.llm.tps : 0,
    big: m => {
      const l = m.llm;
      if (!l || l.state === 'offline' || l.state === 'unloaded') return ['–', ''];
      const v = l.busy > 0 ? l.tps : (ok(l.tpsLast) ? l.tpsLast : l.tpsAvg);
      return [ok(v) ? fmt.num(v, v < 10 ? 1 : 0) : '–', t('tokS')];
    },
    mainFrac: m => { const l = m.llm; return l && l.busy > 0 ? l.tps / Math.max(10, l.peak || 0, (l.tpsAvg || 0) * 1.4) : 0; },
    temp: () => null,
    tempState: () => 'llm',
    second: m => m.llm && ok(m.llm.vram) && ok(m.gpu.vt) ? { frac: m.llm.vram / m.gpu.vt, color: P.lighten(color('llm'), .5) } : null,
    series: () => [{ key: 'llm', color: color('llm'), max: 'auto' }],
    autoFloor: 20,
    stats: m => {
      const l = m.llm || {};
      return [
        ['genSpeed', ok(l.tpsAvg) ? fmt.num(l.tpsAvg, 1) + ' ' + t('tokS') + ' ' + t('avgShort') : '–'],
        ['promptSpeed', ok(l.ppAvg) ? fmt.num(l.ppAvg, 0) + ' ' + t('tokS') : '–'],
        ['active', l.slots ? l.busy + ' / ' + l.slots + (l.queued ? ' +' + l.queued : '') : '–'],
        ['llmVram', ok(l.vram) ? fmt.mb(l.vram) : '–'],
        ['context', l.ctx ? fmt.num(l.ctx / 1024, 0) + 'K' : '–'],
        ['tokens', ok(l.predicted) ? fmt.num(l.predicted, 0) : '–'],
      ];
    },
    sub: () => MOD.llm.name(),
    sig: m => m.llm ? (m.llm.state === 'offline' ? 'off' : 'on') + (m.llm.model || '') : 'none',
    pie: m => Object.assign(vramPie(m, 'vmwp'), { big: MOD.llm.big(m) }),
    bars: m => {
      const l = m.llm || {};
      return [
        { label: t('genSpeed'), frac: MOD.llm.mainFrac(m), text: l.busy > 0 ? fmt.num(l.tps || 0, 0) + ' ' + t('tokS') : '–' },
        { label: t('llmVram'), frac: ok(l.vram) && ok(m.gpu.vt) ? l.vram / m.gpu.vt : 0, text: ok(l.vram) ? fmt.mb(l.vram) : '–' },
        { label: t('active'), frac: l.slots ? l.busy / l.slots : 0, text: l.slots ? l.busy + ' / ' + l.slots : '–' },
      ];
    },
    extraA: 'vramProcs', extraB: 'models',
  },
  fans: {
    name: () => { const n = fanList().length; return n ? n + ' ' + t('fansCount') : ''; },
    brands: () => [],
    foot: m => { const f = topFan(m); return f && ok(f.rpm) ? fmt.rpm(f.rpm) : ''; },
    main: m => m.fans && m.fans.max,
    big: m => [fmt.pct(m.fans && m.fans.max), '%'],
    temp: () => null,
    tempState: () => 'hide',
    second: () => null,
    series: () => [{ key: 'fan', color: color('fans'), max: 100 }],
    stats: (m, tier) => {
      const list = fanList(m);
      // The detailed view already lists every fan next to its rotor, so the stats summarise.
      if (tier === 'xl') {
        const pcts = list.filter(f => ok(f.pct)).map(f => f.pct), spinning = list.filter(f => f.rpm > 0);
        return [
          ['fanMax', pcts.length ? fmt.pct(Math.max(...pcts)) + '%' : '–', t('fanMax')],
          ['fanAvg', pcts.length ? fmt.pct(pcts.reduce((a, b) => a + b, 0) / pcts.length) + '%' : '–', t('fanAvg')],
          ['fanRpm', spinning.length ? fmt.rpm(spinning.reduce((a, f) => a + f.rpm, 0) / spinning.length) : '–', t('fanRpmAvg')],
          ['fanCount', spinning.length + ' / ' + list.length, t('fanCount')],
        ];
      }
      return list.map(f => ['fan:' + f.id, (ok(f.pct) ? fmt.pct(f.pct) + '% · ' : '') + fmt.rpm(f.rpm), fanLabel(f), logoFor(fanBrand(f), 'mini')]);
    },
    sub: m => { const f = topFan(m); return f ? fanLabel(f) + ' · ' + fmt.rpm(f.rpm) : ''; },
    sig: m => String(fanList(m).length) + JSON.stringify(P.g().fanInfo || {}).length + Object.keys(P.state.info.brandLogos || {}).length,
    pie: m => ({ segs: [{ key: 'max', v: (m.fans && m.fans.max || 0) / 100, color: color('fans') }], big: MOD.fans.big(m), sub: '' }),
    bars: m => fanList(m).slice(0, 5).map(f => ({ label: fanLabel(f), frac: (f.pct || 0) / 100, text: ok(f.pct) ? fmt.pct(f.pct) + '%' : fmt.rpm(f.rpm) })),
    extraA: 'fanList', extraB: null,
  },
  power: {
    name: () => psuTitle(),
    brands: () => { const b = psuCfg().brand; return b ? [b] : []; },
    foot: m => m.power && ok(m.power.load) ? t('psuLoad') + ' ' + fmt.pct(m.power.load * 100) + '%' : '',
    main: m => m.power && m.power.wall,
    big: m => [m.power && ok(m.power.wall) ? fmt.num(m.power.wall, 0) : '–', P.u('w')],
    mainFrac: m => m.power && ok(m.power.load) ? m.power.load : 0,
    temp: () => null,
    tempState: () => 'power',
    second: () => null,
    series: () => [{ key: 'pwr', color: color('power'), max: 'auto' }],
    autoFloor: 150,
    stats: m => {
      const p = m.power || {}, tariff = psuCfg().tariff;
      return [
        ['pCpu', fmt.power(p.cpu)], ['pGpu', fmt.power(p.gpu)], ['pRest', ok(p.rest) ? '≈ ' + fmt.power(p.rest) : '–'],
        ['today', energyText(p.today, tariff)], ['month', energyText(p.month, tariff)],
        ['eff', ok(p.eff) ? '≈ ' + fmt.num(p.eff, 0) + '%' : '–'], ['psuLoad', ok(p.load) ? fmt.pct(p.load * 100) + '% · ' + p.watt + ' ' + P.u('w') : '–'],
        ['total', energyText(p.total, tariff)],
      ];
    },
    sub: m => { const p = m.power || {}; return ok(p.cpu) || ok(p.gpu) ? 'CPU ' + fmt.num(p.cpu || 0, 0) + ' · GPU ' + fmt.num(p.gpu || 0, 0) + ' ' + P.u('w') : ''; },
    sig: m => String(m.power ? (m.power.rails || []).length : 0) + JSON.stringify(psuCfg()) + Object.keys(P.state.info.brandLogos || {}).length,
    pie: m => {
      const p = m.power || {}, wall = p.wall || 1;
      const segs = [
        { key: 'cpu', v: (p.cpu || 0) / wall, color: color('cpu'), label: t('pCpu'), text: fmt.power(p.cpu) },
        { key: 'gpu', v: (p.gpu || 0) / wall, color: color('gpu'), label: t('pGpu'), text: fmt.power(p.gpu) },
        { key: 'rest', v: (p.rest || 0) / wall, color: GREY, label: t('pRest'), text: '≈ ' + fmt.power(p.rest) },
        { key: 'loss', v: Math.max(0, wall - (p.dc || 0)) / wall, color: color('power'), label: t('pLoss'), text: '≈ ' + fmt.power(Math.max(0, wall - (p.dc || 0))) },
      ];
      return { segs, big: MOD.power.big(m), sub: t('fromWall') };
    },
    bars: m => MOD.power.pie(m).segs.map(s => ({ label: s.label, frac: s.v, text: s.text })),
    extraA: 'powerPie', extraB: 'rails',
  },
  battery: {
    name: () => pcModel() || (() => { const b = bat(); return b ? [b.maker, b.name].filter(Boolean).join(' ') : ''; })(),
    brands: () => { const d = P.state.info.model || {}; return d.vendor ? [d.vendor] : []; },
    foot: m => batSub(bat(m)),
    main: m => bat(m) && bat(m).pct,
    mainColor: m => batColor(bat(m)),
    big: m => { const b = bat(m); return [b && ok(b.pct) ? fmt.pct(b.pct) : '–', '%']; },
    temp: () => null,
    tempState: () => 'battery',
    // the inner ring is the pack's health: how much of its design capacity it still holds
    second: m => { const b = bat(m); return b && ok(b.health) ? { frac: b.health / 100, color: healthColor(b.health) } : null; },
    series: () => [
      { key: 'bat', color: color('battery'), max: 100 },
      { key: 'batW', color: P.lighten(color('battery'), .5), max: 'auto', fill: false, width: 1.3, dash: [3, 3], dot: false },
    ],
    autoFloor: 20,
    stats: m => {
      const b = bat(m) || {}, chg = batCharging(b);
      return [
        ['batRate', ok(b.rate) && b.rate > 0 ? fmt.power(b.rate) : '–', t(chg ? 'batIn' : 'batOut')],
        ['batTime', ok(b.time) && b.time > 0 ? fmt.uptime(b.time) : '–', t(chg ? 'batToFull' : 'batLeft')],
        ['batHealth', ok(b.health) ? fmt.pct(b.health) + '%' : '–'],
        ['batCycles', ok(b.cycles) ? String(b.cycles) : '–'],
        ['batCap', ok(b.full) ? fmt.num(b.full, 1) + (ok(b.design) ? ' / ' + fmt.num(b.design, 1) : '') + ' ' + t('wh') : '–'],
        ['batVolt', ok(b.volt) ? fmt.num(b.volt, 2) + ' ' + P.u('v') : '–'],
      ];
    },
    sub: m => batSub(bat(m)),
    sig: m => { const b = bat(m); return b ? b.state + (ok(b.time) && b.time > 0 ? 't' : '') + (ok(b.rate) && b.rate > 0 ? 'r' : '') + (ok(b.cycles) ? 'c' : '') + (ok(b.health) ? 'h' : '') : 'none'; },
    pie: m => {
      const b = bat(m) || {}, cap = b.design || b.full || 1, full = b.full || cap, rem = ok(b.rem) ? b.rem : full * (b.pct || 0) / 100;
      const segs = [
        { key: 'rem', v: rem / cap, color: batColor(b), label: t('batCharge'), text: wh(rem) },
        { key: 'used', v: Math.max(0, full - rem) / cap, color: 'transparent', label: t('batUsed'), text: wh(full - rem) },
      ];
      if (b.design && b.full && b.design > b.full) segs.push({ key: 'wear', v: (b.design - b.full) / cap, color: GREY, label: t('batWear'), text: wh(b.design - b.full) });
      return { segs, big: MOD.battery.big(m), sub: ok(b.health) ? t('batHealth') + ' ' + fmt.pct(b.health) + '%' : '' };
    },
    bars: m => {
      const b = bat(m) || {};
      return [
        { label: t('batCharge'), frac: (b.pct || 0) / 100, text: ok(b.pct) ? fmt.pct(b.pct) + '%' : '–' },
        { label: t('batHealth'), frac: (b.health || 0) / 100, text: ok(b.health) ? fmt.pct(b.health) + '%' : '–' },
        { label: t(batCharging(b) ? 'batIn' : 'batOut'), frac: (b.rate || 0) / peakOf('batW', 20), text: ok(b.rate) && b.rate > 0 ? fmt.power(b.rate) : '–' },
      ];
    },
    extraA: 'batPie', extraB: 'batInfo',
  },
};

// ------------------------------------------------------------------ selectable metrics
/**
 * What a module can put in its headline (and its capsule cell): per module a list of metrics,
 * each with the big number, the fill of the main ring / gauge / meter (null = keep the module's own)
 * and an optional colour. A widget's module may pick one ("main"; "cap" for the capsule).
 */
const tempBig = v => ok(v) ? [String(Math.round(P.g().tempUnit === 'F' ? v * 9 / 5 + 32 : v)), '°'] : ['–', '°'];
const numBig = (v, d, unit) => ok(v) ? [fmt.num(v, d), unit] : ['–', unit];
const heatOf = (v, id) => P.heat(v, warnOf(id));
const METRICS = {
  cpu: {
    load: { big: m => [fmt.pct(m.cpu.load), '%'], frac: m => (m.cpu.load || 0) / 100 },
    temp: { big: m => tempBig(m.cpu.temp), frac: m => (m.cpu.temp || 0) / TMAX.cpu, color: m => heatOf(m.cpu.temp, 'cpu'), avail: m => ok(m.cpu.temp) },
    clock: { big: m => splitUnit(fmt.clock(m.cpu.clock)), frac: null, avail: m => ok(m.cpu.clock) },
    power: { big: m => numBig(m.cpu.power, 0, P.u('w')), frac: m => (m.cpu.power || 0) / peakOf('cpuP', 60), avail: m => ok(m.cpu.power) },
  },
  gpu: {
    load: { big: m => [fmt.pct(m.gpu.load), '%'], frac: m => (m.gpu.load || 0) / 100 },
    temp: { big: m => tempBig(m.gpu.temp), frac: m => (m.gpu.temp || 0) / TMAX.gpu, color: m => heatOf(m.gpu.temp, 'gpu'), avail: m => ok(m.gpu.temp) },
    vram: { big: m => [fmt.pct(m.gpu.vram), '%'], frac: m => (m.gpu.vram || 0) / 100, avail: m => ok(m.gpu.vram) },
    hot: { big: m => tempBig(m.gpu.hot), frac: m => (m.gpu.hot || 0) / 105, color: m => heatOf(m.gpu.hot, 'gpu'), avail: m => ok(m.gpu.hot) },
    memT: { big: m => tempBig(m.gpu.memT), frac: m => (m.gpu.memT || 0) / 105, color: m => heatOf(m.gpu.memT, 'gpu'), avail: m => ok(m.gpu.memT) },
    clock: { big: m => splitUnit(fmt.clock(m.gpu.clock)), frac: null, avail: m => ok(m.gpu.clock) },
    power: { big: m => numBig(m.gpu.power, 0, P.u('w')), frac: m => (m.gpu.power || 0) / peakOf('gpuP', 80), avail: m => ok(m.gpu.power) },
  },
  ram: {
    load: { big: m => [fmt.pct(m.ram.load), '%'], frac: m => (m.ram.load || 0) / 100 },
    used: { big: m => splitUnit(fmt.gb(m.ram.used)), frac: m => (m.ram.load || 0) / 100 },
    avail: { big: m => splitUnit(fmt.gb(m.ram.avail)), frac: m => 1 - (m.ram.load || 0) / 100 },
    temp: { big: m => tempBig(m.ram.temp), frac: m => (m.ram.temp || 0) / TMAX.ram, color: m => heatOf(m.ram.temp, 'ram'), avail: m => ok(m.ram.temp) },
  },
  disk: {
    act: { big: m => [fmt.pct(m.disk.act), '%'], frac: m => (m.disk.act || 0) / 100 },
    read: { big: m => splitUnit(fmt.speed(m.disk.read, true)), frac: m => (m.disk.read || 0) / peakOf('diskR', 1048576) },
    write: { big: m => splitUnit(fmt.speed(m.disk.write, true)), frac: m => (m.disk.write || 0) / peakOf('diskW', 1048576) },
    temp: { big: m => tempBig(m.disk.temp), frac: m => (m.disk.temp || 0) / TMAX.disk, color: m => heatOf(m.disk.temp, 'disk'), avail: m => ok(m.disk.temp) },
  },
  net: {
    down: { big: m => splitUnit(fmt.speed(m.net.down)), frac: m => (m.net.down || 0) / peakOf('netD', 262144) },
    up: { big: m => splitUnit(fmt.speed(m.net.up)), frac: m => (m.net.up || 0) / peakOf('netU', 65536) },
    ping: { big: m => { const q = m.net.q, s = q && (q.target === 'isp' ? q.isp : q.inet); return s && ok(s.rtt) ? [String(Math.round(s.rtt)), t('ms')] : ['–', t('ms')]; }, frac: null, avail: m => !!(m.net.q) },
  },
  llm: {
    tps: { big: m => MOD.llm.big(m), frac: m => MOD.llm.mainFrac(m) },
    vram: { big: m => m.llm && ok(m.llm.vram) ? splitUnit(fmt.mb(m.llm.vram)) : ['–', ''], frac: m => m.llm && ok(m.llm.vram) && ok(m.gpu.vt) ? m.llm.vram / m.gpu.vt : 0 },
  },
  fans: {
    max: { big: m => [fmt.pct(m.fans && m.fans.max), '%'], frac: m => ((m.fans && m.fans.max) || 0) / 100 },
    rpm: { big: m => { const f = topFan(m); return f && ok(f.rpm) ? [fmt.num(f.rpm, 0), P.u('rpm')] : ['–', P.u('rpm')]; }, frac: null },
  },
  power: {
    wall: { big: m => MOD.power.big(m), frac: m => m.power && ok(m.power.load) ? m.power.load : 0 },
    pcpu: { big: m => numBig(m.power && m.power.cpu, 0, P.u('w')), frac: m => m.power && m.power.wall ? (m.power.cpu || 0) / m.power.wall : 0 },
    pgpu: { big: m => numBig(m.power && m.power.gpu, 0, P.u('w')), frac: m => m.power && m.power.wall ? (m.power.gpu || 0) / m.power.wall : 0 },
    today: { big: m => numBig(m.power && m.power.today, 2, t('kwh')), frac: null },
  },
  battery: {
    pct: { big: m => MOD.battery.big(m), frac: m => ((bat(m) || {}).pct || 0) / 100 },
    rate: { big: m => numBig((bat(m) || {}).rate, 1, P.u('w')), frac: m => ((bat(m) || {}).rate || 0) / peakOf('batW', 20) },
    time: { big: m => { const b = bat(m) || {}; return ok(b.time) && b.time > 0 ? splitUnit(fmt.uptime(b.time)) : ['–', '']; }, frac: null },
    health: { big: m => { const b = bat(m) || {}; return ok(b.health) ? [fmt.pct(b.health), '%'] : ['–', '%']; }, frac: m => ((bat(m) || {}).health || 0) / 100, color: m => healthColor((bat(m) || {}).health || 0) },
  },
};
/** Modules whose capsule cell can show the main value and the temperature side by side. */
const CAP_BOTH = { cpu: 'load', gpu: 'load', ram: 'load', disk: 'act' };
const modCfg = id => ((P.widget() || {}).modules || []).find(x => x.id === id) || {};
/** The metric a module shows in its headline in this widget (null = the module's default view). */
const mainMetric = id => { const k = modCfg(id).main; return k && METRICS[id] && METRICS[id][k] ? k : null; };

function tierFor(w, h) {
  if (h < 84) return 'row';
  if (w < 118 || h < 112) return 'xs';
  if (w >= 420 && h >= 290) return 'xl';
  if (w >= 300 && h >= 230) return 'l';
  if (w >= 250 && w >= h * 1.45) return 'm';
  return 's';
}

/** Stack of labelled meters, used by the "bars" style for modules other than CPU. */
class MeterStack {
  constructor(c) { this.el = h('div', 'viz mstack'); this.c = c; this.rows = []; }
  set(items) {
    if (this.rows.length !== items.length) {
      this.el.textContent = '';
      this.rows = items.map(() => {
        const r = h('div', 'mrow');
        r.innerHTML = '<div><span></span><b class="num"></b></div>';
        const m = new MeterViz(this.c);
        r.appendChild(m.el);
        this.el.appendChild(r);
        return { lb: r.querySelector('span'), vl: r.querySelector('b'), m };
      });
    }
    items.forEach((it, i) => { const r = this.rows[i]; r.lb.textContent = it.label; r.vl.textContent = it.text; r.m.set(it.frac); });
  }
}

// ------------------------------------------------------------------ tile
class Tile {
  constructor(id, expanded) {
    this.id = id;
    this.expanded = !!expanded;
    this.el = h('div', 'tile');
    this.el.dataset.mod = id;
    this.key = '';
    this.p = null;
    this.charts = [];
    this.fresh = true;
  }
  style() {
    const cfg = P.widget();
    const m = cfg && (cfg.modules || []).find(x => x.id === this.id);
    let s = (m && m.style) || 'auto';
    if (s === 'auto') s = this.id === 'net' ? 'chart' : 'ring';
    return s;
  }
  fit(w, hh) {
    this.w = w; this.h = hh;
    const tier = this.expanded ? 'xl' : tierFor(w, hh);
    const tick = P.state.tick;
    const mod = MOD[this.id];
    const key = [tier, this.style(), mainMetric(this.id), tick ? mod.sig(tick) + mod.tempState(tick) : '-', P.lang(), color(this.id), P.g().tempUnit, P.g().netUnit, P.g().logos !== false, mod.name(), JSON.stringify(mod.brands ? mod.brands() : [])].join('|');
    if (key === this.key) return;
    this.key = key;
    this.tier = tier;
    this.render();
  }
  destroy() { this.charts.forEach(c => c.destroy()); this.charts = []; }

  render() {
    this.destroy();
    const id = this.id, mod = MOD[id], st = this.style(), tier = this.tier, tick = P.state.tick;
    const c = color(id);
    this.el.className = `tile t-${tier} st-${st}` + (id === 'net' || id === 'llm' || this.wordMetric(tick) ? ' wordunit' : '') + (this.expanded ? ' expanded' : '') + (this.el.classList.contains('enter') ? ' enter' : '');
    this.el.style.setProperty('--c', c);
    this.el.style.setProperty('--c2', P.lighten(c, .4));
    const brands = P.g().logos !== false && mod.brands ? [...new Set(mod.brands().map(brandOf).filter(Boolean))] : [];
    const lb = `<span class="lb">${esc(t('m.' + id))}</span>`;
    const capLabel = id === 'net' && isp() ? ispLogo('cap') + `<span class="lb">${esc(isp().name)}</span>`
      : id === 'battery' && tier === 's' ? lb // the word matters more than the maker in a small tile
      : brands.length && (id === 'llm' || id === 'power' || id === 'battery') ? logoFor(brands[0], 'cap') + lb
      : brands.length && ['cpu', 'gpu', 'ram'].includes(id) ? logoFor(brands[0], 'cap') : lb;
    const tp = '<span class="tp num"></span>';
    const big = '<span class="big num"><span data-big></span><small data-unit></small></span>';
    const nm = (id === 'net' ? ispLogo('sm') : '') + brands.map(b => logoFor(b, 'sm')).join('') + `<span class="nm">${esc(brands.length && (id === 'cpu' || id === 'gpu') ? nameSansBrand(mod.name()) : mod.name())}</span>`;
    const back = this.expanded ? `<span class="back" data-nodrag data-back>${ICON.back}</span>` : '';
    const hasViz = ['ring', 'gauge', 'pie'].includes(st);
    let html;

    if (tier === 'row') {
      html = `${lb}${big}${tp}<div class="mid" data-slot="mini"></div>`;
    } else if (tier === 'xs') {
      html = `<div class="hd">${lb}<span class="sp"></span>${tp}</div>${big}${mod.aux ? '<span class="aux num"></span>' : ''}<div class="foot" data-slot="mini"></div>`;
    } else if (tier === 's') {
      if (hasViz) html = `<div data-slot="prim" class="prim"></div><div class="cap">${capLabel}${tp}</div><div class="sub"></div>`;
      else if (st === 'chart') html = `<div class="hd">${lb}${tp}</div>${big}${mod.aux ? '<span class="aux num"></span>' : ''}<div data-slot="chart"></div>`;
      else if (st === 'bars') html = `<div class="hd">${lb}${tp}</div>${big}${mod.aux ? '<span class="aux num"></span>' : ''}<div data-slot="bars" class="fill"></div>`;
      else html = `<div class="hd">${lb}${tp}</div>${big}<div class="sub"></div>`;
    } else if (tier === 'm') {
      if (hasViz) html = `<div data-slot="prim" class="prim"></div><div class="side"><div class="hd">${lb}${nm}${tp}</div><div class="st"></div><div data-slot="spark"></div></div>`;
      else if (st === 'bars') html = `<div class="numbox"><div class="hd">${lb}${tp}</div>${big}<div class="sub"></div></div><div data-slot="bars"></div>`;
      else html = `<div class="numbox"><div class="hd">${lb}${tp}</div>${big}<div class="sub"></div></div>` + (st === 'chart' ? '<div data-slot="chart"></div>' : '<div class="st"></div>');
    } else if (tier === 'l') {
      const head = `<div class="hd">${lb}${nm}${tp}</div>`;
      if (hasViz) html = `${head}<div class="row1"><div data-slot="prim" class="prim"></div><div class="st"></div></div><div data-slot="chart"></div>`;
      else if (st === 'bars') html = `${head}<div class="row1">${big}</div><div data-slot="bars" class="fill"></div><div data-slot="chart"></div>`;
      else html = `${head}<div class="row1">${big}<div class="st"></div></div><div data-slot="chart"></div>`;
    } else {
      const head = `<div class="hd">${back}${lb}${nm}${tp}</div>`;
      const primSlot = hasViz ? '<div data-slot="prim" class="prim"></div>' : st === 'bars' && id !== 'cpu' ? '<div data-slot="bars" class="prim"></div>' : `<div class="prim numprim">${big}</div>`;
      html = `${head}${primSlot}<div class="st"></div><div class="xa" data-x="a"></div><div data-slot="chart"></div><div class="xb" data-x="b"></div>`;
    }
    this.el.innerHTML = `<div class="in">${html}</div>`;
    const p = this.p = { st };
    const q = s => this.el.querySelector(s);

    // primary visual
    if (q('[data-slot="prim"]')) {
      let viz;
      if (id === 'fans' && (st === 'ring' || st === 'pie')) {
        viz = new FanViz(c);
        viz.center.innerHTML = big;
      } else if (st === 'ring') {
        viz = mod.aux
          ? new RingViz([{ width: 9, color: c, color2: P.lighten(c, .45), gap: 2 }, { width: 6.5, color: P.lighten(c, .55), color2: P.lighten(c, .75), gap: 2 }, { width: 5, color: P.lighten(c, .5) }])
          : new RingViz([{ width: 9.5, color: c, color2: P.lighten(c, .45) }, { width: 6.5, color: P.lighten(c, .5), gap: 2.4 }]);
        viz.center.innerHTML = big + '<span class="sub2"></span>';
      } else if (st === 'gauge') {
        viz = new GaugeViz(c);
        viz.center.innerHTML = big;
      } else {
        viz = new DonutViz(tier === 'xl' ? 12 : 13);
        viz.center.innerHTML = big + '<span class="sub2"></span>';
      }
      p.viz = viz;
      this.mount('prim', viz.el);
    }
    // charts
    const chartSlot = q('[data-slot="chart"]') || q('[data-slot="spark"]');
    if (chartSlot && id === 'power' && tier === 'xl') {
      p.energy = new EnergyViz(c);
      this.mount('chart', p.energy.el);
    } else if (chartSlot) {
      const spark = chartSlot.dataset.slot === 'spark';
      const series = mod.series().filter(s => s.key.endsWith('T') ? ok(tick && mod.temp(tick)) : true);
      const ch = new ChartViz(spark ? series.slice(0, 1) : series, { grid: !spark, autoFloor: mod.autoFloor, padTop: spark ? 3 : 8 });
      this.charts.push(ch);
      this.mount(chartSlot.dataset.slot, ch.el);
    }
    const mini = q('[data-slot="mini"]');
    if (mini) {
      if (st === 'chart') {
        const ch = new ChartViz(mod.series().slice(0, 1), { grid: false, autoFloor: mod.autoFloor, padTop: 2, padBot: 1 });
        this.charts.push(ch);
        mini.appendChild(ch.el);
      } else {
        p.meter = new MeterViz(c);
        mini.appendChild(p.meter.el);
        if (mod.aux) { p.auxMeter = new MeterViz(P.lighten(c, .55)); p.auxMeter.el.classList.add('aux'); mini.appendChild(p.auxMeter.el); }
      }
    }
    const barsSlot = q('[data-slot="bars"]');
    if (barsSlot) {
      if (id === 'cpu') p.threads = new ThreadBars((tick && tick.cpu.threads.length) || P.state.info.threads || 16, c);
      else p.mstack = new MeterStack(c);
      this.mount('bars', (p.threads || p.mstack).el);
    }
    // extras for the detailed tier
    if (tier === 'xl') {
      const xa = q('[data-x="a"]'), xb = q('[data-x="b"]');
      let hasA = false, hasB = false;
      if (mod.extraA === 'threads' && !p.threads) { p.xThreads = new ThreadBars((tick && tick.cpu.threads.length) || P.state.info.threads || 16, c); xa.appendChild(p.xThreads.el); hasA = true; }
      else if (mod.extraA === 'threads') { hasA = false; }
      else if (mod.extraA === 'vramProcs' || mod.extraA === 'memPie' || mod.extraA === 'powerPie' || mod.extraA === 'batPie') {
        p.xDonut = new DonutViz(14); p.xLegend = h('div', 'legend');
        p.xDonut.center.innerHTML = '<span class="big num" data-xbig></span><span class="sub2"></span>';
        xa.append(p.xDonut.el, p.xLegend); hasA = true;
      } else if (mod.extraA === 'fanList') { p.xFans = new FanList(c); xa.appendChild(p.xFans.el); xa.style.alignItems = 'stretch'; hasA = true;
      } else if (mod.extraA === 'drives') { p.xDrives = new ListViz(); xa.appendChild(p.xDrives.el); xa.style.alignItems = 'stretch'; hasA = true; }
      if (mod.extraB === 'procCpu' || mod.extraB === 'procMem') {
        xb.innerHTML = `<div class="cap">${esc(t(mod.extraB === 'procCpu' ? 'topCpu' : 'topMem'))}</div>`;
        p.xList = new ListViz(); xb.appendChild(p.xList.el); hasB = true;
      } else if (mod.extraB === 'gpuTemps') {
        xb.innerHTML = `<div class="cap">${esc(t('temp'))}</div>`;
        p.xList = new ListViz(); xb.appendChild(p.xList.el); hasB = true;
      } else if (mod.extraB === 'rails') {
        xb.innerHTML = `<div class="cap">${esc(t('rails'))}</div>`;
        p.xRails = new ListViz(); xb.appendChild(p.xRails.el); hasB = true;
      } else if (mod.extraB === 'batInfo') {
        xb.innerHTML = `<div class="cap">${esc(t('batDetails'))}</div>`;
        p.xBat = new ListViz(); xb.appendChild(p.xBat.el); hasB = true;
      } else if (mod.extraB === 'models') {
        xb.innerHTML = `<div class="cap">${esc(t('modelsList'))}</div>`;
        p.xModels = h('div', 'mlist'); xb.appendChild(p.xModels); hasB = true;
      }
      // Connection numbers live only in the expanded view; tiles show just the bars.
      if (id === 'net' && this.expanded) {
        xb.innerHTML = '';
        p.qpanel = h('div', 'qpanel');
        xb.appendChild(p.qpanel);
        p.qchart = new ChartViz([{ key: 'ping', color: '#30D158', max: 'auto', fillAlpha: .18 }], { grid: false, autoFloor: 10, padTop: 3 });
        this.charts.push(p.qchart);
        hasB = true;
      }
      if (!hasA && !hasB) this.el.classList.add('nox');
      else if (!hasA && hasB && id === 'net') this.el.classList.add('noxa');
      else if (!hasB) this.el.classList.add('noxb');
      else if (!hasA) xa.remove();
    }
    p.big = q('[data-big]') ? new Odo(q('[data-big]')) : null;
    p.unit = q('[data-unit]');
    p.tp = q('.tp');
    p.sub = q('.sub');
    p.sub2 = q('.sub2');
    p.aux = q('.aux.num');
    p.st = q('.st:not(.mstack)');
    if (p.st && tick) {
      const rows = mod.stats(tick, tier).filter(r => r[1] && r[1] !== '–');
      p.statKeys = rows.map(r => r[0]);
      p.st.innerHTML = rows.slice(0, tier === 'm' ? 3 : 5).map(r => `<div><span>${r[3] || ''}${esc(r[2] || t(r[0]))}</span><b></b></div>`).join('');
      p.statEls = [...p.st.querySelectorAll('b')];
    }
    if (p.tp && p.tp.classList.contains('lock') === false && mod.tempState(tick) === 'lock') {
      p.tp.addEventListener('click', e => { e.stopPropagation(); P.action('openSettings', { section: 'sensors' }); });
      p.tp.dataset.nodrag = '';
    }
    const backBtn = q('[data-back]');
    if (backBtn) backBtn.addEventListener('click', e => { e.stopPropagation(); Wd.collapse(); });

    if (tick) {
      if (!this.fresh) this.update(tick, true);
      else requestAnimationFrame(() => this.update(tick)); // first appearance sweeps in
    }
    this.fresh = false;
  }

  mount(slot, el) {
    const s = this.el.querySelector(`[data-slot="${slot}"]`);
    if (!s) return;
    for (const c of s.classList) el.classList.add(c);
    s.replaceWith(el);
  }

  update(m, jump) {
    const p = this.p;
    if (!m || !p) return;
    const id = this.id, mod = MOD[id], st = p.st, c = color(id);
    const temp = mod.temp(m), tstate = mod.tempState(m), warn = warnOf(id);
    // the metric picked for this widget (headline + main ring), else the module's own view
    const mk = mainMetric(id), met = mk ? METRICS[id][mk] : null;
    const mainFrac = met && met.frac ? met.frac(m) : mod.mainFrac ? mod.mainFrac(m) : (mod.main(m) || 0) / 100;

    // headline number
    const [num, unit] = met ? met.big(m) : st === 'pie' ? mod.pie(m).big : mod.big(m);
    if (p.big) p.big.set(num);
    if (p.unit && p.unit.textContent !== unit) p.unit.textContent = unit;

    // temperature / upload chip
    if (p.tp) {
      const tp = p.tp;
      if (tstate === 'ok') {
        tp.className = 'tp num' + (this.tier === 'l' || this.tier === 'xl' || this.tier === 'm' ? ' pill' : '') + (temp >= warn ? ' alert' : '');
        tp.style.setProperty('--heat', P.heat(temp, warn));
        tp.textContent = fmt.temp(temp);
      } else if (tstate === 'lock') {
        tp.className = 'tp lock';
        if (!tp.dataset.locked) { tp.innerHTML = ICON.lock + '<span>–°</span>'; tp.dataset.locked = '1'; tp.title = t('lockedTemp'); }
      } else if (tstate === 'net') {
        const q = m.net.q;
        if (q && P.g().netQuality !== false) {
          if (!p.qbars) { p.qbars = new SignalBars(); tp.textContent = ''; tp.className = 'tp qwrap'; tp.appendChild(p.qbars.el); }
          const sc = q.offline ? 0 : q.score;
          p.qbars.set(sc, t('q.' + sc));
        } else {
          if (p.qbars) { p.qbars = null; }
          tp.className = 'tp none num';
          tp.textContent = ICON.up + ' ' + fmt.speed(m.net.up, true);
        }
      } else if (tstate === 'power') {
        const pw = m.power;
        if (pw && (pw.rails || []).length) {
          if (!p.qbars) { p.qbars = new SignalBars(); tp.textContent = ''; tp.className = 'tp qwrap'; tp.appendChild(p.qbars.el); }
          p.qbars.set(pw.score, t('stab.' + pw.score));
        } else { tp.className = 'tp none'; tp.textContent = ''; }
      } else if (tstate === 'llm') {
        const l = m.llm;
        const st = !l ? 'offline' : l.busy > 0 ? 'busy' : l.state;
        tp.className = 'tp llm' + (st === 'busy' ? ' busy' : '') + (this.tier === 'l' || this.tier === 'xl' || this.tier === 'm' ? ' pill' : '');
        const col = st === 'busy' ? 'var(--green)' : st === 'loading' ? 'var(--orange)' : st === 'offline' ? 'var(--red)' : 'var(--text-3)';
        tp.style.setProperty('--st', col);
        tp.style.setProperty('--heat', col);
        const txt = t('llm.' + st);
        if (tp.textContent !== txt) tp.textContent = txt;
        const bigEl = this.el.querySelector('.big');
        if (bigEl) bigEl.classList.toggle('idle', st !== 'busy');
      } else if (tstate === 'battery') {
        const b = bat(m), state = b ? b.state : 'none';
        const col = !b ? 'var(--text-3)' : state === 'charging' || state === 'full' ? 'var(--green)' : state === 'hold' ? 'var(--orange)' : batColor(b) === color('battery') ? 'var(--text-3)' : batColor(b);
        tp.className = 'tp batt ' + state + (this.tier === 'l' || this.tier === 'xl' || this.tier === 'm' ? ' pill' : '');
        tp.style.setProperty('--st', col);
        tp.style.setProperty('--heat', col);
        const html = !b ? '' : (state === 'charging' ? ICON.bolt : state === 'discharging' ? '' : ICON.plug) + `<span>${esc(t('bat.' + state))}</span>`;
        if (tp.dataset.h !== html) { tp.innerHTML = html; tp.dataset.h = html; }
      } else if (tstate === 'none') {
        tp.className = 'tp none';
        tp.textContent = '';
      } else {
        tp.className = 'tp none';
        tp.textContent = '';
      }
    }
    this.el.classList.toggle('alert', tstate === 'ok' && temp >= warn);
    const an = this.anomaly();

    // primary visual
    const viz = p.viz;
    if (viz instanceof FanViz) {
      const f = topFan(m);
      viz.set(m.fans && m.fans.max, f && f.rpm);
    } else if (viz instanceof RingViz) {
      const mcol = met && met.color ? met.color(m) : mod.mainColor ? mod.mainColor(m) : mk ? c : null;
      if (mcol) viz.color(0, mcol, P.lighten(mcol, .45));
      viz.set(0, mainFrac);
      const aux = mod.aux ? mod.aux(m) : null, ti = mod.aux ? 2 : 1;
      if (aux) viz.set(1, aux.frac);
      // a temperature in the headline puts the load on the inner ring instead
      const sec = met && met.color && mk !== 'health' ? { frac: mod.mainFrac ? mod.mainFrac(m) : (mod.main(m) || 0) / 100, color: P.lighten(c, .35) } : mod.second(m);
      viz.dim(ti, !sec);
      if (sec) { viz.color(ti, sec.color, sec.color); viz.set(ti, sec.frac); }
      if (p.sub2) p.sub2.textContent = id === 'ram' && this.tier !== 's' ? fmt.gb(m.ram.used) : '';
    } else if (viz instanceof GaugeViz) {
      viz.set(mainFrac);
      const aux = mod.aux ? mod.aux(m) : null;
      if (aux) viz.setAux(aux.frac, P.lighten(c, .55));
      const foot = aux ? aux.label + ' ' + aux.text : mod.foot ? mod.foot(m) : '';
      if (viz.foot.textContent !== foot) viz.foot.textContent = foot;
    } else if (viz instanceof DonutViz) {
      const pie = mod.pie(m);
      viz.set(pie.segs);
      if (p.sub2) p.sub2.textContent = pie.sub || '';
    }
    if (p.meter) p.meter.set(mainFrac);
    if (mod.aux) {
      const aux = mod.aux(m);
      if (aux && p.auxMeter) p.auxMeter.set(aux.frac);
      if (aux && p.aux) { const s = aux.label + ' ' + aux.text; if (p.aux.textContent !== s) p.aux.textContent = s; }
    }
    if (p.threads) p.threads.set(m.cpu.threads);
    if (p.mstack && mod.bars) p.mstack.set(mod.bars(m));
    if (p.sub) {
      const s = an ? an.title : mod.sub(m);
      if (p.sub.textContent !== s) p.sub.textContent = s;
      p.sub.classList.toggle('an', !!an);
    }
    if (p.statEls) {
      const rows = mod.stats(m, this.tier);
      p.statEls.forEach((el, i) => {
        const r = rows.find(x => x[0] === p.statKeys[i]);
        const v = r ? r[1] : '–';
        if (el.textContent !== v) el.textContent = v;
      });
    }
    // detailed extras
    if (p.xThreads) p.xThreads.set(m.cpu.threads);
    if (p.xDonut) {
      let pie = mod.extraA === 'vramProcs' ? vramPie(m, id === 'llm' ? 'vmwp' : null) : mod.pie(m);
      p.xDonut.set(pie.segs);
      const xb = p.xDonut.center.querySelector('[data-xbig]');
      xb.innerHTML = esc(pie.big[0]) + `<small>${esc(pie.big[1])}</small>`;
      p.xDonut.center.querySelector('.sub2').textContent = pie.sub || '';
      p.xLegend.innerHTML = pie.segs.filter(s => s.color !== 'transparent').slice(0, 6)
        .map(s => `<div><i style="background:${s.color}"></i><span>${esc(s.label)}</span><b>${esc(s.text || '')}</b></div>`).join('');
    }
    if (p.energy && m.power) p.energy.set(m.power, psuCfg().tariff, psuCfg().currency);
    if (p.xRails && m.power) {
      const rails = (m.power.rails || []).map(r => ({
        key: r.n, label: r.n.replace('V', ' ' + P.u('v')), v: Math.max(.08, 1 - Math.abs(r.dev) / 5),
        color: r.s >= 3 ? 'var(--green)' : r.s === 2 ? 'var(--yellow)' : 'var(--red)',
        text: fmt.num(r.v, 2) + ' ' + P.u('v') + ' · ' + (r.dev >= 0 ? '+' : '') + fmt.num(r.dev, 1) + '%',
      }));
      if (ok(m.power.battery)) rails.push({ key: 'bat', label: t('battery'), v: Math.min(1, m.power.battery / 3.3), color: m.power.battery > 2.8 ? 'var(--green)' : 'var(--orange)', text: fmt.num(m.power.battery, 2) + ' ' + P.u('v') });
      p.xRails.set(rails, 1);
    }
    if (p.xFans) {
      p.xFans.set(fanList(m).map(f => ({
        key: f.id, label: fanLabel(f), rpm: f.rpm, pct: f.pct, color: f.src === 'gpu' ? color('gpu') : c,
        brand: logoFor(fanBrand(f), 'mini'), model: fanInfo(f).model || '',
        rpmText: fmt.rpm(f.rpm), pctText: ok(f.pct) ? fmt.pct(f.pct) + '%' : '',
      })));
    }
    if (p.xBat) {
      const b = bat(m) || {}, cap = b.design || b.full || 1, items = [];
      if (pcModel()) items.push({ key: 'model', label: t('batModel'), text: pcModel(), v: 0 });
      if (ok(b.design)) items.push({ key: 'design', label: t('batDesign'), text: wh(b.design), v: 1, color: GREY });
      if (ok(b.full)) items.push({ key: 'full', label: t('batFullCap'), text: wh(b.full), v: b.full / cap, color: ok(b.health) ? healthColor(b.health) : c });
      if (ok(b.rem)) items.push({ key: 'rem', label: t('batNow'), text: wh(b.rem), v: b.rem / cap, color: batColor(b) });
      if (ok(b.cycles)) items.push({ key: 'cycles', label: t('batCycles'), text: String(b.cycles), v: 0 });
      if (ok(b.volt)) items.push({ key: 'volt', label: t('batVolt'), text: fmt.num(b.volt, 2) + ' ' + P.u('v'), v: 0 });
      const pack = [b.maker, b.name, b.chem].filter(Boolean).join(' · ');
      if (pack) items.push({ key: 'pack', label: t('batPack'), text: pack, v: 0 });
      p.xBat.set(items, 1);
    }
    if (p.xModels && m.llm) {
      const html = (m.llm.models || []).map(x => `<div class="${x.state === 'loaded' ? 'on' : ''}"><i></i><span>${esc(x.id)}${x.file ? ' · ' + esc(x.file.replace(/\.gguf$/i, '')) : ''}</span></div>`).join('');
      if (p.xModels.dataset.h !== html) { p.xModels.innerHTML = html; p.xModels.dataset.h = html; }
    }
    if (p.qpanel) this.updateQuality(m.net.q);
    requestAnimationFrame(() => this.fitText());
    if (p.xDrives) {
      const ds = m.disk.drives || [];
      p.xDrives.set(ds.map(d => ({ key: d.n, label: d.n + (d.label ? ' · ' + d.label : ''), text: fmt.size(d.free) + ' ' + t('free').toLowerCase(), v: 1 - d.free / d.total, color: c })), 1);
    }
    if (p.xList) {
      if (MOD[id].extraB === 'gpuTemps') {
        const rows = [['core', m.gpu.temp], ['memTemp', m.gpu.memT], ['hot', m.gpu.hot]].filter(r => ok(r[1]));
        p.xList.set(rows.map(r => ({ key: r[0], label: t(r[0]), text: fmt.temp(r[1]), v: r[1] / TMAX.gpu, color: P.heat(r[1], warn) })), 1);
      } else if (lastProcs) {
        const cpuList = MOD[id].extraB === 'procCpu';
        const src = (cpuList ? lastProcs.cpu : lastProcs.mem) || [];
        const top = src.slice(0, Math.max(3, Math.floor((this.h - 120) / 22)));
        const max = cpuList ? Math.max(1, ...top.map(x => x.v)) : Math.max(0.01, ...top.map(x => x.v));
        p.xList.set(top.map((x, i) => ({
          key: x.n, label: x.d, icon: P.state.icons[x.n], v: x.v, color: shade(c, Math.min(i, 2)),
          text: cpuList ? fmt.num(x.v, 1) + '%' : fmt.gb(x.v),
        })), max);
      }
    }
  }

  /** A picked metric whose unit is a word (ГГц, МБ/с, об/мин…) sits under the number in round visuals. */
  wordMetric(tick) {
    const mk = mainMetric(this.id);
    if (!mk || !tick) return false;
    try { return String(METRICS[this.id][mk].big(tick)[1] || '').length > 2; } catch (_) { return false; }
  }

  /**
   * Something unusual about this module (see AnomalyService): an amber rim, and a pill in the
   * header in place of the model name; a click opens the explanation in settings.
   */
  anomaly() {
    const a = (P.state.anom || []).filter(x => x.mod === this.id).sort((x, y) => (x.sev === 'warn' ? 0 : 1) - (y.sev === 'warn' ? 0 : 1))[0];
    const info = a ? anomInfo(a) : null;
    const sig = a ? a.key + a.sev + P.lang() : '';
    this.el.classList.toggle('anom', !!a && a.sev === 'warn');
    this.el.classList.toggle('anom-info', !!a && a.sev !== 'warn');
    const hd = this.el.querySelector('.hd');
    let pill = hd && hd.querySelector('.anpill');
    if (!a || !hd) { if (pill) pill.remove(); this.anSig = ''; return info; }
    if (!pill) {
      pill = h('span', 'anpill');
      pill.dataset.nodrag = '';
      pill.addEventListener('click', e => { e.stopPropagation(); P.action('openSettings', { section: 'anomaly' }); });
      hd.insertBefore(pill, hd.querySelector('.tp'));
      this.anSig = '';
    }
    if (this.anSig !== sig) {
      this.anSig = sig;
      pill.innerHTML = AN_ICON + `<span>${esc(info.title)}</span>`;
    }
    pill.title = info.text;
    return info;
  }

  /** Shrinks numbers that do not fit their box (long units, narrow tiles). */
  fitText() {
    for (const el of this.el.querySelectorAll('.big, .aux.num, .sub, .tp.llm, .gauge-foot, .lb, .st b, .fanlist .nm')) {
      const sig = el.textContent.length + ':' + (el.parentElement ? el.parentElement.clientWidth : 0);
      if (el.dataset.fit === sig) continue;
      el.dataset.fit = sig;
      el.style.zoom = '';
      const avail = el.clientWidth, need = el.scrollWidth;
      const floor = el.matches('.st b, .lb, .sub') ? .72 : .5;
      if (avail > 0 && need > avail + 1) el.style.zoom = Math.max(floor, avail / need * 0.98).toFixed(3);
    }
  }

  updateQuality(q) {
    const p = this.p;
    if (!q) { p.qpanel.textContent = ''; return; }
    if (!p.qhead) {
      p.qpanel.innerHTML = '<div class="qh"></div>';
      p.qhead = p.qpanel.firstChild;
      p.qbig = new SignalBars();
      p.qhead.appendChild(p.qbig.el);
      p.qtext = h('span');
      p.qhead.appendChild(p.qtext);
      p.qrows = {};
      for (const k of ['isp', 'gw', 'inet']) {
        const cells = [h('span', 'k'), h('span', 'ip'), h('b'), h('b', 'dim'), h('b', 'dim')];
        cells.forEach(c => p.qpanel.appendChild(c));
        p.qrows[k] = cells;
      }
      p.qpanel.appendChild(p.qchart.el);
    }
    const sc = q.offline ? 0 : q.score;
    p.qbig.set(sc);
    p.qtext.textContent = t('q.' + sc);
    const names = { isp: isp() ? isp().name : t('provider'), gw: t('router'), inet: t('internet') };
    for (const k of ['isp', 'gw', 'inet']) {
      const s = q[k], cells = p.qrows[k];
      const show = !!s;
      cells.forEach(c => { c.style.display = show ? '' : 'none'; });
      if (!s) continue;
      cells[0].textContent = names[k];
      cells[1].textContent = s.ip;
      cells[2].textContent = s.down ? '—' : ok(s.rtt) ? fmt.num(s.rtt, s.rtt < 10 ? 1 : 0) + ' ' + t('ms') : '—';
      cells[3].textContent = '±' + fmt.num(s.jit || 0, 1);
      cells[4].textContent = fmt.num(s.loss || 0, 0) + '%';
    }
  }

  needsProcs() {
    if (this.id !== 'cpu' && this.id !== 'ram') return false;
    return this.style() === 'pie' || this.tier === 'xl';
  }
}

// ------------------------------------------------------------------ widget controller
const Wd = {
  tiles: new Map(),
  order: [],
  expanded: null,
  arrangement: '',
  procs: null,

  sync() {
    const cfg = P.widget();
    if (!cfg) return;
    stage.style.opacity = cfg.opacity ?? 1;
    applyBg(cfg.bgOpacity ?? 1);
    card.classList.toggle('locked', !!cfg.locked);
    const wasCollapsed = document.body.classList.contains('collapsed');
    const collapsed = !!cfg.collapsed;
    document.body.classList.toggle('collapsed', collapsed);
    document.body.classList.remove('folding');
    const wMin = document.querySelector('.wctl .min'), wClose = document.querySelector('.wctl .close');
    wClose.title = t('wcClose'); wMin.title = t(collapsed ? 'wcExpand' : 'wcMin');
    wMin.textContent = collapsed ? '' : ''; // the maximize glyph brings the full widget back
    if (collapsed) Capsule.build((cfg.modules || []).filter(m => m.on && MOD[m.id]).map(m => m.id));
    if (wasCollapsed && !collapsed) { for (const tl of this.tiles.values()) { tl.el.classList.remove('enter'); void tl.el.offsetWidth; tl.el.classList.add('enter'); tl.el.addEventListener('animationend', () => tl.el.classList.remove('enter'), { once: true }); } if (P.state.tick) setTimeout(() => this.tick(P.state.tick), 0); }
    const ids = (cfg.modules || []).filter(m => m.on).map(m => m.id).filter(id => MOD[id]);
    for (const [id, tile] of this.tiles) {
      if (!ids.includes(id)) {
        this.tiles.delete(id);
        tile.el.classList.add('out');
        setTimeout(() => { tile.destroy(); tile.el.remove(); layout(); }, 380);
      }
    }
    ids.forEach((id, i) => {
      if (!this.tiles.has(id)) {
        const tile = new Tile(id);
        tile.el.classList.add('enter');
        tile.el.style.setProperty('--d', (i * 70) + 'ms');
        tile.el.addEventListener('animationend', () => tile.el.classList.remove('enter'), { once: true });
        this.tiles.set(id, tile);
      }
    });
    const changedOrder = ids.join() !== this.order.join();
    this.order = ids;
    if (changedOrder) {
      const before = new Map([...this.tiles].map(([id, tl]) => [id, tl.el.isConnected ? tl.el.getBoundingClientRect() : null]));
      ids.forEach(id => content.appendChild(this.tiles.get(id).el));
      if (this.expanded) content.appendChild(this.expanded.el);
      layout(true);
      flip(before);
    } else layout(true);
    if (this.expanded && !ids.includes(this.expanded.id)) this.collapse();
    this.updateNeed();
  },

  expand(id) {
    if (this.expanded) { this.collapse(); return; }
    const src = this.tiles.get(id);
    if (!src) return;
    const tile = new Tile(id, true);
    this.expanded = tile;
    content.appendChild(tile.el);
    const rc = content.getBoundingClientRect();
    tile.fit(rc.width, rc.height);
    content.classList.add('receded');
    this.clipAnim(tile, src, false);
    this.updateNeed();
  },

  collapse() {
    const tile = this.expanded;
    if (!tile) return;
    this.expanded = null;
    content.classList.remove('receded');
    const src = this.tiles.get(tile.id);
    const done = () => { tile.destroy(); tile.el.remove(); this.updateNeed(); };
    if (!src || P.anim() === 'off') { done(); return; }
    this.clipAnim(tile, src, true).onfinish = done;
  },

  clipAnim(tile, src, reverse) {
    const rc = content.getBoundingClientRect(), r0 = src.el.getBoundingClientRect();
    const rad = getComputedStyle(src.el).borderRadius;
    const from = `inset(${r0.top - rc.top}px ${rc.right - r0.right}px ${rc.bottom - r0.bottom}px ${r0.left - rc.left}px round ${rad})`;
    const to = `inset(0px 0px 0px 0px round ${rad})`;
    const frames = [{ clipPath: from, opacity: .55 }, { clipPath: to, opacity: 1 }];
    const inner = tile.el.querySelector('.in');
    if (inner) inner.animate([{ opacity: 0, transform: 'scale(.97)' }, { opacity: 1, transform: 'none' }],
      { duration: reverse ? 220 : 520, delay: reverse ? 0 : 90, easing: 'cubic-bezier(.16,1,.3,1)', fill: 'both', direction: reverse ? 'reverse' : 'normal' });
    return tile.el.animate(frames, { duration: reverse ? 420 : 620, easing: 'cubic-bezier(.16,1,.3,1)', fill: 'both', direction: reverse ? 'reverse' : 'normal' });
  },

  updateNeed() {
    let need = false;
    for (const tl of this.tiles.values()) if (tl.needsProcs()) need = true;
    if (this.expanded && this.expanded.needsProcs()) need = true;
    if (need !== this.procs) { this.procs = need; P.send('need', { procs: need }); }
  },

  tick(m) {
    if (m.procs && m.procs.ready) lastProcs = m.procs;
    if (document.body.classList.contains('collapsed')) { Capsule.update(m); return; } // tiles are hidden; they catch up on expand
    for (const tl of this.tiles.values()) {
      if (!tl.p) continue;
      const mod = MOD[tl.id];
      const sigNow = mod.sig(m) + mod.tempState(m);
      if (!tl.key.includes('|' + sigNow + '|')) tl.fit(tl.w, tl.h); // sensor availability changed
      tl.update(m);
    }
    if (this.expanded) this.expanded.update(m);
  },
};

// ------------------------------------------------------------------ capsule: the minimized widget
/**
 * A minimized widget is a slim capsule with the headline number of up to four modules (a dot in
 * the module colour, the value, and an amber dot when something is unusual). A click expands it.
 */
const Capsule = {
  el: $('#capsule'),
  sig: '',
  cells: new Map(),
  /** What a module shows in the capsule: its own pick ("cap"), else the headline metric, else the first one. */
  key(id) {
    const c = modCfg(id).cap;
    if (c === 'both' && CAP_BOTH[id]) return 'both';
    if (c && METRICS[id] && METRICS[id][c]) return c;
    return mainMetric(id) || Object.keys(METRICS[id] || { x: 0 })[0];
  },
  build(ids) {
    const want = ids.filter(id => METRICS[id]).slice(0, 4);
    const sig = want.map(id => id + ':' + this.key(id)).join() + P.lang();
    if (sig === this.sig && this.cells.size) return;
    this.sig = sig;
    this.el.textContent = '';
    this.cells.clear();
    this.el.insertAdjacentHTML('beforeend', '<span class="cp-logo"><svg viewBox="0 0 20 20"><circle cx="10" cy="10" r="8" style="stroke:var(--cpu)" stroke-dasharray="38 13" transform="rotate(-90 10 10)"/><circle cx="10" cy="10" r="5" style="stroke:var(--gpu)" stroke-dasharray="22 10" transform="rotate(-90 10 10)"/></svg></span>');
    for (const id of want) {
      const k = this.key(id);
      const c = h('span', 'cm' + (k === 'both' ? ' both' : ''));
      c.style.setProperty('--c', color(id));
      c.title = t('m.' + id) + ' · ' + t(k === 'both' ? 'mt.both' : 'mt.' + k);
      c.innerHTML = '<i></i><b class="num"></b><small></small><em class="num"></em>';
      this.el.appendChild(c);
      this.cells.set(id, { el: c, key: k, b: new Odo(c.querySelector('b')), u: c.querySelector('small'), t2: c.querySelector('em') });
    }
    if (P.state.tick) this.update(P.state.tick);
  },
  update(m) {
    for (const [id, c] of this.cells) {
      const k = c.key, met = METRICS[id][k === 'both' ? CAP_BOTH[id] : k];
      const [num, unit] = met.big(m);
      c.b.set(num);
      if (c.u.textContent !== unit) c.u.textContent = unit;
      // "both": the value and, beside it, the temperature in its heat colour
      const temp = k === 'both' ? METRICS[id].temp : met.color ? met : null;
      const tv = k === 'both' && METRICS[id].temp.avail(m) ? METRICS[id].temp.big(m).join('') : '';
      if (c.t2.textContent !== tv) c.t2.textContent = tv;
      c.el.style.setProperty('--heat', temp && temp.color ? temp.color(m) : 'inherit');
      c.el.classList.toggle('an', (P.state.anom || []).some(a => a.mod === id && a.sev === 'warn'));
    }
  },
};

/** Minimize: the card folds away, then the host shrinks the window to the capsule. */
function minimize(on) {
  if (on) {
    document.body.classList.add('folding');
    setTimeout(() => { P.action('widgetMin', { id: P.state.id, on: true }); }, P.anim() === 'off' ? 0 : 200);
  } else P.action('widgetMin', { id: P.state.id, on: false });
}
document.querySelector('.wctl').addEventListener('click', e => {
  const b = e.target.closest('[data-wc]');
  if (!b) return;
  e.stopPropagation();
  if (b.dataset.wc === 'close') P.action('widgetClose', { id: P.state.id });
  else minimize(!document.body.classList.contains('collapsed'));
});
document.querySelector('.wctl').addEventListener('pointerdown', e => e.stopPropagation());

/** Chooses columns × rows so tiles stay close to a pleasant aspect ratio, then sizes each tile. */
function layout(force) {
  if (document.body.classList.contains('collapsed')) return; // the capsule has its own layout
  const ids = Wd.order;
  const n = ids.length;
  content.classList.toggle('solo', n === 1);
  if (!n) return;
  const r = content.getBoundingClientRect();
  const W0 = r.width, H0 = r.height, gap = 10;
  let best = null;
  for (let cols = 1; cols <= n; cols++) {
    const rows = Math.ceil(n / cols);
    const tw = (W0 - gap * (cols - 1)) / cols, th = (H0 - gap * (rows - 1)) / rows;
    if (tw < 34 || th < 26) continue;
    const ideal = th < 84 ? 3.4 : 1.1;
    let score = Math.abs(Math.log((tw / th) / ideal)) + (cols * rows - n) * .45;
    if (tw < 104) score += (104 - tw) / 22;
    if (th < 84 && tw < 150) score += (150 - tw) / 60;
    if (th < 30) score += (30 - th) / 6;
    if (!best || score < best.score) best = { cols, rows, score };
  }
  if (!best) best = { cols: n, rows: 1 };
  if (H0 < 90 && W0 / n >= 120) best = { cols: n, rows: 1 };
  const key = best.cols + 'x' + best.rows + ':' + ids.join();
  const changed = key !== Wd.arrangement;
  const before = changed && !force ? new Map([...Wd.tiles].map(([id, tl]) => [id, tl.el.getBoundingClientRect()])) : null;
  Wd.arrangement = key;
  content.style.gridTemplateColumns = `repeat(${best.cols}, minmax(0, 1fr))`;
  content.style.gridTemplateRows = `repeat(${best.rows}, minmax(0, 1fr))`;
  const empty = best.cols * best.rows - n;
  ids.forEach((id, i) => {
    const tile = Wd.tiles.get(id);
    tile.el.style.gridColumn = i === n - 1 && empty > 0 ? `span ${empty + 1}` : '';
  });
  for (const id of ids) {
    const tile = Wd.tiles.get(id);
    const rr = tile.el.getBoundingClientRect();
    tile.fit(rr.width, rr.height);
  }
  if (Wd.expanded) Wd.expanded.fit(r.width, r.height);
  if (before) flip(before);
}

/** Tiles glide from where they were to where the new arrangement puts them. */
function flip(before) {
  if (P.anim() === 'off') return;
  for (const [id, tile] of Wd.tiles) {
    const a = before.get(id);
    if (!a) continue;
    const b = tile.el.getBoundingClientRect();
    const dx = a.left - b.left, dy = a.top - b.top;
    if (Math.abs(dx) < 1 && Math.abs(dy) < 1) continue;
    tile.el.animate([{ transform: `translate(${dx}px, ${dy}px) translateZ(10px)` }, { transform: 'translateZ(10px)' }],
      { duration: 700, easing: 'cubic-bezier(.16,1,.3,1)' });
  }
}

new ResizeObserver(() => layout()).observe(content);

// ------------------------------------------------------------------ card motion: tilt, lift, sway
const motion = (() => {
  const apply = () => { card.style.transform = `rotateX(${rx.v.toFixed(3)}deg) rotateY(${ry.v.toFixed(3)}deg) scale(${sc.v.toFixed(4)})`; };
  const rx = new Spring(0, apply, { k: 120, c: 16 });
  const ry = new Spring(0, apply, { k: 120, c: 16 });
  const sc = new Spring(1, apply, { k: 220, c: 22 });
  let swayTimer = 0;
  const tiltOn = () => P.g().tilt !== false && P.anim() === 'full';
  return {
    hover(e) {
      const r = card.getBoundingClientRect();
      const x = (e.clientX - r.left) / r.width, y = (e.clientY - r.top) / r.height;
      card.style.setProperty('--mx', (x * 100).toFixed(1) + '%');
      card.style.setProperty('--my', (y * 100).toFixed(1) + '%');
      card.classList.add('hover');
      if (!tiltOn()) return;
      const max = Math.min(6, 2000 / Math.max(r.width, r.height));
      rx.set((.5 - y) * max);
      ry.set((x - .5) * max);
    },
    leave() { card.classList.remove('hover'); rx.set(0); ry.set(0); },
    sway(dx, dy) {
      if (!tiltOn()) return;
      ry.set(Math.max(-8, Math.min(8, dx * .8)));
      rx.set(Math.max(-8, Math.min(8, -dy * .8)));
      clearTimeout(swayTimer);
      swayTimer = setTimeout(() => { rx.set(0); ry.set(0); }, 70);
    },
    lift(on) { sc.set(on && P.anim() !== 'off' ? 1.018 : 1); card.classList.toggle('lifted', on); },
  };
})();

// ------------------------------------------------------------------ pointer: click, drag, resize, menu
(() => {
  let down = null, mode = null, last = null, pending = false;
  const throttle = cmd => { if (pending) return; pending = true; requestAnimationFrame(() => { pending = false; if (mode) P.send(cmd); }); };

  stage.addEventListener('pointerdown', e => {
    if (e.button !== 0) return;
    if (e.target.closest('[data-nodrag]')) return;
    down = { x: e.screenX, y: e.screenY, target: e.target, id: e.pointerId };
    last = { x: e.screenX, y: e.screenY };
    mode = null;
    stage.setPointerCapture(e.pointerId);
    const rz = e.target.closest('[data-edge]');
    if (rz) {
      const cfg = P.widget();
      if (cfg && cfg.locked) { down = null; return; }
      mode = 'resize';
      P.send('resizeStart', { edge: rz.dataset.edge });
    }
  });
  stage.addEventListener('pointermove', e => {
    if (!down) { motion.hover(e); return; }
    const dx = e.screenX - last.x, dy = e.screenY - last.y;
    last = { x: e.screenX, y: e.screenY };
    if (mode === 'resize') { throttle('resizeMove'); return; }
    if (!mode && Math.hypot(e.screenX - down.x, e.screenY - down.y) > 4) {
      const cfg = P.widget();
      if (cfg && cfg.locked) return;
      mode = 'drag';
      P.send('dragStart');
    }
    if (mode === 'drag') { motion.sway(dx, dy); throttle('dragMove'); }
  });
  const end = e => {
    if (!down) return;
    if (mode === 'resize') P.send('resizeEnd');
    else if (mode === 'drag') P.send('dragEnd');
    else if (e.type === 'pointerup') click(down.target);
    try { stage.releasePointerCapture(down.id); } catch (_) { }
    down = null; mode = null;
  };
  stage.addEventListener('pointerup', end);
  stage.addEventListener('pointercancel', end);
  stage.addEventListener('pointerleave', () => { if (!down) motion.leave(); });
  stage.addEventListener('contextmenu', e => { e.preventDefault(); P.send('menu'); });

  function click(target) {
    if (document.body.classList.contains('collapsed')) { minimize(false); return; }
    if (Wd.expanded) { Wd.collapse(); return; }
    const tile = target.closest && target.closest('.tile');
    if (tile && tile.dataset.mod) Wd.expand(tile.dataset.mod);
  }
})();

// ------------------------------------------------------------------ transparency: Ctrl+wheel = background, Ctrl+Shift+wheel = whole widget
function applyBg(v) {
  const bg = Math.max(0, Math.min(1, v));
  for (const sel of ['.glass', '.shadow', '.rim']) card.querySelector(sel).style.opacity = bg;
  card.querySelector('.shine').style.setProperty('--shine-k', bg);
  content.classList.toggle('hud', bg < .35);
}
const hud = (() => {
  const el = h('div', 'hudtip num');
  card.appendChild(el);
  let timer = 0;
  return (label, v) => {
    el.textContent = label + ' · ' + Math.round(v * 100) + '%';
    el.classList.add('on');
    clearTimeout(timer);
    timer = setTimeout(() => el.classList.remove('on'), 1100);
  };
})();
(() => {
  let pend = null, raf = 0;
  stage.addEventListener('wheel', e => {
    if (!e.ctrlKey) return;
    e.preventDefault();
    const cfg = P.widget();
    if (!cfg) return;
    const whole = e.shiftKey;
    const key = whole ? 'opacity' : 'bgOpacity';
    const cur = pend && pend.key === key ? pend.v : (cfg[key] ?? 1);
    const v = Math.round(Math.max(whole ? .2 : 0, Math.min(1, cur + (e.deltaY < 0 ? .05 : -.05))) * 100) / 100;
    pend = { key, v };
    if (whole) stage.style.opacity = v; else applyBg(v);
    hud(t(whole ? 'hudAll' : 'hudBg'), v);
    if (!raf) raf = setTimeout(() => { raf = 0; if (pend) P.set('widgets.' + P.state.id + '.' + pend.key, pend.v); pend = null; }, 120);
  }, { passive: false });
})();

// ------------------------------------------------------------------ host messages
P.on('init', m => {
  document.documentElement.style.setProperty('--m', m.margin + 'px');
  Wd.sync();
  requestAnimationFrame(() => document.body.classList.add('ready'));
});
P.on('settings', () => Wd.sync());
P.on('look', () => { for (const tl of Wd.tiles.values()) tl.fit(tl.w, tl.h); if (Wd.expanded) Wd.expanded.fit(Wd.expanded.w, Wd.expanded.h); });
// a paused (hidden) page only records history; it catches up from the latest tick on resume
P.on('tick', m => { if (!P.state.paused) Wd.tick(m); });
P.on('pause', m => { if (!m.on && P.state.tick) Wd.tick(P.state.tick); });
P.on('anom', () => { if (!P.state.paused && P.state.tick) Wd.tick(P.state.tick); });
P.on('info', () => { for (const tl of Wd.tiles.values()) tl.key = ''; layout(true); });
P.on('sys', () => { for (const tl of Wd.tiles.values()) tl.key = ''; layout(true); });
P.on('geo', () => placeWall(glass));
P.on('wall', () => placeWall(glass));
P.on('ghost', m => stage.classList.toggle('ghost', m.on));
P.on('interaction', m => motion.lift(m.on && !m.resize));
P.on('snap', m => {
  for (const s of ['l', 'r', 't', 'b']) card.querySelector('.snap-edge.' + s).classList.toggle('on', m.edges.includes(s));
});
P.on('visible', m => {
  const b = document.body;
  b.classList.remove('peek', 'unpeek');
  if (m.on) {
    b.classList.remove('leaving', 'ready');
    void b.offsetWidth;
    b.classList.add('ready');
  } else {
    b.classList.add('leaving');
    setTimeout(() => P.send('hidden'), P.anim() === 'off' ? 0 : 320);
  }
});
P.on('peek', m => {
  const b = document.body;
  b.classList.remove('peek', 'unpeek');
  void b.offsetWidth;
  b.classList.add(m.on ? 'peek' : 'unpeek');
});

P.send('ready');
