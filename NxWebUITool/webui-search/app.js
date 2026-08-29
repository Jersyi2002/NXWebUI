"use strict";

const $ = (s) => document.querySelector(s);

const ICONS = {
  "文件": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M7 3.5h7l5 5V20a1.5 1.5 0 0 1-1.5 1.5H7A1.5 1.5 0 0 1 5.5 20V5A1.5 1.5 0 0 1 7 3.5z"/><path d="M14 3.5V9h5.5"/></svg>',
  "编辑": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 5.5l4 4L8 20H4v-4L14.5 5.5z"/></svg>',
  "视图": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/></svg>',
  "建模": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M12 2.8l7.6 4.4v8.8L12 20.4l-7.6-4.4V7.2L12 2.8z"/><path d="M4.4 7.2L12 11.6l7.6-4.4M12 11.6v8.8"/></svg>',
  "草图": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M4 19C4 10 10 4 20 4"/></svg>',
  "装配": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M12 3l8 4-8 4-8-4 8-4z"/><path d="M4 12l8 4 8-4M4 16.5l8 4 8-4"/></svg>',
  "制图": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><rect x="4" y="3.5" width="16" height="17" rx="1.5"/><path d="M8 8h8M8 12h8M8 16h5"/></svg>',
  "分析": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M3 17L17 3l4 4L7 21l-4-4z"/></svg>',
  "加工": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M12 5V3M12 21v-2M5 12H3M21 12h-2M7 7L5.5 5.5M18.5 18.5L17 17M17 7l1.5-1.5M7 17l-1.5 1.5"/></svg>',
};

const FALLBACK_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="11" cy="11" r="6.5"/><path d="M15.8 15.8L21 21"/></svg>';

/* ---------- WebView2 桥 ---------- */

const Bridge = (() => {
  const hosted = !!(window.chrome && window.chrome.webview);
  let reqId = 0;
  const pending = new Map();

  if (hosted) {
    window.chrome.webview.addEventListener("message", (e) => {
      const msg = e.data;
      if (msg && msg.type === "shown") {
        onPaletteShown();
        return;
      }
      if (msg && msg.type === "response" && pending.has(msg.id)) {
        const { resolve, reject } = pending.get(msg.id);
        pending.delete(msg.id);
        msg.ok ? resolve(msg.data) : reject(new Error(msg.error || "NX 调用失败"));
      }
    });
  }

  function call(action, payload = {}) {
    if (!hosted) {
      return Promise.reject(new Error("preview"));
    }
    return new Promise((resolve, reject) => {
      const id = ++reqId;
      pending.set(id, { resolve, reject });
      window.chrome.webview.postMessage({ type: "invoke", id, action, payload });
      setTimeout(() => {
        if (pending.has(id)) {
          pending.delete(id);
          reject(new Error("NX 响应超时"));
        }
      }, 30000);
    });
  }

  return { hosted, call };
})();

/* ---------- 模糊匹配 ---------- */

function fuzzy(query, target) {
  const q = query.toLowerCase();
  const t = (target || "").toLowerCase();
  let qi = 0, score = 0, streak = 0;
  const hits = [];
  for (let ti = 0; ti < t.length && qi < q.length; ti++) {
    if (t[ti] === q[qi]) {
      streak++;
      score += 1 + streak * 2 + (ti === 0 || /[\s/_\-]/.test(t[ti - 1]) ? 6 : 0);
      hits.push(ti);
      qi++;
    } else streak = 0;
  }
  return qi === q.length ? { score, hits } : null;
}

function search(query, cat) {
  const pool = cat ? CATALOG.filter((c) => c.cat === cat) : CATALOG;
  if (!query) return pool.slice(0, 12).map((cmd) => ({ cmd, score: 0, hits: [] }));
  const out = [];
  for (const cmd of pool) {
    const mName = fuzzy(query, cmd.name);
    const mEn = fuzzy(query, cmd.nameEn);
    const mAll = fuzzy(query, cmd.search || `${cmd.name} ${cmd.nameEn} ${cmd.desc} ${cmd.id}`);
    if (!mName && !mEn && !mAll) continue;
    const score = Math.max(
      mName ? mName.score * 4 : 0,
      mEn ? mEn.score * 3 : 0,
      mAll ? mAll.score : 0
    );
    out.push({ cmd, score, hits: mName ? mName.hits : [] });
  }
  out.sort((a, b) => b.score - a.score);
  return out.slice(0, 12);
}

function highlight(name, hits) {
  if (!name) return "";
  if (!hits || !hits.length) return name;
  const set = new Set(hits);
  return [...name].map((ch, i) => (set.has(i) ? `<b>${ch}</b>` : ch)).join("");
}

/* ---------- 状态 ---------- */

let CATALOG = [];
let CATEGORIES = [];
let activeCat = "";
let results = [];
let sel = 0;

const input = $("#search");
const shell = $("#shell");
const resultsBox = $("#results");
const rowsBox = $("#rows");
const selPill = $("#selPill");
const idle = $("#idle");
const voidBox = $("#void");
const count = $("#count");
const escHint = $("#escHint");
const catDd = $("#catDd");
const catBtn = $("#catBtn");
const catBtnLabel = $("#catBtnLabel");
const catMenu = $("#catMenu");
const catsBox = $("#cats");

/* ---------- 渲染 ---------- */

function renderCats() {
  catsBox.hidden = true;
  if (!catDd) return;
  if (!CATEGORIES.length) {
    catDd.hidden = true;
    return;
  }
  catDd.hidden = false;

  const counts = {};
  for (const c of CATALOG) counts[c.cat] = (counts[c.cat] || 0) + 1;
  const ranked = [...CATEGORIES].sort((a, b) => (counts[b] || 0) - (counts[a] || 0));
  const items = [{ id: "", label: "全部分类", n: CATALOG.length }, ...ranked.map((c) => ({ id: c, label: c, n: counts[c] }))];

  catMenu.innerHTML = items
    .map((c) => `<button type="button" class="cat-dd-item${c.id === activeCat ? " on" : ""}" data-cat="${escapeAttr(c.id)}" role="option">${escapeHtml(c.label)}<span class="n">${c.n}</span></button>`)
    .join("");
  catMenu.querySelectorAll(".cat-dd-item").forEach((item) => {
    item.addEventListener("click", (e) => {
      e.stopPropagation();
      setCat(item.dataset.cat);
      closeCatMenu();
    });
  });
  syncCatLabel();
}

function syncCatLabel() {
  if (catBtnLabel) catBtnLabel.textContent = activeCat || "全部分类";
}

function openCatMenu() {
  if (!catDd || catDd.hidden) return;
  catDd.classList.add("open");
  catMenu.hidden = false;
  catBtn.setAttribute("aria-expanded", "true");
  requestAnimationFrame(() => requestAnimationFrame(reportLayout));
}

function closeCatMenu() {
  if (!catDd) return;
  catDd.classList.remove("open");
  catMenu.hidden = true;
  catBtn.setAttribute("aria-expanded", "false");
  requestAnimationFrame(() => requestAnimationFrame(reportLayout));
}

function toggleCatMenu() {
  if (catMenu.hidden) openCatMenu();
  else closeCatMenu();
}

function setCat(cat) {
  activeCat = cat || "";
  syncCatLabel();
  renderCats();
  render();
}

function render() {
  const q = input.value.trim();
  results = search(q, activeCat);
  sel = 0;

  idle.classList.toggle("hidden", !!(q || activeCat));
  voidBox.hidden = true;

  if (!q && !activeCat) {
    resultsBox.classList.remove("has-rows");
    rowsBox.innerHTML = "";
    selPill.classList.remove("on");
    count.textContent = CATALOG.length ? `${CATALOG.length} 个命令` : "";
    reportLayout();
    return;
  }

  if (!results.length) {
    resultsBox.classList.remove("has-rows");
    rowsBox.innerHTML = "";
    selPill.classList.remove("on");
    count.textContent = "";
    voidBox.hidden = false;
    reportLayout();
    return;
  }

  const scope = activeCat
    ? CATALOG.filter((c) => c.cat === activeCat).length
    : CATALOG.length;
  count.textContent = q
    ? `${results.length} 个结果` + (activeCat ? ` · ${activeCat}` : "")
    : `${Math.min(results.length, scope)} / ${scope} · ${activeCat || "全部"}`;

  resultsBox.classList.add("has-rows");
  rowsBox.innerHTML = results
    .map(({ cmd, hits }, i) => `
      <div class="row" style="--i:${i}" data-i="${i}" role="option">
        <span class="row-icon">${ICONS[cmd.cat] || FALLBACK_ICON}</span>
        <span class="row-main">
          <span class="row-name">${highlight(cmd.name, hits)}</span>
          <span class="row-desc">${escapeHtml(cmd.desc || cmd.nameEn || cmd.id)}</span>
        </span>
        <span class="row-cat">${escapeHtml(cmd.cat || "")}</span>
        ${cmd.key ? `<kbd class="row-key">${escapeHtml(cmd.key)}</kbd>` : ""}
      </div>`)
    .join("");

  rowsBox.querySelectorAll(".row").forEach((row) => {
    row.addEventListener("mouseenter", () => select(+row.dataset.i));
    row.addEventListener("click", () => execute(results[+row.dataset.i].cmd));
  });

  requestAnimationFrame(() => {
    select(0, true);
    reportLayout();
  });
}

function select(i, instant = false) {
  if (!results.length) return;
  sel = (i + results.length) % results.length;
  const row = rowsBox.children[sel];
  if (!row) return;
  rowsBox.querySelectorAll(".row").forEach((r, j) => r.classList.toggle("sel", j === sel));
  if (instant) selPill.style.transition = "none";
  selPill.style.height = row.offsetHeight + "px";
  selPill.style.transform = `translateY(${row.offsetTop}px)`;
  selPill.classList.add("on");
  if (instant) requestAnimationFrame(() => (selPill.style.transition = ""));
}

/* ---------- 执行 ---------- */

const RECENT_KEY = "nx-search-recent";
let recent = [];
try { recent = JSON.parse(localStorage.getItem(RECENT_KEY) || "[]"); } catch { recent = []; }

function saveRecent(cmd) {
  recent = [{ id: cmd.id, name: cmd.name }, ...recent.filter((n) => n.id !== cmd.id)].slice(0, 6);
  localStorage.setItem(RECENT_KEY, JSON.stringify(recent));
  renderRecent();
}

function renderRecent() {
  const box = $("#recent");
  const list = recent.length ? recent : [];
  if (!list.length) {
    box.innerHTML = "";
    return;
  }
  box.innerHTML = list
    .map((n, i) => `<button class="chip" style="--i:${i}" data-id="${escapeAttr(n.id)}"><span class="chip-label">${escapeHtml(n.name)}</span></button>`)
    .join("");
  box.querySelectorAll(".chip").forEach((chip) => {
    chip.addEventListener("click", () => {
      const cmd = CATALOG.find((c) => c.id === chip.dataset.id);
      if (cmd) execute(cmd);
    });
  });
  requestAnimationFrame(() => requestAnimationFrame(reportLayout));
}

function toast(msg) {
  const root = $("#toastRoot");
  const el = document.createElement("div");
  el.className = "toast";
  el.innerHTML = `<span class="t-dot"></span><span></span>`;
  el.lastElementChild.textContent = msg;
  root.appendChild(el);
  setTimeout(() => {
    el.classList.add("leaving");
    el.addEventListener("animationend", () => el.remove(), { once: true });
  }, 2200);
}

async function execute(cmd) {
  shell.classList.remove("bump");
  void shell.offsetWidth;
  shell.classList.add("bump");
  const ring = $("#ring");
  ring.classList.remove("go");
  void ring.offsetWidth;
  ring.classList.add("go");
  saveRecent(cmd);

  if (!Bridge.hosted) {
    toast(`预览：${cmd.name}`);
    return;
  }
  try {
    await Bridge.call("execute", { id: cmd.id, type: cmd.type || "BUTTON" });
  } catch (err) {
    toast(err.message);
  }
}

/* ---------- 事件 ---------- */

let debounce;
input.addEventListener("input", () => {
  clearTimeout(debounce);
  debounce = setTimeout(render, 40);
});

if (catBtn) {
  catBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    toggleCatMenu();
  });
}
document.addEventListener("click", (e) => {
  if (catMenu && !catMenu.hidden &&
      !(catDd && catDd.contains(e.target)) &&
      !catMenu.contains(e.target)) {
    closeCatMenu();
  }
});

input.addEventListener("keydown", (e) => {
  if (e.key === "ArrowDown") { e.preventDefault(); select(sel + 1); }
  else if (e.key === "ArrowUp") { e.preventDefault(); select(sel - 1); }
  else if (e.key === "Enter") {
    e.preventDefault();
    if (results[sel]) execute(results[sel].cmd);
  } else if (e.key === "Escape") {
    e.preventDefault();
    if (catMenu && !catMenu.hidden) closeCatMenu();
    else requestClose();
  }
});

document.addEventListener("keydown", (e) => {
  if (e.key === "/" && document.activeElement !== input) {
    e.preventDefault();
    input.focus();
  }
});

addEventListener("resize", () => select(sel, true));

/* ---------- 幽灵占位 ---------- */

const GHOSTS = ["试试「拉伸」…", "搜索拼音或英文：extrude", "先选分类再搜", "Alt+Q 打开"];
const ghost = $("#ghost");

async function ghostLoop() {
  let gi = 0;
  await wait(1200);
  while (true) {
    if (document.activeElement === input || input.value) {
      ghost.innerHTML = "";
      await wait(500);
      continue;
    }
    const text = GHOSTS[gi++ % GHOSTS.length];
    for (const ch of text) {
      if (document.activeElement === input || input.value) break;
      ghost.textContent = (ghost.textContent || "") + ch;
      ghost.innerHTML = escapeHtml(ghost.textContent) + '<span class="g-caret"></span>';
      await wait(70 + Math.random() * 40);
    }
    await wait(2000);
    while ((ghost.textContent || "").length) {
      if (document.activeElement === input || input.value) break;
      ghost.textContent = ghost.textContent.slice(0, -1);
      ghost.innerHTML = escapeHtml(ghost.textContent) + '<span class="g-caret"></span>';
      await wait(24);
    }
    ghost.innerHTML = "";
    await wait(350);
  }
}

function escapeHtml(s) {
  return String(s || "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
function escapeAttr(s) {
  return escapeHtml(s).replace(/"/g, "&quot;");
}
function wait(ms) { return new Promise((ok) => setTimeout(ok, ms)); }

input.addEventListener("focus", () => (ghost.innerHTML = ""));

function requestClose() {
  if (!Bridge.hosted) return;
  Bridge.call("close").catch(() => {});
}

let layoutTick = 0;
function reportLayout() {
  if (!Bridge.hosted) return;
  const pal = document.getElementById("palette") || document.body;
  let height = Math.ceil(Math.max(pal.scrollHeight, pal.getBoundingClientRect().height, document.body.scrollHeight));
  if (catMenu && !catMenu.hidden) {
    const pr = pal.getBoundingClientRect();
    const mr = catMenu.getBoundingClientRect();
    height = Math.max(height, Math.ceil(mr.bottom - pr.top + 20));
  }
  const id = ++layoutTick;
  requestAnimationFrame(() => {
    if (id !== layoutTick) return;
    Bridge.call("resize", { height }).catch(() => {});
  });
}

/* ---------- 启动 ---------- */

function onPaletteShown() {
  input.value = "";
  activeCat = "";
  closeCatMenu();
  syncCatLabel();
  render();
  renderRecent();
  setTimeout(() => input.focus(), 30);
}

async function boot() {
  if (CATALOG.length) {
    onPaletteShown();
    return;
  }
  $("#total").textContent = "…";
  if (Bridge.hosted) {
    try {
      const data = await Bridge.call("getCatalog");
      CATALOG = (data.commands || []).map((c) => ({
        ...c,
        search: (c.search || `${c.name} ${c.nameEn} ${c.desc} ${c.id} ${c.cat} ${c.synonyms || ""}`).toLowerCase()
      }));
      CATEGORIES = data.categories || [];
    } catch (err) {
      toast(err.message);
    }
  } else {
    CATALOG = [
      { id: "UG_MODELING_EXTRUDED_FEATURE", name: "拉伸", nameEn: "Extrude", desc: "沿矢量拉伸截面", cat: "建模", key: "", type: "BUTTON" },
      { id: "UG_MODELING_REVOLVED_FEATURE", name: "旋转", nameEn: "Revolve", desc: "绕轴旋转截面", cat: "建模", key: "", type: "BUTTON" },
      { id: "UG_SKETCH_SKETCH", name: "草图", nameEn: "Sketch", desc: "创建草图", cat: "草图", key: "", type: "BUTTON" },
      { id: "UG_FILE_NEW", name: "新建", nameEn: "New", desc: "创建新文件", cat: "文件", key: "Ctrl+N", type: "BUTTON" },
      { id: "UG_MODELING_BLEND_FEATURE", name: "边倒圆", nameEn: "Edge Blend", desc: "对边倒圆角", cat: "建模", key: "", type: "BUTTON" },
      { id: "UG_MODELING_HOLE_FEATURE", name: "孔", nameEn: "Hole", desc: "创建孔特征", cat: "建模", key: "", type: "BUTTON" },
      { id: "UG_MODELING_UNITE_FEATURE", name: "求和", nameEn: "Unite", desc: "布尔求和", cat: "建模", key: "", type: "BUTTON" },
      { id: "UG_MODELING_SUBTRACT_FEATURE", name: "求差", nameEn: "Subtract", desc: "布尔求差", cat: "建模", key: "", type: "BUTTON" },
      { id: "UG_VIEW_FIT", name: "适合窗口", nameEn: "Fit", desc: "缩放到全部对象", cat: "视图", key: "Ctrl+F", type: "BUTTON" },
      { id: "UG_SKETCH_LINE", name: "直线", nameEn: "Line", desc: "绘制直线", cat: "草图", key: "", type: "BUTTON" },
    ].map((c) => ({ ...c, search: `${c.name} ${c.nameEn} ${c.desc} ${c.id}`.toLowerCase() }));
    CATEGORIES = [...new Set(CATALOG.map((c) => c.cat))];
    if (!recent.length) {
      recent = CATALOG.slice(0, 4).map((c) => ({ id: c.id, name: c.name }));
    }
  }

  $("#total").textContent = CATALOG.length;
  renderCats();
  renderRecent();
  if (!Bridge.hosted) {
    const q = new URLSearchParams(location.search).get("q");
    if (q) input.value = q;
  }
  render();
  ghostLoop();
  setTimeout(() => input.focus(), 200);
  setTimeout(reportLayout, 80);
}

boot();
