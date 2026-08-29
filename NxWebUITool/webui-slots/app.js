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

const FALLBACK_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="7"/></svg>';
const EMPTY_ICON = FALLBACK_ICON;
const CLOCK8 = ["上", "右上", "右", "右下", "下", "左下", "左", "左上"];
const CLOCK4 = ["上", "右", "下", "左"];
const SLOT_R = 158;
const CHILD_DIST = 92;
const NATIVE_N = 8;
const SLOT_MIN = 4;
const SLOT_MAX = 10;
const NATIVE_POS = [
  [-112, -112], [0, -112], [112, -112], [112, 0],
  [112, 112], [0, 112], [-112, 112], [-112, 0],
];
const NATIVE_NAMES = ["左上", "上", "右上", "右", "右下", "下", "左下", "左"];

const Bridge = (() => {
  const hosted = !!(window.chrome && window.chrome.webview);
  let reqId = 0;
  const pending = new Map();

  if (hosted) {
    window.chrome.webview.addEventListener("message", (e) => {
      const msg = e.data;
      if (msg && msg.type === "shown") {
        onShown(msg);
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
    if (!hosted) return Promise.reject(new Error("preview"));
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
  if (!query) return pool.slice(0, 16).map((cmd) => ({ cmd, score: 0, hits: [] }));
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
  return out.slice(0, 16);
}

function highlight(name, hits) {
  if (!name) return "";
  if (!hits || !hits.length) return name;
  const set = new Set(hits);
  return [...name].map((ch, i) => (set.has(i) ? `<b>${ch}</b>` : ch)).join("");
}

function iconFor(slot) {
  if (!slot || !slot.id) return EMPTY_ICON;
  const url = slot.icon && String(slot.icon).indexOf("data:image/") === 0 ? slot.icon : "";
  if (url) return `<img src="${url}" alt="" draggable="false">`;
  return ICONS[slot.cat] || FALLBACK_ICON;
}

function isCustomCount(n) {
  return Number.isInteger(n) && n >= SLOT_MIN && n <= SLOT_MAX;
}

function clockName(i, n = slots.length) {
  if (n === 8) return CLOCK8[i] || `槽 ${i + 1}`;
  if (n === 4) return CLOCK4[i] || `槽 ${i + 1}`;
  return `槽 ${i + 1}`;
}

function polar(i) {
  const n = Math.max(1, slots.length);
  const a = -Math.PI / 2 + i * (2 * Math.PI / n);
  return { a, x: Math.cos(a) * SLOT_R, y: Math.sin(a) * SLOT_R };
}

function childrenOf(slot) {
  if (!slot) return [];
  if (Array.isArray(slot.children)) return slot.children.filter((child) => child && child.id).slice(0, 3);
  return slot.sub && slot.sub.id ? [slot.sub] : [];
}

function childPoint(parentIndex, childIndex) {
  const parent = polar(parentIndex);
  const angle = parent.a - 46 * Math.PI / 180 + childIndex * 46 * Math.PI / 180;
  return {
    x: parent.x + Math.cos(angle) * CHILD_DIST,
    y: parent.y + Math.sin(angle) * CHILD_DIST,
    angle,
  };
}

function slimSlot(s) {
  if (!s || !s.id) return null;
  const out = {
    id: s.id,
    type: s.type || "BUTTON",
    name: s.name || "",
    cat: s.cat || "",
    bitmap: s.bitmap || "",
  };
  const children = Array.isArray(s.children)
    ? s.children.filter((child) => child && child.id).slice(0, 3)
    : (s.sub && s.sub.id ? [s.sub] : []);
  if (children.length) {
    out.children = children.map((child) => ({
      id: child.id,
      type: child.type || "BUTTON",
      name: child.name || "",
      cat: child.cat || "",
      bitmap: child.bitmap || "",
    }));
  }
  return out;
}

function slimSlots() {
  return slots.map(slimSlot);
}

// 缩短父槽时被裁掉的槽位按位置存进 stash；再加长时优先还原，
// 避免「减少→增加」把已保存的槽位清空。随 saveSlots 一并落盘。
let slotStash = [];

function slimStash() {
  return slotStash.map(slimSlot);
}

/** Resize that keeps content: shrinking stashes the trimmed tail (latest
 * state wins per position), growing restores from the stash first. */
function applyCount(n) {
  if (n === slots.length) return false;
  const base = [];
  for (let i = 0; i < SLOT_MAX; i++) base.push(slotStash[i] || null);
  if (n < slots.length) {
    for (let i = n; i < slots.length; i++) if (slots[i]) base[i] = slots[i];
    slots = slots.slice(0, n);
  } else {
    const grown = slots.slice();
    for (let i = slots.length; i < n; i++) {
      grown[i] = base[i];
      base[i] = null;
    }
    slots = grown;
  }
  slotStash = base;
  return true;
}

function syncHint() {
  if (editorMode === "native") {
    const slot = slots[active];
    const targetName = slot && slot.id ? (slot.name || slot.id) : "空槽";
    $("#wheelHint").textContent = `${NATIVE_NAMES[active]} · 槽 ${active + 1}\n${targetName}`;
    $("#clearSlot").textContent = "清除当前槽";
    return;
  }
  const slot = slots[active];
  const children = childrenOf(slot);
  const target = activeTarget < 0 ? slot : children[activeTarget];
  const targetName = target && target.id ? (target.name || target.id) : "空槽";
  const kind = activeTarget < 0 ? "主槽" : `子槽 ${activeTarget + 1}`;
  $("#wheelHint").textContent = `${clockName(active)} · ${kind}\n${targetName}`;
  $("#clearSlot").textContent = `清除${kind}`;
}

let CATALOG = [];
let CATEGORIES = [];
let slots = new Array(8).fill(null);
let customSlots = slots;
let nativeSlots = new Array(NATIVE_N).fill(null);
let editorMode = "custom";
let nativeApps = [];
let nativeApplication = "";
let nativeBar = 1;
let nativeSource = "default";
let active = 0;
let activeTarget = -1;
let activeCat = "";
let results = [];
let sel = 0;
let uiStyle = "classic";

const input = $("#search");
const shell = $("#shell");
const resultsBox = $("#results");
const rowsBox = $("#rows");
const selPill = $("#selPill");
const idle = $("#idle");
const voidBox = $("#void");
const count = $("#count");
const catDd = $("#catDd");
const catBtn = $("#catBtn");
const catBtnLabel = $("#catBtnLabel");
const catMenu = $("#catMenu");
const nativeControls = $("#nativeControls");
const nativeAppBtn = $("#nativeAppBtn");
const nativeAppMenu = $("#nativeAppMenu");
const nativeAppLabel = $("#nativeAppLabel");

const DRAG_PREFIX = "nxcmd:";

function cmdRecord(cmd) {
  if (!cmd || !cmd.id) return null;
  return {
    id: cmd.id,
    type: cmd.type || "BUTTON",
    name: cmd.name || "",
    cat: cmd.cat || "",
    bitmap: cmd.bitmap || "",
    icon: cmd.icon || "",
  };
}

function readDragCmd(event) {
  try {
    const raw = event.dataTransfer.getData("text/plain") || "";
    if (raw.indexOf(DRAG_PREFIX) !== 0) return null;
    const parsed = JSON.parse(raw.slice(DRAG_PREFIX.length));
    return parsed && parsed.id ? parsed : null;
  } catch {
    return null;
  }
}

let dragGhost = null;
let dragGhostOx = 0;
let dragGhostOy = 0;
let dragGhostBound = false;
let dragImageCanvas = null;

function hideNativeDragImage(event) {
  try {
    if (!dragImageCanvas) {
      dragImageCanvas = document.createElement("canvas");
      dragImageCanvas.width = 2;
      dragImageCanvas.height = 2;
      dragImageCanvas.style.cssText = "position:absolute;left:-9999px;top:-9999px;width:2px;height:2px;opacity:0;pointer-events:none";
      document.body.appendChild(dragImageCanvas);
    }
    event.dataTransfer.setDragImage(dragImageCanvas, 1, 1);
  } catch { /* 合成事件没有原生拖影像 */ }
}

function ghostKind(el) {
  if (el.classList.contains("native-slot")) return "square";
  if (el.classList.contains("child")) return "child";
  if (el.classList.contains("slot")) return "circle";
  return "chip";
}

function ghostFace(el) {
  const wrap = document.createElement("div");
  wrap.className = "face";
  const face = el.querySelector(".face");
  if (face) {
    wrap.innerHTML = face.innerHTML;
    wrap.querySelectorAll(".child-name, .child-badge, .native-index").forEach((node) => node.remove());
  } else {
    const icon = el.querySelector(".row-icon");
    wrap.innerHTML = icon ? icon.innerHTML : FALLBACK_ICON;
  }
  const img = wrap.querySelector("img");
  if (img) img.draggable = false;
  return wrap;
}

function moveDragGhost(x, y) {
  if (!dragGhost || (!x && !y)) return;
  dragGhost.style.transform = `translate(${x - dragGhostOx}px, ${y - dragGhostOy}px)`;
}

function clearDragGhost() {
  if (dragGhost) {
    dragGhost.remove();
    dragGhost = null;
  }
  document.querySelectorAll(".slot.ghosting").forEach((slot) => slot.classList.remove("ghosting"));
}

function bindDragGhostTracking() {
  if (dragGhostBound) return;
  dragGhostBound = true;
  document.addEventListener("dragover", (event) => {
    if (dragGhost) moveDragGhost(event.clientX, event.clientY);
  }, true);
}

function startDragGhost(el, event) {
  hideNativeDragImage(event);
  clearDragGhost();
  bindDragGhostTracking();
  const ghost = document.createElement("div");
  ghost.className = "drag-ghost " + ghostKind(el);
  ghost.appendChild(ghostFace(el));
  document.body.appendChild(ghost);
  const box = ghost.getBoundingClientRect();
  dragGhostOx = box.width / 2;
  dragGhostOy = box.height / 2;
  dragGhost = ghost;
  moveDragGhost(event.clientX, event.clientY);
  if (el.classList.contains("slot")) el.classList.add("ghosting");
}

function bindDragSource(el, cmd) {
  const record = cmdRecord(cmd);
  if (!el || !record) return;
  el.draggable = true;
  el.addEventListener("dragstart", (event) => {
    const json = DRAG_PREFIX + JSON.stringify(record);
    event.dataTransfer.effectAllowed = "copy";
    event.dataTransfer.setData("text/plain", json);
    document.body.classList.add("slots-dragging");
    startDragGhost(el, event);
  });
  el.addEventListener("dragend", () => {
    document.body.classList.remove("slots-dragging");
    document.querySelectorAll(".slot.drop").forEach((slot) => slot.classList.remove("drop"));
    clearDragGhost();
  });
}

function canDropAt(parentIndex, childIndex) {
  if (editorMode === "native") return childIndex < 0;
  if (childIndex < 0) return true;
  const parent = slots[parentIndex];
  if (!parent || !parent.id) return false;
  return childIndex <= childrenOf(parent).length;
}

function bindDropTarget(el, parentIndex, childIndex) {
  if (!el) return;
  el.addEventListener("dragover", (event) => {
    if (!canDropAt(parentIndex, childIndex)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
    el.classList.add("drop");
  });
  el.addEventListener("dragleave", (event) => {
    if (!el.contains(event.relatedTarget)) el.classList.remove("drop");
  });
  el.addEventListener("drop", (event) => {
    event.preventDefault();
    el.classList.remove("drop");
    document.body.classList.remove("slots-dragging");
    clearDragGhost();
    const cmd = readDragCmd(event);
    if (!cmd) return;
    void dropAssign(parentIndex, childIndex, cmd);
  });
}

async function dropAssign(parentIndex, childIndex, cmd) {
  if (editorMode === "native") {
    active = parentIndex;
    activeTarget = -1;
    await assignNative(cmd);
    return;
  }
  if (childIndex >= 0) {
    const parent = slots[parentIndex];
    if (!parent || !parent.id) {
      toast("请先设置主槽");
      return;
    }
    if (childIndex > childrenOf(parent).length) {
      toast("请按顺序填写子槽");
      return;
    }
    active = parentIndex;
    activeTarget = childIndex;
  } else {
    active = parentIndex;
    activeTarget = -1;
  }
  await assign(cmd);
}

function bindSlotInteractions() {
  $("#slotList").querySelectorAll(".slot").forEach((el) => {
    if (el.classList.contains("child")) {
      const childIndex = +el.dataset.ci;
      const locked = el.classList.contains("locked");
      el.addEventListener("click", (event) => {
        event.stopPropagation();
        if (!locked) setTarget(childIndex);
      });
      if (!locked) {
        bindDropTarget(el, active, childIndex);
        const child = childrenOf(slots[active])[childIndex];
        if (child) bindDragSource(el, child);
      }
      return;
    }
    const index = +el.dataset.i;
    el.addEventListener("click", () => setActive(index));
    bindDropTarget(el, index, -1);
    if (slots[index] && slots[index].id) bindDragSource(el, slots[index]);
  });
}

function renderSlots() {
  if (editorMode === "native") {
    renderNativeSlots();
    return;
  }
  let html = slots.map((slot, i) => {
    const { x, y } = polar(i);
    const empty = !slot || !slot.id;
    const name = empty ? `槽 ${i + 1}` : (slot.name || slot.id);
    const childCount = childrenOf(slot).length;
    return `<div class="slot${empty ? " empty" : ""}${active === i ? " group-on" : ""}${active === i && activeTarget < 0 ? " on" : ""}" data-i="${i}" style="transform:translate(${x}px,${y}px)">
      <div class="face">${iconFor(slot)}${childCount ? `<span class="child-badge">${childCount}</span>` : ""}</div>
      <div class="caption"><div class="label">${escapeHtml(name)}</div></div>
    </div>`;
  }).join("");
  const parent = slots[active];
  const children = childrenOf(parent);
  if (parent && parent.id) {
    const parentPoint = polar(active);
    for (let i = 0; i < 3; i++) {
      const child = children[i];
      const point = childPoint(active, i);
      const locked = i > children.length;
      const lineAngle = Math.atan2(point.y - parentPoint.y, point.x - parentPoint.x) * 180 / Math.PI;
      html += `<div class="branch-line${locked ? " locked" : ""}" style="width:${CHILD_DIST}px;transform:translate(${parentPoint.x}px,${parentPoint.y}px) rotate(${lineAngle}deg)"></div>`;
      html += `<div class="slot child${child ? "" : " empty"}${locked ? " locked" : ""}${activeTarget === i ? " on" : ""}" data-ci="${i}" style="transform:translate(${point.x}px,${point.y}px)">
        <div class="face">${iconFor(child)}<span class="child-name">${escapeHtml(child ? (child.name || child.id) : `子槽 ${i + 1}`)}</span></div>
      </div>`;
    }
  }
  $("#slotList").innerHTML = html;
  bindSlotInteractions();
  syncHint();
}

function renderNativeSlots() {
  const html = slots.map((slot, i) => {
    const [x, y] = NATIVE_POS[i];
    const empty = !slot || !slot.id;
    const name = empty ? `槽 ${i + 1}` : (slot.name || slot.id);
    return `<div class="slot native-slot${empty ? " empty" : ""}${active === i ? " on" : ""}" data-i="${i}" style="transform:translate(${x}px,${y}px)">
      <div class="face"><span class="native-index">${i + 1}</span>${iconFor(slot)}</div>
      <div class="native-label">${escapeHtml(name)}</div>
    </div>`;
  }).join("");
  $("#slotList").innerHTML = html;
  bindSlotInteractions();
  syncHint();
}

function setActive(i) {
  const n = Math.max(1, slots.length);
  active = ((i % n) + n) % n;
  activeTarget = -1;
  renderSlots();
  input.focus();
}

function normalizeStyle(value) {
  const raw = String(value || "").toLowerCase();
  return raw === "radialz" || raw === "radial-z" ? "radialz" : "classic";
}

function applyStyle(next) {
  const style = normalizeStyle(next);
  uiStyle = style;
  document.documentElement.setAttribute("data-style", style);
  document.body.setAttribute("data-style", style);
  const classicBtn = $("#classicStyle");
  const radialzBtn = $("#radialzStyle");
  if (classicBtn) {
    classicBtn.classList.toggle("on", style === "classic");
    classicBtn.setAttribute("aria-pressed", String(style === "classic"));
  }
  if (radialzBtn) {
    radialzBtn.classList.toggle("on", style === "radialz");
    radialzBtn.setAttribute("aria-pressed", String(style === "radialz"));
  }
  return style;
}

async function setUiStyle(next) {
  const style = normalizeStyle(next);
  const changed = style !== uiStyle;
  applyStyle(style);
  if (Bridge.hosted) {
    try {
      const saved = await Bridge.call("saveUi", { style });
      if (saved && saved.style) applyStyle(saved.style);
    } catch (err) {
      toast(err.message);
      return;
    }
  }
  if (changed) toast(style === "radialz" ? "已保存为 RadialZ 风格" : "已保存为经典风格");
}

function syncCountCtrl() {
  const styleCtrl = $("#styleCtrl");
  const ctrl = $("#countCtrl");
  const slider = $("#slotCount");
  const value = $("#slotCountValue");
  const native = editorMode === "native";
  if (styleCtrl) styleCtrl.hidden = native;
  if (ctrl) ctrl.hidden = native;
  if (native || !slider || !value) return;
  slider.value = String(slots.length);
  value.textContent = String(slots.length);
}

async function setCustomCount(n, persist = true) {
  n = Math.max(SLOT_MIN, Math.min(SLOT_MAX, n | 0));
  if (editorMode === "native") return;
  if (applyCount(n)) {
    customSlots = slots;
    if (active >= n) active = n - 1;
    activeTarget = -1;
    renderSlots();
  }
  syncCountCtrl();
  if (!persist) return;
  if (Bridge.hosted) {
    try { await Bridge.call("saveSlots", { slots: slimSlots(), stash: slimStash() }); }
    catch (err) { toast(err.message); return; }
  }
  toast(`父槽 ${n} 个`);
}

function setTarget(i) {
  if (editorMode === "native") return;
  activeTarget = Math.max(0, Math.min(2, i));
  renderSlots();
  input.focus();
}

function renderNativeToolbar() {
  const app = nativeApps.find((item) => item.id === nativeApplication);
  nativeAppLabel.textContent = app ? app.name : (nativeApplication || "选择应用");
  nativeAppMenu.innerHTML = nativeApps.map((item) =>
    `<button type="button" class="native-app-item${item.id === nativeApplication ? " on" : ""}" data-app="${escapeAttr(item.id)}" role="option">
      <span>${escapeHtml(item.name)}</span><small>${escapeHtml(item.id)}</small>
    </button>`).join("");
  nativeAppMenu.querySelectorAll(".native-app-item").forEach((item) => {
    item.addEventListener("click", async (event) => {
      event.stopPropagation();
      nativeApplication = item.dataset.app;
      closeNativeAppMenu();
      await loadNativeRadial();
    });
  });
  document.querySelectorAll(".bar-btn").forEach((btn) =>
    btn.classList.toggle("on", +btn.dataset.bar === nativeBar));
}

function openNativeAppMenu() {
  if (!nativeApps.length) return;
  nativeAppMenu.hidden = false;
  nativeAppBtn.classList.add("open");
  nativeAppBtn.setAttribute("aria-expanded", "true");
}

function closeNativeAppMenu() {
  nativeAppMenu.hidden = true;
  nativeAppBtn.classList.remove("open");
  nativeAppBtn.setAttribute("aria-expanded", "false");
}

function syncModeToolbar() {
  const native = editorMode === "native";
  $("#customMode").classList.toggle("on", !native);
  $("#customMode").setAttribute("aria-selected", String(!native));
  $("#nativeMode").classList.toggle("on", native);
  $("#nativeMode").setAttribute("aria-selected", String(native));
  nativeControls.hidden = !native;
  $(".wheel-scale").classList.toggle("native-view", native);
  $("#clearSlot").classList.toggle("native-clear", native);
  syncCountCtrl();
  renderNativeToolbar();
}

async function setEditorMode(mode) {
  if (mode === editorMode) return;
  if (mode === "native" && !nativeApps.length) {
    toast("未找到 NX 原生菜单配置");
    return;
  }
  if (editorMode === "custom") customSlots = slots;
  else nativeSlots = slots;
  editorMode = mode;
  active = 0;
  activeTarget = -1;
  if (editorMode === "native") {
    slots = nativeSlots;
    syncModeToolbar();
    await loadNativeRadial();
  } else {
    slots = customSlots;
    syncModeToolbar();
    renderSlots();
  }
  input.focus();
}

async function loadNativeRadial() {
  if (!nativeApplication) return;
  if (Bridge.hosted) {
    try {
      const data = await Bridge.call("loadNativeRadial", { application: nativeApplication, bar: nativeBar });
      nativeSlots = Array.isArray(data.slots) && data.slots.length === NATIVE_N
        ? data.slots
        : new Array(NATIVE_N).fill(null);
      nativeSource = data.source || "default";
    } catch (err) {
      toast(err.message);
      return;
    }
  }
  slots = nativeSlots;
  activeTarget = -1;
  renderNativeToolbar();
  renderSlots();
}

function renderCats() {
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
  catBtnLabel.textContent = activeCat || "全部分类";
}

function openCatMenu() {
  catDd.classList.add("open");
  catMenu.hidden = false;
  catBtn.setAttribute("aria-expanded", "true");
}

function closeCatMenu() {
  catDd.classList.remove("open");
  catMenu.hidden = true;
  catBtn.setAttribute("aria-expanded", "false");
}

function setCat(cat) {
  activeCat = cat || "";
  renderCats();
  render();
}

async function render() {
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
    return;
  }

  if (!results.length) {
    resultsBox.classList.remove("has-rows");
    rowsBox.innerHTML = "";
    selPill.classList.remove("on");
    count.textContent = "";
    voidBox.hidden = false;
    return;
  }

  const names = [...new Set(results.map((x) => x.cmd.bitmap).filter(Boolean))];
  if (Bridge.hosted && names.length) {
    try {
      const res = await Bridge.call("ensureIcons", { names });
      const icons = (res && res.icons) || {};
      for (const x of results) {
        if (x.cmd.bitmap && icons[x.cmd.bitmap]) x.cmd.icon = icons[x.cmd.bitmap];
      }
    } catch { /* 无图标仍列出 */ }
  }

  count.textContent = q
    ? `${results.length} 个结果` + (activeCat ? ` · ${activeCat}` : "")
    : `${results.length} · ${activeCat || "全部"}`;

  resultsBox.classList.add("has-rows");
  rowsBox.innerHTML = results.map(({ cmd, hits }, i) => `
    <div class="row" style="--i:${i}" data-i="${i}" role="option">
      <span class="row-icon">${iconFor(cmd)}</span>
      <span class="row-main">
        <span class="row-name">${highlight(cmd.name, hits)}</span>
        <span class="row-desc">${escapeHtml(cmd.desc || cmd.nameEn || cmd.id)}</span>
      </span>
      <span class="row-cat">${escapeHtml(cmd.cat || "")}</span>
      ${cmd.key ? `<kbd class="row-key">${escapeHtml(cmd.key)}</kbd>` : ""}
    </div>`).join("");

  rowsBox.querySelectorAll(".row").forEach((row) => {
    const index = +row.dataset.i;
    row.addEventListener("mouseenter", () => select(index));
    row.addEventListener("click", () => assign(results[index].cmd));
    bindDragSource(row, results[index].cmd);
  });
  requestAnimationFrame(() => select(0, true));
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

const RECENT_KEY = "nx-slots-recent";
let recent = [];
try { recent = JSON.parse(localStorage.getItem(RECENT_KEY) || "[]"); } catch { recent = []; }

function saveRecent(cmd) {
  recent = [{ id: cmd.id, name: cmd.name }, ...recent.filter((n) => n.id !== cmd.id)].slice(0, 6);
  localStorage.setItem(RECENT_KEY, JSON.stringify(recent));
  renderRecent();
}

function renderRecent() {
  const box = $("#recent");
  if (!recent.length) {
    box.innerHTML = "";
    return;
  }
  box.innerHTML = recent
    .map((n, i) => `<button class="chip" style="--i:${i}" data-id="${escapeAttr(n.id)}"><span class="chip-label">${escapeHtml(n.name)}</span></button>`)
    .join("");
  box.querySelectorAll(".chip").forEach((chip) => {
    const cmd = CATALOG.find((c) => c.id === chip.dataset.id);
    chip.addEventListener("click", () => {
      if (cmd) assign(cmd);
    });
    if (cmd) bindDragSource(chip, cmd);
  });
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
  }, 1800);
}

async function assign(cmd) {
  if (editorMode === "native") {
    await assignNative(cmd);
    return;
  }
  const record = cmd
    ? { id: cmd.id, type: cmd.type || "BUTTON", name: cmd.name, cat: cmd.cat, bitmap: cmd.bitmap, icon: cmd.icon }
    : null;
  if (activeTarget < 0) {
    const preservedChildren = childrenOf(slots[active]);
    slots[active] = record;
    if (record && preservedChildren.length) slots[active].children = preservedChildren;
  } else {
    const parent = slots[active];
    if (!parent || !parent.id) {
      toast("请先设置主槽");
      return;
    }
    const children = childrenOf(parent);
    if (activeTarget > children.length) {
      toast("请按顺序填写子槽");
      return;
    }
    if (record) children[activeTarget] = record;
    else if (activeTarget < children.length) children.splice(activeTarget, 1);
    if (children.length) parent.children = children;
    else delete parent.children;
    delete parent.sub;
    if (!record) activeTarget = Math.min(activeTarget, children.length);
  }
  const written = activeTarget < 0 ? slots[active] : childrenOf(slots[active])[activeTarget];
  if (written && written.bitmap && !written.icon && Bridge.hosted) {
    try {
      const res = await Bridge.call("ensureIcons", { names: [written.bitmap] });
      const icons = (res && res.icons) || {};
      written.icon = icons[written.bitmap];
    } catch { /* 无图标 */ }
  }
  renderSlots();
  if (cmd) saveRecent(cmd);
  shell.classList.remove("bump");
  void shell.offsetWidth;
  shell.classList.add("bump");
  if (Bridge.hosted) {
    try { await Bridge.call("saveSlots", { slots: slimSlots(), stash: slimStash() }); }
    catch (err) { toast(err.message); return; }
  }
  const kind = activeTarget < 0 ? "主槽" : `子槽 ${activeTarget + 1}`;
  toast(cmd ? `已写入${kind}「${cmd.name}」` : `已清除${kind}`);
}

async function assignNative(cmd) {
  const record = cmd
    ? { id: cmd.id, type: cmd.type || "BUTTON", name: cmd.name, cat: cmd.cat, bitmap: cmd.bitmap, icon: cmd.icon }
    : null;
  if (record) {
    for (let i = 0; i < slots.length; i++) {
      if (i !== active && slots[i] && slots[i].id === record.id) slots[i] = null;
    }
  }
  slots[active] = record;
  nativeSlots = slots;
  if (record && record.bitmap && !record.icon && Bridge.hosted) {
    try {
      const res = await Bridge.call("ensureIcons", { names: [record.bitmap] });
      const icons = (res && res.icons) || {};
      record.icon = icons[record.bitmap];
    } catch { /* 无图标 */ }
  }
  renderSlots();
  if (cmd) saveRecent(cmd);
  if (Bridge.hosted) {
    try {
      await Bridge.call("saveNativeRadial", {
        application: nativeApplication,
        bar: nativeBar,
        slots: slimSlots(),
      });
      nativeSource = "custom";
    } catch (err) {
      toast(err.message);
      return;
    }
  }
  shell.classList.remove("bump");
  void shell.offsetWidth;
  shell.classList.add("bump");
  toast(cmd ? `已写入槽 ${active + 1}「${cmd.name}」· 重启 NX 生效` : `已清除槽 ${active + 1} · 重启 NX 生效`);
}

function requestClose() {
  if (!Bridge.hosted) return;
  Bridge.call("close").catch(() => {});
}

let debounce;
input.addEventListener("input", () => {
  clearTimeout(debounce);
  debounce = setTimeout(render, 40);
});

if (catBtn) {
  catBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    catMenu.hidden ? openCatMenu() : closeCatMenu();
  });
}
$("#customMode").addEventListener("click", () => setEditorMode("custom"));
$("#nativeMode").addEventListener("click", () => setEditorMode("native"));
if ($("#classicStyle")) $("#classicStyle").addEventListener("click", () => { void setUiStyle("classic"); });
if ($("#radialzStyle")) $("#radialzStyle").addEventListener("click", () => { void setUiStyle("radialz"); });
nativeAppBtn.addEventListener("click", (event) => {
  event.stopPropagation();
  nativeAppMenu.hidden ? openNativeAppMenu() : closeNativeAppMenu();
});
document.querySelectorAll(".bar-btn").forEach((btn) => {
  btn.addEventListener("click", async () => {
    const bar = +btn.dataset.bar;
    if (bar === nativeBar) return;
    nativeBar = bar;
    active = 0;
    await loadNativeRadial();
    input.focus();
  });
});
$("#nativeReset").addEventListener("click", async () => {
  if (!nativeApplication) return;
  if (Bridge.hosted) {
    try {
      const data = await Bridge.call("resetNativeRadial", { application: nativeApplication, bar: nativeBar });
      nativeSlots = Array.isArray(data.slots) && data.slots.length === NATIVE_N
        ? data.slots
        : new Array(NATIVE_N).fill(null);
      nativeSource = "default";
      slots = nativeSlots;
      renderSlots();
      toast(`Radial ${nativeBar} 已恢复默认 · 重启 NX 生效`);
    } catch (err) { toast(err.message); }
  } else {
    nativeSlots = new Array(NATIVE_N).fill(null);
    slots = nativeSlots;
    renderSlots();
  }
});
document.addEventListener("click", (e) => {
  if (catMenu && !catMenu.hidden &&
      !(catDd && catDd.contains(e.target)) &&
      !catMenu.contains(e.target)) {
    closeCatMenu();
  }
  if (nativeAppMenu && !nativeAppMenu.hidden &&
      !nativeAppBtn.contains(e.target) && !nativeAppMenu.contains(e.target)) {
    closeNativeAppMenu();
  }
});

input.addEventListener("keydown", (e) => {
  if (e.key === "ArrowDown") { e.preventDefault(); select(sel + 1); }
  else if (e.key === "ArrowUp") { e.preventDefault(); select(sel - 1); }
  else if (e.key === "Enter") {
    e.preventDefault();
    if (results[sel]) assign(results[sel].cmd);
  } else if (e.key === "Escape") {
    e.preventDefault();
    if (catMenu && !catMenu.hidden) closeCatMenu();
    else if (nativeAppMenu && !nativeAppMenu.hidden) closeNativeAppMenu();
    else requestClose();
  }
});

document.addEventListener("keydown", (e) => {
  if (e.key === "/" && document.activeElement !== input) {
    e.preventDefault();
    input.focus();
  }
});

$("#clearSlot").addEventListener("click", () => assign(null));

const slotCountEl = $("#slotCount");
if (slotCountEl) {
  slotCountEl.addEventListener("input", () => { void setCustomCount(+slotCountEl.value, false); });
  slotCountEl.addEventListener("change", () => { void setCustomCount(+slotCountEl.value, true); });
}

const GHOSTS = ["指定到当前槽位…", "试试「拉伸」", "搜索拼音或英文：extrude"];
const ghost = $("#ghost");

async function ghostLoop() {
  let gi = 0;
  await wait(800);
  while (true) {
    if (document.activeElement === input || input.value) {
      ghost.innerHTML = "";
      await wait(400);
      continue;
    }
    const text = GHOSTS[gi++ % GHOSTS.length];
    for (const ch of text) {
      if (document.activeElement === input || input.value) break;
      ghost.textContent = (ghost.textContent || "") + ch;
      ghost.innerHTML = escapeHtml(ghost.textContent) + '<span class="g-caret"></span>';
      await wait(70);
    }
    await wait(1800);
    while ((ghost.textContent || "").length) {
      if (document.activeElement === input || input.value) break;
      ghost.textContent = ghost.textContent.slice(0, -1);
      ghost.innerHTML = escapeHtml(ghost.textContent) + '<span class="g-caret"></span>';
      await wait(22);
    }
    ghost.innerHTML = "";
    await wait(300);
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

async function onShown(msg) {
  if (typeof msg.focus === "number") active = Math.max(0, Math.min(slots.length - 1, msg.focus));
  activeTarget = -1;
  input.value = "";
  activeCat = "";
  closeCatMenu();
  if (Bridge.hosted) {
    if (editorMode === "native") {
      await loadNativeRadial();
    } else {
      try {
        const saved = await Bridge.call("loadSlots");
        if (Array.isArray(saved.slots) && isCustomCount(saved.slots.length)) {
          customSlots = saved.slots;
          slots = customSlots;
        }
        if (Array.isArray(saved.stash)) slotStash = saved.stash;
        if (saved && saved.style) applyStyle(saved.style);
      } catch { /* 沿用内存槽位 */ }
    }
  }
  syncModeToolbar();
  syncCountCtrl();
  renderSlots();
  renderCats();
  render();
  renderRecent();
  setTimeout(() => input.focus(), 30);
}

async function boot() {
  if (Bridge.hosted) {
    try {
      const [cat, saved] = await Promise.all([
        Bridge.call("getCatalog"),
        Bridge.call("loadSlots"),
      ]);
      CATALOG = (cat.commands || []).map((c) => ({
        ...c,
        search: `${c.name} ${c.nameEn} ${c.desc} ${c.id} ${c.cat} ${c.synonyms || ""}`.toLowerCase(),
      }));
      CATEGORIES = cat.categories || [];
      customSlots = Array.isArray(saved.slots) && isCustomCount(saved.slots.length)
        ? saved.slots
        : new Array(8).fill(null);
      slots = customSlots;
      slotStash = Array.isArray(saved.stash) ? saved.stash : [];
      if (saved && saved.style) applyStyle(saved.style);
    } catch (err) {
      toast(err.message);
    }
    try {
      const nativeInfo = await Bridge.call("getNativeRadialInfo");
      nativeApps = Array.isArray(nativeInfo.apps) ? nativeInfo.apps : [];
      nativeApplication = nativeInfo.defaultApplication || (nativeApps[0] && nativeApps[0].id) || "";
    } catch (err) {
      $("#nativeMode").disabled = true;
      toast(err.message);
    }
  } else {
    customSlots = [
      { id: "UG_CREATE_SKETCH", name: "草图", cat: "草图" },
      { id: "UG_MODELING_EXTRUDED_FEATURE", name: "拉伸", cat: "建模" },
      { id: "UG_MODELING_REVOLVED_FEATURE", name: "旋转", cat: "建模" },
      { id: "UG_MODELING_HOLE_FEATURE", name: "孔", cat: "建模" },
      { id: "UG_MODELING_BLEND_FEATURE", name: "边倒圆", cat: "建模" },
      { id: "UG_MODELING_SUBTRACT_FEATURE", name: "求差", cat: "建模" },
      { id: "UG_MODELING_UNITE_FEATURE", name: "求和", cat: "建模" },
      { id: "UG_VIEW_FIT", name: "适合窗口", cat: "视图" },
    ];
    slots = customSlots;
    CATALOG = customSlots.map((c) => ({ ...c, nameEn: "", desc: "", search: c.name, type: "BUTTON" }));
    CATEGORIES = [...new Set(CATALOG.map((c) => c.cat))];
    nativeApps = [
      { id: "UG_APP_MODELING", name: "建模" },
      { id: "UG_APP_DRAFTING", name: "制图" },
    ];
    nativeApplication = "UG_APP_MODELING";
    nativeSlots = customSlots.map((slot) => ({ ...slot }));
  }

  $("#total").textContent = CATALOG.length;
  applyStyle(uiStyle);
  syncModeToolbar();
  syncCountCtrl();
  renderSlots();
  renderCats();
  renderRecent();
  render();
  ghostLoop();
  setTimeout(() => input.focus(), 160);
}

boot();
