# PROJECT_STATE

后续 Agent 记忆。细节以代码为准。

## 当前目标

NX 2506 WebUI：命令搜索 + 空格环形菜单 + 初始化项目。不拖垮 NX 启动。

## 加载

独立部署器（不依赖 Agent Manager）：`F:\NxWebUIDeployer`（WebUI + WebView2）。AgentManager `/workbench/plugins` 仍可部署同一 LocalAppData 目录。

AgentManager `/workbench/plugins` 一键部署后：

- `UGII_CUSTOM_DIRECTORY_FILE`（HKCU）→ `%LOCALAPPDATA%\NxWebUITool\custom_dirs.dat`
- 插件目录 → `%LOCALAPPDATA%\NxWebUITool\deploy`
- 官方 `E:\NX2506\UGII\menus\custom_dirs.dat` 也会尽量改成同一路径
- 合并时保留 QuickCAM、`F:\NXPL001`，去掉仓库 deploy 与 `F:\NxWebUITool\deploy`

源码 / 修复来源仍是仓库 `NxWebUITool/deploy`（打包进 extraResources `nxwebui`）。桌面 NX 不必 bat。

- **不要**同步 DLL 到 `F:\NxWebUITool`；**不要**编进 `D:\DC2026`
- men/btn/rtb 必须 **GBK**（`write-gbk.ps1`）
- 改 webui / DLL 后必须完全退出 NX；已一键部署则到 AgentManager 点「修复 / 更新」

## 架构

```
startup/     NxWebUI.men/.btn  NxRadialStartup.dll（Space 观察钩子，无 WebView）
application/ NxCommandSearch.dll / NxRadialMenu.dll / NxRadialSlots.dll / NxProjectInit.dll
             NxCommandSearch.UI.dll（WinForms + WebView2）
             webui/  webui/radial/  webui/slots/  profiles/All/NxWebUI.rtb
```

入口 DLL 不得引用 WinForms/WebView2；各按钮各一个 `Main`。命令用 `UF_MB_ask_button_id` + `MB_main_activate_button_from_event_loop`，禁止宏回放。

## 关键行为（已定案）

**空格环形菜单**：按住 ≥110ms 弹出，滑选，松开执行。命令进行中也可弹出（不把 DA2 参数栏的 caret 当输入）。仅修饰键 / IME 组字抑制 HUD；Win32 Edit 不再拦弹出。输入框内短按空格仍输入（钩子吞键+补发），长按弹菜单不污染输入。无 `ACCELERATOR Space`。非模态叠层；DA2 忙则新命令排队，选新命令时对 NX 模态发 WM_CLOSE 替换（不发 Escape）。启动空闲预热 WebView。空槽显示最近使用幽灵命令；圆心重复上次。

**槽位**：`%LOCALAPPDATA%\NxWebUITool\radial-slots.json`。空格父槽 **4–10**（`RadialSlots.MinCount/MaxCount`，默认 8；**数组长度即数量**）。每主槽最多 3 个子槽（旧 `sub` → `children[0]`）。NX 槽位窗滑块改数量（仅空格模式；原生模式隐藏）。风格 `radial-ui.json` classic|radialz，NX 槽位窗空格模式可改。槽位窗打开时空格不弹环形菜单；命令可拖到槽位。**NX 原生 Radial 仍固定 8**（`RadialSlots.Count` + `native-radial.json` + `profiles/<APP>/<APP>.dtx`）。图标从 `high_quality.2s.bma` 解。

**溢出 stash**：缩短父槽时被裁槽位按位置存 `%LOCALAPPDATA%\NxWebUITool\radial-slots-stash.json`，加长优先还原（契约 §2.5）。JS `applyCount` 维护；C# `ReadStash/SaveStashFromJson`（saveSlots payload 带 `stash` 才写）；AgentManager 侧 `readStash/writeStash` IPC。

**DPI**：宿主 DPI-unaware，用页面 `devicePixelRatio` 反推窗体尺寸。空格圆窗 **640 DIP**（页面设计仍 600），给外向子槽 hover 光晕留边。改 HTML/CSS/JS 必须 bump `?v=`（当前 radial **v=22**、slots **v=11**），否则 WebView2 HTTP 缓存跨重启仍跑旧页。`onShown` 数据不变不得重渲染（`applySlots` 指纹含 usage/icons）。

**初始化项目**：仅两个父组「前排」「后排」。`UF_MODL_create_set_of_feature` 的 hide_state 必须为 **1**（隐藏/嵌入成员）；传 0 会把子组显示成多余顶层父组。厚度 `厚度=5` mm。

**环形二次执行**：隐藏 WebView 前先回响应；DA2 忙则排队等到空闲再激活（不丢、不发 Escape）。表单再次 Visible 时重发 `shown` 清 JS busy。

**快捷键**：搜索 Alt+Q。

## 关键文件

| 路径 | 作用 |
|------|------|
| `SearchHost.cs` | 非模态环形叠层、预热、中断替换、延后执行 |
| `RadialForm.cs` | 光标处椭圆菜单 |
| `RadialStartupPlugin.cs` / `NxUiFocus.cs` | Space 吞键钩子 / 输入与 IME 检测 |
| `RadialUsage.cs` | 空格执行日志（幽灵槽 / 圆心重复上次） |
| `ProjectInit.cs` | 前排/后排特征组 + 厚度 |
| `RadialSlots.cs` / `NativeRadialProfiles.cs` | 槽位与原生 Radial DTX |
| `webui-slots/` | NX 槽位窗（父槽滑块 + Classic/RadialZ） |
| `webui-radial/` | 空格圆盘 |
| `CommandInvoker.cs` | 激活 NX 按钮 |
| `docs/agentmanager-radial-slots.md` | 槽位契约（AgentManager 副本 `docs/nxwebui-radial-slots.md`） |

## 已确认决策

- 只编 **E:\NX2506** net48
- custom_dirs 若覆盖官方列表，必须保留 QuickCAM 与 NXPL001
- 不要把 `F:\NXPL` 的 `SelectDirectionEdges` 并进本 DLL；选边在 NXPL001
- 空格父槽 4–10；原生 Radial 保持 8

## 已知问题

- 改 webui / DLL 后必须完全退出 NX
- Gateway 空会话部分命令无对话框
- 4–10 父槽滑选、三子槽、原生 Radial Profile、多屏 DPI 待 2506 实机确认
- PS 注册 AssemblyResolve 会 StackOverflow；NX 侧靠 EntryLoader resolver

## 下一步

- 完全退出 NX 2506，确认空格滑选松开执行、IME 空格不弹菜单、输入框可弹菜单、圆心重复上次、父槽滑块 4–10、初始化只出两个父组
- 已一键部署则先在 AgentManager 工作台「修复 / 更新」
- 原生 8 槽保存后重启，确认 Ctrl+Shift+MB1/2/3
- Alt+Q / Space 冲突再换键
