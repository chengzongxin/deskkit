# DeskKit 技术实现规格 · Cursor 开发主文档

> **状态：历史文档，不要按本文实现。** 现行架构见 [`docs/10-Current-Architecture.md`](../10-Current-Architecture.md)。本文的 Tauri v2、React 和插件运行时已作废。

> 版本 v1.0 ｜ 生成 2026-11-13 ｜ 状态：**已作废，仅作审计**
> 适用：Windows + macOS 双端同期发布的桌面快捷启动与常用工具（Tauri v2）
> 本文件是**实现阶段的唯一事实来源**。产品语义回查 `docs/01-PRD.md`，架构推演回查 `docs/02-Architecture.md`，UI 与交互还原回查 `handoff/PROTOTYPE-SPEC.md`。三者与本文件冲突时，以本文件第 2 章「硬约束」为准。
> 关联原型：`prototype/index.html`（单文件、零构建、QA 140 项断言全通过），它是**可运行的规格**，不是可复制的生产代码。

---

## 0. 给 Cursor 的开场指令

把这段文字作为每次新会话的第一条上下文：

```
你正在实现 DeskKit —— 一个 Tauri v2 桌面启动器。先完整阅读 handoff/TECH-SPEC.md，
再按第 10 章的 T01→T05 顺序实现。规则：
1. 每个文件输出完整可编译代码，禁止 TODO / 伪代码 / "...省略" /
   "类似上文" / 只写函数签名不写函数体。
2. 严格遵守第 2 章硬约束；任何平台判断只能出现在 src-tauri/src/platform/。
3. 契约先行：T01 未把 shared/contracts 写完，不要开始 T02/T03。
4. 每完成一个任务，运行第 10 章对应「完成判据」自检，报告通过/失败，不要跳过。
5. 不确定时按本文件的默认值实现，并在回复里列出你的假设，不要自行发明架构。
```

阅读顺序：`CURSOR.md`（接入方式）→ 本文件第 2、3、4 章 → 按 T01–T05 逐步展开 → `PROTOTYPE-SPEC.md`（UI 还原）→ `docs/03-QA-Report.md`（验收口径）。

---

## 1. 项目定义与技术选型

### 1.1 一句话

按一个键（`Alt+Space` / `⌥+Space`）呼出 720px 浮层，搜索即可启动应用/文件/网址、调用内置工具或第三方插件，办完即走；Windows 与 macOS **同版本号、同发布日、功能 100% 对齐**。

### 1.2 选型结论（不要更改）

| 项 | 结论 |
|---|---|
| 宿主 | **Tauri v2**：Rust Core + 系统 WebView（Win WebView2 / mac WKWebView） |
| 前端 | React 18 + TypeScript + Vite；zustand 管状态；样式色值只取自 `src/theme/tokens.ts` |
| 否决 Electron | 空闲内存 **150–320MB**（2026-11 复核区间：更保守口径 150–168MB；11-06 记录的 250–320MB 属偏高口径；**方向不变，仍 >80MB**），`NFR-03 ≤80MB` 架构性不达标，无法靠设计压缩 |
| 否决原生混合 | ① 第三方无法用 HTML/JS 低成本写插件，违背「开放插件运行时」；② 两套原生 UI 让双端 100% 对齐成本最高 |
| 选 Tauri 的关键 | v2 原生 `capabilities → permissions → scopes` **default-deny** 可直接复用为插件权限体系；Core 与 WebView 进程可分离；官方插件矩阵齐备 |

**常驻/激活拆分（解决「Web 插件生态 ↔ ≤80MB」张力）**：全局热键由 Rust Core 直接注册（不经 WebView）→ 呼出 P95 <100ms；预热一个**挂起态** WebView 规避冷创建 200–500ms → 冷启首帧 <300ms；插件 WebView **用完即建、用完即毁、零常驻**。

内存预算（空闲，目标 ≤80MB）：

| 组成 | 预算 |
|---|---|
| Rust Core | 12MB |
| mmap 索引 | 8MB |
| 剪贴板索引 | 4MB |
| 挂起 UI | 0–16MB |
| 托盘 / 菜单栏 | 2MB |
| 预留 | 16MB |
| **合计常驻** | **≈42MB**（留 38MB 余量） |

装 100 个插件空闲内存仍 ≈42MB；激活时瞬时峰值 ≈138MB，不受 NFR-03 约束。

> **2026-11-13 复核补注（只补注，不推翻任何结论）**：
> ① 上表 **≈42MB 是设计预算、非实测**；**T02 必须实测空闲 RSS 验证（两端各测）**。Tauri 空闲口径在 **20–134MB** 波动，广传「96% 更小 / 省 5 倍内存」基准**缺方法论披露**（未给出 app 规格 / OS 版本 / 框架版本），**不作为实测依据**。
> ② Electron 包体积复核区间 **85–187MB**（11-06 记录的 120–187MB 属偏高口径）；**固定 WebView2 Runtime 分发会让 Tauri 安装包额外 +~180MB，发布体积需按目标平台单独评估**。
> ③ 两处修正只调整数字口径，**选型结论、否决理由与 NFR 对策均不变**。核实来源与日期见 `docs/05-Architecture-Review.md` §9。

---

## 2. 硬约束（INVARIANTS · 违反即返工）

| # | 约束 | 检查方式 | 违反后果 |
|---|---|---|---|
| INV-01 | **唯一平台分叉点**：`#[cfg(target_os)]` / `process.platform` 只允许出现在 `src-tauri/src/platform/`；前端零平台判断 | CI 静态扫描 G4；`grep -rn "process.platform" src/` 必须为 0 | 双端同期失效 |
| INV-02 | **13 项白名单之外不得有任何平台差异**（见 7.1） | 双端截图 diff + DOM/文案/尺寸三重断言 | 视为缺陷 |
| INV-03 | **权限是运行时强制拦截，不是 UI 弹窗**：5 道防线见 6.4；授权弹窗只写决策，不承担拦截 | 撤销后下一次 `bridge.call` 必须立即返回 `E_PERMISSION_DENIED` | 安全边界失效（原型曾在此处出现 P1 缺陷） |
| INV-04 | **default-deny**：插件 capability 仅含 `dk-plugin:allow-bridge-call`；插件直调任意 Tauri command 必须被 ACL 拒绝且不弹窗 | 集成测试断言 `Command not allowed` | 沙箱失效 |
| INV-05 | manifest 的 `lifecycle.autostart` / `backgroundAllowed` **宿主强制按 false 处理**（忽略插件声明值） | 安装期钳制单测 | 违反「不得静默自启/后台采集」 |
| INV-06 | 能力由 Core 代执行，**只返回结果数据**，永不返回句柄 / 路径对象 / fd / token | 代码审查 + 类型约束 | 越权 |
| INV-07 | `once` 授权**会话级、不落盘、用过即失效**；渲染期只读判定不得提前消耗 | 单测 `render×3` 后 once 仍在 | 权限语义错误 |
| INV-08 | **所有色值只在 `src/theme/tokens.ts`**；所有尺寸只取自 PRD 5.7 规范表 | `:root` / CSS 中不得出现散落 hex | 视觉漂移 |
| INV-09 | **Esc 逐级回退用回退栈实现**，禁止一键直接隐藏（内页→结果→触发词→清空→隐藏） | 状态机单测 | 交互回归 |
| INV-10 | 每个请求必带 `deadlineMs`；输入变化**先 cancel 再 request**；晚到响应按 id 失效丢弃 | 协议单测 | 结果错帧 / 插件拖垮主浮层 |
| INV-11 | 所有持久化 JSON 带 `schemaVersion` + 单向 migration 链；设置/分组变更**临时文件 + 原子替换** | 升级测试 | 升级丢数据 |
| INV-12 | **本地优先**：默认零外发；云同步默认关闭且不实现；仅市场/在线翻译/更新走网络且受 `E_OFFLINE` 降级 | 全仓扫描默认外发 | 隐私违规 |
| INV-13 | 单一 `release/version.json` + `CHANGELOG.md` + `feature-manifest.json`，CI 双 job **全绿才 publish**，任一失败整批不发 | 发布流水线 | 版本不同期 |
| INV-14 | 功能开关**不得按平台分叉**；`ctx.platform` 只允许用于修饰键文案映射 | CI 静态扫描 | 功能对齐失效 |

---

## 3. 目录结构与文件职责

```
deskkit/
├── package.json / pnpm-workspace.yaml / vite.config.ts / tsconfig.json
├── src/                              # 100% 共用 Web 前端
│   ├── app/router.tsx                # 六界面跳转；bootstrap.ts = 预热挂载 + 引导判定
│   ├── state/launcher-store.ts       # 单一数据源 + 状态机 + Esc 回退栈
│   ├── views/launcher|panel|tools|market|settings|onboarding/
│   ├── tools/                        # 5 组内置工具（source=builtin，走同一协议）
│   ├── theme/tokens.ts               # 语义色值唯一来源
│   └── plugin-bridge/                # 插件侧 SDK，只依赖 deskkit API，屏蔽 Tauri 细节
├── shared/                           # Rust / TS 类型镜像，T01 契约先行
│   ├── contracts/manifest.schema.json / protocol.ts / errors.ts / ipc-channels.ts
│   └── schema/settings|groups|triggers|clipboard|grants.schema.json
├── src-tauri/
│   ├── Cargo.toml / tauri.conf.json / build.rs
│   ├── capabilities/main.json / plugin-template.json
│   ├── src/main.rs / lib.rs          # Builder 注册、Core 事件循环、IPC 总闸
│   ├── src/core/                     # index / search / clipboard / settings / hotkey / tools / context / system_permission
│   ├── src/plugin_runtime/           # manifest / bridge / broker / scope / executor / host / forms / lifecycle / market
│   └── src/platform/                 # ★ 唯一平台分叉点：mod.rs(trait) + windows.rs + macos.rs
├── plugins/sample-translate/         # 示例插件，同时声明 4 种形态
├── release/version.json / CHANGELOG.md / feature-manifest.json
├── migration/                        # schemaVersion 单向迁移链
├── tests/parity/                     # 双端截图基线 / schema 一致性 / 功能清单断言
├── scripts/parity-check.*            # CI 静态扫描
└── .github/workflows/release.yml     # 双端 matrix 构建 + G0–G7 闸门
```

平台差异代码目标 **<3%**，且物理集中于 `platform/`。

---

## 4. 契约先行：`shared/contracts/`（T01 必须先完成）

> 以下类型定义可直接复制进 `shared/contracts/protocol.ts`。Rust 侧用 serde 做等价镜像，字段逐一对齐，CI 用 schema 一致性断言校验。

### 4.1 核心类型

```ts
// 统一响应包装：所有 Core command 与 bridge.call 共用
export interface ApiResult<T> {
  code: 0 | ErrorCode;
  data?: T;
  message?: string;
  requestId: string;
  tookMs: number;
}

// 统一信封：所有方向、所有插件形态共用一份
export interface Envelope {
  v: '1';
  id: string;                       // req_<uuid>
  ts: number;                       // Unix ms UTC
  type: 'request' | 'response' | 'event' | 'cancel' | 'ping' | 'error' | 'bridge.call';
  pluginId?: string;
  sessionId?: string;
  method?: 'onQuery' | 'onTrigger' | 'onOpenSlot' | 'onContext'
         | 'onHotkey' | 'bridge.call' | 'ready' | 'result.patch';
  params?: Record<string, unknown>;
  ctx: CallContext;
  ok?: boolean;
  data?: unknown;
  code?: ErrorCode;
  message?: string;
  tookMs?: number;
}

export interface CallContext {
  deadlineMs: number;
  locale: string;
  platform: 'windows' | 'macos';    // 仅用于修饰键文案映射，禁止功能分叉
  grantedScopes: string[];
  trigger?: string;
  contentType?: ContentType;
  payloadRef?: string;              // 大对象一律句柄，不内联二进制
  requestId: string;
}

export type ContentType = 'text/plain' | 'text/uri-list' | 'image/*' | 'file';
export type IndexKind = 'app' | 'file' | 'url' | 'tool' | 'plugin' | 'calc';
export type ResultGroup = '应用' | '文件' | '网址' | '常用工具' | '插件' | '计算';

export interface IndexItem {
  id: string;
  kind: IndexKind;
  title: string;
  subtitle: string;                 // 位置 / 类型 / 来源
  iconRef: string;
  sourcePath?: string;
  pinyin: string;                   // 全拼，Rust 侧生成
  initials: string;                 // 首字母
  keywords: string[];
  lastUsedAt: number;
  useCount: number;
  pinnedTo?: string;
}

export interface Group {
  id: string; name: string; colorToken: string; order: number; collapsed: boolean;
  items: LaunchItem[];
}
export interface LaunchItem { groupId: string; itemId: string; order: number; addedAt: number; sourceType: string; }

export interface ClipboardRecord {
  id: string; contentType: ContentType; payloadRef: string; preview: string;
  sourceApp: string; createdAt: number; favorite: boolean; retention: '7d' | '30d' | 'forever';
}

export interface PluginInstance {
  pluginId: string; version: string; state: PluginState; manifestRef: string;
  webviewLabel?: string; source: 'official' | 'community' | 'local' | 'builtin';
  integrity: string; installedAt: number; crashCount: number;
}

export type PluginState =
  | 'DISCOVERED' | 'VALIDATING' | 'INSTALLING' | 'INSTALLED' | 'AUTHORIZING'
  | 'ENABLED' | 'RUNNING' | 'SUSPENDED' | 'TIMEOUT' | 'CRASHED'
  | 'FAILED_SAFE' | 'DISABLED' | 'UPDATING' | 'ROLLBACK' | 'UNINSTALLING'
  | 'REMOVED' | 'REJECTED';

export interface PermissionDecl {
  id: PermissionId;
  reason: string;                   // 「谁 / 要什么 / 用来做什么」，空则拒绝安装
  required: boolean;
  scope: { hosts?: string[]; methods?: string[]; paths?: string[]; types?: ContentType[] };
}

export type PermissionId =
  | 'clipboard.read' | 'clipboard.write' | 'fs.access'
  | 'net.fetch' | 'notification.show' | 'screen.capture' | 'hotkey.register';

export type GrantDecision = 'allow' | 'deny' | 'once';
export interface PermissionGrant {
  pluginId: string; capabilityId: PermissionId; decision: GrantDecision;
  scope: PermissionDecl['scope']; grantedAt: number; sessionId?: string; // once 不落盘
}

export interface TriggerWord {
  word: string; targetType: 'tool' | 'plugin'; targetId: string;
  exclusive: boolean; ownerPluginId?: string; conflictWith: string[];
}

export interface SearchQuery {
  raw: string; trigger?: string; payload?: string; tokens: string[];
  contextType?: ContentType; deadlineMs: number;
}
export interface ResultItem {
  id: string; group: ResultGroup; title: string; subtitle: string; iconRef: string;
  actions: Array<'open' | 'reveal' | 'copyPath' | 'pin' | 'bindHotkey' | 'uninstall'>;
  sourceId: string; asyncLoading?: boolean;
  capability?: string;              // V1：按能力校验，解决 OBS-06
}
export interface SearchResult { items: ResultItem[]; tookMs: number; degradedPlugins: string[]; }

export interface Hotkey { key: string; alt: boolean; ctrl: boolean; shift: boolean; meta: boolean; }
export interface ModifierLabels { alt: string; ctrl: string; shift: string; meta: string; }

export interface Settings {
  schemaVersion: number;
  hotkeys: Record<string, Hotkey>;
  blurHide: boolean; autoLaunch: boolean; multiScreen: 'followMouse' | 'remember' | 'primary';
  theme: 'light' | 'dark' | 'system'; density: 'compact' | 'standard';
  cloudSync: false;                 // MVP 强制 false
  retention: '7d' | '30d' | 'forever'; clipboardPaused: boolean;
  excludedApps: string[];
}
```

### 4.2 错误码常量（`shared/contracts/errors.ts`，Rust 侧镜像）

```ts
export const ErrorCode = {
  E_PERMISSION_REQUIRED: 'E_PERMISSION_REQUIRED', // 未授权 → 弹窗，决策后重放
  E_PERMISSION_DENIED:   'E_PERMISSION_DENIED',   // 已拒绝/已撤销 → 静默降级
  E_SCOPE_VIOLATION:     'E_SCOPE_VIOLATION',     // 越界，记审计
  E_TIMEOUT:             'E_TIMEOUT',
  E_CANCELLED:           'E_CANCELLED',
  E_PLUGIN_CRASHED:      'E_PLUGIN_CRASHED',
  E_CAPABILITY_UNKNOWN:  'E_CAPABILITY_UNKNOWN',
  E_MANIFEST_INVALID:    'E_MANIFEST_INVALID',
  E_VERSION_INCOMPATIBLE:'E_VERSION_INCOMPATIBLE',
  E_RATE_LIMIT:          'E_RATE_LIMIT',
  E_OFFLINE:             'E_OFFLINE',
  E_USER_REJECTED:       'E_USER_REJECTED',
} as const;
export type ErrorCode = typeof ErrorCode[keyof typeof ErrorCode];
```

宿主级错误码另用 `DK-<域>-<三位号>`（如 `DK-IDX-001`）；**禁止各模块自造字符串**。

### 4.3 IPC 通道常量（`shared/contracts/ipc-channels.ts`）

```ts
// Command：dk_<domain>_<action>
export const CMD = {
  searchQuery:        'dk_search_query',
  indexRebuild:       'dk_index_rebuild',
  clipboardList:      'dk_clipboard_list',
  settingsLoad:       'dk_settings_load',
  settingsSave:       'dk_settings_save',
  hotkeyRegister:     'dk_hotkey_register',
  pluginBridgeCall:   'dk_plugin_bridge_call',
  pluginInstall:      'dk_plugin_install',
  pluginUninstall:    'dk_plugin_uninstall',
  permissionGrant:    'dk_permission_grant',
  permissionRevoke:   'dk_permission_revoke',
  screenCapture:      'dk_screen_capture',
} as const;
// Event：dk://event/<name>   插件桥：dk://plugin/<id>/bridge
export const EVT = {
  inputChanged:      'dk://event/input.changed',
  visibilityHidden:  'dk://event/visibility.hidden',
  permissionRevoked: 'dk://event/permission.revoked',
  themeChanged:      'dk://event/theme.changed',
  pluginResultPatch: 'dk://event/result.patch',
} as const;
```

通道名与类型集中定义、Rust/TS 双端镜像、编译期校验，**写错即报错**。

### 4.4 持久化 schema 要点

`settings / groups / triggers / clipboard / grants` 五份 JSON 都必须带 `schemaVersion`；`migration/` 单向迁移链；每次变更临时文件 + 原子替换；两端 schema 完全一致可整体迁移。时间统一 **ISO 8601 UTC** 存储，展示按系统时区且两端格式一致。

---

## 5. 核心服务接口

### 5.1 `PlatformAdapter`（`src-tauri/src/platform/mod.rs` · 唯一分叉点）

```rust
pub trait PlatformAdapter {
    fn modifier_labels() -> ModifierLabels;
    fn format_hotkey(h: &Hotkey) -> String;
    fn default_hotkey() -> Hotkey;
    fn detect_hotkey_conflict(h: &Hotkey) -> ConflictInfo;
    fn register_global_hotkey(h: &Hotkey) -> Result<(), ConflictInfo>;
    fn create_resident_entry(menu: ResidentMenu) -> Result<(), Error>;
    fn required_permissions() -> Vec<SystemPermission>;
    fn permission_status(p: SystemPermission) -> PermissionState;
    fn open_permission_settings(p: SystemPermission) -> Result<(), Error>;
    fn set_auto_launch(enabled: bool) -> Result<(), Error>;
    fn data_dir() -> PathBuf;
    fn app_index_sources() -> Vec<IndexSource>;
    fn hotkey_prerequisites() -> PrereqResult;
    fn capture_screen(req: CaptureRequest) -> Result<Image, Error>;
    fn screen_capture_status() -> PermissionState;
    fn apply_window_material(win: &Window, m: Material) -> Result<(), Error>;
    fn handle_close_request(win: &Window) -> CloseBehavior;
    fn updater_config() -> UpdaterConfig;
}
```

编译期由 `#[cfg(target_os)]` 选择 `windows.rs` / `macos.rs`。**上层一律不感知平台**。

### 5.2 其余服务方法（按架构 §5 类图）

| 服务 | 方法 |
|---|---|
| `IndexService` | `lookup(String) -> Vec<IndexItem>`、`rebuild(sources)`、`watch_incremental()` |
| `SearchService` | `search(SearchQuery) -> SearchResult`、`match_pinyin(String) -> Vec<IndexItem>`、`eval_expression(String) -> Option<String>` |
| `ClipboardService` | `record(ContentType)`、`list()`、`clear()`、`set_retention()`、`is_excluded(app)` |
| `SettingsRepository` | `load() -> Settings`、`save_atomic(Settings)`、`migrate(i32)` |
| `HotkeyService` | `register(Hotkey)`、`detect_conflict(Hotkey) -> ConflictInfo`、`warm_webview()` |
| `ContextMatcher` | `match(ClipboardRecord) -> Vec<ResultItem>` |
| `PluginHost` | `install(String) -> PluginInstance`、`uninstall(String)`、`invoke(pluginId, method, Envelope) -> Envelope`、`heartbeat()`、`recycle_idle()` |
| `PermissionBroker` | `check(pluginId, cap, args) -> Decision`、`grant(...)`、`revoke(...)`、`broadcast_revoked(...)` |
| `ScopeValidator` | `validate(scope, args) -> bool`（**deny 优先于 allow**） |
| `CapabilityExecutor` | `execute(cap, args) -> Object`（Core 内代执行） |
| `LifecycleManager` | `list()`、`verify_bundle()`、`update_rollback()`、`cleanup_uninstall()` |

### 5.3 前端 `LauncherStore`（zustand）

状态：`HIDDEN | A1 | A2 | A3 | A4 | TRIGGER | TOOLPAGE`（详见 `PROTOTYPE-SPEC.md` 第 2 章）。
必须暴露：`setState(next)`、`escBack()`（回退栈 pop，禁止一键隐藏）、`setQuery()`、`moveSelection()`、`executeResult()`、`switchPlatformContext()`（仅注入文案映射，不分支 UI）。

---

## 6. 插件运行时

### 6.1 Manifest 契约（合法示例，直接作为 `manifest.schema.json` 的测试用例）

```json
{
  "manifest_version": "1.0",
  "id": "com.deskkit.translate",
  "name": "聚合翻译",
  "version": "1.2.0",
  "description": "划词 / 触发词翻译，支持多引擎与离线基础词库",
  "author": { "name": "DeskKit Official", "url": "https://example.com" },
  "license": "MIT",
  "homepage": "https://example.com/plugins/translate",
  "icon": "assets/icon.png",
  "screenshots": ["assets/s1.png"],
  "source": "official",
  "category": "office",
  "min_host_version": "0.9.0",
  "engines": { "deskkit": ">=0.9.0 <2.0.0" },
  "entry": {
    "type": "webview", "html": "index.html", "sandbox": true,
    "csp": "default-src 'self'; connect-src 'none'; img-src 'self' asset:"
  },
  "forms": [
    { "type": "search", "keywords": ["翻译", "translate", "fy"], "group": "plugin", "priority": 80 },
    { "type": "trigger", "trigger": "fy", "exclusive": true, "placeholder": "输入待译文本", "exitBy": "esc" },
    { "type": "tool", "slot": 3, "label": "翻译", "icon": "assets/tool.png", "shortcutDigit": 3,
      "page": { "width": 720, "height": 520, "resizable": false } },
    { "type": "context", "accepts": ["text/plain", "text/uri-list"], "matchHint": "剪贴板文本 → 翻译", "priority": 90 }
  ],
  "permissions": [
    { "id": "clipboard.read", "reason": "读取你复制的文本用于翻译", "required": false,
      "scope": { "types": ["text/plain"] } },
    { "id": "net.fetch", "reason": "调用在线翻译服务", "required": false,
      "scope": { "hosts": ["api.example.com"], "methods": ["GET", "POST"] } },
    { "id": "clipboard.write", "reason": "一键复制译文", "required": true,
      "scope": { "types": ["text/plain"] } }
  ],
  "contributes": {
    "triggers": ["fy"], "panelSlots": [3],
    "commands": ["translate.swapLang", "translate.copyResult"],
    "contextHandlers": ["text/plain"]
  },
  "lifecycle": {
    "timeoutMs": 800, "maxMemoryMb": 64, "maxConcurrentCalls": 2,
    "backgroundAllowed": false, "autostart": false, "unloadAfterIdleMs": 20000
  },
  "integrity": { "algorithm": "sha256", "digest": "<sha256 of plugin bundle>", "sizeBytes": 248312 },
  "signature": { "by": "official", "value": "<ed25519 signature>" },
  "updatePolicy": { "channel": "stable", "autoCheck": true, "rollbackOnFailure": true },
  "i18n": { "defaultLocale": "zh-CN", "resources": "locales/*.json" }
}
```

### 6.2 安装期强制校验（`VALIDATING`）

| 字段 | 规则 |
|---|---|
| `manifest_version` | 必须在宿主 `[MIN_SUPPORTED, MAX_SUPPORTED]`，否则 `E_VERSION_INCOMPATIBLE` |
| `id` | 反向域名、全仓唯一；冲突列出占用者 |
| `source` | `official` / `community` / `local`，决定来源标识与信任等级 |
| `entry.csp` | 宿主强制注入下限：禁 `unsafe-inline`、禁任意 `connect-src`；插件只能更严 |
| `forms[].type` | `search` / `trigger` / `tool` / `context`，至少 1 项，可组合 |
| `forms[].trigger` | 全局唯一，冲突提示并允许改词 |
| `forms[].slot` | 1–9，占用时给改位提示 |
| `permissions[].id` | 必须属于固定 6 类（读写合并计类），否则 `E_CAPABILITY_UNKNOWN` |
| `permissions[].reason` | 非空，否则拒绝安装 |
| `permissions[].required` | 被拒时插件仍可安装，相关功能降级，**不影响其他功能** |
| `lifecycle.autostart` / `backgroundAllowed` | **宿主强制 false**（INV-05） |
| `lifecycle.timeoutMs` | 宿主钳制到 `[100, 3000]` |
| `integrity` | 下载后 sha256 + 体积双校验，失败不进 `INSTALLED` |
| `signature` | 官方必填，ed25519 验签；`local` 可省略但默认不自动启用 |

> **manifest 声明 ≠ 已授权**：声明只决定「可申请什么」，实际调用由 `GrantTable` 运行时决定。

### 6.3 消息协议

| `type` | 方向 | 用途 | 时限 |
|---|---|---|---|
| `request` | Host → Plugin | 触发 4 形态方法 | 见下表 |
| `bridge.call` | Plugin → Host | 申请受限能力（**唯一合法出沙箱通道**） | 1500ms（授权弹窗期间挂起计时） |
| `response` | 双向 | 对应 `id` 的结果或结构化错误 | 随原请求 |
| `event` | 双向 | `input.changed` / `visibility.hidden` / `permission.revoked` / `theme.changed` / `ready` / `result.patch` | 即时 |
| `cancel` | Host → Plugin | 取消在飞请求（输入变化、失焦、Esc、禁用） | 立即 |
| `ping` | Host → Plugin | 心跳 | 200ms ×3 → `CRASHED` |

超时预算：

| 场景 | deadline | 超时表现 |
|---|---|---|
| `onQuery` 搜索结果型 | 800ms（可配 300–1500） | **丢弃该插件分组**，其余结果按时出 |
| `onTrigger` 增量 | 300ms | 保留上一帧，轻提示，不阻塞输入 |
| `onOpenSlot` / 内页 render | 3000ms | 「该插件暂时不可用」+ 重试/禁用/卸载 |
| `onContext` | 1500ms | 顶部推荐卡降级为内置工具 |
| `bridge.call` | 1500ms | `E_TIMEOUT`；网络类另受 `E_OFFLINE` 降级 |
| 授权弹窗 | **挂起，不计入** | 决策后**重放**原请求 |
| 输入 → 结果整体 | **100ms** | 本地索引 <30ms 先行，插件结果异步合流 |

**取消语义**：每次输入变化宿主先发 `cancel`（带上一个 `id`）再发新 `request`；宿主把旧 `id` 标记失效，晚到 `response` 直接丢弃（INV-10）。

### 6.4 权限 5 道防线（INV-03，最易写错）

```
① 装载期   CapabilityBuilder 动态生成 plugin:<id> capability
           只授予 dk-plugin:allow-bridge-call，其余 default-deny
② IPC 入口 ACL Resolver
   插件直写 invoke("fs:read_file") → "Command not allowed"（无弹窗，运行时强制）
③ PermissionBroker 查 GrantTable
   allow → 继续 ｜ deny / 已撤销 → E_PERMISSION_DENIED（立即生效）
   未授权 → E_PERMISSION_REQUIRED + 授权弹窗（允许/拒绝/仅本次），决策后重放
④ ScopeValidator 验域名白名单 / 路径 glob（deny 优先）/ 剪贴板类型
   越界 → E_SCOPE_VIOLATION + 记权限总览审计
⑤ CapabilityExecutor 在 Core 进程内用主程序 capability 代执行
   只返回结果数据，永不返回句柄 / 路径 / token
```

撤销路径：删 grant → 广播 `permission.revoked` → 作废在飞请求 → 下一次调用立即 `E_PERMISSION_DENIED`，**无需重启插件或宿主**。
授权弹窗只负责把用户决策写入 `GrantTable`，**从不承担拦截职责**。

「插件不可」→ 技术强制：

| 产品禁令 | 强制手段 |
|---|---|
| 静默开机自启 | `autostart` 钳制 false；插件 WebView 仅在显式 invoke 时创建 |
| 后台常驻采集 | 不暴露 headless / background / 定时器；idle 回收；心跳发现异常常驻即回收并禁用 |
| 未授权读取 | ACL + Broker + Scope 三重 |
| 修改其他插件/主程序设置 | bridge 命名空间与 store 前缀按 `pluginId` 隔离；不暴露 `settings.write` / `plugin.manage` |
| 绕过权限弹窗 | 能力执行只在 Core；`GrantTable` 只由 Core 写入 |

### 6.5 四形态统一映射

> 差异只在「谁触发 + 渲染载体/生命周期」，协议层完全一致：`invoke(method, params, ctx)` → 同一 Envelope、同一 Broker、同一错误码。

| 形态 | 触发组件 | method | 渲染载体 | 结果 |
|---|---|---|---|---|
| ① 搜索结果型 | `SearchService` 命中 `keywords` | `onQuery` | 主浮层独立分组（异步合流） | `ResultItem[]` |
| ② 触发词专属型 `fy ` | `LauncherStore` 识别「触发词+空格」 | `onTrigger`（`ctx.exclusive=true`） | 固定插件卡片 | 结果卡片 + 复制 |
| ③ 工具宫格型 | C 面板格子 / 数字键 1–9 | `onOpenSlot` | 独立内页 WebView `plugin:<id>:page` | `PageHandle` + `result.patch` |
| ④ 上下文型 | `ContextMatcher`（剪贴板/选区） | `onContext` | 顶部推荐卡 / 超级面板 | `ResultItem[]` |

`ctx` 不含任何 OS 原始句柄；`ctx.platform` 只允许用于修饰键文案映射。**内置工具以 `source=builtin` 注册为「一等公民」走同一协议**，V1 开放生态时协议零改动。

### 6.6 生命周期状态机

```
DISCOVERED → VALIDATING → INSTALLING → INSTALLED → AUTHORIZING → ENABLED → RUNNING
VALIDATING/INSTALLING --校验或验签失败--> REJECTED
RUNNING --失焦/idle--> SUSPENDED --再次唤起--> RUNNING
RUNNING --超时--> TIMEOUT → FAILED_SAFE
RUNNING --心跳失败/进程退出--> CRASHED → FAILED_SAFE
FAILED_SAFE --重试--> RUNNING ｜ --禁用--> DISABLED ｜ --卸载--> UNINSTALLING
ENABLED ↔ DISABLED（禁用保留配置与 grant，不出现在搜索与上下文）
ENABLED → UPDATING → ENABLED ｜ UPDATING --失败--> ROLLBACK → ENABLED
ENABLED → UNINSTALLING → REMOVED
```

**卸载 7 项原子清理**：① 移除触发词注册 ② 释放宫格固定位 ③ 解绑全局热键 ④ 删除 capability ⑤ 作废全部 grant ⑥ 回收 WebView 进程 ⑦ 按用户选择处理 `plugins/<id>/data`。**不清空用户其他数据**。
`FAILED_SAFE` UI 固定为「该插件暂时不可用」+ 重试 / 禁用 / 卸载；主浮层不崩。

### 6.7 崩溃隔离

进程级隔离（每插件独立 WebView）｜心跳 `ping` 200ms ×3｜每请求 `deadlineMs` + `cancel`｜本地结果先出、插件分组独立 loading｜`maxConcurrentCalls` / `maxMemoryMb` / 并发 WebView ≤2｜连续崩溃自动 `DISABLED`｜崩溃时触发词/宫格位/热键保留但标记不可用，卸载才彻底清理。

---

## 7. 双端同期

### 7.1 13 项白名单 → 接口映射（唯一合法差异）

| # | 差异项 | `PlatformAdapter` 方法 | Windows | macOS |
|---|---|---|---|---|
| 1 | 修饰键文案 | `modifier_labels()` / `format_hotkey()` | `Alt` / `Ctrl` | `⌥` / `⌘` |
| 2 | 默认呼出键 | `default_hotkey()` | `Alt+Space` | `⌥+Space` |
| 3 | 系统热键冲突 | `detect_hotkey_conflict()` / `register_global_hotkey()` | 抢注窗口系统菜单；列占用者 | 事件拦截避免插入 NBSP；避开 Spotlight |
| 4 | 常驻入口 | `create_resident_entry()` | 任务栏通知区域 | 顶部菜单栏（NSPopover） |
| 5 | 系统权限 | `required_permissions()` / `permission_status()` | 开机自启 / 文件索引 | 辅助功能 / 输入监控 / 屏幕录制 / 完全磁盘访问 |
| 6 | 开机自启 | `set_auto_launch(bool)` | 启动项 | 登录项 |
| 7 | 数据路径 | `data_dir()` | `%APPDATA%/DeskKit` | `~/Library/Application Support/DeskKit` |
| 8 | 索引来源 | `app_index_sources()` | 开始菜单 / 桌面 / 注册应用 | `/Applications` / LaunchServices / Spotlight |
| 9 | 热键前置权限 | `hotkey_prerequisites()` | 直接注册 | 需辅助功能权限，未授予引导强提示 |
| 10 | 截图权限 | `capture_screen()` / `screen_capture_status()` | 系统截图接口 | 需屏幕录制权限，未授权给引导页而非黑屏 |
| 11 | 窗口材质 | `apply_window_material()` | 亚克力 / 云母 | vibrancy（**圆角/投影/密度/配色完全一致**） |
| 12 | 关闭行为 | `handle_close_request()` | 最小化到托盘 | 隐藏到菜单栏（均常驻不退出） |
| 13 | 更新分发 | `updater_config()` | 安装包 / 自动更新 | 同上（版本号由 CI 注入，不属平台分叉） |

UI 抽象：数据目录只显示「数据目录」按钮，不暴露原始路径；引导 Step3 与「关于」页**只列本平台所需权限**。

**其余 100% 共用**：Web 前端、核心服务、插件运行时、manifest/协议、数据 schema、功能清单。

### 7.2 发布单一事实来源

`release/version.json`（唯一版本号）+ `release/CHANGELOG.md`（唯一更新日志）+ `release/feature-manifest.json`（P0/P1 逐条 + 两端 `shipped` 状态）。CI 把三者注入所有产物；「关于」页展示同版本号与双端功能对齐状态。

### 7.3 CI 闸门 G0–G7

| 闸门 | 断言 |
|---|---|
| G0 | 契约 schema 一致性（Rust / TS 镜像逐字段对齐） |
| G1 | 单一版本号：`version.json` 与两端产物版本一致 |
| G2 | 双端构建 matrix 两个 job 均成功 |
| G3 | `feature-manifest.json` 中 P0/P1 两端均 `shipped` |
| G4 | 静态扫描：`platform/` 外无 `#[cfg(target_os)]` / `process.platform`；**功能开关无平台分叉** |
| G5 | 双端截图 diff：仅白名单 #11 材质允许差异，DOM 结构 / 归一化文案 / computed 尺寸三者一致 |
| G6 | schema 一致性与升级迁移测试通过 |
| G7 | 签名与公证（Win 签名 / mac notarization） |

**任一失败 → 整批不发**。V1 静态扫描基线（A1–A4）见 `docs/02-Architecture.md` §4.4.1；核心断言 A2「UI 文案分支 = 0」必须保留，A1 主断言为「归属函数集合」而非硬编码数量。

---

## 8. 性能预算与对策（NFR-01 ~ NFR-10）

| 编号 | 要求 | 对策 | 可验收判据 |
|---|---|---|---|
| NFR-01 | 呼出 <100ms P95；冷启首次 <300ms | 热键在 Rust Core 注册；预热挂起 WebView；索引 mmap | 打点 `hotkey→inputFocused` P95 <100ms |
| NFR-02 | 输入→结果 <100ms；插件慢不拖垮主浮层 | Rust 倒排 + 拼音 trie（万级 <30ms）；本地与插件结果异步合流 | 本地 P95 <30ms；单插件挂起主结果仍按时出 |
| NFR-03 | 空闲 ≤80MB | 见 1.2 内存预算（**≈42MB 为设计预算、非实测；T02 必须实测空闲 RSS 验证；Tauri 空闲口径 20–134MB 波动，广传基准缺方法论披露，不作为实测依据**）；插件零常驻 | 空闲 5 分钟后 RSS ≤80MB（两端各测） |
| NFR-04 | 内置工具与已装插件完全离线 | 词库/汇率/索引/插件包全本地；`E_OFFLINE` 降级 | 飞行模式跑通 17 条 P0 主流程 |
| NFR-05 | 默认仅本机；剪贴板可暂停/清空/排除应用 | `data_dir()` 抽象 + 保留策略 + 排除名单 | 全仓无默认外发 |
| NFR-06 | 权限可撤销且立即生效；崩溃隔离 | 6.4 / 6.7 | 撤销后下一次调用立即 `E_PERMISSION_DENIED`；杀插件进程主浮层不崩 |
| NFR-07 | 同版本号同期；P0/P1 对齐 100% | 7.2 / 7.3 | 任一构建失败整批不发 |
| NFR-08 | 全键盘可达；动效 ≤120ms | 状态机 + 语义 token | 按 PRD 6.2 键盘表全键盘走通 |
| NFR-09 | 插件超时有出口；设置自动保存不丢 | 超时熔断 + `FAILED_SAFE`；原子落盘 | 强杀进程后重启数据完整 |
| NFR-10 | 升级保留分组/触发词/设置/剪贴板 | `schemaVersion` + migration 链；更新失败回滚 | 跨版本升级四类数据零丢失 |

---

## 9. 依赖清单

| 分类 | 包 |
|---|---|
| 前端 | `react` / `react-dom` ^18.3、`typescript` ^5.6、`vite` ^6、`tailwindcss` ^3.4、`zustand` ^5、`@tauri-apps/api` ^2、`dayjs` ^1.11、`decimal.js` ^10 |
| Rust Core | `tauri` / `tauri-build` 2、`serde` / `serde_json` ^1、`tokio` ^1、`tantivy` ^0.22（或自研倒排）、`pinyin` crate、`rusqlite` / `sled`、`reqwest` ^0.12（**唯一网络出口**）、`sha2` / `ed25519-dalek`、`semver` / `chrono` / `thiserror` / `tracing`、`notify` ^6、`xcap` |
| Tauri 官方插件 | `plugin-global-shortcut`、`plugin-clipboard-manager`、`plugin-fs` / `http` / `notification` / `dialog`、`plugin-autostart` / `opener` / `shell`、`plugin-store` / `window-state` / `single-instance`、`plugin-updater`、`plugin-os` / `log` / `process`（均 ^2） |
| 社区补系统能力 | `tauri-plugin-macos-permissions`（TCC）、`tauri-plugin-screenshots` / `context-menu`、`tauri-nspanel`（mac 菜单栏） |
| 构建 / CI | `@tauri-apps/cli` ^2、`pnpm` ^9、`sccache`、GitHub Actions matrix、`cargo-audit` / `cargo-deny` |
| 测试 | `vitest`、`@playwright/test`（双端截图 diff 基线）、`cargo test` / `cargo clippy` |

> 原则：搜索 / 拼音 / 索引 / 权限 / 系统能力**全部放 Rust 侧**（保证 <100ms、离线、可拦截）；前端只做渲染与状态。

---

## 10. 实现顺序（T01 → T05，按顺序提交）

依赖图：`T01 → {T02, T03 可并行} → T04 → T05`。

### T01 · 项目基础设施与双端发布骨架（无依赖，P0）

**目标**：一次配置 → 两端构建 → 单一版本发布；**把所有跨模块契约写完**，使 T02/T03 可并行。

文件：`package.json`、`pnpm-workspace.yaml`、`vite.config.ts`、`tsconfig.json`、`src-tauri/Cargo.toml`、`tauri.conf.json`、`build.rs`、`src/main.rs`、`src/lib.rs`、`capabilities/main.json`、`capabilities/plugin-template.json`、`shared/contracts/*`（4.1–4.3 全部）、`shared/schema/*`（5 份）、`release/*`（3 份）、`.github/workflows/release.yml`（G0/G1/G2/G4 先落地）。

**完成判据**：`pnpm i && pnpm tauri build` 在 Win/mac 均成功；`cargo clippy` 无 error；`shared/contracts` 中 Rust/TS 类型镜像逐字段一致；CI 双 job 全绿才 publish。

### T02 · 平台抽象层与 Rust 核心服务（依赖 T01，P0）

文件：`platform/mod.rs` + `windows.rs` + `macos.rs`（13 项白名单）、`core/hotkey.rs`、`index.rs`、`search.rs`、`clipboard.rs`、`context.rs`、`settings.rs`、`system_permission.rs`、`tools/*`（5 组）、`migration/*`。

**完成判据**：`Alt/⌥+Space` 两端均可注册且无字符副作用；`wx` / `kzmb` / `520+999-1314` 命中正确；本地检索 <30ms；空闲 RSS ≤80MB；设置原子落盘 + migration 单测通过；`platform/` 外零 `cfg(target_os)`。

### T03 · 插件运行时（依赖 T01，与 T02 可并行，P0 闭环 + V1 增强）

文件：`plugin_runtime/{manifest,bridge,broker,scope,executor,host,forms,lifecycle,market}.rs`、`src/plugin-bridge/*`、`plugins/sample-translate/*`（同时声明 4 种形态）。

**完成判据**：跑通「市场安装 → 权限清单 → 授权弹窗 → 触发词唤起 → 执行 → 撤销 → 恢复 → 禁用 → 卸载」全链路；撤销后下一次 `bridge.call` 立即 `E_PERMISSION_DENIED`；插件直调 Tauri command 被 ACL 拒绝且无弹窗；`once` 用过即失效且不落盘；杀插件进程主浮层不崩；卸载 7 项清理可审计。

### T04 · UI 层与内置工具（依赖 T02 + T03，P0）

文件：`app/router.tsx` / `bootstrap.ts`、`state/launcher-store.ts`、`views/{launcher,panel,tools,market,settings,onboarding}/*`、`theme/tokens.ts`、`styles/*`。UI 细节见 `handoff/PROTOTYPE-SPEC.md`。

**完成判据**：6 界面可达并返回；A1–A4 + TRIGGER + TOOLPAGE 七态可触发；PRD 6.2 键盘表全键盘走通且 `Esc` 逐级回退；色值零散落；尺寸与 PRD 5.7 一致；6 类空态齐备。

### T05 · 集成、双端对齐验证（依赖 T01–T04，P0）

文件：`app/integration.ts`、`tests/parity/*`、`scripts/parity-check.*`、`docs/parity-checklist.md`、`release.yml` 闸门补全。

**完成判据**：G0–G7 全绿；双端 DOM/文案/尺寸三重对比白名单外差异 0；`prototype/index.html` 保持为独立参考产物，不进入代码库主线。

---

## 11. 编码约定

| 主题 | 约定 |
|---|---|
| 命名 | Rust 文件 `snake_case`；TS 文件 `kebab-case`；TS 变量 `camelCase`、类型 `PascalCase`；插件 id 反向域名；自定义前缀 `dk:` / `dk_` |
| API 响应 | 统一 `{ code, data, message, requestId, tookMs }`，`code=0` 成功；错误码集中在 `shared/contracts/errors.ts`，Rust 侧镜像 |
| 错误码命名空间 | 协议级 `E_*`（12 个）；宿主级 `DK-<域>-<三位号>`；禁止自造字符串 |
| IPC | command `dk_<domain>_<action>`；event `dk://event/<name>`；插件桥 `dk://plugin/<id>/bridge` |
| manifest 兼容 | `manifest_version` 独立 semver；只增可选字段、不删不改必填；破坏性变更升 major 并保留 N 个适配器；不满足 `min_host_version` → `E_VERSION_INCOMPATIBLE` 且不影响其他插件 |
| 权限 | 固定 6 类；`deny` 优先；`once` 会话级不落盘；撤销立即生效并作废在飞请求；声明 ≠ 授权 |
| 时间 | 持久化 ISO 8601 UTC；展示按系统时区，两端格式一致 |
| 数据与升级 | 每个持久化文件带 `schemaVersion`；atomic replace；两端 schema 完全一致 |
| 大对象 | 图片 / 文件 / 选区一律 `payloadRef` 句柄，不内联二进制；读取仍受权限与 scope 约束 |
| 主题与尺寸 | 色值唯一来源 `src/theme/tokens.ts`；尺寸唯一来源 PRD 5.7；深色沿用同一语义 token 换值，不改布局 |

---

## 12. 验收清单（每完成一个任务自检）

### 12.1 功能（17 条 P0）

| 编号 | 功能 | 验收要点 |
|---|---|---|
| DK-P0-01 | 全局呼出/隐藏 | 两端默认键生效；<100ms P95；无字符副作用；冲突给占用者与一键改键 |
| DK-P0-02 | 模糊 + 拼音搜索 | `wx`/`weixin`/`微信` 均命中微信；`kzmb` 命中控制面板类项；<100ms；分组标题固定 |
| DK-P0-03 | 应用/文件/网址启动 | 回车即开；网址走默认浏览器；结果结构两端一致 |
| DK-P0-04 | 剪贴板历史 | 默认开启可暂停；默认态可见最近 5 条；`Ctrl/⌘+Shift+V` 直达；保留 7/30/永久 + 清空 + 排除应用 |
| DK-P0-05 | 计算器/单位换算 | `520+999-1314` → `205`，Enter 复制；`3km to mi`、`100cny to usd` 可解析 |
| DK-P0-06 | 翻译 | `fy hello` 出译文；离线明确提示且主流程不报错 |
| DK-P0-07 | 截图取色 | 移动实时显示 HEX/RGB/HSL；Enter 复制；mac 未授权给引导而非静默失败 |
| DK-P0-08 | 时间戳/颜色转换 | `1730000000` 识别为时间戳；`#3B6CF6` 给出 RGB/HSL + 色块；两端格式一致 |
| DK-P0-09 | 工具宫格 | 3×3、数字键 1–9、内页可展开；与 A 结果互通 |
| DK-P0-10 | 插件运行时 | 安装→授权→触发→执行→禁用/卸载闭环；权限逐项撤销立即生效 |
| DK-P0-11 | 本地插件市场 | 安装前必展示权限清单；来源标识；断网降级且已装插件仍可用 |
| DK-P0-12 | 触发词 | `fy`/`js`/`color`/`ts`/`clip`/`ocr`；冲突检测；Esc 逐级退出 |
| DK-P0-13 | 上下文匹配 | 复制文本 → 翻译/剪贴板/二维码；复制图片 → OCR/取色 |
| DK-P0-14 | 分组与拖拽 | 从系统拖入识别；占位虚线 + 目标高亮；删分组项目移入「未分组」不丢；右键 8 项 |
| DK-P0-15 | 双端设置一致 | 设置项一一对应；`Alt`↔`⌥` 文案自动映射；「关于」展示同版本号与对齐状态 |
| DK-P0-16 | 引导与空态 | 3 步引导可跳过；权限页只列本平台；6 类空态有文案 + 主按钮 |
| DK-P0-17 | 失焦/多屏 | 失焦 150ms 隐藏；靠边内收 16px；三种多屏策略 |

### 12.2 非功能 / 双端 / 安全

`cargo test` + `cargo clippy` + `vitest` 全绿｜Playwright 双端截图 diff 白名单外差异 0｜空闲 RSS ≤80MB｜飞行模式跑通 P0 主流程｜撤销权限后下一次调用被拦截｜插件直调 command 被 ACL 拒绝｜强杀插件进程主浮层可输入｜跨版本升级四类数据零丢失｜CI G0–G7 全绿。

---

## 13. 已知缺口与待决问题

**待用户/产品决策**（建议默认值，未定不影响开工）：

| Q | 问题 | 建议默认 |
|---|---|---|
| Q1 | 商业模式（免费/会员/买断） | MVP 完全免费、不设插件数上限、不引入账号 |
| Q2 | 云同步与敏感数据上云 | 默认关闭且不同步剪贴板；仅预留 `SyncAdapter` 接口 |
| Q3 | 第三方权限分级与审核 | 开放固定 6 类；「网络 + 剪贴板」敏感组合标 `requiresReview`；不开放系统命令 / 任意 shell |
| Q4 | 默认呼出键是否坚持 `Alt/⌥+Space` | 坚持，但强制冲突检测 + 一键改键 |
| Q5 | 是否需要 Linux | MVP/V1 只做 Win + mac；`PlatformAdapter` 留扩展位 |
| Q6 | 内置工具组合 | 维持 5 组；OCR / 二维码 / 批量重命名放 P1 |
| Q7 | 团队分发 / 多语言 | 个人版优先；`ctx.locale` 与 `i18n` 字段已预留 |
| Q8 | 视觉确认 | 浅色 + `#3B6CF6`；深色 V1（同一语义 token 换值） |
| A1 | 插件能否带 sidecar 原生二进制 | MVP 只允许纯 HTML/JS 插件 |
| A2 | 插件签名信任链签发方 | 官方签名为 ed25519；社区/本地按来源标识分级 |
| A3 | 最低系统版本 / WebView2 预装策略 | 安装器检测并引导 WebView2 Runtime |

**已知设计取舍**：

- **2026-11 格局补注（方向印证，非切换理由）**：Wails v3 于 **2026-10-26 GA**（Go Core + 系统 WebView）；**Raycast 2.0（2026-05）** 采用与本项目**同构**的「原生壳 + 系统 WebView + 共享 React UI + Rust Core」四层混合架构，并明确否决 Electron（深层 OS 集成、不愿在 mac 打包 Chromium）与纯原生双 UI（两套栈 = 两倍工作）——**反向印证本次选型方向**。同时如实保留：**WebView 显隐控制力仍是 Tauri 相对自建混合壳的真实劣势**（Raycast 需专门驯服 WKWebView「每天显隐数百次」的行为）。核实来源与日期见 `docs/05-Architecture-Review.md` §9。**不因「Tauri 更现代」而维持选型**；维持理由是 NFR-03 / NFR-06 / 插件生态 Web 化 / 双端同期四条硬判据仍成立。
- **OBS-06（P3，V1 处理）**：单项权限拒绝/撤销时，`executeResult` 按插件**全量声明权限**校验会连带拦截「通用唤起入口」，但主能力仍可用。生产实现建议给 `ResultItem` 附加 `capability` 字段，按能力校验（见 4.1 已预留 `capability?: string`），或修订授权弹窗文案。**MVP 按现状实现即可**，不要为此临时改协议。
- 静态原型未验证：逐像素双端 diff 基线、NFR-01/02/03 性能指标、NFR-05/10 持久化与升级迁移、NFR-07 工程级同期、原生热键抢注 / 托盘菜单栏 / 多屏、插件进程级沙箱。这些必须在 T02–T05 补测。
- 原型中的「仅本次授权」`store.grants` 是内存态，刷新即丢；生产按会话级 `GrantTable` 实现（不落盘）。

---

## 14. 文档索引与优先级

| 文档 | 管什么 | 何时读 |
|---|---|---|
| `handoff/CURSOR.md` | 接入方式、规则文件、分阶段 Prompt、踩坑 | 最先 |
| **`handoff/TECH-SPEC.md`（本文）** | **实现唯一事实来源** | 全程 |
| `handoff/PROTOTYPE-SPEC.md` | 6 界面 / 状态机 / 键盘 / token / 数据 / 算法 | 做 T04 时 |
| `docs/01-PRD.md` | 产品语义、竞品、需求池验收要点、6.6 白名单 | 语义有疑问时 |
| `docs/02-Architecture.md` | 架构推演、时序图、类图、V1 静态扫描基线 | 设计细节有疑问时 |
| `docs/03-QA-Report.md` | 140 项验收口径与历史缺陷 | 写测试时 |
| `prototype/index.html` | 可运行的 UI 参考实现 | 对照视觉与交互时 |
| `docs/04-Naming.md` | 命名方案（一唤 / Hail） | 定品牌时；**当前 UI 仍用 DeskKit 代号，改名需走变更流程** |

**冲突优先级**：本文第 2 章硬约束 > 本文其余章节 > 架构文档 > PRD > 原型实现细节。
