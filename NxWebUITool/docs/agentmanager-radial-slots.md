# AgentManager 对接 NxWebUITool：环形命令槽位

日期：2026-08-19
供：`F:\AgentManager`（Electron 主进程 / WorkBench）读写本机 NX 空格环形菜单的 4–10 个父槽
源：`NxWebUITool/`（本仓库内 Siemens NX 2506 插件子项目，非官方插件类型）

规范以 **文件契约** 为准。AgentManager **不要** Load NX DLL、不要注入进程、不要模拟按键去点 NX 菜单。读写 JSON 即可。

实现代码：`NxWebUITool/NxCommandSearch/RadialSlots.cs`。

---

## 1. 双方职责

| 项目 | 做什么 | 不做什么 |
|---|---|---|
| **NxWebUITool** | NX 内空格弹出圆盘、执行命令、槽位管理窗 | 不提供 HTTP / named pipe / NX Open 对外 API |
| **AgentManager** | 在桌面端展示/编辑 4–10 个父槽，写入同一 JSON | 不编译、不部署、不启动本插件 |

NX 正在运行时也可以改文件：空格菜单 **每次弹出都会重新读盘**。不必为了改槽位而退出 NX。不要和 NX 里的「环形命令槽位」窗口同时写。

---

## 2. 槽位文件

### 2.1 主路径（读写都用这个）

```
%LOCALAPPDATA%\NxWebUITool\radial-slots.json
```

本机展开一般为：

```
C:\Users\<用户>\AppData\Local\NxWebUITool\radial-slots.json
```

Node：

```js
path.join(process.env.LOCALAPPDATA, 'NxWebUITool', 'radial-slots.json')
```

目录不存在时先 `mkdir`（`{ recursive: true }`）。

### 2.2 旧路径（只读迁移）

若主路径不存在，可读一次后写回主路径：

```
<NxWebUITool 部署根>\deploy\application\radial-slots.json
```

当前仓库部署根：`NxWebUITool/deploy/application/`。
**AgentManager 只往主路径写**，不要写进 `deploy\`（会被编译冲掉，且 NX 进程可能占用）。

### 2.3 编码

UTF-8。BOM 可有可无（NX 用 net48 `System.Text.Json` 读）。建议无 BOM。

### 2.4 圆盘视觉风格（独立文件，不进槽位 JSON）

```
%LOCALAPPDATA%\NxWebUITool\radial-ui.json
```

```json
{ "style": "classic" }
```

`style` 只能是 `classic`（默认，原空格菜单外观）或 `radialz`（RadialZ：深灰底 + 橙主色 + 青辅色、同心环/辐条、hover 1.12 双层发光）。缺文件、坏 JSON、未知值一律按 `classic`。AgentManager WorkBench 径向面板与 NX 槽位管理窗（空格模式「风格」开关，`saveUi`）切换后立即原子写入；NX 空格菜单每次弹出 `loadSlots` 时带上当前 style。几何半径/死区仍用原值，避免 600px 圆窗裁切回归。**不要**把 style 写进 `radial-slots.json`（NX `SaveFromJson` 只会落槽位字段）。

### 2.5 溢出槽位 stash（独立文件，缩短时保命）

```
%LOCALAPPDATA%\NxWebUITool\radial-slots-stash.json
```

父槽数量缩短时，被裁掉的末尾槽位按 **绝对位置** 存进这份文件；之后加长时优先从 stash 还原，而不是补 `null`——避免「减少→增加」把之前保存的槽位清空。规则：

- 格式：裸数组，下标 = 主槽位置，最多 10 项，空位为 `null`；元素结构同 §3.1 槽对象。
- 缩短 N→M：位置 M…N-1 的当前内容写入 stash 对应下标（同位置以最新内容为准）。
- 加长 M→N：新位置先取 stash，取到即还原并把该 stash 项清回 `null`。
- 写入方：AgentManager WorkBench 面板（滑块变化即写）与 NX 槽位管理窗（`saveSlots` payload 带 `stash` 字段才写；不带则不动该文件）。
- 读入侧必须宽松：缺文件、坏 JSON、非法元素一律当空位处理，**不允许**因 stash 损坏影响槽位本身。

### 2.6 执行日志（空格圆盘专用，AgentManager 不读写）

```
%LOCALAPPDATA%\NxWebUITool\radial-usage.json
```

每次空格菜单**真正激活**命令时记一笔。空槽的幽灵命令与圆心「重复上次」只读这份文件，**不写回** `radial-slots.json`。AgentManager 不要创建、不要编辑、不要清空它。

空格 `loadSlots` 的运行时载荷是 `{ slots, icons, usage, style }`：`slots` 不再内嵌 `icon`（图标进去重的 `icons` 映射），`usage` 为最近使用列表。磁盘上的槽位文件格式不变。

---

## 3. JSON 契约

必须是 **长度 4–10** 的数组（父槽数量）。不是 `{ "slots": [...] }` 包一层。缺字段、长度不在 4–10 → NX **整份丢弃**，回退默认 8 槽。长度即父槽数：缩短丢掉末尾、加长补 `null`；被裁掉的内容按 §2.5 存入 stash 文件，加长时优先还原。

### 3.1 槽对象

| 字段 | 必需 | 说明 |
|---|---|---|
| `id` | **是** | NX 按钮名，与 `.btn` 里 `BUTTON` / `TOGGLE_BUTTON` 后的标识一致，例如 `UG_MODELING_EXTRUDED_FEATURE` |
| `type` | 否 | `BUTTON`（默认）/ `TOGGLE` / `APPLICATION`。不确定就写 `BUTTON` |
| `name` | 建议 | 中文显示名。可空，NX 会按 `id` 从命令表补 |
| `cat` | 建议 | 分类，如 `建模` / `草图` / `视图`。可空，NX 会补 |
| `bitmap` | 建议 | `.btn` 的 `BITMAP` 名，无扩展名，如 `extrude`。NX 用它从 `UGII\bitmaps\high_quality.2s.bma` 取图标 |
| `icon` | **禁止写入** | 运行时 PNG data URL。写入会撑爆文件；NX 保存时也会丢掉 |
| `children` | 否 | 该主槽的子命令数组，最多 3 个；每项结构同槽位但禁止继续嵌套 |

空槽用 JSON `null`，不要用 `{}`（没有 `id` 的对象会被当成空槽）。

### 3.2 方位（下标从 0 起，12 点起，顺时针均分）

| 下标 | 位置 | 默认 `id` | 默认 BITMAP |
|---|---|---|---|
| 0 | 上 | `UG_CREATE_SKETCH` | `sketch` |
| 1 | 右上 | `UG_MODELING_EXTRUDED_FEATURE` | `extrude` |
| 2 | 右 | `UG_MODELING_REVOLVED_FEATURE` | `revolution` |
| 3 | 右下 | `UG_MODELING_HOLE_FEATURE` | `hole` |
| 4 | 下 | `UG_MODELING_BLEND_FEATURE` | `blend` |
| 5 | 左下 | `UG_MODELING_SUBTRACT_FEATURE` | `booleansubtract` |
| 6 | 左 | `UG_MODELING_UNITE_FEATURE` | `booleanunite` |
| 7 | 左上 | `UG_VIEW_FIT` | `fit` |

执行时 NX 用 `UF_MB_ask_button_id(id)` + `MB_main_activate_button_from_event_loop`。`id` 必须是 **已注册的菜单按钮名**，不是中文名、不是 DLL 文件名。

### 3.3 合法示例

```json
[
  { "id": "UG_CREATE_SKETCH", "type": "BUTTON", "name": "草图", "cat": "草图", "bitmap": "sketch" },
  { "id": "UG_MODELING_EXTRUDED_FEATURE", "type": "BUTTON", "name": "拉伸", "cat": "建模", "bitmap": "extrude" },
  { "id": "UG_MODELING_REVOLVED_FEATURE", "type": "BUTTON", "name": "旋转", "cat": "建模", "bitmap": "revolution" },
  { "id": "UG_MODELING_HOLE_FEATURE", "type": "BUTTON", "name": "孔", "cat": "建模", "bitmap": "hole" },
  { "id": "UG_MODELING_BLEND_FEATURE", "type": "BUTTON", "name": "边倒圆", "cat": "建模", "bitmap": "blend" },
  { "id": "UG_MODELING_SUBTRACT_FEATURE", "type": "BUTTON", "name": "求差", "cat": "建模", "bitmap": "booleansubtract" },
  { "id": "UG_MODELING_UNITE_FEATURE", "type": "BUTTON", "name": "求和", "cat": "建模", "bitmap": "booleanunite" },
  { "id": "UG_VIEW_FIT", "type": "BUTTON", "name": "适合窗口", "cat": "视图", "bitmap": "fit" }
]
```

第 4 槽清空：把下标 4 写成 `null`，其余保持对象。

### 3.4 子槽（`children`，每个主槽最多三个，可选）

每个主槽可挂 1–3 个叶子命令。槽位自身仍是普通可执行命令；悬停主槽时，子槽按 RadialZ 的方式沿远离圆心方向扇出。

```json
[
  { "id": "UG_MODELING_EXTRUDED_FEATURE", "type": "BUTTON", "name": "拉伸", "cat": "建模", "bitmap": "extrude",
    "children": [
      { "id": "UG_MODELING_BLOCK_FEATURE", "type": "BUTTON", "name": "块", "cat": "建模", "bitmap": "block" },
      { "id": "UG_MODELING_HOLE_FEATURE", "type": "BUTTON", "name": "孔", "cat": "建模", "bitmap": "hole" },
      { "id": "UG_MODELING_BLEND_FEATURE", "type": "BUTTON", "name": "边倒圆", "cat": "建模", "bitmap": "blend" }
    ] },
  null, null, null, null, null, null, null
]
```

规则：

- `children` 必须是数组，长度 `1..3`；每项字段与槽位一致（`id/type/name/cat/bitmap`）。
- **禁止嵌套**：子项不得再带 `children` 或旧 `sub`。写入侧拒绝；NX 读入时剥掉。
- 子项无 `id` 时读入侧忽略；写入侧拒绝。没有子槽时不写 `children`。
- 兼容旧文件：若没有有效 `children`，读取端会把旧版单对象 `sub` 提升为 `children[0]`；再次保存时统一写新格式。
- 子命令可与主环或其他方位重复，不强制唯一（编辑器可给重复提示）。
- NX 交互：按住空格滑入主槽即高亮并展开子槽；子槽数量为 1/2/3 时分别使用 0°、±34°、±46°扇形展开；继续滑入某个子槽，松开执行该子命令。点击模式下主槽和子槽都可直接点击执行。
- 子槽按圆片中心距离命中，并保留主槽到子槽的移动走廊，避免滑向两侧子槽时误切到相邻主扇区。

---

## 4. TypeScript（建议放主进程）

```ts
export type NxRadialSlotType = 'BUTTON' | 'TOGGLE' | 'APPLICATION'

export interface NxRadialChild {
  id: string
  type?: NxRadialSlotType
  name?: string
  cat?: string
  bitmap?: string
}

export interface NxRadialSlot {
  id: string
  type?: NxRadialSlotType
  name?: string
  cat?: string
  bitmap?: string
  children?: NxRadialChild[] // 最多 3 个
}

export const NX_RADIAL_SLOT_MIN = 4
export const NX_RADIAL_SLOT_MAX = 10
export const NX_RADIAL_SLOT_COUNT = 8

export const NX_RADIAL_DEFAULT_IDS = [
  'UG_CREATE_SKETCH',
  'UG_MODELING_EXTRUDED_FEATURE',
  'UG_MODELING_REVOLVED_FEATURE',
  'UG_MODELING_HOLE_FEATURE',
  'UG_MODELING_BLEND_FEATURE',
  'UG_MODELING_SUBTRACT_FEATURE',
  'UG_MODELING_UNITE_FEATURE',
  'UG_VIEW_FIT',
] as const

export function nxRadialSlotsPath(): string {
  const root = process.env.LOCALAPPDATA
  if (!root) throw new Error('LOCALAPPDATA 未设置')
  return path.join(root, 'NxWebUITool', 'radial-slots.json')
}
```

读写要点：

1. 读：`JSON.parse` 后检查 `Array.isArray` 且 `length` 在 4–10。
2. 写前剥掉 `icon`，空槽写 `null`。
3. **原子写**：写入 `radial-slots.json.tmp`，再 `fs.rename` 覆盖目标（Windows 上 `rename` 到已存在文件可用 `fs.promises.copyFile` + `unlink`，或 `writeFile` 后同步）。避免 NX 读到半截 JSON。
4. 写入后不必通知 NX。下次空格弹出即生效。已打开的槽位管理窗要关掉再开才会看到 AgentManager 的写入。

---

## 5. 命令目录（给搜索 / 补全 `name`/`bitmap`）

槽位 `id` 来自 NX `.btn`。NxWebUITool 在本机扫过一次命令搜索后，会缓存：

```
%TEMP%\NxWebUITool\catalog-v2-<key>.json
```

`<key>` = UTF-8(`UGII_BASE_DIR`) 的 Base64，再 `+`→`-`、`/`→`_`、去掉 `=`。
NX 2506 默认 `UGII_BASE_DIR=E:\NX2506` 时文件名为：

```
%TEMP%\NxWebUITool\catalog-v2-RTpcTlgyNTA2.json
```

结构：

```ts
interface NxCommandCatalog {
  commands: Array<{
    id: string
    type: string      // BUTTON | TOGGLE | APPLICATION
    name: string      // 显示名（优先中文 TOOLBAR_LABEL）
    nameEn: string
    desc: string
    synonyms: string
    cat: string
    key: string       // 加速键
    bitmap: string    // 写入槽位的 BITMAP 名
    source: string    // 来源 .btn 文件名
  }>
  categories: string[]
}
```

建议：AgentManager 用 `id` 精确匹配；UI 搜索用 `name` / `nameEn` / `id` / `cat` / `desc` / `synonyms` / `key` / `source`。写入槽位时带上 `id`、`type`、`name`、`cat`、`bitmap`。

缓存不存在或损坏时，AgentManager 直接按 NxWebUITool `CommandCatalog` 的同一规则扫描 NX2506 的 `menus/startup/*.btn`，应用 `LOCALIZATION/simpl_chinese` 标签，再写回上述缓存；因此完整命令搜索不依赖用户先在 NX 按 Alt+Q。扫描只读命令定义目录，并跳过 NXBIN、UGOPEN、HELP、LOCALIZATION 等大型无关树。

**不要**把 `D:\DC2026`（NX 2606 / .NET 10）当本插件的命令源。本插件只针对 `E:\NX2506`。

---

## 6. 插件侧对照（只读，供排查）

| 项 | 值 |
|---|---|
| 仓库 | `NxWebUITool/`（本机 `F:\AgentManager\NxWebUITool`） |
| 加载 | `UGII_CUSTOM_DIRECTORY_FILE` → `F:\AgentManager\NxWebUITool\deploy\custom_dirs.dat` |
| 空格菜单入口 | `deploy\application\NxRadialMenu.dll` → `SearchHost.RunRadial` |
| 槽位窗入口 | `deploy\application\NxRadialSlots.dll` → `SearchHost.RunSlots` |
| NX 菜单 | WebUI → 环形命令 / 环形命令槽位 |
| 读槽实现 | 槽位窗 `LoadWithIcons()`；空格圆盘 `LoadForRadial()`（mtime 记忆化 + 去重 icons + usage） |
| 写槽实现 | `RadialSlots.SaveFromJson`：只认 `id/type/name/cat/bitmap/children`（兼容读旧 `sub`），始终写主路径 |

AgentManager **不要**引用这些 DLL。

---

## 7. 建议的 AgentManager 落地（未实现，按此做）

主进程（`electron/main`）：

- IPC 例：`workbench:nxRadial:read` / `workbench:nxRadial:write` / `workbench:nxRadial:catalog`
- `write` 校验 length=8、每个非空元素有非空 `id`
- preload 只暴露 `window.agentManager.workbench.nxRadial.*`

渲染层：

- 圆盘 8 槽方位与上表一致（12 点起顺时针），点槽再搜索命令写入
- 图标：AgentManager 可**只读解析** `UGII\bitmaps\high_quality.2s.bma` 用于显示（bma 格式见 `NxCommandSearch/NxIconCache.cs`），产物只进自身 userData 缓存；**槽位文件仍不落 `icon`**。没有 `bitmap` 或解析失败时用分类占位图标即可

---

## 8. 不要做的事

- 不要写 `icon` 字段
- 不要写 `radial-usage.json`（仅 NX 空格菜单维护）
- 不要让子项继续嵌套 `children` / `sub`（一层为止）
- 不要给一个主槽写超过 3 个 `children`
- 不要写成 `{ "slots": [ ... ] }`（NX 磁盘格式是裸数组；插件内部 WebView 消息才包 `slots`）
- 不要改 `id` 大小写以外的字符（匹配不区分大小写，但应保持官方拼写）
- 不要把本插件编进 / 加载进 Designcenter 2026（`D:\DC2026`）
- 不要用宏 `MENU` 回放去「点按钮」；槽位只存按钮名，执行由 NX 插件完成
- 不要在 NX 槽位窗打开时并行写入（后写覆盖）

---

## 9. 验证清单

1. 写主路径一份合法 8 槽 JSON（改一个 `id`，例如下标 1 改成 `UG_MODELING_BLOCK_FEATURE`，`bitmap`=`block`，`name`=`块`）。
2. NX 已开的话直接按空格：右上槽应变为「块」。未开则启动 NX 后再按空格。
3. 打开 WebUI → 环形命令槽位，应看到同一套槽。
4. 故意写 7 个元素或坏 JSON：NX 应回退默认 8 槽，AgentManager 应拒绝写入并报错。
5. 空槽：某一格 `null` 且没有可用的最近使用记录时，该格显示空槽，点它不执行。有执行日志时该格显示幽灵最近命令（不写回槽位文件）；圆心死区松开/点击重复上次命令。

---

## 10. 变更时

改 `RadialSlots.cs` 的路径、数组长度或字段名时，必须同步改本文件，并在 `NxWebUITool/PROJECT_STATE.md` 记一笔。AgentManager 侧文档副本：`docs/nxwebui-radial-slots.md`。
