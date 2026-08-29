/* ============================================================
   NX WebUI — 前端逻辑
   桥接层：在 WebView2 内走 chrome.webview 消息通道；
   在普通浏览器中自动降级为 Mock 数据，方便独立预览。
   ============================================================ */

"use strict";

/* ---------- WebView2 桥接层 ---------- */

const Bridge = (() => {
  const hosted = !!(window.chrome && window.chrome.webview);
  let reqId = 0;
  const pending = new Map();

  if (hosted) {
    window.chrome.webview.addEventListener("message", (e) => {
      const msg = e.data;
      if (msg && msg.type === "response" && pending.has(msg.id)) {
        const { resolve, reject } = pending.get(msg.id);
        pending.delete(msg.id);
        msg.ok ? resolve(msg.data) : reject(new Error(msg.error || "NX 调用失败"));
      }
    });
  }

  // Mock 数据：浏览器预览时使用，模拟网络/进程延迟
  const mockDB = {
    version: "NX 2412.3000",
    workPart: "bracket_asm.prt",
    displayPart: "bracket_asm.prt",
    units: "毫米 (mm)",
    bodyCount: 12,
  };

  function mockCall(action) {
    return new Promise((resolve, reject) => {
      const delay = 500 + Math.random() * 500;
      setTimeout(() => {
        if (action === "getSessionInfo") {
          // 轻微扰动，让刷新时的闪烁动画可见
          mockDB.bodyCount = 8 + Math.floor(Math.random() * 10);
          resolve({ ...mockDB });
        } else {
          reject(new Error(`动作「${action}」尚未接入 NX Open`));
        }
      }, delay);
    });
  }

  function call(action, payload = {}) {
    if (!hosted) return mockCall(action, payload);
    return new Promise((resolve, reject) => {
      const id = ++reqId;
      pending.set(id, { resolve, reject });
      window.chrome.webview.postMessage({ type: "invoke", id, action, payload });
      // 超时保护
      setTimeout(() => {
        if (pending.has(id)) {
          pending.delete(id);
          reject(new Error("NX 响应超时"));
        }
      }, 10000);
    });
  }

  return { hosted, call };
})();

/* ---------- 工具函数 ---------- */

const $ = (sel) => document.querySelector(sel);

function now() {
  return new Date().toLocaleTimeString("zh-CN", { hour12: false });
}

function addLog(msg, level = "info") {
  const log = $("#log");
  const entry = document.createElement("div");
  entry.className = "log-entry";
  entry.dataset.level = level;
  entry.innerHTML = `<span class="log-time">${now()}</span><span class="log-msg"></span>`;
  entry.querySelector(".log-msg").textContent = msg;
  log.appendChild(entry);
  log.scrollTop = log.scrollHeight;
}

function toast(msg) {
  const root = $("#toastRoot");
  const el = document.createElement("div");
  el.className = "toast";
  el.textContent = msg;
  root.appendChild(el);
  setTimeout(() => {
    el.classList.add("leaving");
    el.addEventListener("animationend", () => el.remove(), { once: true });
  }, 2400);
}

/* 按钮涟漪 */
document.addEventListener("click", (e) => {
  const btn = e.target.closest(".btn-primary");
  if (!btn) return;
  const rect = btn.getBoundingClientRect();
  const ripple = document.createElement("span");
  const size = Math.max(rect.width, rect.height);
  ripple.className = "ripple";
  ripple.style.cssText = `width:${size}px;height:${size}px;left:${e.clientX - rect.left - size / 2}px;top:${e.clientY - rect.top - size / 2}px`;
  btn.appendChild(ripple);
  ripple.addEventListener("animationend", () => ripple.remove(), { once: true });
});

/* ---------- 会话信息刷新 ---------- */

const FIELD_LABELS = {
  version: "NX 版本",
  workPart: "工作部件",
  displayPart: "显示部件",
  units: "单位",
  bodyCount: "实体数量",
};

let refreshing = false;

async function refreshSessionInfo() {
  if (refreshing) return;
  refreshing = true;

  const btn = $("#refreshBtn");
  const list = $("#infoList");
  btn.classList.add("loading");
  btn.disabled = true;
  list.classList.add("loading");
  addLog("调用 getSessionInfo …");

  try {
    const data = await Bridge.call("getSessionInfo");

    // 逐行填入，带闪烁动画
    Object.entries(FIELD_LABELS).forEach(([key], i) => {
      const el = list.querySelector(`[data-field="${key}"]`);
      setTimeout(() => {
        el.textContent = data[key] ?? "—";
        el.classList.remove("flash");
        void el.offsetWidth; // 重启动画
        el.classList.add("flash");
      }, i * 90);
    });

    $("#lastUpdated").textContent = `更新于 ${now()}`;
    addLog("会话信息已更新", "ok");
  } catch (err) {
    addLog(err.message, "error");
    toast(err.message);
  } finally {
    setTimeout(() => list.classList.remove("loading"), FIELD_LABELS ? 500 : 0);
    btn.classList.remove("loading");
    btn.disabled = false;
    refreshing = false;
  }
}

$("#refreshBtn").addEventListener("click", refreshSessionInfo);

document.addEventListener("keydown", (e) => {
  if (e.key.toLowerCase() === "r" && !e.ctrlKey && !e.metaKey && !e.altKey) {
    refreshSessionInfo();
  }
});

/* ---------- 快捷操作（占位） ---------- */

document.querySelectorAll(".action-card").forEach((card) => {
  card.addEventListener("click", async () => {
    const action = card.dataset.action;
    addLog(`调用 ${action} …`);
    try {
      await Bridge.call(action);
    } catch (err) {
      addLog(err.message, "warn");
      toast("该功能将在下一阶段接入 NX Open");
    }
  });
});

/* ---------- 日志清空 ---------- */

$("#clearLogBtn").addEventListener("click", () => {
  $("#log").innerHTML = "";
  addLog("日志已清空");
});

/* ---------- 主题切换 ---------- */

const themeBtn = $("#themeBtn");
const savedTheme = localStorage.getItem("nx-webui-theme");
if (savedTheme) {
  document.documentElement.dataset.theme = savedTheme;
} else if (window.matchMedia("(prefers-color-scheme: dark)").matches) {
  document.documentElement.dataset.theme = "dark";
}

themeBtn.addEventListener("click", () => {
  const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = next;
  localStorage.setItem("nx-webui-theme", next);
  addLog(next === "dark" ? "已切换到深色主题" : "已切换到浅色主题");
});

/* ---------- 初始化 ---------- */

(function init() {
  const pill = $("#statusPill");
  const text = $("#statusText");

  if (Bridge.hosted) {
    pill.dataset.state = "online";
    text.textContent = "已连接 NX";
    addLog("WebView2 桥接已就绪", "ok");
  } else {
    pill.dataset.state = "preview";
    text.textContent = "浏览器预览模式";
    addLog("未检测到 WebView2，使用 Mock 数据预览", "warn");
  }

  // 入场后自动拉取一次，展示完整动效链路
  setTimeout(refreshSessionInfo, 600);
})();
