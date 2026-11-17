# DeskKit · Cursor 接入指南

> **状态：历史文档，不要按本文开工。** 不要把 `cursor-rules.mdc` 复制到 `.cursor/rules/`。现行架构见 [`docs/10-Current-Architecture.md`](../10-Current-Architecture.md)。

> 面向：把 `handoff/` 这套文档交给 Cursor 从零实现真实 Tauri v2 代码库。该路径已作废。
> 前置：本机已装 Node 18+、pnpm 9、Rust stable、Tauri v2 系统依赖；Windows 需 WebView2 Runtime，macOS 需 Xcode Command Line Tools。

---

## 0. 三步开始

1. 把整个 `deskkit/` 目录复制成你的新仓库根目录（或把 `handoff/`、`docs/`、`prototype/`、`tests/` 一起拷进去）。
2. 把 `handoff/cursor-rules.mdc` 复制到仓库 `.cursor/rules/deskkit.mdc`。
3. 在 Cursor 里新建会话，粘贴下面这段开场白，然后按第 3 章的阶段 Prompt 依次下发：

```
先阅读 .cursor/rules/deskkit.mdc、handoff/TECH-SPEC.md 第 0/2/3/4 章、
handoff/PROTOTYPE-SPEC.md 第 1/2/3/5 章。
本轮只做 T01（项目基础设施与契约先行），不要提前写 T02/T03 的业务代码。
每个文件输出完整可编译代码，禁止 TODO、伪代码、省略号。
完成后运行 handoff/TECH-SPEC.md 第 10 章 T01 的完成判据并逐条报告结果。
```

> 建议：**一个阶段一个会话**（或至少用 `/clear` 后重新贴上下文），避免 Cursor 在长上下文里丢失硬约束。每个阶段结束把「完成判据报告」贴进下一阶段开场。

---

## 1. 文件放置

```
<repo-root>/
├── .cursor/rules/deskkit.mdc      ← 复制 handoff/cursor-rules.mdc
├── handoff/                        ← 本套文档（作为规格，不参与构建）
├── docs/                           ← PRD / 架构 / QA 报告
├── prototype/index.html            ← 可运行 UI 参考，双击即可离线打开
└── tests/                          ← 原型回归脚本，作为验收思路参考
```

`handoff/` 与 `docs/` 是**规格文档**，不要 import、不要打进 bundle。真实代码从 `TECH-SPEC.md` 第 3 章的目录结构开始建。

> 文档里出现的 `src/`、`src-tauri/`、`shared/`、`release/`、`migration/`、`scripts/`、`docs/parity-checklist.md` 等路径，是 **T01–T05 需要新建的目标文件**，当前仓库还不存在，不是断链。现有目录只有 `handoff/`、`docs/`、`prototype/`、`tests/` 四组。

---

## 2. 规则文件说明

`cursor-rules.mdc` 已写好，包含 14 条硬约束、关键性能数值、禁止事项与自检命令，设置 `alwaysApply: true`。**不要删减**，尤其是权限拦截、平台分叉、Esc 回退栈三条 —— 原型阶段在这三处都真实踩过坑。

如果你的 Cursor 版本不支持 `.mdc`，把规则正文复制到 `.cursorrules`（仓库根目录）或项目的 AI 规则配置里。

---

## 3. 分阶段 Prompt 模板（可直接粘贴）

### T01 · 基础设施与契约先行

```
按 handoff/TECH-SPEC.md 第 10 章 T01 实现。
重点：第 4 章的 shared/contracts 全量类型定义必须逐字落地（Envelope / ApiResult /
ErrorCode / CMD / EVT / 全部数据模型），Rust 侧用 serde 做等价镜像；
release/version.json、CHANGELOG.md、feature-manifest.json 作为唯一事实来源；
.github/workflows/release.yml 先落 G0/G1/G2/G4。
禁止提前实现搜索、插件运行时等业务逻辑。
完成判据：pnpm i && pnpm tauri build 在配置层面可通过；cargo clippy 无 error；
Rust/TS 契约字段逐一对齐（写一条脚本校验并报告）。
```

### T02 · 平台抽象层与 Rust 核心服务

```
按 TECH-SPEC.md 第 7.1 章实现 PlatformAdapter 的 18 个方法与 windows.rs / macos.rs，
13 项白名单逐项落点；再实现 core/ 下 8 个服务。
硬约束：platform/ 之外不得出现任何 cfg(target_os)。
重点：全局热键注册 + 挂起 WebView 预热、拼音 trie 索引、算式解析、
剪贴板保留策略与排除名单、settings 原子落盘 + schemaVersion 迁移链。
完成判据：TECH-SPEC.md 第 10 章 T02 全部；本地检索 <30ms；空闲 RSS ≤80MB。
```

### T03 · 插件运行时

```
按 TECH-SPEC.md 第 6 章实现插件运行时，manifest 用 6.1 的示例作为 schema 测试用例。
必须实现 6.4 的 5 道防线：动态最小 capability → IPC ACL → PermissionBroker →
ScopeValidator → CapabilityExecutor 代执行。
禁止把权限判断做成 UI 弹窗里的 if；授权弹窗只写 GrantTable。
once 授权会话级不落盘；撤销后立即生效并作废在飞请求。
附一个同时声明 4 种形态的示例插件 plugins/sample-translate/。
完成判据：TECH-SPEC.md 第 10 章 T03 全链路；杀掉插件进程主浮层不崩。
```

### T04 · UI 层与内置工具

```
按 handoff/PROTOTYPE-SPEC.md 全量还原 6 套界面、7 个状态、键盘映射、视觉 token。
打开 prototype/index.html 对照视觉与交互，但不要复制它的模拟实现：
生产代码用 React + zustand，数据来自 Core，插件执行走真实 bridge。
重点：Esc 回退栈逐级回退（禁止一键隐藏）、A 四态、C 宫格与 5 组工具内页、
D 安装授权链、E 权限总览与撤销、F 引导与 6 类空态。
色值只从 src/theme/tokens.ts 取；尺寸严格用 PROTOTYPE-SPEC 第 5 章的表。
完成判据：TECH-SPEC.md 第 10 章 T04；全键盘走通 PRD 6.2 键盘表。
```

### T05 · 集成与双端对齐

```
按 TECH-SPEC.md 第 7.3 章补全 CI 闸门 G0–G7 与 tests/parity/。
写 scripts/parity-check 静态扫描：platform/ 外无 cfg(target_os) / process.platform；
功能开关无平台分叉；版本号一致；schema 一致。
用 Playwright 建双端截图基线，只允许白名单 #11 窗口材质有差异，
DOM 结构 / 归一化文案 / computed 尺寸三者必须一致。
完成判据：G0–G7 全绿；任一平台构建失败整批不发。
```

---

## 4. 验收命令

```bash
# 静态
cargo clippy -- -D warnings
cargo test
pnpm vitest run
pnpm tsc --noEmit

# 双端构建（CI 两个 job 都要跑）
pnpm tauri build --target x86_64-pc-windows-msvc
pnpm tauri build --target aarch64-apple-darwin   # 或 x86_64-apple-darwin

# 对齐闸门
node scripts/parity-check.mjs
pnpm playwright test tests/parity/

# 原型回归（作为交互口径参考，零第三方依赖）
node tests/prototype-smoke.js          # 85 条，file:// 真实浏览器
node tests/fix-regression.js           # 36 条，权限网关与缺陷定向
node tests/parity-supplement.js        # 19 个界面状态双端复验
```

> 三条原型脚本用系统 Chrome `--headless=new` + CDP 驱动 `file://`，无需 npm 包。它们验证的是**原型**，生产代码的验收要看 `cargo test` / `vitest` / Playwright 双端基线。

---

## 5. 高频踩坑清单（这些在原型阶段真实踩过）

| # | 坑 | 正确做法 |
|---|---|---|
| 1 | 把权限做成「UI 弹窗里判断 + 放行」 | 运行时 5 道防线；撤销后下一次 `bridge.call` 立即 `E_PERMISSION_DENIED`（原型 DEF-01 P1 缺陷） |
| 2 | `once` 授权在渲染期被提前消耗 | 渲染期用 `guardPeek()` 只读判定，真正执行能力时才消耗（原型修复项） |
| 3 | `Esc` 一键直接隐藏 | 用回退栈：内页 → 结果 → 触发词胶囊 → 清空 → 隐藏 |
| 4 | 色值散落在业务 CSS | 只在 `tokens.ts` / `:root` 定义；分组第 4 色是 `#9B6CF6`，不是 `accent-hover` |
| 5 | 工具宫格尺寸 token 定义了却没引用 | `.tool-cell` 必须显式 `width/height = var(--cell-w/h)`，否则计算值是 auto |
| 6 | 卸载插件只删列表项 | 7 项清理：触发词 / 宫格位 / 热键 / capability / grants / WebView / 用户数据（原型 OBS-03） |
| 7 | 状态色类名 `toast-ok` 与 CSS `.toast.ok` 不匹配 | 用独立类 `ok` / `warn` / `err`（原型 DEF-02） |
| 8 | 前端用 `process.platform` 切文案 | 由 `PlatformAdapter.modifier_labels()` 注入；前端零平台判断 |
| 9 | `Alt+Space` 在 macOS 输入框插入 NBSP | 注册系统热键时做事件拦截；验收要求「任意应用输入框不产生字符副作用」 |
| 10 | `Alt+Space` 在 Windows 呼出窗口系统菜单 | 抢注 + 占用者识别 + 一键改键（备选 `Alt/⌥+R`） |
| 11 | 冷创建 WebView 导致首次呼出 >300ms | 预热挂起态 WebView；热键由 Rust Core 直接注册 |
| 12 | 插件慢把整个结果区拖住 | 本地结果 <30ms 先出；插件分组异步合流、独立 loading、超时丢弃该分组 |
| 13 | 每次输入产生结果错帧 | 先 `cancel` 旧 id 再发新 `request`；晚到响应按 id 失效丢弃 |
| 14 | 双端各写一套 UI 文案 | 白名单外任何差异视为缺陷；CI 用 DOM/文案/尺寸三重断言 |
| 15 | 在线翻译/市场断网就崩 | `E_OFFLINE` 降级；已装插件与 5 组内置工具完全离线可用 |

---

## 6. 文档冲突时的优先级

`TECH-SPEC.md` 第 2 章硬约束 > `TECH-SPEC.md` 其余章节 > `PROTOTYPE-SPEC.md` > `docs/02-Architecture.md` > `docs/01-PRD.md` > `prototype/index.html` 的实现细节。

- **产品语义有疑问** → 查 `docs/01-PRD.md`（尤其 4.2 需求池验收要点、6.6 双端白名单）。
- **架构细节有疑问** → 查 `docs/02-Architecture.md`（§3 插件运行时、§4 双端、§5 类图、§6 时序图）。
- **UI 长什么样 / 按什么键** → 查 `handoff/PROTOTYPE-SPEC.md`，并打开 `prototype/index.html` 目视对照。
- **验收口径** → 查 `docs/03-QA-Report.md`（140 项断言分组）与 `TECH-SPEC.md` 第 12 章。

---

## 7. 变更流程（改需求 / 改品牌名时）

当前 UI 用的是工程代号 **DeskKit**；命名方案（`docs/04-Naming.md`）推荐「一唤 / Hail」，但**尚未定名**。如果要落地改名：

1. 先确认正式名（中文主名 / 英文副名 / 是否保留 DeskKit 作仓库代号 / 是否出海）。
2. 全局替换品牌位，清单见 `docs/04-Naming.md` 第 7 章：页面标题、主启动器底栏、面板顶栏、工具面板标题、插件市场页头、设置「关于」页、引导 Step1、空态文案、文档页眉、favicon。
3. 检查与新名字是否和既有触发词 `fy/js/color/ts/clip/ocr` 及快捷键别名冲突。
4. 同步更新 `release/version.json`、`feature-manifest.json` 与双端商店名。
5. **重跑双端截图基线与全部验收**（改名会产生新的验收指纹），更新 `docs/03-QA-Report.md` 基线表。

任何涉及 UI、快捷键、权限语义、平台差异的变更，都必须同步更新本套 `handoff/` 文档，否则 Cursor 后续会话会按过期规格继续写代码。
