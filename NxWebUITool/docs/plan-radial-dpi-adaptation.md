# 环形菜单 DPI / 分辨率全适配方案

状态：P1/P2/P3 已实施（2026-08-24）；待 NX 多屏实机确认
关联：`NxCommandSearch/RadialForm.cs`、`webui-radial/app.js`、`webui-radial/styles.css`

## 背景：已修的根因

宿主进程（ugraf.exe）是 **DPI-unaware**,WinForms 的 `DeviceDpi` 恒为 96；而 WebView2 按显示器真实缩放光栅化（`devicePixelRatio` = 实际缩放）。两者不一致导致：

- 150% 缩放下，按 `DeviceDpi` 算出的 600 物理像素窗体，WebView2 视口只有 400×400 CSS px
- 页面按 600 CSS px 设计，外环 sub 圆片（半径 236px）整圈落到视口与窗口椭圆区域外 → 不可见不可点

**当前修复**(`RadialForm.cs`)：`SyncScaleFromPageAsync()` 在 `EnsureCoreWebView2Async` 后和 `NavigationCompleted` 时通过 `ExecuteScriptAsync("window.devicePixelRatio")` 取真实 dpr，窗体物理尺寸 = 600 DIP × dpr，视口回到 600×600 CSS px。

## 当前覆盖情况

| 场景 | 状态 |
|---|---|
| 单显示器任意 Windows 缩放（100/125/150/200%） | ✅ 已适配 |
| 不同分辨率（1080p/2K/4K) | ✅ 菜单固定 600 DIP，与分辨率无关；`PlaceAtCursor` 有工作区边缘 clamp |
| 跨显示器不同 DPI(菜单跟随光标出现在另一块屏） | ✅ 每次 `PrepareShow` 重新读取页面 dpr；待 NX 多屏实机确认 |
| NX 运行期间修改 Windows 缩放 | ✅ 下次弹出菜单时重新同步 |
| C# 缩放同步失效时页面端兜底 | ✅ 页面按实际视口等比缩放，命中坐标同步反算 |
| 极小工作区（远程窗口、低分辨率） | ✅ 窗体限制在工作区内，页面自动缩小 |

## 方案

### P1 — 每次弹出菜单时重新同步 dpr（修跨屏/运行时改缩放）

`RadialForm.SyncScaleFromPageAsync()` 已是现成方法，只需在每次显示时调用：

- `PrepareShow()` 或 `Shown` 事件里 `_ = SyncScaleFromPageAsync()`（窗体已 init 时有效；一次 ExecuteScript 开销可忽略）
- dpr 变化时该方法内部已调 `PlaceAtCursor()` 重排窗体，无需额外处理
- 注意 `SyncScaleFromPageAsync` 是 async void 风险：调用点用 discard + 方法内 try/catch 已兜底，保持现状即可

验证：双屏不同缩放（如 100% + 150%），光标分别放两块屏上按空格，外环都应完整；或单屏改 Windows 缩放后**不重启 NX** 直接按空格验证。

### P2 — 页面端自适应兜底（防 C# 时序竞争/未知环境）

即使 C# 同步失效，页面也不应「外环消失」。`webui-radial/app.js`:

1. `boot`/`onShown` 时计算 `zoom = Math.min(1, innerWidth / 600, innerHeight / 600)`
2. `zoom < 1` 时对 `.stage` 或 `.wheel` 加 `transform: scale(zoom)`（中心缩放，用 `transform-origin: center`）
3. `hitFromPoint` 的半径常量（`DEAD`/`SUB_INNER`/`OUTER`）乘以 `zoom` 后再比较；`getBoundingClientRect()` 返回的是缩放后坐标，中心点计算不受影响
4. `polar()` 半径（`SLOT_R`/`SUB_R`）同样乘以 `zoom`

验证：临时把 `FormDip` 改小（如 400）构建一次，菜单应整体等比缩小而非裁掉外环；验证后改回。

### P3 — 小工作区缩小（低优先级）

`PlaceAtCursor` 里若目标屏幕 `WorkingArea.Height / dpr < 700 DIP`，把 `FormDip` 等比缩到 `WorkingArea` 可容纳的尺寸（页面侧由 P2 的 zoom 自动跟随）。

### 不做

- Windows 文本缩放、高对比度等无障碍场景：菜单是图形化 marking menu，不在范围内
- SlotsForm/SearchForm:普通可滚动窗口，不依赖固定几何假设，暂不动

## 实施顺序

P1(10 行内） → P2(30 行内） → P3（可选）。每步构建后按上面验证方法实测。
