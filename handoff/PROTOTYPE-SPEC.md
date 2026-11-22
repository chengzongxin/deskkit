# DeskKit 原型 UI 与交互规格 · Cursor 还原 UI 用

> 版本 v1.0 ｜ 生成 2026-11-13
> 关联可运行参考：`prototype/index.html`（单文件 208,228 bytes / md5 `1fc47e74727696c2fa21a9c7a104d293`，CSS+JS 全内联，`file://` 双击离线可开，QA 140 项断言全通过）。
> 本文描述的是已验收原型的交互，不是生产技术栈。生产代码按 [`docs/10-Current-Architecture.md`](../docs/10-Current-Architecture.md) 用 C++/ImGui 和 AppKit 重写，不要复制原型里的模拟数据。
>
> **可以照做的章**：第 2 章状态机（把 `TRIGGER` 改称 `TOOL`）、第 3 章键盘、第 5 章视觉 token、第 7 章内置工具算法、第 9.1 / 9.4 / 9.5 节、第 10 章里原型能看见的四类差异。
>
> **不要实现的章**：第 1.5 节插件市场、第 1.6 节插件 Tab、第 8 章权限网关、第 9.2 / 9.3 节安装与卸载、第 11 章插件超时和断网市场、第 12 章里的市场与授权动作、第 13 章的 React / Tauri 路径、第 14 章的插件运行时。触发词删掉 `ocr`。

---

## 0. 怎么用这份文档

| 你要做什么 | 读哪一章 |
|---|---|
| 建 6 个页面骨架、定尺寸 | 第 1 章 + 第 5 章 |
| 写状态机 / Esc 回退 | 第 2 章 |
| 接键盘事件 | 第 3 章（含 `preventDefault` 清单） |
| 做搜索、触发词、结果分组 | 第 4 章 + 第 6 章 |
| 写 CSS / 主题 | 第 5 章（**唯一色值来源**） |
| 实现 5 组内置工具 | 第 7 章 |
| 实现权限相关 UI | 第 8 章 |
| 写交互流程与弹层 | 第 9 章 + 第 12 章 |
| 做双端适配 | 第 10 章 |
| 补空态 / 演示态 | 第 11 章 |
| 把原型函数映射到生产模块 | 第 13 章 |
| 明确哪些必须真做 | 第 14 章 |

原型整体代码分区顺序：CSS 变量区 → 布局/组件样式 → 常量数据 → 状态机 → 单一 store → 各视图渲染函数 → 业务动作 → `handleAct` 事件分发 → `init()`。生产代码按 `TECH-SPEC.md` 第 3 章目录重组，但保持这个职责分层。

---

## 1. 六套界面（A–F）

### 1.1 尺寸与分区总表

| 界面 | 尺寸 | 分区 | 状态 |
|---|---|---|---|
| **A 主启动器**（全局搜索浮层） | 宽 720；默认态 ~360，结果态 `64 + 8×48 + 32`，上限 480；空态 ~260；水平居中、距顶 22% | ① 输入行 64 ② 结果区（滚动） ③ 底栏 32 ④ 右上工具区 | A1 默认 / A2 输入 / A3 结果 / A4 空结果 / TRIGGER 触发词独占 / TOOLPAGE 工具内页 / HIDDEN |
| **B 快捷启动面板** | 960 × 640 | 顶栏 48 + 左分组导航 200 + 分组卡片区 + 底栏 36；网格单元 80×80 | 普通 / 编辑（抖动 + ⊖ 角标 + 虚线「+」+ ✓完成）/ 拖拽中（半透明浮起 + 虚线占位 + 目标分组高亮） |
| **C 常用工具面板** | 宫格 420 × 360；内页 720 × 520 | 宫格 3×3，单元 112×96，图标 32，数字 1–9 角标；内页 = 顶栏 48 + 工作区 + 底部操作条 44 | grid / page |
| **D 插件市场 / 已装管理** | 1000 × 680 | 左导航 200（发现 / 已装 / 更新🔴 / 已固定 / 权限说明 / 开发者中心）+ 3 列插件卡片 | discover / installed / 5 类弹层 |
| **E 设置页** | 1000 × 680 | 4 Tab：常规 / 快捷键 / 插件（权限总览）/ 关于与双端信息 | 4 Tab + 录制改键态 |
| **F 首次启动引导** | 760 × 520 | 3 步：欢迎 → 设呼出键 → 平台权限与预置工具包 | step1/2/3 + 画廊 |

原型另有**顶部原型控制台**（产品名 / Win·mac 分段开关 / 呼出·隐藏 / A–F 导航 / 插件超时 / 断网 / ? 速查 / 模拟复制）与模拟桌面背景（浅色渐变 + 桌面图标 + 窗口轮廓）；mac 显示顶部菜单栏，Win 显示底部任务栏。控制台是演示脚手架，**生产代码不需要**。

### 1.2 A 主启动器四态（核心）

| 态 | 触发 | 内容 |
|---|---|---|
| **A1 默认态** | 呼出 / 清空输入 | 占位「搜索应用、文件、工具，或直接输入算式…」；最近使用 6 条（图标 + 名称 + 类型角标）；推荐工具 3 Chip；若剪贴板有新内容，顶部插入**剪贴板匹配卡**「刚复制了文本 → 翻译 / 剪贴板 / 二维码」 |
| **A2 输入态** | 开始输入 | 匹配片段用强调色高亮；输入框右侧出现清除 ✕；触发词输完（如 `fy `）渲染成**胶囊**并与正文分隔；结果实时刷新（无长等待）；首行默认选中但不执行 |
| **A3 结果态** | 有匹配 | 按 `应用 / 文件 / 网址 / 常用工具 / 插件 / 计算` 分组；每组 3–5 条；选中行左侧 3px 强调条 + 整行淡底；右侧 `⏎` 与数字 1–9 角标；匹配字符高亮；最多 8 行 |
| **A4 空结果态** | 无匹配 | 居中空插画 + 「没有找到与 "xxx" 相关的内容」+ 3 个出口：`在插件市场搜索` / `用浏览器搜索` / `清空重来` |

输入行右侧固定三个图标：`⌘K` 操作菜单 / 设置 / 关闭。底栏 32px 显示快捷键提示 + 品牌名。

A3 布局示意：

```
┌─ 输入行 64px ────────────────────────────────────────────────┐
│  🔍  [ fy ] hello world                      ⌘K   ⚙    ✕   │
└──────────────────────────────────────────────────────────────┘
┌─ 结果区（最多 8 行 × 48px） ──────────────────────────────────┐
│  常用工具 · 触发词 fy                              插件     │
│ ▌[译] 聚合翻译      hello world → 你好，世界        ⏎   1  │
│  [板] 剪贴板历史    最近复制：https://…            ⏎   2  │
│  [= ] 计算器        = 205                         ⏎   3  │
│  应用 / 文件 / 网址 …                                       │
└──────────────────────────────────────────────────────────────┘
┌─ 底栏 32px ──────────────────────────────────────────────────┐
│ ↑↓ 选择   ⏎ 执行   Tab 补全   ⌘K 操作   Esc 关闭    DeskKit │
└──────────────────────────────────────────────────────────────┘
```

### 1.3 B 快捷启动面板

左导航 5 个分组各带色标：办公 / 开发 / 设计 / 系统 / 常用网址。80×80 图标网格，图标 40。编辑态有抖动 + ⊖ 删除角标 + 虚线「+」卡片。HTML5 drag-and-drop 真实可拖：排序、跨分组、从系统拖入模拟、拖出窗口二次确认。右键菜单 8 项：打开、打开所在目录、复制路径、重命名、移到分组…、固定到工具面板、移除、属性。**删除分组时项目移入「未分组」，不丢失。**

### 1.4 C 工具宫格（9 格，来自 `TOOLS` 常量）

| # | id | 标题 | 拼音 / 首字母 | 关键词 |
|---|---|---|---|---|
| 1 | `clipboard` | 剪贴板 | jianqieben / jqb | 剪贴板, clip, history, 复制记录 |
| 2 | `picker` | 截图取色 | jietuquse / jtqs | 取色, 截图, color, picker |
| 3 | `translate` | 翻译 | fanyi / fy | 翻译, translate, fy |
| 4 | `calc` | 计算器 | jisuanqi / jsq | 计算器, 算式, js, calc |
| 5 | `timestamp` | 时间戳 | shijianchuo / sjc | 时间戳, ts, time, 日期 |
| 6 | `unit` | 单位换算 | danweihuansuan / dwhs | 单位, 换算, unit, km, cny |
| 7 | `color` | 颜色转换 | yansezhuanhuan / yszh | 颜色, color, hex, rgb, hsl |
| 8 | `qr` | 二维码 | erweima / ewm | 二维码, qr |
| 9 | `add` | 添加 | tianjia / tj | 添加, add |

为在 420×360 外框内放下 3×112×96，C 宫格 head/foot 收为 30/22、gap 4、padding 4（PRD 5.7 未规定 C 内部 head/foot，48/36 是 B 面板规格）；`.tool-cell` 必须显式引用 `--cell-w/--cell-h`，否则计算值是 auto。

### 1.5 D 插件市场

3 列卡片字段：图标、名称、简介、`source` 来源标识（官方认证 / 社区 / 本地开发）、安装量、评分、权限标签、版本、分类。原型 8 个种子插件：

| id | 名称 | 来源 | 权限 | 触发词 | 默认状态 |
|---|---|---|---|---|---|
| `translate` | 聚合翻译 | official | clipboard.read, net.fetch | fy | 已装·启用·固定·有更新 |
| `ocr` | OCR 识字 | official | screen.capture, clipboard.read | ocr | 已装·启用·有更新 |
| `date` | 日期助手 | community | — | — | 未装 |
| `qrtool` | 二维码生成 | community | clipboard.write | qr | 未装 |
| `rename` | 批量重命名 | local | fs.access | — | 已装·禁用 |
| `snippet` | 文本片段 | community | clipboard.write, hotkey.register | — | 未装 |
| `notify` | 定时提醒 | community | notification.show | — | 未装 |
| `capture` | 区域截图 | official | screen.capture, clipboard.write | — | 未装 |

5 类弹层：详情 / 授权弹窗 / 更新 / 卸载确认 / 冲突提示。授权弹窗三选项：**允许 / 拒绝 / 仅本次**，每项权限带用途说明。

### 1.6 E 设置页 4 Tab

| Tab | 内容 |
|---|---|
| 常规 | 开机自启、呼出键、外观（主题/密度）、数据隐私（剪贴板保留时长 / 暂停 / 排除应用）、失焦隐藏开关、多屏行为 |
| 快捷键 | 键位列表 + **录制式改键 + 冲突检测提示**（提示文案两端必须完全一致） |
| 插件 | **权限总览**：哪个插件在用哪类权限、可撤销 / 可恢复，撤销后立即生效 |
| 关于与双端信息 | 同版本号 + 双端功能对齐状态 ✅ + **按平台变化的系统权限清单**（Win 2 项 / mac 4 项） |

### 1.7 F 引导与空态

3 步：欢迎 → 设置呼出键（录制 + 冲突建议键）→ 平台权限与预置工具包。可跳过、可重开。
6 类空态：面板无项 / 剪贴板空 / 无结果 / 无插件 / 无更新 / 断网。每类都要有文案 + 主行动按钮。

---

## 2. 状态机与 Esc 回退栈

### 2.1 状态与合法迁移（照抄原型的 `TRANSITIONS`）

```ts
const TRANSITIONS = {
  HIDDEN:   ['A1', 'TOOLPAGE'],
  A1:       ['A2', 'A3', 'TRIGGER', 'HIDDEN', 'TOOLPAGE'],
  A2:       ['A3', 'A4', 'A1', 'TRIGGER', 'HIDDEN', 'TOOLPAGE'],
  A3:       ['A2', 'A4', 'A1', 'TRIGGER', 'TOOLPAGE', 'HIDDEN'],
  A4:       ['A2', 'A3', 'A1', 'HIDDEN'],
  TRIGGER:  ['A2', 'A3', 'TOOLPAGE', 'HIDDEN', 'A1'],
  TOOLPAGE: ['A3', 'TRIGGER', 'HIDDEN', 'A1', 'A2'],
} as const;
const STATE_DEPTH = { HIDDEN: 0, A1: 1, A2: 2, A4: 3, A3: 3, TRIGGER: 4, TOOLPAGE: 5 };
```

`HIDDEN → TOOLPAGE` 是合法路径（从 C 宫格直接打开工具内页），不要记为非法迁移。`setState(next)` 把不在 `TRANSITIONS` 中的迁移标记 `forced` 写入 history 便于审查，但正常键盘流 `forced` 应为 0。

### 2.2 Esc 回退栈算法（INV-09）

```
escBack():
  stack = store.machine.escStack
  if stack.length > 0:  setState(stack.pop())     // 逐级回退
  else:                 setState('HIDDEN')        // 栈空才隐藏
```

必须验证的两条追踪：

- `TRIGGER → A4 → A1 → HIDDEN`
- `TOOLPAGE → A1 → HIDDEN`

**禁止一键直接隐藏**。进入更深状态时把返回态压栈；渲染期重渲染不得改变栈。

---

## 3. 键盘映射表（全量）

| 按键 | 行为 | `preventDefault` |
|---|---|---|
| `Alt+Space`（Win）/ `⌥+Space`（mac） | 呼出并聚焦输入框，带入选区上下文；再次按下 Toggle 隐藏 | ✅ 必须（否则 mac 插入 NBSP、Win 呼出系统菜单） |
| `Ctrl/⌘ + Shift + Space` | **备用演示呼出键**（浏览器/系统抢占默认键时） | ✅ |
| 输入任意字符 | 实时模糊匹配（中文 / 全拼 / 首字母 / 英文 / 算式），<100ms 刷新，首行选中但不执行 | — |
| `↑` / `↓` 或 `Ctrl/⌘ + N/P` | 上下移动选中，到顶/到底停住，跨分组连续 | ✅ |
| `Enter` | 执行选中项（无选中执行第一条）；执行后默认隐藏；计算/翻译类 = 复制结果并给 Toast | ✅ |
| `Shift+Enter` | 副操作（打开所在目录 / 复制路径 / 以管理员运行，随结果类型变化） | ✅ |
| `Tab` | ① 补全触发词 ② 把选中标题补进输入框 ③ A ↔ C 工具面板切换（按上下文优先级） | ✅ 必须（否则焦点跳走） |
| 数字 `1`–`9` | 直达对应序号结果 / 宫格工具 | 视上下文 |
| `Ctrl/⌘ + K` | 选中结果的操作菜单（打开 / 所在目录 / 复制路径 / 固定 / 绑快捷键 / 卸载…） | ✅ 必须（浏览器地址栏搜索） |
| `Ctrl/⌘ + ,` | 打开设置页 | ✅ |
| `Ctrl/⌘ + T` | 打开常用工具面板 | ✅ 必须（浏览器新标签） |
| `Ctrl/⌘ + Shift + V` | 直达剪贴板历史 | ✅ 必须（浏览器纯文本粘贴） |
| `F1` / `?` | 快捷键速查浮层 | ✅（`?` 不抢占输入框，避免无法输入含问号文本） |
| `Esc` | 逐级回退（见 2.2） | ✅ |
| 鼠标悬停 | 高亮但**不抢焦点、不改变选中项** | — |
| 鼠标单击 / 右键 | 执行 / 打开同 `⌘K` 操作菜单 | 右键需阻止默认菜单 |
| 拖拽结果项 | 拖到 B 面板 = 添加快捷项 | — |

**数字键冲突策略**（原型取舍，生产可沿用）：输入框为空时纯数字直达最近使用；有输入时保留数字以输入算式（如 `123*4`），可点结果行数字角标或用 `Alt/⌥ + 数字` 强制直达；C 工具宫格内纯数字直达。必须在快捷键速查里说明。

**双端文案映射**：所有快捷键展示按平台自动映射 `Ctrl ↔ ⌘`、`Alt ↔ ⌥`、`Shift ↔ ⇧`，由 `PlatformAdapter.modifier_labels()` 注入，前端不写平台分支。

---

## 4. 触发词与搜索

### 4.1 触发词表（来自 `TRIGGER_DEFS`）

| 词 | target | 标签 | 占位提示 |
|---|---|---|---|
| `fy` | translate | 翻译 | 输入待译文本，Enter 复制译文 |
| `js` | calc | 计算器 | 输入算式或 `100cny to usd` |
| `color` | color | 颜色转换 | 输入 `#3B6CF6` / RGB / HSL |
| `ts` | timestamp | 时间戳 | 输入时间戳或日期 |
| `clip` | clipboard | 剪贴板历史 | 输入关键词筛选剪贴板记录 |
| `ocr` | ocr | OCR 识字 | 拖入图片或截图识字 |

输入「触发词 + 空格」渲染为**胶囊**并与正文分隔，结果区切为对应插件独占视图；触发词可在设置页自定义并做冲突检测。

### 4.2 搜索匹配

- 中文 / 全拼 / 首字母 / 英文 / 算式；示例：`wx`→微信、`kzmb`→控制面板、`word`→Word、`fy`→翻译。
- 算式直接在结果区顶部生成「计算」分组（如 `123*4` → `492`、`520+999-1314` → `205`）。
- 结果分组固定顺序：`应用 / 文件 / 网址 / 常用工具 / 插件 / 计算`。
- 匹配字符高亮；输入即出结果，**无「搜索中」长等待**；插件结果异步合流、超时只丢该分组。
- 所有插入 `innerHTML` 的用户输入必须先 `esc()` 转义（`& < > " '`），原型已做，生产不要漏。

### 4.3 上下文匹配

`store.clipboard.hasNew` 为真时，A1 顶部出现剪贴板匹配卡，点击直达翻译 / 剪贴板 / 二维码；复制图片应推荐 OCR / 取色；选中文本 + 超级面板弹出匹配工具。生产由 `ContextMatcher.match()` 驱动。

---

## 5. 视觉 token（生产写入 `contracts/tokens.json`，不要写入 `tokens.ts`）

### 5.1 语义色值（PRD 5.8）

| Token | 值 | 用途 |
|---|---|---|
| `--bg-float` | `#FFFFFF` | 浮层底 |
| `--bg-canvas` | `#F7F8FA` | 窗口底 |
| `--bg-subtle` | `#F2F4F7` | 卡片底 |
| `--bg-hover` | `#EEF1F6` | 悬停 |
| `--bg-selected` / `--accent-soft` | `#E8EFFD` | 选中 / 强调淡底 |
| `--bg-input` | `#FFFFFF` | 输入底 |
| `--border-default` | `#E5E8ED` | 卡片 / 分割线 |
| `--border-strong` | `#D6DBE3` | 输入框描边 |
| `--text-primary` / `--shadow-ink` | `#1C2430` | 主文字 / 投影基色 |
| `--text-secondary` | `#5C6675` | 副标题 |
| `--text-tertiary` | `#98A1B0` | 占位 / 禁用 / 快捷键提示 |
| `--accent` | `#3B6CF6` | 选中条、匹配高亮、主按钮、焦点环 |
| `--accent-hover` | `#2F5BE0` | 主按钮悬停 |
| `--on-accent` | `#FFFFFF` | 反白文字 |
| `--success` | `#18A058` | 已授予 / 成功 |
| `--warning` | `#F0A020` | 冲突 / 离线 / 待更新 |
| `--danger` | `#E5484D` | 删除 / 拒绝 / 错误 |
| `--chip-1..3` | `var(--accent)` / `var(--success)` / `var(--warning)` | 分组色标 |
| `--chip-group-4` → `--chip-4` | `#9B6CF6` | 分组第 4 色（**独立 token，不复用 accent-hover**） |
| `--chip-5` | `var(--danger)` | 分组第 5 色 |

两层投影：`--shadow-float: 0 12px 40px rgba(28,36,48,.14), 0 2px 8px rgba(28,36,48,.06)`；`--shadow-soft: 0 2px 8px rgba(28,36,48,.06)`。
控制台专用（生产不需要）：`--console-bg / --console-fg / --console-line / --console-chip / --console-chip-on`；桌面背景 `--desk-1 / --desk-2`。

> **硬约束**：业务代码里不得散落 hex。生产色值只定义在 `contracts/tokens.json`。原型里的 `:root` 只是这套色值的参照。深色主题放到 V1，沿用同一语义名换值，不改布局与尺寸。

### 5.2 尺寸（PRD 5.7）

| Token | 值 | Token | 值 |
|---|---|---|---|
| `--w-launcher` | 720px | `--h-input` | 64px |
| `--h-result` | 48px（紧凑 40） | `--h-bottom` | 32px |
| `--h-max` | 480px | `--top-offset` | 22% |
| `--r-float` / `--r-card` / `--r-ctl` / `--r-pill` | 14 / 10 / 8 / 999px | 图标圆角 | 8px |
| `--w-nav` | 200px | `--w-tools` × `--h-tools` | 420 × 360px |
| `--cell-w` × `--cell-h` | 112 × 96px | 宫格图标 | 32px |
| `--w-inner` × `--h-inner` | 720 × 520px | 内页底栏 | 44px |
| `--w-panel` × `--h-panel` | 960 × 640px | `--w-market` × `--h-market` | 1000 × 680px |
| `--w-guide` × `--h-guide` | 760 × 520px | `--w-modal` / `--w-modal-wide` | 520 / 760px |
| `--h-topbar` / `--h-foot` | 48 / 36px | `--dur` | 120ms |

间距 4px 栅格：`4 / 8 / 12 / 16 / 24 / 32`；浮层内边距 16；结果行左右 12；靠边自动内收 16px；选中强调条左侧 3px。
字体层级：输入 `18/500`｜窗口标题 `16/600`｜结果主 `14/500`｜副 `12/400`｜分组标题 `12/600`｜角标 `11/500`｜宫格标签 `12/400`｜数值 `等宽 14/600`。
字体栈：`-apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif`；数值用 `ui-monospace, SFMono-Regular, Menlo, Consolas, "Courier New", monospace`。
动效：浮层出现 120ms、拖拽占位 100ms、结果刷新无动画。

### 5.3 Toast 状态色（易错）

CSS 选择器是 `.toast.ok / .warn / .err .toast-dot`，因此 className 必须是独立类 `toast ok` / `toast warn` / `toast err`，**不能**写成 `toast-ok`。三色实测：`ok=rgb(24,160,88)`、`warn=rgb(240,160,32)`、`err=rgb(229,72,77)`，默认 `#3B6CF6`。

---

## 6. 数据模型（原型 `store` 全量结构）

```ts
const store = {
  platform: 'win',                          // 仅用于白名单 4 类可见差异
  demo: { timeout: false, offline: false },
  view: 'A',                                // A|B|C|D|E|F
  modalOpen: false,
  grants: {},                               // 会话级「仅本次」授权，不落盘
  blurTimer: null, recording: null,
  menu: { open: false, items: [], index: 0, x: 0, y: 0, title: '' },
  machine: { state: 'A1', history: [], escStack: [] },
  launcher: { visible: true, raw: '', trigger: '', payload: '', query: '',
              groups: [], flat: [], selected: 0, focused: false, a2Timer: null, trSeq: 0 },
  clipboard: { hasNew: true, list: CLIPBOARD_SEED, paused: false, filter: '全部' },
  groups: GROUP_SEED,                       // 5 组，见 1.3
  renames: {},
  panel: { edit: false, dragging: null, activeNav: 'all', search: '' },
  tools: { mode: 'grid', activeTool: null, returnTo: 'C', workDirty: false,
           trSeq: 0, history: [], pickedHex: '#3B6CF6', pinList: [] },
  market: { tab: 'discover', cat: '全部', search: '', flow: null },
  settings: { tab: 'general', blurHide: true, autoLaunch: true,
              multiScreen: '跟随鼠标所在屏', theme: '浅色', density: '紧凑',
              cloudSync: false, localDev: false, autoUpdate: true,
              hotkeys: {}, calling: '全局快捷键', retention: '7 天' },
  guide: { step: 1, gallery: false, recordCombo: '', recordState: '', preset: {...} },
  plugins: PLUGIN_SEED,                     // 8 个，见 1.5
  triggers: TRIGGER_DEFS,                   // 6 个，见 4.1
};
```

其他常量：`COLOR_TOKENS`、`PERM_DEFS`（6 类权限 + label + reason）、`PLATFORM_PERMS`（Win 2 项 / mac 4 项系统权限清单）、`TEXT_MAPS`（修饰键文案映射）、`INDEX_ITEMS`（应用/文件/网址索引）、`TOOLS`、`TRIGGER_DEFS`、`RECENT_IDS`、`FAV_IDS`、`CLIPBOARD_SEED`、`GROUP_SEED`、`PLUGIN_SEED`、`DICT`（离线词典）、`UNIT_TABLE`、`ICONS`（内联 SVG）、`TRANSITIONS`、`STATE_DEPTH`、`CAPABILITY_PERMS`、`PERM_DENIED_TEXT`。

**`PLATFORM_PERMS` 内容**（引导 Step3 与「关于」页只列本平台所需）：

| Win | mac |
|---|---|
| 开机自启（已授予）、文件索引（已授予） | 辅助功能（已授予）、输入监控（已授予）、屏幕录制（未授予）、完全磁盘访问（已授予） |

---

## 7. 内置工具算法规格

| 工具 | 算法与验收 |
|---|---|
| **计算器 / 单位换算** | 递归下降解析四则（`expr → term → power → factor → num/ws`），支持括号、优先级、小数；非法表达式不崩溃、除零有防护；`123*4=492`、`(1+2)*3=9`、`520+999-1314=205`。单位表 `UNIT_TABLE` 分组 len/wt/data/money/temp，含 `km=1000m`、`mi=1609.344m`、`lb=0.45359237kg`、`kb=1024b`、`usd=7.12cny` 等；摄氏/华氏/开尔文单独换算。Enter 复制结果 |
| **颜色转换** | `#3B6CF6 → rgb(59,108,246)`；HEX ↔ RGB ↔ HSL 双向且往返一致，支持 3/6 位 HEX、`clamp255`、`pad2`；实时色块预览；非法输入不崩溃 |
| **时间戳** | 10 位（秒）/ 13 位（毫秒）自动识别 ↔ 日期双向；`tsToDate` / `dateToTs` / `fmtDate`；展示按系统时区、两端格式一致 |
| **翻译** | 内置离线词典 `DICT`（约 50 条，`hello world → 你好，世界`）+ 触发词 `fy` 联动 + 300–800ms 模拟延时；离线时明确提示「当前为离线基础翻译」且主流程不报错。生产接在线引擎但必须保留离线降级 |
| **剪贴板历史** | 记录文本/图片/文件，支持搜索、收藏、一键粘贴复用、保留时长（7/30/永久）、暂停记录、一键清空、排除指定应用 |
| **截图取色** | 取色态鼠标移动实时显示 HEX/RGB/HSL，Enter 复制 HEX；mac 未授予屏幕录制权限给**明确引导页**而非黑屏/空结果 |
| **二维码** | 文本/网址生成（原型为模拟），支持复制/保存（模拟） |

工具内页通用：顶栏返回、底部操作条（复制结果 / 固定到面板 / 历史）、`tool-back` 返回宫格。计算类结果用等宽字体。

---

## 8. 权限网关（原型实现 → 生产映射）

原型里的 `guard` 系列是对生产 `PermissionBroker` 的**行为模拟**，生产必须用 `TECH-SPEC.md` 6.4 的 5 道防线重写，不要照抄这段 JS。但语义必须一致：

| 原型函数 | 生产对应 | 语义 |
|---|---|---|
| `CAPABILITY_PERMS` | `PermissionDecl` + `ResultItem.capability` | 能力 → 所需权限：`translate→net.fetch`、`ocr/clipboard→clipboard.read`、`picker/screenshot→screen.capture`、`qr→clipboard.write` |
| `providersFor(permId)` | `PluginHost` 注册表 | 由已安装且启用的插件的权限声明推导提供者，**不硬编码插件名** |
| `guard(pluginId, permId)` | `PermissionBroker.check()` | 执行期判定，会消耗 `once` |
| `guardPeek(...)` | 渲染期只读查询 | **不消耗 `once`**，避免重渲染提前用掉一次性授权 |
| `guardCapability(cap, pluginId)` | 能力级校验 | 任一提供者的对应权限可用即放行，保证「拒绝不影响插件其他功能」 |
| `denyFeedback(...)` / `applyPermDeniedView(...)` | UI 降级 | 拒绝 Toast：「权限已撤销 / 未授权：「网络访问」受限（聚合翻译），能力已拦截；需重新申请（设置 → 插件 → 权限总览 点「恢复」）」；渲染期只改文案不重复弹 Toast |
| `revokePerm` / `restorePerm` | `PermissionBroker.revoke/grant` | 撤销与恢复同步作废 / 重建 grants，立即生效 |
| `runDownload` 的 `once` 分支 | `GrantTable` | 按声明权限写会话级 grant，用过即删、不落盘 |

三条执行链必须**全部**接入网关：① `executeResult case 'plugin'`（搜索结果执行）② 触发词独占视图（`scheduleTriggerTranslate` / `triggerCopyValue` / `trig-clip` / `trig-copy` / Enter 分支）③ 工具内页（`updateTranslateImmediate` / `scheduleTranslate` / `copyToolResult` / `tool-clip-copy` / 取色 hover）。

> **已知取舍 OBS-06（P3，V1 处理）**：`executeResult` 按插件**全量声明权限**校验，单项撤销会连带拦截「通用唤起入口」，但主能力仍可用。生产建议给 `ResultItem` 加 `capability` 按能力校验（`TECH-SPEC.md` 4.1 已预留字段），或修订授权弹窗文案。MVP 不必为此改协议。

---

## 9. 关键交互流程

### 9.1 搜索 → 结果 → 执行

```
Alt/⌥+Space → 呼出并聚焦（<100ms）→ A1（最近使用 + 剪贴板匹配卡）
→ 输入 → A2/A3 实时分组结果（本地 <30ms 先出，插件异步合流）
→ ↑↓ 选择 → Enter 执行（启动类打开；工具类给结果；插件类走网关）
→ Toast 可见反馈 → 默认隐藏浮层，回到原应用
```

### 9.2 插件安装授权链

`详情 → 权限清单 → 安装 → 授权弹窗（允许 / 拒绝 / 仅本次）→ 下载校验进度 → 完成 → 询问固定到面板 / 设置触发词`。已装列表支持启用/禁用开关、更新（红点 + 日志 + 失败回滚）、卸载二次确认（是否保留本地数据）。禁用后不出现在搜索结果与上下文匹配，但保留配置。

### 9.3 卸载清理（对应生产 7 项）

原型 `doUninstall` 同步清理：`p.trigger`、`p.revoked`、`store.tools.pinList`、`store.settings.hotkeys` 中绑定该插件的键、`store.grants` 中该插件的一次性授权，以及 `store.triggers` 过滤。生产再加：删除 capability、回收 WebView、按用户选择处理 `plugins/<id>/data`。

### 9.4 拖拽、右键、失焦、多屏

- 拖拽：排序 / 跨分组 / 从系统拖入模拟 / 拖出窗口二次确认；拖拽中有半透明浮起 + 80×80 虚线占位 + 目标分组高亮。
- 右键：B 面板 8 项菜单、结果项同 `⌘K` 操作菜单；菜单结构两端一致。
- 失焦：点击浮层外 150ms 后隐藏，设置内可开关；浮层靠边自动内收 16px。
- 多屏：跟随鼠标所在屏 / 记住上次 / 固定主屏，三策略可切换。
- 关闭行为：两端均「常驻不退出」（Win 最小化到托盘 / mac 隐藏到菜单栏），退出需二次确认。

### 9.5 执行反馈

所有 `Enter` / 点击执行必须给可见 Toast（如「已启动 Word」「已复制结果」），不要静默。Toast 用独立状态类（见 5.3）。

---

## 10. 双端切换白名单（原型只切 4 类可见差异）

顶部控制台的 `Windows / macOS` 开关注入平台上下文后，**只允许以下变化**：

| # | 变化 | 白名单依据 |
|---|---|---|
| 1 | 修饰键文案 `Ctrl/Alt` ↔ `⌘/⌥`（全站自动映射） | 6.6 #1 |
| 2 | 默认呼出键显示 `Alt+Space` ↔ `⌥+Space`；备用 `Ctrl+Shift+Space` ↔ `⌘+⇧+Space` | 6.6 #2 |
| 3 | 引导 Step3 与设置「关于」页的系统权限清单（Win 2 项 / mac 4 项，只列本平台） | 6.6 #5 / #10 |
| 4 | mac 顶部菜单栏 ↔ Win 底部任务栏（含托盘/菜单栏图标，菜单结构两端一致，5 项） | 6.6 #4 |

**其余所有 UI、尺寸、结构、文案保持完全一致**。原型 QA 用 DOM 结构指纹 + 归一化文案 + computed 尺寸三重断言，在 19 个界面状态 × 两端下验证「白名单外差异 = 0」；`store.platform` 等值分支只允许出现在映射函数集合 `{defaultHotkey, backupHotkey, keycap, t, renderF(Step2), onRecordKey, renderConsoleState, renderResident}` 内，**UI 文案分支 = 0**。

> 白名单 #6–#13（自启实现、数据路径、索引来源、窗口材质、更新分发等）属真实运行时能力，静态原型只验证「无 UI 分叉」，生产由 `PlatformAdapter` 落地。

---

## 11. 演示态与空态

| 演示态 | 表现 | 验收点 |
|---|---|---|
| **插件超时** | 该插件结果显示「该插件暂时不可用」+ 重试 / 禁用 / 卸载三个出口 | 主浮层仍可输入、其余结果照样出（NFR-09 崩溃隔离） |
| **断网** | 市场降级为本地「已装插件」视图 + 明确离线提示 | 已装插件与 5 组内置工具仍完全可用（NFR-04） |

6 类空态：面板无项 / 剪贴板空 / 无结果 / 无插件 / 无更新 / 断网。每类必须有文案 + 主行动按钮；文案两端完全一致。

---

## 12. `data-act` 动作分发表（103 个字面值 / 109 个 `handleAct` case）

原型用单一 `handleAct` 分发所有点击。生产按视图拆成事件处理器，但**动作语义与覆盖数必须对齐**（QA 断言「所有 data-act 都有对应 case，零死按钮」）。

| 分组 | data-act |
|---|---|
| 控制台 / 全局 | `platform`, `toggle`, `view`, `demo`, `simulate-copy`, `cheat`, `resident-menu`, `clear`, `action-menu`, `hide`, `modal-backdrop`, `modal-close`, `popup-item`, `feedback`, `guide-restart`, `f-gallery` |
| A 启动器 | `recent`, `recent-num`, `result`, `result-num`, `empty-market`, `empty-browser`, `drop-trigger`, `clip-action`, `clip-dismiss`, `tool-open`, `demo-query`, `trig-clip`, `trig-open-tool`, `trig-copy`, `plugin-retry`, `plugin-disable`, `plugin-uninstall` |
| B 面板 | `b-add`, `b-edit`, `b-more`, `b-nav`, `b-newgroup`, `b-fold`, `b-group-menu`, `b-simulate-drop`, `b-cell`, `b-del`, `b-edit-done`, `b-remove-outside`, `rename-confirm`, `group-rename`, `group-create` |
| C 工具 | `tool-back`, `tool-pin`, `tool-pin-plugin`, `tool-action`, `tool-copy`, `tool-sample`, `tool-unit-sample`, `tool-ts-now`, `tool-ts-sample`, `tool-tr-clear`, `tool-tr-paste`, `tool-clip-tab`, `tool-clip-pause`, `tool-clip-clear`, `tool-clip-try`, `tool-clip-copy`, `tool-history`, `picker-permission`, `qr-save`, `qr-copy` |
| D 市场 | `plugin-detail`, `plugin-install`, `d-tab`, `d-cat`, `d-offline-installed`, `d-offline-retry`, `d-check-update`, `batch-update`, `plugin-toggle`, `plugin-update`, `plugin-more`, `auth-deny`, `auth-once`, `auth-allow`, `install-done`, `update-run`, `uninstall-run`, `dev-guide` |
| E 设置 | `cfg-toggle`, `e-tab`, `theme-chip`, `density-chip`, `data-manage`, `clip-pause`, `clip-clear`, `record`, `hotkey-reset`, `perm-`（动态前缀）, `trigger-edit`, `about-perm` |
| F 引导 | `f-back`, `f-record`, `f-default`, `f-preset`, `f-step` |

> `perm-` 是动态前缀（如 `perm-net.fetch`），实现时用前缀匹配而不是穷举。

---

## 13. 原型 → 生产模块迁移对照

| 原型函数 / 常量 | 生产落点 |
|---|---|
| `store` / `setState` / `rebuildEscStack` / `escBack` | `src/state/launcher-store.ts`（zustand） |
| `TRANSITIONS` / `STATE_DEPTH` | `src/state/launcher-store.ts` |
| `renderA` / `renderA1` / `renderResults` / `renderA4` / `renderTriggerView` / `renderAFoot` | `src/views/launcher/*` |
| `renderB` + `b*Menu` + `onDragStart/Over/Drop/End` | `src/views/panel/*` |
| `renderC` / `renderToolWork` / `openToolPage` / `updateToolOutput` | `src/views/tools/*` |
| `calcParse` / `evalExpression` / `convertUnit` / `hexToRgb` / `rgbToHsl` / `tsToDate` / `dateToTs` / `translateText` | `src-tauri/src/core/tools/*`（**下沉 Rust 侧**）或 `src/tools/*` |
| `renderD` / `openPluginDetail` / `startInstall` / `openAuthModal` / `runDownload` / `doUninstall` | `src/views/market/*` + `plugin_runtime/lifecycle.rs` |
| `renderE` / `revokePerm` / `restorePerm` / `onRecordKey` | `src/views/settings/*` + `PermissionBroker` |
| `renderF` / `guideStep` / `applyPlatformPerm` | `src/views/onboarding/*` + `PlatformAdapter` |
| `guard*` / `CAPABILITY_PERMS` / `providersFor` | `plugin_runtime/broker.rs` / `scope.rs` / `executor.rs` |
| `handleAct` | 按视图拆分的事件处理器 |
| `defaultHotkey` / `backupHotkey` / `keycap` / `t` / `TEXT_MAPS` / `PLATFORM_PERMS` | `PlatformAdapter.modifier_labels()` / `required_permissions()`（**数据注入，不是 UI 分支**） |
| `INDEX_ITEMS` / `RECENT_IDS` / `FAV_IDS` / `CLIPBOARD_SEED` / `GROUP_SEED` / `PLUGIN_SEED` | 真实索引服务与持久化 JSON（带 `schemaVersion`） |
| `toast` / `openModal` / `showPopup` / `renderPopup` | 通用组件 `src/components/*` |
| `:root` 全部 token | `src/theme/tokens.ts` + `src/styles/*` |

---

## 14. 原型不实现 / 生产必须真实实现

原型是**零网络、零真实系统能力**的模拟，以下必须换成真实实现（这是 T02–T05 的主要工作量）：

| 项 | 原型 | 生产 |
|---|---|---|
| 应用/文件/网址启动 | Toast 模拟 | `plugin-opener` / 系统 API，真实打开；副操作打开所在目录 / 复制路径 |
| 应用索引 | 内置常量 `INDEX_ITEMS` | `app_index_sources()` + 倒排 + 拼音 trie + 增量监听 |
| 全局热键 | 浏览器 keydown 模拟 | Rust Core 注册 + 冲突检测 + mac NBSP 拦截 |
| 常驻入口 | 模拟菜单栏/任务栏 | 真托盘（Win）/ 菜单栏 NSPopover（mac） |
| 剪贴板 | 内置种子列表 | 真实监听 + 保留策略 + 排除应用 + 原子持久化 |
| 截图取色 | 模拟取色 + 权限提示 | 真实屏幕捕获；mac 屏幕录制权限引导 |
| 翻译 | 离线小词典 | 在线引擎 + 离线降级（`E_OFFLINE`） |
| 插件执行 / 安装 / 更新 / 卸载 | 模拟数据 + 300–800ms 延时 | 真实 WebView 沙箱、manifest 校验、验签、权限网关、生命周期 |
| 权限网关 | 内存 `store.grants` + `p.revoked` | 5 道防线 + `GrantTable` + 撤销广播 |
| 二维码 / OCR / 批量重命名 | 模拟 | P1 真实插件 |
| 数据持久化 | 刷新即丢 | `schemaVersion` + migration + 原子替换 |
| 窗口 / 多屏 / 失焦 | 简化模拟 | 真实窗口管理，靠边内收 16px，三策略 |
| 平台权限 | 常量清单 | 真 TCC 状态查询 + 跳转授权 |
| 深色主题 | 按钮提示 V1 | V1 同一语义 token 换值 |

特别说明：原型「仅本次授权」`store.grants` 为内存态、刷新即丢，属于演示取舍；生产按会话级 `GrantTable` 实现（不落盘、用过即失效）。

---

## 15. 验收脚本

```bash
node tests/prototype-smoke.js        # 85 条：真实 Chrome headless + CDP 驱动 file://
node tests/fix-regression.js         # 36 条：权限网关 / Toast / OBS 落地定向回归
node tests/parity-supplement.js      # 19 个界面状态双端 DOM + 文案复验
```

历史最终结论：**140 项断言全通过，白名单外差异 0，零外部请求、零未捕获异常、`:root` 外零硬编码色值、103 个 `data-act` 全覆盖**。详见 `docs/03-QA-Report.md`。

> 这些脚本验证的是**原型**。生产代码的验收要用 `cargo test` / `vitest` / Playwright 双端截图基线（`TECH-SPEC.md` 第 12 章）。
