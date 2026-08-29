"use strict";

const STATE_LABEL = {
  "not-installed": "未安装",
  installed: "已安装",
  partial: "不完整",
  legacy: "旧路径",
};

const $ = (id) => document.getElementById(id);

/* Same WebView2 bridge as NxWebUITool/webui-search/app.js */
const Bridge = (() => {
  const hosted = !!(window.chrome && window.chrome.webview);
  let reqId = 0;
  const pending = new Map();

  if (hosted) {
    window.chrome.webview.addEventListener("message", (e) => {
      let msg = e.data;
      if (typeof msg === "string") {
        try { msg = JSON.parse(msg); } catch { return; }
      }
      if (!msg) return;
      if (msg.data && msg.data.status) applyStatus(msg.data);
      if (msg.type === "response" && pending.has(msg.id)) {
        const { resolve, reject } = pending.get(msg.id);
        pending.delete(msg.id);
        msg.ok ? resolve(msg.data) : reject(new Error(msg.error || "调用失败"));
      } else if (msg.type === "response" && msg.ok === false && msg.error && msg.id === 0) {
        $("errorBanner").textContent = msg.error;
        setHidden($("errorBanner"), false);
      }
    });
  }

  function call(action, payload) {
    if (!hosted) return Promise.reject(new Error("WebView 桥未就绪"));
    return new Promise((resolve, reject) => {
      const id = ++reqId;
      pending.set(id, { resolve, reject });
      window.chrome.webview.postMessage({ type: "invoke", id, action, payload: payload || {} });
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

let busy = false;
let current = null;

function setHidden(el, hidden) {
  if (hidden) el.setAttribute("hidden", "");
  else el.removeAttribute("hidden");
}

function applyStatus(payload) {
  const status = payload.status || payload;
  if (!status) return;
  current = status;
  const state = status.state || "not-installed";
  $("stateBadge").textContent = STATE_LABEL[state] || state;
  $("stateBadge").dataset.state = state;

  const pill = $("statusPill");
  if (status.nxRunning) {
    pill.dataset.state = "warn";
    $("statusText").textContent = "NX 运行中";
  } else if (state === "installed") {
    pill.dataset.state = "online";
    $("statusText").textContent = "已就绪";
  } else if (state === "not-installed") {
    pill.dataset.state = "pending";
    $("statusText").textContent = "未安装";
  } else {
    pill.dataset.state = "warn";
    $("statusText").textContent = STATE_LABEL[state];
  }

  $("installDir").textContent = status.installDir || "—";
  $("sourceDir").textContent = status.sourceDir || "未找到载荷";
  $("envFile").textContent = status.envCustomDirsFile || "未设置";
  $("files").textContent = status.filesPresent
    ? "完整"
    : `缺 ${status.missingFiles ? status.missingFiles.length : "?"} 个文件`;

  const nx = status.preferredNx;
  if (nx) {
    $("nx").textContent = `${nx.release} · ${nx.version} · ${nx.baseDir}`;
  } else if (status.nxInstallations && status.nxInstallations.length) {
    $("nx").textContent = status.nxInstallations.map((item) => `${item.release} · ${item.baseDir}`).join("\n");
  } else {
    $("nx").textContent = "未检测到 NX";
  }

  const preserved = (status.customDirectories || []).filter((dir) =>
    dir.toLowerCase() !== (status.installDir || "").toLowerCase());
  $("preserved").textContent = preserved.length ? preserved.join("\n") : "无";

  setHidden($("nxBanner"), !status.nxRunning);
  const warn = status.warning && !status.nxRunning ? status.warning : "";
  $("warnBanner").textContent = warn;
  setHidden($("warnBanner"), !warn);

  const canDeploy = Boolean(status.sourceDir) && !status.nxRunning && !busy;
  $("deployBtn").disabled = !canDeploy;
  $("uninstallBtn").disabled = status.nxRunning || busy || state === "not-installed";
  $("deployLabel").textContent = state === "not-installed" ? "安装" : "修复 / 更新";

  if (payload.log && payload.log.length) {
    $("log").textContent = payload.log.join("\n");
    setHidden($("logCard"), false);
  }
  if (payload.error) {
    $("errorBanner").textContent = payload.error;
    setHidden($("errorBanner"), false);
  } else if (!payload.log) {
    setHidden($("errorBanner"), true);
  }
}

async function refresh() {
  setHidden($("errorBanner"), true);
  try {
    const data = await Bridge.call("status");
    applyStatus(data);
  } catch (err) {
    $("errorBanner").textContent = err.message;
    setHidden($("errorBanner"), false);
  }
}

async function run(action) {
  if (busy) return;
  busy = true;
  $("deployBtn").classList.toggle("loading", action === "deploy");
  $("deployBtn").disabled = true;
  $("uninstallBtn").disabled = true;
  setHidden($("confirmUninstall"), true);
  try {
    const data = await Bridge.call(action);
    applyStatus(data);
    if (data && data.ok === false && data.error) {
      $("errorBanner").textContent = data.error;
      setHidden($("errorBanner"), false);
    }
  } catch (err) {
    $("errorBanner").textContent = err.message;
    setHidden($("errorBanner"), false);
  } finally {
    busy = false;
    $("deployBtn").classList.remove("loading");
    if (current) applyStatus({ status: current, log: $("log").textContent ? $("log").textContent.split("\n") : [] });
  }
}

$("refreshBtn").addEventListener("click", () => void refresh());
$("deployBtn").addEventListener("click", () => void run("deploy"));
$("openBtn").addEventListener("click", () => void Bridge.call("openInstallDir"));
$("uninstallBtn").addEventListener("click", () => setHidden($("confirmUninstall"), false));
$("confirmNo").addEventListener("click", () => setHidden($("confirmUninstall"), true));
$("confirmYes").addEventListener("click", () => void run("uninstall"));

const themeBtn = $("themeBtn");
const saved = localStorage.getItem("nx-webui-deployer-theme");
if (saved === "dark") document.documentElement.setAttribute("data-theme", "dark");
themeBtn.addEventListener("click", () => {
  const next = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
  if (next === "dark") document.documentElement.setAttribute("data-theme", "dark");
  else document.documentElement.removeAttribute("data-theme");
  localStorage.setItem("nx-webui-deployer-theme", next);
});

if (Bridge.hosted) void refresh();
else {
  $("errorBanner").textContent = "WebView 桥未就绪";
  setHidden($("errorBanner"), false);
}
