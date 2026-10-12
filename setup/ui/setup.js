"use strict";
/* Pulse Setup — pages, motion and the host bridge (SetupWindow in setup\Window.cs). */

const T = {
  ru: {
    welcomeTitle: "Добро пожаловать",
    welcomeLead: "Pulse — живые виджеты нагрузки, температур и энергии прямо на рабочем столе.",
    f1: "Процессор, видеокарта, память и диски",
    f2: "Сеть, качество связи и вентиляторы",
    f3: "Локальная нейросеть и FPS в играх",
    f4: "Потребление в ваттах и учёт энергии",
    install: "Установить", update: "Обновить", reinstall: "Переустановить",
    options: "Параметры", optionsTitle: "Параметры",
    oAutostart: "Запуск вместе с Windows", oAutostartSub: "Виджеты появятся сразу после входа",
    oSensors: "Датчики процессора", oSensorsSub: "Температура, частоты и мощность CPU (драйвер PawnIO)",
    oDesktop: "Ярлык на рабочем столе", oDesktopSub: "Ярлык в меню «Пуск» создаётся всегда",
    oFolder: "Папка установки", change: "Изменить", back: "Назад",
    installing: "Установка", updating: "Обновление", removing: "Удаление",
    s_closing: "Закрываем запущенный Pulse", s_copying: "Копируем файлы", s_shortcuts: "Создаём ярлыки",
    s_registering: "Регистрируем в системе", s_webview2: "Устанавливаем Microsoft Edge WebView2",
    s_sensors: "Подключаем датчики и автозапуск", s_autostart: "Настраиваем автозапуск", s_done: "Готово",
    s_unregistering: "Отключаем автозапуск и задачу", s_removing: "Удаляем файлы программы",
    s_removingData: "Удаляем настройки и историю", s_removed: "Готово",
    doneTitle: "Pulse установлен", doneLead: "Виджеты уже на рабочем столе. Значок Pulse — в области уведомлений, правый клик по виджету открывает меню.",
    updatedTitle: "Pulse обновлён", updatedLead: "Ваши виджеты, настройки и история энергии сохранены.",
    open: "Открыть Pulse", close: "Закрыть",
    removedTitle: "Pulse удалён", removedLead: "Спасибо, что пользовались Pulse.", removedKeep: "Настройки и история энергии сохранены — они вернутся при новой установке.",
    failTitle: "Что-то пошло не так", failLead: "Ошибка: ",
    unTitle: "Удалить Pulse?", unLead: "Виджеты, задача автозапуска и ярлыки будут удалены с этого компьютера.",
    unKeep: "Сохранить настройки", unKeepSub: "Расположение виджетов и история энергии", unBtn: "Удалить", cancel: "Отмена",
    version: "Версия ",
    devLaptop: "Ноутбук", devDesktop: "Компьютер",
    devLaptopSub: "Добавим виджет аккумулятора, а от батареи Pulse сам перейдёт в экономный режим",
    devDesktopSub: "Виджеты нагрузки и температур, потребление от блока питания",
    f4laptop: "Аккумулятор: заряд, здоровье и время работы",
  },
  en: {
    welcomeTitle: "Welcome",
    welcomeLead: "Pulse puts live load, temperature and power widgets right on your desktop.",
    f1: "CPU, graphics, memory and drives",
    f2: "Network, connection quality and fans",
    f3: "Local AI model and in-game FPS",
    f4: "Power draw in watts and energy history",
    install: "Install", update: "Update", reinstall: "Reinstall",
    options: "Options", optionsTitle: "Options",
    oAutostart: "Start with Windows", oAutostartSub: "Widgets appear right after you sign in",
    oSensors: "CPU sensors", oSensorsSub: "CPU temperature, clocks and power (PawnIO driver)",
    oDesktop: "Desktop shortcut", oDesktopSub: "A Start menu shortcut is always created",
    oFolder: "Install location", change: "Change", back: "Back",
    installing: "Installing", updating: "Updating", removing: "Removing",
    s_closing: "Closing the running Pulse", s_copying: "Copying files", s_shortcuts: "Creating shortcuts",
    s_registering: "Registering with Windows", s_webview2: "Installing Microsoft Edge WebView2",
    s_sensors: "Connecting sensors and autostart", s_autostart: "Setting up autostart", s_done: "Done",
    s_unregistering: "Turning off autostart", s_removing: "Removing program files",
    s_removingData: "Removing settings and history", s_removed: "Done",
    doneTitle: "Pulse is installed", doneLead: "Your widgets are already on the desktop. Pulse lives in the notification area; right-click a widget for its menu.",
    updatedTitle: "Pulse is up to date", updatedLead: "Your widgets, settings and energy history were kept.",
    open: "Open Pulse", close: "Close",
    removedTitle: "Pulse is removed", removedLead: "Thanks for using Pulse.", removedKeep: "Your settings and energy history were kept for a future install.",
    failTitle: "Something went wrong", failLead: "Error: ",
    unTitle: "Remove Pulse?", unLead: "The widgets, the sign-in task and the shortcuts will be removed from this PC.",
    unKeep: "Keep my settings", unKeepSub: "Widget layout and energy history", unBtn: "Remove", cancel: "Cancel",
    version: "Version ",
    devLaptop: "Laptop", devDesktop: "Desktop",
    devLaptopSub: "We’ll add a battery widget, and on battery Pulse switches to eco by itself",
    devDesktopSub: "Load and temperature widgets, power draw from the PSU",
    f4laptop: "Battery: charge, health and time left",
  },
  uz: {
    welcomeTitle: "Xush kelibsiz",
    welcomeLead: "Pulse — ish stolida yuklama, harorat va energiya vidjetlari.",
    f1: "Protsessor, videokarta, xotira va disklar",
    f2: "Tarmoq, aloqa sifati va ventilyatorlar",
    f3: "Lokal neyrotarmoq va o'yinlarda FPS",
    f4: "Vattlardagi iste'mol va energiya hisobi",
    install: "O'rnatish", update: "Yangilash", reinstall: "Qayta o'rnatish",
    options: "Sozlamalar", optionsTitle: "Sozlamalar",
    oAutostart: "Windows bilan birga ishga tushirish", oAutostartSub: "Vidjetlar tizimga kirgandan so'ng darhol paydo bo'ladi",
    oSensors: "Protsessor datchiklari", oSensorsSub: "CPU harorati, chastotasi va quvvati (PawnIO drayveri)",
    oDesktop: "Ish stolida yorliq", oDesktopSub: "«Pusk» menyusida yorliq doim yaratiladi",
    oFolder: "O'rnatish papkasi", change: "O'zgartirish", back: "Orqaga",
    installing: "O'rnatilmoqda", updating: "Yangilanmoqda", removing: "O'chirilmoqda",
    s_closing: "Ishlayotgan Pulse yopilmoqda", s_copying: "Fayllar nusxalanmoqda", s_shortcuts: "Yorliqlar yaratilmoqda",
    s_registering: "Tizimda ro'yxatdan o'tkazilmoqda", s_webview2: "Microsoft Edge WebView2 o'rnatilmoqda",
    s_sensors: "Datchiklar va avtoishga tushirish ulanmoqda", s_autostart: "Avtoishga tushirish sozlanmoqda", s_done: "Tayyor",
    s_unregistering: "Avtoishga tushirish o'chirilmoqda", s_removing: "Dastur fayllari o'chirilmoqda",
    s_removingData: "Sozlamalar va tarix o'chirilmoqda", s_removed: "Tayyor",
    doneTitle: "Pulse o'rnatildi", doneLead: "Vidjetlar allaqachon ish stolida. Pulse belgisi bildirishnomalar sohasida, vidjetni o'ng tugma bilan bosing — menyu ochiladi.",
    updatedTitle: "Pulse yangilandi", updatedLead: "Vidjetlar, sozlamalar va energiya tarixi saqlandi.",
    open: "Pulse'ni ochish", close: "Yopish",
    removedTitle: "Pulse o'chirildi", removedLead: "Pulse'dan foydalanganingiz uchun rahmat.", removedKeep: "Sozlamalar va energiya tarixi keyingi o'rnatish uchun saqlandi.",
    failTitle: "Nimadir noto'g'ri ketdi", failLead: "Xato: ",
    unTitle: "Pulse o'chirilsinmi?", unLead: "Vidjetlar, avtoishga tushirish vazifasi va yorliqlar ushbu kompyuterdan o'chiriladi.",
    unKeep: "Sozlamalarni saqlash", unKeepSub: "Vidjetlar joylashuvi va energiya tarixi", unBtn: "O'chirish", cancel: "Bekor qilish",
    version: "Versiya ",
    devLaptop: "Noutbuk", devDesktop: "Kompyuter",
    devLaptopSub: "Batareya vidjetini qoʻshamiz, batareyada Pulse oʻzi tejamkor rejimga oʻtadi",
    devDesktopSub: "Yuklama va harorat vidjetlari, quvvat blokidan isteʼmol",
    f4laptop: "Batareya: zaryad, holati va ishlash vaqti",
  },
};
const LANGS = [["ru", "RU"], ["uz", "UZ"], ["en", "EN"]];
const INSTALL_STEPS = ["closing", "copying", "shortcuts", "registering", "sensors", "done"];
const UNINSTALL_STEPS = ["closing", "unregistering", "removing", "removingData", "removed"];

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const host = window.chrome && chrome.webview;
const send = (m) => { if (host) host.postMessage(m); else console.log("→", m); };

const S = {
  lang: "ru", mode: "install", installed: false, dir: "", version: "",
  page: null, opts: { autostart: true, sensors: true, desktop: false, keep: true },
  steps: [], step: null, f: 0, shown: 0, result: null,
};
const t = (k) => (T[S.lang] && T[S.lang][k]) || T.en[k] || k;

/* ---------------------------------------------------------------- text */

function applyText() {
  document.documentElement.lang = S.lang;
  $$("[data-t]").forEach((el) => { el.textContent = t(el.dataset.t); });
  const verb = S.installed ? (S.sameVersion ? "reinstall" : "update") : "install";
  $("#installBtn").textContent = t(verb);
  $("#installBtn2").textContent = t(verb);
  $("#ver").textContent = t("version") + S.version;
  $("#dir").textContent = S.dir;
  $("#dir").title = S.dir;
  $$(".langs button").forEach((b) => b.classList.toggle("on", b.dataset.lang === S.lang));
  renderDevice();
  moveThumb();
  if (S.page === "progress") renderSteps();
  if (S.page === "done") renderDone();
}

const DEV_ICONS = {
  laptop: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"><rect x="4.5" y="5" width="15" height="10" rx="1.6"/><path d="M2.5 18.5h19l-1.6-2.2H4.1z"/></svg>',
  desktop: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round" stroke-linecap="round"><rect x="3" y="4" width="18" height="12" rx="1.8"/><path d="M9 20h6M12 16v4"/></svg>',
};

/** "Laptop · Lenovo ThinkPad X1 Carbon Gen 10" and what that means for the setup. */
function renderDevice() {
  const box = $("#device");
  if (!S.device) { box.hidden = true; return; }
  const laptop = S.device === "laptop";
  const model = [S.model && S.model.vendor, S.model && S.model.model].filter(Boolean).join(" ");
  box.hidden = false;
  box.className = "device " + S.device;
  $("#devIcon").innerHTML = DEV_ICONS[S.device] || DEV_ICONS.desktop;
  $("#devName").textContent = t(laptop ? "devLaptop" : "devDesktop") + (model ? " · " + model : "");
  $("#devName").title = model;
  $("#devSub").textContent = t(laptop ? "devLaptopSub" : "devDesktopSub");
  $("#f4").textContent = t(laptop ? "f4laptop" : "f4");
  $("#f4dot").style.setProperty("--c", laptop ? "#34C759" : "#FFD60A");
}

function buildLangs() {
  const box = $("#langs");
  box.innerHTML = '<span class="thumb"></span>' + LANGS.map(([k, n]) => `<button data-lang="${k}">${n}</button>`).join("");
  box.addEventListener("click", (e) => {
    const b = e.target.closest("button");
    if (!b) return;
    S.lang = b.dataset.lang;
    applyText();
  });
}

function moveThumb() {
  const on = $(".langs button.on"), th = $(".langs .thumb");
  if (!on || !th) return;
  th.style.width = on.offsetWidth + "px";
  th.style.transform = `translateX(${on.offsetLeft}px)`;
}

/* ---------------------------------------------------------------- pages */

function go(name) {
  if (S.page === name) return;
  const prev = S.page && $(`.page[data-page="${S.page}"]`);
  const next = $(`.page[data-page="${name}"]`);
  if (prev) {
    prev.classList.remove("on");
    prev.classList.add("gone");
    setTimeout(() => prev.classList.remove("gone"), 900);
  }
  next.classList.remove("gone");
  // restart entry animations of the page that comes in
  void next.offsetWidth;
  next.classList.add("on");
  S.page = name;
}

/* ---------------------------------------------------------------- progress */

function ringsAt(f) {
  // the outer ring is the progress itself, the inner two follow with a little lag for depth
  const r = [[".r1", 527.8], [".r2", 402.1], [".r3", 276.5]];
  r.forEach(([sel, len], i) => {
    const el = $(sel);
    const k = Math.max(0, Math.min(1, f - i * 0.04));
    el.style.strokeDashoffset = (len * (1 - k)).toFixed(1);
  });
}

function renderSteps() {
  const list = $("#steps");
  const cur = S.steps.indexOf(S.step);
  list.innerHTML = S.steps.filter((k) => k !== "done" && k !== "removed").map((k, i) => {
    const cls = S.result ? "ok" : i < cur ? "ok" : i === cur ? "now" : "";
    return `<li class="${cls}"><i><svg viewBox="0 0 10 10"><path d="M2 5.2l2 2 4-4.4"/></svg></i><span>${t("s_" + k)}</span></li>`;
  }).join("");
  $("#progTitle").textContent = t(S.mode === "uninstall" ? "removing" : S.installed ? "updating" : "installing");
  $("#status").textContent = S.step ? t("s_" + S.step) : "";
}

let rafId = 0;
function tickPct() {
  // number glides towards the real fraction (critically damped), ring follows it
  const target = S.f * 100;
  S.shown += (target - S.shown) * 0.12;
  if (Math.abs(target - S.shown) < 0.05) S.shown = target;
  $("#pct").textContent = Math.round(S.shown);
  ringsAt(S.shown / 100);
  if (S.shown !== target || S.page === "progress") rafId = requestAnimationFrame(tickPct);
  else rafId = 0;
}

function onProgress(f, step) {
  S.f = Math.max(S.f, f);
  if (step) {
    // steps that do not apply on this machine are simply skipped in the list
    if (step === "webview2" && S.steps.indexOf("webview2") < 0) S.steps.splice(S.steps.indexOf("sensors"), 0, "webview2");
    if (step === "autostart" && S.steps.indexOf("autostart") < 0) S.steps[S.steps.indexOf("sensors")] = "autostart";
    if (step === "removingData" && S.steps.indexOf("removingData") < 0) S.steps.splice(S.steps.length - 1, 0, "removingData");
    S.step = step;
  }
  renderSteps();
  if (!rafId) rafId = requestAnimationFrame(tickPct);
}

function startWork() {
  document.body.classList.add("busy");
  S.f = 0; S.shown = 0; S.result = null;
  $$(".r").forEach((r) => { r.style.transitionDelay = "0s"; r.style.transition = "opacity .4s"; });
  ringsAt(0);
  go("progress");
  renderSteps();
  rafId = requestAnimationFrame(tickPct);
}

function onFinished(error) {
  S.result = error ? { error } : { ok: true };
  S.f = error ? S.f : 1;
  setTimeout(() => {
    document.body.classList.remove("busy");
    $$(".r").forEach((r) => { r.style.transition = ""; });
    if (!error) { ringsAt(1); document.body.classList.add("finished"); }
    renderDone();
    go("done");
  }, error ? 200 : 650);
}

function renderDone() {
  const r = S.result || {};
  const title = $("#doneTitle"), lead = $("#doneLead"), btn = $("#finishBtn");
  if (r.error) {
    title.textContent = t("failTitle");
    lead.innerHTML = "";
    lead.append(t("failLead"));
    const e = document.createElement("span");
    e.className = "error"; e.textContent = r.error;
    lead.append(e);
    btn.textContent = t("close");
    btn.dataset.next = "close";
  } else if (S.mode === "uninstall") {
    title.textContent = t("removedTitle");
    lead.textContent = t("removedLead") + (S.opts.keep ? " " + t("removedKeep") : "");
    btn.textContent = t("close");
    btn.dataset.next = "close";
  } else {
    title.textContent = t(S.installed ? "updatedTitle" : "doneTitle");
    lead.textContent = t(S.installed ? "updatedLead" : "doneLead");
    btn.textContent = t("open");
    btn.dataset.next = "launch";
  }
}

/* ---------------------------------------------------------------- actions */

function act(name, el) {
  switch (name) {
    case "close": exit("close"); break;
    case "minimize": send({ cmd: "minimize" }); break;
    case "options": go("options"); break;
    case "back": go("welcome"); break;
    case "browse": send({ cmd: "browse" }); break;
    case "install":
      startWork();
      send({ cmd: "install", autostart: S.opts.autostart, sensors: S.opts.sensors, desktop: S.opts.desktop, launch: true });
      break;
    case "uninstall":
      startWork();
      send({ cmd: "uninstall", keep: S.opts.keep });
      break;
    case "finish": exit(el.dataset.next === "launch" ? "launch" : "close"); break;
  }
}

function exit(cmd) {
  document.body.classList.add("out");
  setTimeout(() => send({ cmd }), 260);
}

document.addEventListener("click", (e) => {
  const a = e.target.closest("[data-act]");
  if (a) { act(a.dataset.act, a); return; }
  const o = e.target.closest(".opt");
  const sw = o && $(".sw", o);
  if (sw) {
    const k = sw.dataset.opt;
    S.opts[k] = !S.opts[k];
    sw.classList.toggle("on", S.opts[k]);
  }
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape" && !document.body.classList.contains("busy")) exit("close");
  if (e.key === "Enter" && S.page) {
    const b = $(`.page[data-page="${S.page}"] .btn.primary, .page[data-page="${S.page}"] .btn.danger`);
    if (b) b.click();
  }
});

/* ---------------------------------------------------------------- window drag + logo tilt */

let drag = null;
document.addEventListener("pointerdown", (e) => {
  if (e.button !== 0 || !e.target.closest("[data-drag]") || e.target.closest("button")) return;
  drag = true;
  send({ cmd: "dragStart" });
});
document.addEventListener("pointermove", (e) => {
  if (drag) send({ cmd: "dragMove" });
  const logo = $("#logo"), r = logo.getBoundingClientRect();
  const dx = (e.clientX - (r.left + r.width / 2)) / 260, dy = (e.clientY - (r.top + r.height / 2)) / 260;
  const c = (v) => Math.max(-1, Math.min(1, v));
  logo.style.transform = `rotateY(${(c(dx) * 16).toFixed(2)}deg) rotateX(${(-c(dy) * 16).toFixed(2)}deg)`;
});
document.addEventListener("pointerup", () => { if (drag) { drag = null; send({ cmd: "dragEnd" }); } });
document.addEventListener("mouseleave", () => { $("#logo").style.transform = ""; });

/* ---------------------------------------------------------------- host messages */

function onMessage(m) {
  switch (m.type) {
    case "init":
      S.mode = m.mode; S.version = m.version; S.dir = m.dir; S.installed = !!m.installed; S.lang = m.lang || "ru";
      S.sameVersion = !!m.sameVersion; S.device = m.device || null; S.model = m.model || null;
      S.steps = (S.mode === "uninstall" ? UNINSTALL_STEPS : INSTALL_STEPS).slice();
      if (S.mode === "uninstall") S.steps = S.steps.filter((k) => k !== "removingData");
      applyText();
      go(S.mode === "uninstall" ? "uninstall" : "welcome");
      if (m.auto) setTimeout(() => act(S.mode === "uninstall" ? "uninstall" : "install"), 1200);
      requestAnimationFrame(() => document.body.classList.add("in"));
      break;
    case "dir": S.dir = m.dir; applyText(); break;
    case "progress": onProgress(m.f, m.step); break;
    case "finished": onFinished(m.error); break;
  }
}

buildLangs();
if (host) {
  host.addEventListener("message", (e) => onMessage(e.data));
  send({ cmd: "ready" });
} else {
  // opened in a normal browser: preview with fake data
  const h = location.hash.slice(1);
  onMessage({ type: "init", mode: h === "un" ? "uninstall" : "install", version: "1.1.0", dir: "C:\\Program Files\\Pulse", installed: false, lang: "ru",
    device: h === "desk" ? "desktop" : "laptop", model: { vendor: "Lenovo", model: "ThinkPad X1 Carbon Gen 10" } });
  if (h === "options") go("options");
  if (h === "done") { startWork(); onProgress(1, "done"); onFinished(null); }
}
window.addEventListener("resize", moveThumb);
