"use strict";

const $ = (s) => document.querySelector(s);

const ICONS = {
  "文件": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M7 3.5h7l5 5V20a1.5 1.5 0 0 1-1.5 1.5H7A1.5 1.5 0 0 1 7 3.5z"/><path d="M14 3.5V9h5.5"/></svg>',
  "编辑": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 5.5l4 4L8 20H4v-4L14.5 5.5z"/></svg>',
  "视图": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/></svg>',
  "建模": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M12 2.8l7.6 4.4v8.8L12 20.4l-7.6-4.4V7.2L12 2.8z"/><path d="M4.4 7.2L12 11.6l7.6-4.4M12 11.6v8.8"/></svg>',
  "草图": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M4 19C4 10 10 4 20 4"/></svg>',
  "装配": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M12 3l8 4-8 4-8-4 8-4 8-4z"/><path d="M4 12l8 4 8-4M4 16.5l8 4 8-4"/></svg>',
  "制图": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><rect x="4" y="3.5" width="16" height="17" rx="1.5"/><path d="M8 8h8M8 12h8M8 16h5"/></svg>',
  "分析": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M3 17L17 3l4 4L7 21l-4-4z"/></svg>',
  "加工": '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M12 5V3M12 21v-2M5 12H3M21 12h-2M7 7L5.5 5.5M18.5 18.5L17 17M17 7l1.5-1.5M7 17l-1.5 1.5"/></svg>',
};

const ID_ICONS = {
  UG_CREATE_SKETCH: ICONS["草图"],
  UG_MODELING_EXTRUDED_FEATURE: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M6 20V9l6-4 6 4v11"/><path d="M6 9l6 4 6-4M12 13v7"/></svg>',
  UG_MODELING_REVOLVED_FEATURE: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M12 4v16"/><path d="M16 7a8 8 0 1 1-8 0"/></svg>',
  UG_MODELING_HOLE_FEATURE: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="7"/><circle cx="12" cy="12" r="2.2"/></svg>',
  UG_MODELING_BLEND_FEATURE: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M5 19V9a4 4 0 0 1 4-4h10"/></svg>',
  UG_MODELING_SUBTRACT_FEATURE: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="10" cy="12" r="5.5"/><circle cx="15" cy="12" r="5.5"/><path d="M8 12h7" stroke-linecap="round"/></svg>',
  UG_MODELING_UNITE_FEATURE: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="10" cy="12" r="5.5"/><circle cx="15" cy="12" r="5.5"/></svg>',
  UG_VIEW_FIT: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/></svg>',
};

const FALLBACK_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M5 19l9.5-9.5"/><path d="M12.5 7.5l4 4"/><path d="M15.2 4.8l4 4-2.2 2.2-4-4z"/></svg>';
const EMPTY_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="7"/></svg>';

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
      if (msg && msg.type === "spaceup") {
        onSpaceUp();
        return;
      }
      if (msg && msg.type === "pointer" && Number.isFinite(msg.x) && Number.isFinite(msg.y)) {
        updateHoverAt(msg.x, msg.y);
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

// RadialZ 式分支菜单：主环半径 158；每个主槽最多三个子槽向外扇出。
const SLOT_R = 158;
const CHILD_DIST = 92;
const CHILD_HIT = 34;
const DEAD = 46;
const DESIGN_SIDE = 600;
const SLOT_MIN = 4;
const SLOT_MAX = 10;

let slots = new Array(8).fill(null);
let slotsJson = ""; // 当前已渲染数据的指纹：shown 重复到达时避免无谓重渲染吞掉进行中的点击
let iconMap = {};   // bitmap 名 → PNG data URL（loadSlots 附带，槽位本体不再内嵌图标）
let usage = [];     // 最近使用命令（radial-usage.json），空槽动态填充 + 圆心重复上次
let hover = -1;    // 主环高亮方位
let expandedSlot = -1; // 当前展开子槽的主槽
let childHover = -1;   // 当前高亮的子槽索引
let lastHit = { ring: "none", index: -1 }; // 最近一次指针命中的环（含 center）
let spaceHeld = false;
let busy = false;
let layoutScale = 1;

const wheel = $("#wheel");
const slotsBox = $("#slots");
const hint = $("#hint");
const deco = $("#deco");

let uiStyle = "classic";
let fanTimers = [];

/** Keep the full radial menu inside the actual WebView viewport. This is the
 * page-side fallback for small work areas and host/WebView DPI mismatches. */
function syncLayoutScale() {
  const width = Math.max(1, document.documentElement.clientWidth || window.innerWidth || DESIGN_SIDE);
  const height = Math.max(1, document.documentElement.clientHeight || window.innerHeight || DESIGN_SIDE);
  const next = Math.min(1, width / DESIGN_SIDE, height / DESIGN_SIDE);
  if (Math.abs(next - layoutScale) < 0.001) return false;
  layoutScale = next;
  document.documentElement.style.setProperty("--radial-scale", next.toFixed(4));
  return true;
}

function normalizeStyle(value) {
  const raw = String(value || "").toLowerCase();
  return raw === "radialz" || raw === "radial-z" ? "radialz" : "classic";
}

function applyStyle(next) {
  const style = normalizeStyle(next);
  if (uiStyle === style && document.documentElement.getAttribute("data-style") === style) {
    renderDeco();
    return style;
  }
  uiStyle = style;
  document.documentElement.setAttribute("data-style", style);
  document.body.setAttribute("data-style", style);
  renderDeco();
  return style;
}

function hexAlpha(hex, a) {
  const n = parseInt(String(hex).replace("#", ""), 16);
  if (!Number.isFinite(n)) return `rgba(248,107,51,${a})`;
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
}

function renderDeco() {
  if (!deco) return;
  if (uiStyle !== "radialz") {
    deco.innerHTML = "";
    return;
  }
  const cx = 260;
  const cy = 260;
  const rings = [
    { frac: 0.28, op: 0.05 },
    { frac: 0.52, op: 0.03 },
    { frac: 0.76, op: 0.02 },
  ];
  let html = "";
  for (const ring of rings) {
    html += `<circle cx="${cx}" cy="${cy}" r="${SLOT_R * ring.frac}" fill="none" stroke="rgba(255,255,255,${ring.op})" stroke-width="1"/>`;
  }
  for (let i = 0; i < slots.length; i++) {
    const { a } = polar(i);
    const inner = 8;
    const outer = SLOT_R - 38 - 6;
    const x1 = cx + Math.cos(a) * inner;
    const y1 = cy + Math.sin(a) * inner;
    const x2 = cx + Math.cos(a) * outer;
    const y2 = cy + Math.sin(a) * outer;
    html += `<line x1="${x1.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}" stroke="${hexAlpha("#f86b33", 0.3)}" stroke-width="1"/>`;
  }
  deco.innerHTML = html;
}

function iconFor(slot) {
  if (!slot || !slot.id) return EMPTY_ICON;
  const fallback = ID_ICONS[slot.id] || ICONS[slot.cat] || FALLBACK_ICON;
  const mapped = slot.bitmap ? iconMap[slot.bitmap] : "";
  const url = mapped
    || (slot.icon && String(slot.icon).indexOf("data:image/") === 0 ? slot.icon : "");
  if (url) {
    return `<img class="nx-icon" src="${url}" alt="" draggable="false" onerror="this.outerHTML=decodeURIComponent(this.dataset.fb)" data-fb="${encodeURIComponent(fallback)}">`;
  }
  return fallback;
}

function polar(i, r = SLOT_R) {
  const n = Math.max(1, slots.length);
  const a = -Math.PI / 2 + i * (2 * Math.PI / n);
  return { a, x: Math.cos(a) * r, y: Math.sin(a) * r };
}

function payloadFingerprint(nextSlots) {
  return JSON.stringify(nextSlots)
    + "|" + usage.map((u) => u.id).join(",")
    + "|" + Object.keys(iconMap).sort().join(",");
}

// 数据没变就不动 DOM：renderSlots 会整体替换 innerHTML，
// 若落在用户 mousedown/mouseup 之间会吞掉 click（症状：点图标要两下才执行）
function applySlots(next) {
  if (!Array.isArray(next) || next.length < SLOT_MIN || next.length > SLOT_MAX) return false;
  const json = payloadFingerprint(next);
  if (json === slotsJson) return false;
  slots = next;
  slotsJson = json;
  renderSlots();
  return true;
}

// loadSlots 瘦载荷：{ slots（不含 icon）, icons 映射, usage 最近使用 }
function applyPayload(saved) {
  iconMap = (saved && saved.icons && typeof saved.icons === "object") ? saved.icons : {};
  usage = Array.isArray(saved && saved.usage)
    ? saved.usage.filter((item) => item && item.id)
    : [];
  const next = saved && Array.isArray(saved.slots) ? saved.slots : null;
  if (applySlots(next)) return true;
  const json = payloadFingerprint(slots);
  if (json === slotsJson) return false;
  slotsJson = json;
  renderSlots();
  return true;
}

function lastCommand() {
  return usage.length ? usage[0] : null;
}

/** 空槽的动态内容：跳过已钉在圆盘上的命令，第 k 个空槽显示第 k 条最近使用。
 * 只影响展示与执行，永不写回 radial-slots.json。 */
function ghostAt(i) {
  const taken = new Set();
  for (const slot of slots) {
    if (slot && slot.id) taken.add(slot.id);
  }
  const pool = usage.filter((item) => item && item.id && !taken.has(item.id));
  if (!pool.length) return null;
  let k = -1;
  for (let j = 0; j <= i; j++) {
    if (!slots[j] || !slots[j].id) k++;
  }
  return k >= 0 && k < pool.length ? pool[k] : null;
}

/** 主槽实际执行的命令：已配置槽位优先，空槽回落到幽灵（最近使用）命令。 */
function commandAt(i) {
  const slot = slots[i];
  if (slot && slot.id) return slot;
  return ghostAt(i);
}

function childrenOf(i) {
  const slot = slots[i];
  if (!slot) return [];
  if (Array.isArray(slot.children)) return slot.children.filter((child) => child && child.id).slice(0, 3);
  return slot.sub && slot.sub.id ? [slot.sub] : [];
}

function prefersReducedMotion() {
  return !!(window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches);
}

function clearFanTimers() {
  for (const id of fanTimers) clearTimeout(id);
  fanTimers = [];
}

function armFan() {
  clearFanTimers();
  const nodes = [...slotsBox.querySelectorAll(".slot.child,.branch-line")];
  if (!nodes.length) return;
  if (prefersReducedMotion()) {
    nodes.forEach((el) => el.classList.add("ready"));
    return;
  }
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      const children = nodes.filter((el) => el.classList.contains("child"));
      const lines = nodes.filter((el) => el.classList.contains("branch-line"));
      children.forEach((el, i) => {
        const id = window.setTimeout(() => {
          el.classList.add("ready");
          if (lines[i]) lines[i].classList.add("ready");
        }, i * 28);
        fanTimers.push(id);
      });
    });
  });
}

function childPoint(parentIndex, childIndex) {
  const children = childrenOf(parentIndex);
  const parent = polar(parentIndex);
  const spreadDeg = children.length === 1 ? 0 : children.length === 2 ? 34 : 46;
  const angle = parent.a - spreadDeg * Math.PI / 180
    + (children.length > 1 ? (spreadDeg * 2 * Math.PI / 180) * childIndex / (children.length - 1) : 0);
  return {
    x: parent.x + Math.cos(angle) * CHILD_DIST,
    y: parent.y + Math.sin(angle) * CHILD_DIST,
    angle,
  };
}

function renderChildren() {
  clearFanTimers();
  slotsBox.querySelectorAll(".slot.child,.branch-line").forEach((el) => el.remove());
  const children = childrenOf(expandedSlot);
  if (expandedSlot < 0 || children.length === 0) return;
  const parent = polar(expandedSlot);
  let html = "";
  children.forEach((child, childIndex) => {
    const point = childPoint(expandedSlot, childIndex);
    const lineAngle = Math.atan2(point.y - parent.y, point.x - parent.x) * 180 / Math.PI;
    const fromX = (parent.x - point.x).toFixed(1);
    const fromY = (parent.y - point.y).toFixed(1);
    html += `<div class="branch-line" style="--i:${childIndex};--line-w:${CHILD_DIST}px;transform:translate(${parent.x}px,${parent.y}px) rotate(${lineAngle}deg)"></div>`;
    html += `<div class="slot child${childHover === childIndex ? " on" : ""}" data-pi="${expandedSlot}" data-ci="${childIndex}" style="--i:${childIndex};--from-x:${fromX}px;--from-y:${fromY}px;transform:translate(${point.x}px,${point.y}px)">
      <div class="face">${iconFor(child)}<span class="child-name">${escapeHtml(child.name || child.id)}</span></div>
    </div>`;
  });
  slotsBox.insertAdjacentHTML("beforeend", html);
  armFan();
}

function renderSlots() {
  let html = slots.map((slot, i) => {
    const { x, y } = polar(i);
    const empty = !slot || !slot.id;
    const ghost = empty ? ghostAt(i) : null;
    const name = empty ? (ghost ? (ghost.name || ghost.id) : `槽 ${i + 1}`) : (slot.name || slot.id);
    const childCount = childrenOf(i).length;
    return `<div class="slot${empty ? " empty" : ""}${ghost ? " ghost" : ""}${childCount ? " nested" : ""}${hover === i ? " on" : ""}" data-i="${i}" style="--i:${i};transform:translate(${x}px,${y}px)">
      <div class="face">${iconFor(slot || ghost)}${childCount ? `<span class="child-badge">${childCount}</span>` : ""}${ghost ? `<span class="ghost-badge">↻</span>` : ""}</div>
      <div class="caption">
        <div class="label">${escapeHtml(name)}</div>
      </div>
    </div>`;
  }).join("");
  slotsBox.innerHTML = html;
  slotsBox.classList.toggle("has-children", slots.some((_, i) => childrenOf(i).length > 0));
  renderChildren();
  renderDeco();
  requestAnimationFrame(() => {
    slotsBox.querySelectorAll(".slot:not(.child)").forEach((el) => el.classList.add("ready"));
  });
}

function syncHint() {
  if (expandedSlot >= 0 && childHover >= 0) {
    const child = childrenOf(expandedSlot)[childHover];
    hint.textContent = child ? (child.name || child.id) : "";
    return;
  }
  if (hover >= 0) {
    const cmd = commandAt(hover);
    if (cmd) {
      const pinned = !!(slots[hover] && slots[hover].id);
      hint.textContent = pinned ? (cmd.name || cmd.id) : `最近 · ${cmd.name || cmd.id}`;
      return;
    }
    hint.textContent = spaceHeld ? "空槽 · 松开关闭" : "空槽 · 点击关闭";
    return;
  }
  if (lastHit.ring === "center") {
    const last = lastCommand();
    if (last) {
      hint.textContent = `重复 · ${last.name || last.id}`;
      return;
    }
  }
  hint.textContent = spaceHeld ? "滑向命令松开执行 · 子槽向外展开" : "点图标执行 · 子槽向外展开";
}

function setHover(i) {
  if (hover === i) return;
  hover = i;
  slotsBox.querySelectorAll(".slot:not(.child)").forEach((el) => {
    el.classList.toggle("on", +el.dataset.i === i);
  });
  const nextExpanded = i >= 0 && childrenOf(i).length > 0 ? i : -1;
  if (nextExpanded !== expandedSlot) {
    expandedSlot = nextExpanded;
    childHover = -1;
    renderChildren();
  }
  syncHint();
}

function setChildHover(i) {
  if (childHover === i) return;
  childHover = i;
  slotsBox.querySelectorAll(".slot.child").forEach((el) => {
    el.classList.toggle("on", +el.dataset.ci === i);
  });
  syncHint();
}

function childHit(dx, dy) {
  if (expandedSlot < 0) return -1;
  const children = childrenOf(expandedSlot);
  for (let i = 0; i < children.length; i++) {
    const point = childPoint(expandedSlot, i);
    if (Math.hypot(dx - point.x, dy - point.y) <= CHILD_HIT) return i;
  }
  return -1;
}

function angleDistance(a, b) {
  return Math.abs(Math.atan2(Math.sin(a - b), Math.cos(a - b)));
}

/** Once the pointer has passed a child circle, keep that child selectable
 * along its outward ray. This preserves marking-menu intent anywhere on the
 * screen instead of dropping back to the parent beyond the 34px circle. */
function childRayHit(dx, dy) {
  if (expandedSlot < 0) return -1;
  const children = childrenOf(expandedSlot);
  if (children.length === 0) return -1;
  const parent = polar(expandedSlot);
  const vx = dx - parent.x;
  const vy = dy - parent.y;
  if (Math.hypot(vx, vy) < CHILD_DIST + CHILD_HIT * 0.5) return -1;

  const pointerAngle = Math.atan2(vy, vx);
  let bestIndex = -1;
  let bestDistance = Infinity;
  for (let i = 0; i < children.length; i++) {
    const distance = angleDistance(pointerAngle, childPoint(expandedSlot, i).angle);
    if (distance < bestDistance) {
      bestDistance = distance;
      bestIndex = i;
    }
  }
  const separationDeg = children.length === 2 ? 68 : children.length === 3 ? 46 : 52;
  const tolerance = Math.min(24, separationDeg / 2 - 2) * Math.PI / 180;
  return bestDistance <= tolerance ? bestIndex : -1;
}

function inBranchCorridor(dx, dy) {
  if (expandedSlot < 0) return false;
  const parent = polar(expandedSlot);
  const vx0 = dx - parent.x;
  const vy0 = dy - parent.y;
  return childrenOf(expandedSlot).some((_, i) => {
    const child = childPoint(expandedSlot, i);
    const vx = child.x - parent.x;
    const vy = child.y - parent.y;
    const len2 = vx * vx + vy * vy;
    const t = Math.max(0, Math.min(1, (vx0 * vx + vy0 * vy) / len2));
    return Math.hypot(vx0 - vx * t, vy0 - vy * t) <= 25;
  });
}

/** 命中：子槽圆片与延长射线优先，主槽按角度覆盖整个屏幕。 */
function hitFromPoint(clientX, clientY) {
  const rect = wheel.getBoundingClientRect();
  const cx = rect.left + rect.width / 2;
  const cy = rect.top + rect.height / 2;
  const scale = layoutScale || 1;
  const dx = (clientX - cx) / scale;
  const dy = (clientY - cy) / scale;
  const dist = Math.hypot(dx, dy);
  const child = childHit(dx, dy);
  if (child >= 0) return { ring: "child", index: child };
  if (inBranchCorridor(dx, dy)) return { ring: "main", index: expandedSlot };
  const rayChild = childRayHit(dx, dy);
  if (rayChild >= 0) return { ring: "child", index: rayChild };
  if (dist < DEAD) return lastCommand() ? { ring: "center", index: -1 } : { ring: "none", index: -1 };
  let a = Math.atan2(dy, dx) + Math.PI / 2;
  if (a < 0) a += Math.PI * 2;
  const n = Math.max(1, slots.length);
  const sector = Math.round(a / (2 * Math.PI / n)) % n;
  return { ring: "main", index: sector };
}

async function onShown(msg) {
  syncLayoutScale();
  spaceHeld = !!msg.spaceHeld;
  lastHit = { ring: "none", index: -1 };
  document.body.classList.toggle("held", spaceHeld);
  document.body.classList.remove("repeat-center");
  if (msg && msg.style) applyStyle(msg.style);
  setHover(-1);
  expandedSlot = -1;
  setChildHover(-1);
  renderChildren();
  syncHint();
  busy = false;
  if (Bridge.hosted) {
    try {
      const saved = await Bridge.call("loadSlots");
      if (saved && saved.style) applyStyle(saved.style);
      applyPayload(saved);
    } catch { /* 沿用当前槽位 */ }
  }
}

function onSpaceUp() {
  if (busy) return;
  if (expandedSlot >= 0 && childHover >= 0) {
    const child = childrenOf(expandedSlot)[childHover];
    if (child) { execute(child); return; }
  }
  if (hover >= 0) {
    const cmd = commandAt(hover);
    if (cmd) { execute(cmd); return; }
    requestClose();
    return;
  }
  if (lastHit.ring === "center") {
    const last = lastCommand();
    if (last) { execute(last); return; }
  }
  requestClose();
}

function execute(slot) {
  if (!slot || !slot.id || busy) return;
  busy = true;
  if (!Bridge.hosted) {
    busy = false;
    return;
  }
  Bridge.call("execute", { id: slot.id, type: slot.type || "BUTTON" })
    .catch(() => { busy = false; });
}

function requestClose() {
  if (!Bridge.hosted) return;
  Bridge.call("close").catch(() => {});
}

function escapeHtml(s) {
  return String(s || "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

function updateHoverAt(clientX, clientY) {
  const hit = hitFromPoint(clientX, clientY);
  lastHit = hit;
  document.body.classList.toggle("repeat-center", hit.ring === "center");
  if (hit.ring === "child") {
    setChildHover(hit.index);
  } else if (hit.ring === "main") {
    setChildHover(-1);
    setHover(hit.index);
  } else {
    setHover(-1);
    setChildHover(-1);
  }
  syncHint();
}

document.addEventListener("pointermove", (e) => {
  updateHoverAt(e.clientX, e.clientY);
});

document.addEventListener("click", (e) => {
  const childEl = e.target.closest(".slot.child");
  if (childEl) {
    const child = childrenOf(+childEl.dataset.pi)[+childEl.dataset.ci];
    if (child) execute(child);
    return;
  }
  const el = e.target.closest(".slot");
  if (el) {
    const cmd = commandAt(+el.dataset.i);
    if (cmd) execute(cmd);
    else requestClose();
    return;
  }
  const hit = hitFromPoint(e.clientX, e.clientY);
  if (hit.ring === "center") {
    const last = lastCommand();
    if (last) execute(last);
    else requestClose();
    return;
  }
  if (e.target === $("#stage") || e.target === document.body || hit.ring === "none") requestClose();
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    e.preventDefault();
    requestClose();
  }
  if (e.key === " " || e.code === "Space") e.preventDefault();
});

document.addEventListener("keyup", (e) => {
  if ((e.key === " " || e.code === "Space") && spaceHeld) {
    e.preventDefault();
    onSpaceUp();
  }
});

syncLayoutScale();
window.addEventListener("resize", syncLayoutScale, { passive: true });
renderSlots();

async function boot() {
  syncLayoutScale();
  applyStyle(new URLSearchParams(location.search).get("style"));
  if (!Bridge.hosted) {
    document.documentElement.style.background = "#1c1c1a";
    document.body.style.background = "radial-gradient(ellipse at 50% 42%, #3a3936 0%, #161614 72%)";
    slots = [
      { id: "UG_CREATE_SKETCH", name: "草图", cat: "草图" },
      { id: "UG_MODELING_EXTRUDED_FEATURE", name: "拉伸", cat: "建模",
        children: [
          { id: "UG_MODELING_BLOCK_FEATURE", name: "块", cat: "建模" },
          { id: "UG_MODELING_HOLE_FEATURE", name: "孔", cat: "建模" },
          { id: "UG_MODELING_BLEND_FEATURE", name: "边倒圆", cat: "建模" },
        ] },
      { id: "UG_MODELING_REVOLVED_FEATURE", name: "旋转", cat: "建模" },
      { id: "UG_MODELING_HOLE_FEATURE", name: "孔", cat: "建模" },
      { id: "UG_MODELING_BLEND_FEATURE", name: "边倒圆", cat: "建模" },
      { id: "UG_MODELING_SUBTRACT_FEATURE", name: "求差", cat: "建模" },
      { id: "UG_MODELING_UNITE_FEATURE", name: "求和", cat: "建模" },
      { id: "UG_VIEW_FIT", name: "适合窗口", cat: "视图" },
    ];
    renderSlots();
    return;
  }

  try {
    const saved = await Bridge.call("loadSlots");
    if (saved && saved.style) applyStyle(saved.style);
    if (!applyPayload(saved) && slots.every((s) => !s)) renderSlots();
  } catch {
    renderSlots();
  }
}

boot();
