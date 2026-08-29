"use strict";

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];

const wait = (ms) => new Promise((ok) => setTimeout(ok, ms));

const hour = new Date().getHours();
const greeting =
  hour < 6 ? "夜深了" : hour < 12 ? "早上好" : hour < 18 ? "下午好" : "晚上好";

/* ---------- 光斑追随 ---------- */

const follow = $(".lantern-follow");
let mx = innerWidth / 2, my = innerHeight / 3;
addEventListener("pointermove", (e) => {
  mx = e.clientX;
  my = e.clientY;
  document.documentElement.style.setProperty("--mx", mx + "px");
  document.documentElement.style.setProperty("--my", my + "px");
});

/* ---------- 开场：逐字 + 打字机 ---------- */

function splitDisplay() {
  const el = $("#display");
  const raw = el.textContent;
  el.setAttribute("aria-label", raw);
  el.textContent = "";
  [...raw].forEach((ch, i) => {
    const span = document.createElement("span");
    span.className = ch === " " ? "ch space" : "ch";
    span.style.setProperty("--i", i);
    span.textContent = ch === " " ? "\u00a0" : ch;
    el.appendChild(span);
  });
}

async function typeKicker(text) {
  const el = $("#kicker");
  el.textContent = "";
  for (const ch of text) {
    el.textContent += ch;
    await wait(42);
  }
}

async function typeLede(text) {
  const el = $("#lede");
  el.innerHTML = "";
  const caret = document.createElement("span");
  caret.className = "caret";
  el.appendChild(caret);
  for (const ch of text) {
    caret.insertAdjacentText("beforebegin", ch);
    await wait(16 + Math.random() * 22);
  }
  await wait(1800);
  caret.remove();
}

splitDisplay();
typeKicker(greeting + " · 稿笺");
wait(900).then(() =>
  typeLede("这不是命令面板，是一张可以写给 NX 的纸。点一个念头，或直接写下你想发生的事。")
);

/* ---------- 磁吸念头 ---------- */

const intents = $("#intents");
const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;

intents.addEventListener("pointermove", (e) => {
  if (reduce) return;
  $$(".intent").forEach((btn) => {
    const r = btn.getBoundingClientRect();
    const cx = r.left + r.width / 2;
    const cy = r.top + r.height / 2;
    const dx = e.clientX - cx;
    const dy = e.clientY - cy;
    const d = Math.hypot(dx, dy);
    const reach = 140;
    if (d < reach) {
      const f = (1 - d / reach) * 10;
      btn.style.setProperty("--tx", (dx / d) * f + "px");
      btn.style.setProperty("--ty", (dy / d) * f + "px");
    } else {
      btn.style.setProperty("--tx", "0px");
      btn.style.setProperty("--ty", "0px");
    }
  });
});

intents.addEventListener("pointerleave", () => {
  $$(".intent").forEach((btn) => {
    btn.style.setProperty("--tx", "0px");
    btn.style.setProperty("--ty", "0px");
  });
});

$$(".intent").forEach((btn) => {
  btn.addEventListener("click", () => {
    const map = {
      session: "看看此刻的会话",
      sculpt: "塑造一个方块",
      gaze: "看看现在选了什么",
      night: "__night__",
    };
    const v = map[btn.dataset.intent];
    if (v === "__night__") {
      wipeTheme(btn);
      return;
    }
    ask(v);
  });
});

/* ---------- 主题圆形擦除 ---------- */

function wipeTheme(fromEl) {
  const r = fromEl.getBoundingClientRect();
  const x = r.left + r.width / 2;
  const y = r.top + r.height / 2;
  const next = document.documentElement.dataset.theme === "night" ? "day" : "night";
  const wipe = document.createElement("div");
  wipe.className = `wipe to-${next}`;
  wipe.style.setProperty("--x", x + "px");
  wipe.style.setProperty("--y", y + "px");
  document.body.appendChild(wipe);
  wipe.addEventListener(
    "animationend",
    () => {
      if (next === "night") document.documentElement.dataset.theme = "night";
      else delete document.documentElement.dataset.theme;
      wipe.remove();
    },
    { once: true }
  );
}

$("#lantern").addEventListener("click", (e) => wipeTheme(e.currentTarget));

/* ---------- 写作态 ---------- */

const workspace = $("#workspace");
const colloquy = $("#colloquy");
const folio = $("#folio");
const quill = $("#quill");
const sendBtn = $(".send");

function enterWriting() {
  if (document.body.classList.contains("is-writing")) return;
  document.body.classList.add("is-writing");
  workspace.hidden = false;
  requestAnimationFrame(() => workspace.classList.add("is-on"));
}

function blotAt(x, y) {
  const b = document.createElement("div");
  b.className = "blot";
  b.style.left = x - 6 + "px";
  b.style.top = y - 6 + "px";
  document.body.appendChild(b);
  b.addEventListener("animationend", () => b.remove(), { once: true });
}

function addMe(text) {
  const el = document.createElement("div");
  el.className = "utterance me";
  el.textContent = text;
  colloquy.appendChild(el);
  colloquy.scrollTop = colloquy.scrollHeight;
}

function addThinking() {
  const wrap = document.createElement("div");
  wrap.className = "utterance nx";
  wrap.innerHTML = `<div class="who">NX Folio</div><div class="thinking"><i></i><i></i><i></i></div>`;
  colloquy.appendChild(wrap);
  colloquy.scrollTop = colloquy.scrollHeight;
  return wrap;
}

async function streamReply(wrap, text) {
  const body = document.createElement("div");
  body.className = "body";
  wrap.querySelector(".thinking").replaceWith(body);
  for (const ch of text) {
    body.textContent += ch;
    colloquy.scrollTop = colloquy.scrollHeight;
    await wait(12 + Math.random() * 24);
  }
}

function countUp(el, to) {
  const t0 = performance.now();
  const dur = 900;
  const tick = (now) => {
    const p = Math.min(1, (now - t0) / dur);
    const e = 1 - Math.pow(1 - p, 3);
    el.textContent = String(Math.round(to * e));
    if (p < 1) requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
}

/* ---------- 稿笺场景 ---------- */

function sheetSession() {
  folio.innerHTML = `
    <article class="sheet">
      <h2>此刻</h2>
      <p class="headline">bracket_asm.prt</p>
      <p class="whisper">工作部件与显示部件是同一份稿。单位是毫米。下面这个数字，是场景里安静待着的实体。</p>
      <div class="stat"><b id="bodyN">0</b><span>个实体</span></div>
      <div class="meta">
        <span class="tag" style="--i:0">NX 2412</span>
        <span class="tag" style="--i:1">毫米</span>
        <span class="tag" style="--i:2">预览数据</span>
      </div>
    </article>`;
  countUp($("#bodyN"), 12);
}

function sheetSculpt() {
  folio.innerHTML = `
    <article class="sheet">
      <h2>塑造</h2>
      <p class="headline">一块尚未落地的体</p>
      <p class="whisper">拖动刻度，方块会跟着呼吸。接入 NX Open 后，这些数字会变成真正的 Block 特征。</p>
      <div class="stage-3d" id="stage">
        <div class="cube" id="cube">
          <i class="ft"></i><i class="bk"></i><i class="rt"></i><i class="lt"></i><i class="tp"></i><i class="bt"></i>
        </div>
      </div>
      <div class="dials">
        <label class="dial">长 <input type="range" min="40" max="160" value="88" data-axis="sx"> <em>88</em></label>
        <label class="dial">宽 <input type="range" min="28" max="120" value="56" data-axis="sy"> <em>56</em></label>
        <label class="dial">高 <input type="range" min="32" max="140" value="64" data-axis="sz"> <em>64</em></label>
      </div>
    </article>`;
  const cube = $("#cube");
  $$(".dial input").forEach((input) => {
    const em = input.parentElement.querySelector("em");
    const apply = () => {
      em.textContent = input.value;
      cube.style.setProperty("--" + input.dataset.axis, input.value + "px");
    };
    input.addEventListener("input", apply);
    apply();
  });
}

function sheetGaze() {
  folio.innerHTML = `
    <article class="sheet">
      <h2>注视</h2>
      <p class="headline">场景里还没有被点名的东西</p>
      <p class="whisper">雷达在空转。接入选择后，被点中的体、面、边会像信号一样落进来。</p>
      <div class="radar">
        <div class="sweep"></div>
        <span class="blip" style="left:62%;top:38%;--d:.2s"></span>
        <span class="blip" style="left:40%;top:58%;--d:.8s"></span>
        <span class="blip" style="left:70%;top:64%;--d:1.4s"></span>
      </div>
      <p class="empty-gaze">当前选择为空 · 这是预览</p>
    </article>`;
}

function sheetBlank(title, line) {
  folio.innerHTML = `
    <article class="sheet">
      <h2>稿笺</h2>
      <p class="headline">${title}</p>
      <p class="whisper">${line}</p>
    </article>`;
}

function interpret(text) {
  if (/夜|暗|深色|黑/.test(text)) return "night";
  if (/方|块|体|block|塑造|创建/.test(text)) return "sculpt";
  if (/选|注视|gaze/.test(text)) return "gaze";
  if (/会话|版本|零件|部件|此刻|session/.test(text)) return "session";
  return "echo";
}

const replies = {
  session: "我先读了当前会话。零件名、单位和实体数写在右边那张纸上——现在是预览，接上 NX 之后，这些字会变成真的。",
  sculpt: "好。右边是一块还停在想象里的长方体。拉动长宽高，它会跟着你的手改变呼吸。下一步，把它写进工作部件。",
  gaze: "我看了一眼选择集。此刻是空的。你在 NX 里点中物体之后，雷达上的光点就会变成名字。",
  echo: "这句话我记下了。Folio 还没有接上 NX Open，所以我只能把你的意图摊开在稿笺上，等桥接通的那一天执行。",
};

let busy = false;

async function ask(text, origin) {
  const t = text.trim();
  if (!t || busy) return;
  const kind = interpret(t);
  if (kind === "night") {
    wipeTheme(origin || sendBtn);
    return;
  }

  busy = true;
  sendBtn.classList.add("is-busy");
  enterWriting();
  addMe(t);
  const think = addThinking();
  await wait(520 + Math.random() * 380);
  await streamReply(think, replies[kind] || replies.echo);

  if (kind === "session") sheetSession();
  else if (kind === "sculpt") sheetSculpt();
  else if (kind === "gaze") sheetGaze();
  else sheetBlank("未命名的意图", t);

  sendBtn.classList.remove("is-busy");
  busy = false;
}

$("#dock").addEventListener("submit", (e) => {
  e.preventDefault();
  blotAt(mx, my);
  const v = quill.value;
  quill.value = "";
  quill.style.height = "auto";
  ask(v, sendBtn);
});

quill.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    $("#dock").requestSubmit();
  }
});

quill.addEventListener("input", () => {
  quill.style.height = "auto";
  quill.style.height = Math.min(quill.scrollHeight, 140) + "px";
});
