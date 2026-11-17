# DeskKit 高保真交互原型 · QA 独立验收报告

> **这是旧原型的验收记录，不是现行产品的验收标准。** 被测页面含已取消的插件市场。不要为了让这些断言继续通过而去实现插件。现行范围见 [`docs/06-Incremental-PRD.md`](06-Incremental-PRD.md)，架构见 [`docs/10-Current-Architecture.md`](10-Current-Architecture.md)。

| 项 | 值 |
|---|---|
| 报告人 | 严过关（Yan）· QA 工程师 |
| 被测对象 | `prototype/index.html`（单文件，CSS/JS 全内联，`file://` 离线打开） |
| 验收依据 | `docs/01-PRD.md` 5.1–5.8 / 6.1–6.6 / 第 7 节；`docs/02-Architecture.md` §0.1 / §4.2 / §4.4 / §4.4.1 / §10.1 |
| 测试脚本 | `tests/prototype-smoke.js`（CommonJS，零第三方依赖，`node deskkit/tests/prototype-smoke.js` 可直接运行） |
| 轮次 | 首轮验收第 1 轮 + 第 2 轮回归（已达 2 轮上限）→ 工程师修复后由 team-lead 重新调度「修复后回归」1 轮 |
| 最终状态 | **全部通过（PASS）**，详见第 10 节 |
| 报告日期 | 2026-11-06 |

---

## 0. 结论先行（最终版 · 修复后回归已完成）

> **最终判定：`NOONE` · 全部通过 · 可交付。**

- 工程师已修复首轮验收发现的 **DEF-01（P1）** 与 **DEF-02（P3）**，并一并落地 **OBS-01~OBS-04**。QA 对修复后的 `prototype/index.html`（`208,228 bytes / md5 1fc47e74727696c2fa21a9c7a104d293`）独立复跑，未采信工程师自检结论。
- **原验收脚本 `tests/prototype-smoke.js`：85 / 85 通过，0 失败，100.0%**（P08、E07 转 PASS，原 83 条零回归；K15 forced=0、D05 白名单外差异 0、E08 尺寸、X01 异常、X02 外部请求均保持 PASS）。
- **新增定向回归 `tests/fix-regression.js`：36 / 36 通过，0 失败，100.0%**（运行时权限网关、仅本次授权、三条执行链、卸载清理、C 宫格尺寸、计算样式级 Toast 三色、双端 19 状态）。
- **双端补充核查 `tests/parity-supplement.js`：19 / 19 状态，白名单外差异 0**，退出码 0。
- **双端同期结论：白名单外差异 0**，PRD 6.6 对齐判据成立；4 类允许差异逐项核对通过。详见第 6 节。
- **零外部请求、零未捕获异常、零 `:root` 外硬编码色值、`node --check` 通过**。
- 新增 **1 项非阻断观察 OBS-06**（单项拒绝会连带拦截插件通用唤起入口，主能力仍可用），建议 V1 优化，不阻断本次交付，详见第 4、10 节。
- 历史记录：首轮验收为 83 / 85（97.6%）、路由 `SOURCE_BUG`，第 3～9 节保留该轮完整用例、缺陷与判定作为审计轨迹；**当前有效结论以第 0 节与第 10 节为准**。

---

## 1. 测试环境与方法

### 1.1 环境

| 项 | 实测 |
|---|---|
| Node | v22.22.2（内置 `WebSocket` / `fetch`），npm 10.9.7 |
| 浏览器自动化 | 无 playwright / puppeteer；使用**系统真实 Chrome**：`/Applications/Google Chrome.app/Contents/MacOS/Google Chrome`，`--headless=new` |
| 驱动方式 | Chrome DevTools Protocol（CDP），Node 22 内置 `WebSocket` 连接 `webSocketDebuggerUrl`，零第三方依赖 |
| 视口 | 1440 × 900 |
| 第三方依赖 | 0 |

**环境适配说明（重要）**：本工作区文件系统对文件做了 `%TSD-Header-###%` 包装，Chrome 作为原生进程直读 `file:///.../prototype/index.html` 会读到二进制包装体（页面无 `<script>`、`store` 未定义）。因此测试脚本启用 CDP `Fetch.enable` 在 Request 阶段拦截 `file://` 主文档请求，用 `Fetch.fulfillRequest` 把 Node 侧解码后的**原始 HTML 字节逐字节等价注入**。`location.href` 仍为 `file:///Users/.../deskkit/prototype/index.html`，页面由真实 Chrome 渲染、真实执行内嵌脚本。**被测源码文件未做任何修改**，注入内容与其完全一致。

### 1.2 方法

| 层面 | 手段 |
|---|---|
| 静态 | Node 正则全文件扫描（外部资源、色值散落、token 完整性、平台引用点、data-act 分支差集）；内嵌脚本抽取后 `node --check` |
| 渲染 | 真实 Chrome `file://` 加载，读取 DOM / `getComputedStyle`，校验六套界面可见性与尺寸 |
| 点击 | CDP `Input.dispatchMouseEvent`（真实坐标点击）+ 浏览器内 `element.click()` 事件委托点击 |
| 键盘 | CDP `Input.dispatchKeyEvent` 真实 keydown/keyup（含 `text` 以检验空格 / NBSP 副作用）+ 合成 `KeyboardEvent` 断言 `defaultPrevented` |
| 拖拽 | 触发 B 面板「模拟从系统拖入」真实业务路径 `bSimulateDrop()`，校验分组项数变化 |
| 截图 | `Page.captureScreenshot` × 10 张，落盘 `tests/screenshots/` |
| 零网络 | `Network.requestWillBeSent` + `Fetch.requestPaused` 全量监听，非主文档请求计为违规 |
| 异常 | `Runtime.exceptionThrown` + `Log.entryAdded(level=error)` 全量监听 |
| 双端 | 静态引用点清单 + 动态 `grid`/`page` 两形态 × 六视图 ×（DOM 结构 / 归一化文案 / 计算尺寸）三重对比 |

**未做像素级视觉回归**：有真实浏览器并留存 10 张截图，但未建立双端逐像素 diff 基线。原型层按架构 §4.4.1 采用「DOM 结构 + 文案 + 尺寸」静态断言替代（G5 截图基线属 V1 真实项目闸门，本次原型明确不实现）。此项列入第 7 节遗留风险。

---

## 2. 用例分组表（首轮验收历史快照）

> 分组 11 组、共 85 条。`实际` 列截取前 150 字符。**本表为首轮验收（83/85）的历史快照**，保留作为审计轨迹；修复后复跑的当前结果（85/85 全通过）见 `tests/results.json` 与本报告第 10 节。

| 编号 | 用例 | 预期 | 实际 | 结论 |
|---|---|---|---|---|
| **G1 结构 / 离线 / Token / 语法** | | | | |
| S01 | 单文件：无 <link> / <script src> / <iframe src> | 均为 0 | 0 处 | ✅ 通过 |
| S02 | 零网络：无 http(s):// 字面量 | 0 处 | 0 处 | ✅ 通过 |
| S03 | 零网络 API：无 fetch( / XMLHttpRequest / WebSocket / EventSource / importScripts | 0 处 | 0 处 | ✅ 通过 |
| S04 | CSS 无 @import / url( 外部资源 | 0 处 | 0 处 | ✅ 通过 |
| S05 | :root 之外无硬编码 hex / rgb()/rgba() | 0 处 | 0 处 | ✅ 通过 |
| S06 | PRD 5.7 的 24 个尺寸 token 齐全且值精确 | 24/24 匹配 | 24/24 匹配 | ✅ 通过 |
| S07 | PRD 5.8 的 15 个语义色值 + 4 个状态色齐全且精确 | 19/19 匹配 | 19/19 匹配 | ✅ 通过 |
| S08 | 分组 5 色均引用语义变量（无散落 hex） | 5 色均 var() | var(--accent) / var(--success) / var(--warning) / var(--accent-hover) / var(--danger) | ✅ 通过 |
| S09 | 内嵌脚本 node --check 语法通过 | 退出码 0 | node --check OK | ✅ 通过 |
| S10 | data-act 全覆盖：每个字面值都有事件分发分支 | 0 个死按钮 | 103 个字面值 / 109 个 case，未覆盖 0 | ✅ 通过 |
| S11 | 显式状态机 7 态 + Esc 回退栈存在 | TRANSITIONS 7 态 / rebuildEscStack+escBack | 7 态迁移表 + 回退栈齐备 | ✅ 通过 |
| S12 | 静态：平台等值分支仅落在白名单函数集合内 | 越界 0 处 | defaultHotkey,backupHotkey,t,renderF,onRecordKey,onRecordKey,onRecordKey（7 处，全部白名单内） | ✅ 通过 |
| S13 | 静态：render* / openModal 文案中无平台权限名硬编码 | 0 处 | 平台权限名仅存在于 PLATFORM_PERMS 常量 | ✅ 通过 |
| **G2 真实浏览器渲染 / 零网络请求** | | | | |
| V01 | file:// 加载成功，store 初始化为 A1 且脚本执行 | state='A1' / view='A' | {"view":"A","state":"A1","visible":true,"platform":"win","menu":false,"modal":false,"toolmode":"grid","activeTool":null,"raw":"","trigger":"","payload | ✅ 通过 |
| V02 | 页面无未捕获 JS 异常 / 控制台 error | 0 条 | 0 条 | ✅ 通过 |
| V03 | 浏览器实际发出的请求只有主文档（零外部请求） | 0 个外部请求 | 主文档 file:// + 0 个外部请求 | ✅ 通过 |
| V04 | 六套界面逐一可打开、可返回 A，且同一时刻仅一套可见 | A–F 全通过 | A=view-a \| B=view-b \| C=view-c \| D=view-d \| E=view-e \| F=view-f | ✅ 通过 |
| V05 | A1/A2/A3/A4 四态互斥且状态机 state 正确 | A1/A2/A3/A4 | A1 / A2 / A3 / A4 | ✅ 通过 |
| V06 | 触发词态 TRIGGER 与工具内页态 TOOLPAGE 可触发且互斥 | TRIGGER / TOOLPAGE 且各只占一态 | TRIGGER(胶囊+独占视图) / TOOLPAGE(view-c.page) | ✅ 通过 |
| V07 | A4 空态三个出口按钮可见可点且各有效 | 3 个出口均响应 | empty-browser / empty-market / clear 均有效 | ✅ 通过 |
| **G3 键盘流 / 呼出 / Esc 逐级回退** | | | | |
| K01 | 默认呼出键 Alt+Space / ⌥+Space 被 preventDefault（不产生空格/NBSP） | 三组组合键均 defaultPrevented=true | true,true,true | ✅ 通过 |
| K02 | 真实键盘 Alt+Space：切换呼出/隐藏且不向输入框插入空格 | 可见性翻转 + 探针值保持 XYZ | toggle OK / probe=XYZ | ✅ 通过 |
| K03 | mac ⌥+Space（NBSP 字符）不产生 NBSP 副作用 | 探针值仍为 XYZ | NBSP 被拦截 | ✅ 通过 |
| K04 | 备用呼出键 Ctrl/⌘+Shift+Space 可呼出并 preventDefault | toggle 生效且阻止默认 | Ctrl+Shift+Space OK | ✅ 通过 |
| K05 | ↑↓ 与 Ctrl/⌘+N/P 移动选中且到边界停住 | selected 0→1→0 | 1→0→1→0 | ✅ 通过 |
| K06 | Enter 执行选中项并给出 Toast 反馈；Shift+Enter 执行副操作 | 两次均出现 Toast | Enter / Shift+Enter 均有 Toast | ✅ 通过 |
| K07 | Tab 补全触发词 / 结果标题 / 切换 A↔C | fy 补全进入 TRIGGER | Tab(f)→TRIGGER(fy) | ✅ 通过 |
| K08 | Ctrl/⌘+K 操作菜单、Ctrl/⌘+, 设置页、Ctrl/⌘+T 工具宫格、Ctrl/⌘+Shift+V 剪贴板 | 4 条快捷键均响应 | K / , / T / Shift+V 全部响应 | ✅ 通过 |
| K09 | 数字键 1–9 直达结果 / 宫格工具 | Digit1 与 Digit4 生效 | Digit1→执行 / Digit4→计算器 | ✅ 通过 |
| K10 | F1 与 ? 打开快捷键速查；? 在输入框内不抢占且可正常输入 | F1/弹窗 + 输入框保留 ? | F1/? 打开；输入框内 ? 不抢占 | ✅ 通过 |
| K11 | 普通字符输入不被全局快捷键劫持（含 t / ? / 空格 / 数字） | 输入值完整且视图不变 | 输入值=t1 wd | ✅ 通过 |
| K12 | Esc 逐级回退链①：TRIGGER → A4 → A1 → HIDDEN | 4 级依次，不跳级 | TRIGGER → A4 → A1 → HIDDEN | ✅ 通过 |
| K13 | Esc 逐级回退链②：TOOLPAGE → A1 → HIDDEN | 3 级依次，禁止一键隐藏 | TOOLPAGE → A1 → HIDDEN | ✅ 通过 |
| K14 | Esc 栈为空（HIDDEN）时再按 Esc 不崩溃；快速连按呼出键状态稳定 | 无异常 / 状态确定 | HIDDEN 稳定，无异常 | ✅ 通过 |
| K15 | 状态机无非法迁移（forced 标记仅由窗口导航产生） | 正常键盘流 forced=0 | forced=0（记录值，架构 §4.4.1 A4 基线） | ✅ 通过 |
| **G4 搜索 / 拼音 / 触发词 / 错误输入** | | | | |
| Q01 | 拼音/首字母/英文命中：wx→微信 kzmb→控制面板 word→Word fy→翻译 | 4 项均命中 | wx→微信 \| kzmb→控制面板 \| word→Word \| fy→翻译/聚合翻译 | ✅ 通过 |
| Q02 | 算式 123*4 产生计算结果行 = 492 | 结果行含 492 | = 492 / copy=492 | ✅ 通过 |
| Q03 | 六个触发词 fy/js/color/ts/clip/ocr 均胶囊化并切到独占视图 | 6/6 TRIGGER + 胶囊 | fy / js / color / ts / clip / ocr 全部通过 | ✅ 通过 |
| Q04 | 无匹配进入 A4，三个出口可点且文案完整 | 空态 + 3 出口 | A4 文案 + 3 出口 | ✅ 通过 |
| Q05 | 匹配字符高亮 <mark> 且首行选中 | mark 存在、A3 有 .sel | mark + .sel 均在 | ✅ 通过 |
| Q06 | 边界：空输入、超长输入(>500 字符) 均不崩溃且渲染正常 | A1 / A4 正常 | 空输入→A1 / 620 字符→A4，无异常 | ✅ 通过 |
| Q07 | XSS 注入：<script>/<img onerror>/&/引号 被转义，无元素注入、无脚本执行 | 0 个注入元素 | 4 组 payload 均被 esc() 转义，__xss=0 | ✅ 通过 |
| Q08 | 边界：连续切换平台与视图 24 次无异常 | 0 异常 | 24 次切换无异常 | ✅ 通过 |
| **G5 工具内页算法（独立断言）** | | | | |
| T01 | 计算器 123*4 = 492；(1+2)*3 = 9 | 492 / 9 | 492 / 9 / 492 / 9 | ✅ 通过 |
| T02 | 除零有防护、非法表达式不崩溃 | evalExpression 返回 null，无抛错 | 除零→null/—，非法式→null/「表达式非法」，不崩溃 | ✅ 通过 |
| T03 | 单位换算 1km = 1000m；100cny to usd 走换算分支 | 1000 / 含 USD | 1km→1000 M / 14.044944 USD | ✅ 通过 |
| T04 | 颜色 #3B6CF6 → rgb(59,108,246)，HEX↔RGB 精确往返 | 精确一致 | {"r":59,"g":108,"b":246} / #3B6CF6 | ✅ 通过 |
| T05 | HEX→HSL→RGB 往返一致（容差 ±2/通道） | 最大通道误差 ≤2 | maxDiff=2 / #3B6CF6→#3C6EF6 #18A058→#18A057 #F0A020→#EF9F1F #FFFFFF→#FFFFFF #000000→#000000 #808080→#808080 | ✅ 通过 |
| T06 | 颜色工具内页三栏 DOM 联动显示 rgb(59, 108, 246) | 三栏均有值 | #3B6CF6 \| rgb(59, 108, 246) \| hsl(224, 91%, 60%) | ✅ 通过 |
| T07 | 时间戳 10 位 / 13 位 ↔ 日期双向正确 | getTime 精确、日期回解一致 | 10/13 位→1730000000000 / 日期往返 2024-10-27 11:33:20 | ✅ 通过 |
| T08 | 翻译内置词典 hello world 命中 | 你好，世界 | 你好，世界 \| 你好 \| 「notindict」· 离线基础词库暂未收录，可复制原文 | ✅ 通过 |
| T09 | 工具内页 UI：计算器 123*4 / 单位 1km→m / 时间戳双向 | 三处 UI 结果正确 | 492 / 1000 M / 2024-10-27 11:33:20 | ✅ 通过 |
| T10 | 触发词 js / color / ts 的结果计算独立正确 | 492 / rgb / 日期 | 492 \| #3B6CF6 \| 2024-10-27 11:33:20 \| 你好，世界 | ✅ 通过 |
| **G6 插件生命周期 / 权限 / 安装卸载** | | | | |
| P01 | 详情 → 权限清单可见（含用途说明） | 弹窗含权限项 | 详情+权限清单+更新日志 | ✅ 通过 |
| P02 | 安装 → 授权弹窗含 允许 / 拒绝 / 仅本次 三要素 | 3 个决策按钮 + 权限用途 | auth-deny/auth-once/auth-allow | ✅ 通过 |
| P03 | 允许 → 下载校验 4 步日志 → 安装完成弹窗询问固定/触发词 | 日志 4 步 + 完成弹窗 | 完成弹窗 + 固定/触发词询问 | ✅ 通过 |
| P04 | 完成 → installed/enabled/pinned 且触发词注册（冲突检测） | 状态正确 + trigger 生效 | {"installed":true,"enabled":true,"pinned":true,"trigger":"qr","has":true} | ✅ 通过 |
| P05 | 拒绝授权 → 插件其他功能仍可用（已装/已启用），受限权限记入 revoked | installed=true / revoked 全量 | {"installed":true,"enabled":true,"revoked":["screen.capture","clipboard.write"],"decision":"deny"} | ✅ 通过 |
| P06 | 仅本次授权 → decision=once 且弹窗可继续 | decision=once | decision=once | ✅ 通过 |
| P07 | 撤销权限 → 立即写入 revoked 且权限总览 UI 显示「已撤销」 | revoked 含该权限 + UI 文案 | {"revoked":["net.fetch"]} | ✅ 通过 |
| P08 | 撤销后立即生效：再次调用受限能力应被拦截（权限网关） | 应给出拒绝/需重新申请的拦截反馈，而非正常执行 | 失败：受限能力未被拦截，仍正常执行（Toast=已唤起插件 聚合翻译（模拟执行））；源码无运行时权限网关，revoked 仅用于设置页展示 | ❌ 失败 |
| P09 | 禁用插件 → 不再出现在搜索结果与上下文匹配 | 搜索命中 0 条 | {"enabled":false,"hits":0} | ✅ 通过 |
| P10 | 卸载二次确认 + 清理触发词/固定项/启用态 | 确认弹窗 + 清理完成 | 二次确认 + 触发词/固定项已清理 | ✅ 通过 |
| P11 | 卸载后搜索不再命中该插件；重复安装同一插件不产生重复条目 | 0 命中 / 1 条目 | 卸载 0 命中 / 插件条目数 8→8 | ✅ 通过 |
| **G7 演示态 / NFR-04 / NFR-09** | | | | |
| M01 | 插件超时：结果区出现「该插件暂时不可用」+ 重试/禁用/卸载 三出口 | 降级行 + 3 出口 | plugin-retry/plugin-disable/plugin-uninstall | ✅ 通过 |
| M02 | 超时态下主浮层仍可用（NFR-09 崩溃隔离） | A3 可见 / 输入框可输入 / 其他结果行仍在 | {"state":"A3","rows":2,"degraded":1,"inputDisabled":false,"visible":true} | ✅ 通过 |
| M03 | 超时三出口可点：重试恢复 / 禁用生效 / 卸载打开二次确认 | 三出口均有效 | 重试/禁用/卸载均有效 | ✅ 通过 |
| M04 | 断网：市场降级为本地已装视图 + 明确提示，已装插件仍可用（NFR-04） | 离线横幅 + 已装列表 + 触发词可用 | 降级横幅+已装 3 条，fy 触发词可用 | ✅ 通过 |
| **G8 双端同期 / PRD 6.6 白名单** | | | | |
| D01 | 静态：列出全部 store.platform / 平台判断引用点（供人工核对） | 输出清单 | 38 处，明细 → tests/platform-refs.json | ✅ 通过 |
| D02 | 白名单 #1/#2：修饰键文案与默认呼出键按平台映射正确 | win/mac 各自正确 | win: Alt + Space / mac: ⌥ + Space | ✅ 通过 |
| D03 | 白名单 #4：mac 顶部菜单栏 / Win 底部任务栏；常驻菜单结构两端一致 | 位置互换 + 菜单 5 项一致 | 呼出 / 隐藏/快捷启动面板/工具面板/设置/退出（两端一致） | ✅ 通过 |
| D04 | 白名单 #5：引导 Step3 / 关于页按平台只列本平台权限项 | win 2 项 / mac 4 项 | win=开机自启/文件索引 mac=辅助功能/输入监控/屏幕录制/完全磁盘访问 | ✅ 通过 |
| D05 | 对齐判据：六套界面在两端结构 / 文案 / 尺寸完全一致（白名单外 0 差异） | 0 处越界差异 | grid/page 两种形态 × 6 视图 × (结构+文案+尺寸) 差异 0 | ✅ 通过 |
| D06 | 白名单映射 {{PLATFORM}} 标签已核对（§4.4.1 列入 t() 白名单函数） | 仅平台标签，非产品分叉 | Windows / macOS（t() 映射，属白名单 #1 文案映射） | ✅ 通过 |
| **G9 空态 / 边界 / data-act 动态点击覆盖** | | | | |
| E01 | 6 类空态主行动按钮均可点击且有响应 | 6/6 有效 | empty-b-add/empty-clip/empty-noresult/empty-plugins/empty-updates/empty-offline | ✅ 通过 |
| E02 | 空态下点击 A4 主行动按钮 / 空剪贴板 / 空更新 不崩溃 | 0 异常 | 剪贴板 / 更新 / A4 空态均正常 | ✅ 通过 |
| E03 | 动态点击覆盖：DOM 中出现的每个 data-act 值点击后无 JS 异常 | 0 异常 / 0 死按钮 | 85 个 act（覆盖 25 种界面状态），成功点击 81 个，异常 0 | ✅ 通过 |
| E04 | 真实鼠标坐标点击（非 JS .click）能触发平台切换 | platform=mac | CDP mousePressed/Released → macOS | ✅ 通过 |
| E05 | 拖拽：B 面板模拟从系统拖入 → 新增快捷项 | 分组项数 +1 | 20→21 | ✅ 通过 |
| E06 | E 设置页触发词冲突检测 / 空值校验 | 冲突被拒、空值被拒 | 冲突/空值均被拒绝 | ✅ 通过 |
| E07 | Toast 类型样式类正确（ok/warn/err 应命中 CSS .toast.ok 等） | class 含独立 kind 类 | 失败：Toast kind 类名与 CSS 不匹配：toast toast-ok \| toast toast-warn \| toast toast-err（CSS 用 .toast.ok，代码生成 toast-ok） | ❌ 失败 |
| E08 | 尺寸规范落地：720px / 420×360 / 1000×680 / 760×520 等关键尺寸 | 与 PRD 5.7 一致 | {"view-a":"720pxx480px","view-c":"420pxx360px","view-d":"1000pxx680px","view-f":"760pxx520px"} | ✅ 通过 |
| **G10 截图留存** | | | | |
| SHOT | 输出 10 张关键状态截图到 tests/screenshots/ | 10 张 PNG | 10 张：01-A1-win.png, 02-A3-results.png, 03-A4-empty.png, 04-trigger-fy.png, 05-toolpage-calc.png, 06-market-offline.png, 07-timeout-degraded.png, 08-ma | ✅ 通过 |
| **G11 全局运行时异常** | | | | |
| X01 | 全流程结束后仍无未捕获 JS 异常 / console error | 0 条 | 0 条 | ✅ 通过 |
| X02 | 全流程结束后仍无外部网络请求 | 0 个 | 0 个外部请求 | ✅ 通过 |

---

## 3. 通过率统计

### 3.1 首轮验收（历史）

| 分组 | 用例数 | 通过 | 失败 | 通过率 |
|---|---:|---:|---:|---:|
| G1 结构 / 离线 / Token / 语法 | 13 | 13 | 0 | 100% |
| G2 真实浏览器渲染 / 零网络请求 | 7 | 7 | 0 | 100% |
| G3 键盘流 / 呼出 / Esc 逐级回退 | 15 | 15 | 0 | 100% |
| G4 搜索 / 拼音 / 触发词 / 错误输入 | 8 | 8 | 0 | 100% |
| G5 工具内页算法 | 10 | 10 | 0 | 100% |
| G6 插件生命周期 / 权限 / 安装卸载 | 11 | 10 | 1 | 90.9% |
| G7 演示态 / NFR-04 / NFR-09 | 4 | 4 | 0 | 100% |
| G8 双端同期 / PRD 6.6 白名单 | 6 | 6 | 0 | 100% |
| G9 空态 / 边界 / data-act 动态点击覆盖 | 8 | 7 | 1 | 87.5% |
| G10 截图留存 | 1 | 1 | 0 | 100% |
| G11 全局运行时异常 | 2 | 2 | 0 | 100% |
| **合计** | **85** | **83** | **2** | **97.6%** |

### 3.2 修复后回归（当前有效结论）

| 测试集 | 用例数 | 通过 | 失败 | 通过率 | 说明 |
|---|---:|---:|---:|---:|---|
| `tests/prototype-smoke.js` 原 85 条复跑 | 85 | **85** | **0** | **100.0%** | P08 / E07 转 PASS，原 83 条零回归 |
| `tests/fix-regression.js` 定向回归 36 条 | 36 | **36** | **0** | **100.0%** | 网关 / 授权语义 / 三链 / OBS / 双端 |
| `tests/parity-supplement.js` 双端 19 状态 | 19 | **19** | **0** | **100.0%** | 白名单外差异 0，退出码 0 |
| **合计** | **140** | **140** | **0** | **100.0%** | 三条脚本均以真实 Chrome headless + CDP 执行 |

覆盖率评估（估算）：公开 UI 状态 / 全局快捷键 / 六套界面 / 5 组内置工具算法 / 插件生命周期 / 权限网关 / 双端对齐 / 错误路径均有用例；按「关键路径覆盖」口径估算 ≥ 90%，未追求 100% 分支覆盖。

---

## 4. 缺陷清单（首轮验收）与修复回归结论

> **状态更新：下列 DEF-01 / DEF-02 均已由工程师修复，QA 已独立回归通过。** 本节保留首轮缺陷描述作为审计轨迹，当前有效状态以本表为准。

| 编号 | 严重级别 | 首轮判定 | 修复内容 | 回归证据 | 当前状态 |
|---|---|---|---|---|---|
| DEF-01 | P1 | `SOURCE_BUG` / P08 失败 | 新增「2.5 运行时权限网关」：`guard` / `guardCapability` / `guardPeek` / `guardCapabilityPeek` / `denyFeedback` / `denyIfBlocked` / `CAPABILITY_PERMS`；撤销与拒绝授权共用 `p.revoked`；`store.grants` 承载「仅本次」用过即失效；三条执行链全部接入 | 原脚本 P08 → PASS；定向回归 R11–R28、R33 → 36/36 PASS | **已修复并验证** |
| DEF-02 | P3 | `SOURCE_BUG` / E07 失败 | `toast()` 的 `className` 改为 `'toast' + (kind ? ' ' + kind : '')` | 原脚本 E07 → PASS；定向回归 R07–R10 用 `getComputedStyle` 实测圆点色 `rgb(24,160,88)` / `rgb(240,160,32)` / `rgb(229,72,77)` | **已修复并验证** |

### DEF-01 · P1 · 撤销权限未「立即生效」，无运行时权限网关（首轮记录）

| 项 | 内容 |
|---|---|
| 编号 / 用例 | DEF-01 / **P08（失败）**；关联 P05、P07（通过但同根因） |
| 严重级别 | **P1（验收项不满足）** |
| 复现步骤 | ① 设置 → 插件 → 权限总览，点击「聚合翻译」的 `net.fetch` →「撤销」；② 回到 A，搜索「聚合翻译」；③ Enter / 点击执行该插件结果（或用触发词 `fy hello` 调用翻译） |
| 期望 | PRD 6.5「撤销后立即生效，插件下次调用需重新申请」、NFR-06「可撤销且立即生效」、架构 §0.1 NFR-06 判据「撤销后下一次 `bridge.call` 立即 `E_PERMISSION_DENIED`」、§9「撤销立即生效并作废在飞请求」→ 调用应被拦截，给出明确的「权限已撤销 / 需重新申请」提示，且**不得正常执行** |
| 实际 | Toast 为「已唤起插件 聚合翻译（模拟执行）」，能力正常执行；`p.revoked` 只被设置页用来显示「已撤销」文案 | 
| 代码位置 | `revokePerm` **2555–2561**（仅 `p.revoked.push` + `renderAll`）；`store.plugins` 初始化 **794**（`revoked:[]`）；`runDownload` **2453**（授权决策写入 revoked）；`auth-allow/once/deny` **2778–2785**；**唯一读取点** `renderE` **1898–1902**（仅 UI 展示）；执行路径 `executeResult` case `'plugin'` **2175**、`triggerCopyValue` **1704–1714**、`scheduleTriggerTranslate` **1694–1703** 均无权限校验；全文无 `PermissionBroker` / `E_PERMISSION_DENIED` / `bridge.call` 等价网关 |
| 影响 | 「拒绝授权」（`auth-deny` 会把全部权限写入 revoked）后插件仍可无阻调用受限能力；NFR-06、PRD 6.5 权限闭环不成立 |
| 修复建议 | 增加统一 `guard(pluginId, permId)`：调用插件能力前检查 `p.revoked` 与 `store.market.flow.decision`，命中则给出明确拒绝 / 重新申请反馈并中止执行；搜索结果执行、触发词独占视图、工具内页三条调用链统一接入。`p.revoked` 从「纯展示字段」升级为「运行时裁决输入」 |

### DEF-02 · P3 · Toast 类型类名与 CSS 选择器不匹配（首轮记录）

| 项 | 内容 |
|---|---|
| 编号 / 用例 | DEF-02 / **E07（失败）** |
| 严重级别 | **P3（视觉降级，不影响功能）** |
| 复现步骤 | 任意触发 `toast('x','ok')` / `toast('x','warn')` / `toast('x','err')`，检查生成的 `.className` |
| 期望 | CSS **413–415** 定义 `.toast.ok .toast-dot` / `.toast.warn .toast-dot` / `.toast.err .toast-dot`，类名应为独立类 `ok` / `warn` / `err`，圆点按状态变色 |
| 实际 | `toast()` **859** 生成 `toast toast-ok` / `toast toast-warn` / `toast toast-err`；圆点始终命中 `.toast .toast-dot`（**412**）的强调色，成功 / 警告 / 错误三色永不生效 |
| 修复建议 | `box.className = 'toast' + (kind ? ' ' + kind : '')`（去掉 `toast-` 前缀） |

### 观察项（不计入失败）

| 编号 | 观察 | 判定 / 当前状态 |
|---|---|---|
| OBS-01 | 首轮：PRD 5.8 分组第 4 色为 `#9B6CF6`，原型 `--chip-4` 实际为 `#2F5BE0` | **已修复**：新增 `:root` token `--chip-group-4:#9B6CF6`，`--chip-4:var(--chip-group-4)`；R04 静态 + R29 计算色 `rgb(155,108,246)` 双重通过 |
| OBS-02 | 首轮：`--cell-w:112px` / `--cell-h:96px` 已定义但 `.tool-cell` 计算值为 `auto` | **已修复**：R30 实测 9 格 × `112px×96px`，C 外框 `420×360`，`#c-grid` 无溢出；head/foot 收到 `30/22`（PRD 5.7 未规定 C 宫格 head/foot，48/36 属 B 面板规格，外框不变） |
| OBS-03 | 首轮：卸载后 `pinList` / `p.trigger` 残留 | **已修复**：R31 实测 `trigger` / `revoked` / `pinned` / `triggers` / `pinList` / `hotkeys` / `grants` / `installed` / `enabled` 共 9 项全清 |
| OBS-04 | 首轮：HIDDEN→TOOLPAGE 产生 `forced:true` | **已修复**：`TRANSITIONS.HIDDEN` 补入 `TOOLPAGE`；R05 静态 + R32 动态 `forced=0` 通过；原脚本 K15 保持 PASS |
| OBS-05 | `{{PLATFORM}}` 占位符使 D/E 标题栏与关于页显示 `Windows` / `macOS` | **白名单内**，非越界差异（§4.4.1 把该占位符列入 `t()` 映射）；修复后 R35 / parity-supplement 19 状态复核仍为 0 差异 |
| **OBS-06（回归新发现 · 非阻断）** | `executeResult` case `'plugin'`（约 2262–2266 行）按插件**全部声明权限**做 `find(perm => !guard(pl.id, perm))` 全量校验。仅拒绝 / 撤销其中一项（如 `clipboard.read`）时，插件的「通用唤起入口」会被连带拦截，而该插件主能力仍可用（R21 实测 `guardCapabilityPeek('translate')=true`，触发词 `fy` 仍进入翻译）。与授权弹窗文案「拒绝某项不影响插件其他功能」存在表述不一致 | **P3 观察，不阻断交付**。PRD 6.5 要求「受限能力被拦截」，当前实现是更严格的整插件拦截，不构成需求违背；且原型插件结果项（`buildResults` 约 1203 行）未携带 `capability` 字段，属建模粒度限制。建议 V1 给结果项附加 `capability` 并改为按能力校验，或同步修订弹窗文案。本次按 2 轮上限约定不再开修复轮次 |

---

## 5. 智能路由判定

| 轮次 | 用例数 | 通过 | 失败 | 路由判定 | 处置 |
|---|---:|---:|---:|---|---|
| 第 1 轮 | 85 | 74 | 11 | `TEST_BUG` ×9 + `SOURCE_BUG` ×2 | 9 个测试脚本缺陷自行修复（S12 Node 端误用 `window`；V05 传错事件对象字段；K05 结果数不足导致 moveSelection 被 clamp；K07 把「fy」误当触发词前缀，应改用「f」；Q07 把原型合法内联 SVG 图标计入注入；T02 把合法一元正号 `1++2` 判为非法；T07 拿两个不同时刻做往返；D04 用子串误把 mac 用途说明中的「文件索引」当权限项；D05 模板串 `\b`/`\s` 被 Node 先消费）；2 个源码缺陷上报，未改源码 |
| 第 2 轮回归 | 85 | 82 | 3 | `TEST_BUG` ×1 + `SOURCE_BUG` ×2 | 剩余 1 例为 D05 最后一处 `String.raw` 转义缺陷，修正后做**最终确认执行** |
| 最终确认 | **85** | **83** | **2** | **`SOURCE_BUG`** | 2 例均为源码缺陷（DEF-01 / DEF-02），按规则**不自行修改源码**，缺陷清单交 `team-lead` 转工程师 |
| **修复后回归（team-lead 重新调度）** | **85 + 36 + 19** | **140** | **0** | **`NOONE`** | 工程师完成修复后，QA 独立复跑原脚本 + 新增定向回归 + 双端补充核查；期间 3 个 `TEST_BUG`（`JSON.stringify(Promise)` 序列化成 `{}`、断言把 `null` 写成 `undefined`）自行修正，均为测试脚本缺陷，非产品缺陷 |

- **最终路由：`NOONE` → 全部通过 → 可交付**。
- 全程未修改 `prototype/index.html`；仅新增 `tests/` 与 `docs/03-QA-Report.md`。
- 首轮 2 轮上限已闭合；本轮「修复后回归」为 team-lead 重新调度的新验证活动，只做 1 轮产品验证（3 条脚本各 1 次），未陷入无限循环。

---

## 6. 双端白名单核对结论（重点）

### 6.1 静态：平台引用点逐条列出

全文件 `store.platform` / 平台判断相关引用点共 **38 处**，逐条明细落盘 `tests/platform-refs.json`。归类如下：

| 类别 | 代表行号 | 归属 | 对应白名单 |
|---|---|---|---|
| 修饰键文案映射 `TEXT_MAPS` / `M()` / `t()` / `keycap()` | 627、826、829–841、1819–1824、2006–2007 | 白名单函数集合 | #1 |
| 默认 / 备用呼出键 `defaultHotkey()` / `backupHotkey()` | 827–828、1975–1979、2590、2594、2847–2848、3112 | 白名单函数集合 | #2 |
| 录制改键与冲突建议键（含引导 Step2 渲染） | 1972、2878–2888 | `onRecordKey` / `renderF`（§4.4.1 明确列入） | #1 / #3 |
| `{{PLATFORM}}` 标签占位符 | 841 | `t()`（§4.4.1 明确列入） | #1 文案映射 |
| 系统权限清单（数据驱动，非分支） | 1917、1981、2581 | `PLATFORM_PERMS[platform]` 常量 | #5 / #9 / #10 |
| 常驻入口可见性（class 切换，非文案） | 1177–1178 | `renderResident()` | #4 |
| 原型控制台开关高亮（不属于产品 UI） | 1166 | `renderConsoleState()` | 控制台，非产品分叉 |
| 平台上下文写入 | 2083 | `switchPlatform()` | 注入点 |

**断言结果（S12 / S13）**：7 处 `store.platform==='win'?…` 等值分支全部落在白名单函数集合 `{defaultHotkey, backupHotkey, keycap, t, renderF, onRecordKey, renderConsoleState, renderResident}` 内，**越界 0**；平台权限名（辅助功能 / 输入监控 / 屏幕录制 / 完全磁盘访问 / 开机自启 / 文件索引）只出现在 `PLATFORM_PERMS` 常量，未写死在 `render*` 字符串字面量（对应 §4.4.1 A2 / A3）。

### 6.2 动态：允许差异逐项验证

| 允许差异 | 验证用例 | 结果 |
|---|---|---|
| ① 修饰键文案映射 | D02 | win `Ctrl + K` / mac `⌘K`；`t()` 映射正确 ✅ |
| ② 默认呼出键显示 | D02 | win `Alt + Space`、备用 `Ctrl + Shift + Space`；mac `⌥ + Space`、备用 `⌘ + ⇧ + Space` ✅ |
| ③ 引导 Step3 与「关于」页系统权限清单 | D04 | win 2 项（开机自启 / 文件索引）、mac 4 项（辅助功能 / 输入监控 / 屏幕录制 / 完全磁盘访问），只列本平台 ✅ |
| ④ 常驻入口位置 | D03 | mac 顶部 `#menubar` / Win 底部 `#taskbar`；常驻弹出菜单 5 项（呼出 / 快捷启动面板 / 工具面板 / 设置 / 退出）两端完全一致 ✅ |

**对齐判据（D05）**：在 `grid` 与 `page` 两种 C 形态下，对 A–F 六套界面逐套对比「DOM 结构指纹（标签 / class / data-act / data-v）+ 归一化文案 + `getComputedStyle` 宽高」，并对比 `#hintbar`；排除上述 4 类允许差异后，**白名单外差异 = 0**（结构与尺寸差异亦为 0）。

### 6.3 结论

> PRD 6.6「除 13 项白名单外，任何功能、界面、文案、快捷键语义的双端差异均视为缺陷」这一对齐判据，**在本次原型上成立**。未发现越界差异；`{{PLATFORM}}` 标签与 Step2 冲突建议键均已按 §4.4.1 归入白名单映射函数，属合法数据驱动差异。

范围说明：白名单 #6–#13（开机自启实现、数据路径、索引来源、全局热键注册、截图权限、窗口材质、关闭行为、更新分发）属于**真实运行时能力**，原型以统一文案 / 模拟数据呈现，两端无分叉；本次仅验证「原型层无分叉」，未做运行时级双端验证。

---

## 7. 未覆盖项与遗留风险

| # | 未覆盖 / 风险 | 说明 |
|---|---|---|
| 1 | **像素级视觉回归未做** | 已用真实 Chrome 截图 10 张，但未建立双端逐像素 diff 基线；按 §4.4.1 用 DOM 结构 / 文案 / 尺寸三重断言替代。G5 截图基线属 V1 真实项目闸门 |
| 2 | NFR-01 / 02 / 03 性能指标 | 呼出 <100ms P95、输入→结果 <100ms、万级索引、空闲 ≤80MB 常驻内存，纯静态原型无法测量 |
| 3 | NFR-05 / NFR-10 | 本地持久化、剪贴板保留策略、schemaVersion 升级迁移，原型未实现持久化 |
| 4 | NFR-07 工程级同期 | CI 双 job / 单一版本号 / 签名公证，属 V1 工程能力；原型仅展示「关于」页对齐状态 |
| 5 | 原生系统级行为 | 真实 Win/mac 全局热键抢注、`⌥+Space` 系统级 NBSP 冲突、托盘 / 菜单栏原生行为、多屏边缘内收，headless Chrome 无法验证；仅验证浏览器层事件拦截与 DOM 呈现 |
| 6 | 插件真实运行时 | 插件为模拟数据，无真实沙箱 / 独立 WebView / 进程隔离；NFR-09「崩溃隔离」仅验证演示态 UI 出口（M01–M03），未做进程级强杀 |
| 7 | 截图覆盖度 | 10 张均为 1440×900 headless 视口，未覆盖多分辨率 / 多屏 / 字号缩放 / 深色主题（P1） |
| 8 | 遗留缺陷 | **已清零**。DEF-01（P1）、DEF-02（P3）均已修复并回归通过（见第 4、10 节） |
| 9 | 观察项 | OBS-01～OBS-04 已随本次修复落地；OBS-05 属白名单内；**OBS-06（单项拒绝连带拦截插件通用入口）为回归新发现的非阻断 P3 观察，工程师已提交 A/B 选项待主理人定方向、当前不改文件**，见第 4、10.5 节 |
| 10 | 修复引入范围 | 本次修复新增约 6,190 bytes 源码（权限网关 + 尺寸调整 + 卸载清理）。已用原 85 条 + 36 条定向 + 19 状态双端复核；但「C 宫格 head/foot 30/22、gap 4、padding 4」为原型内部布局参数，PRD 5.7 未规定，外框 420×360 不变，未做多分辨率 / 字号缩放验证 |
| 11 | 逐像素视觉回归 | 仍未建立双端逐像素 diff 基线（见第 7 节 #1）；本次修复涉及颜色与尺寸，建议 V1 补基线 |

---

## 8. 总体结论（最终）

**通过（Pass），最终路由 `NOONE`。**

原型在「单文件离线、语义 token 唯一色源、显式状态机 + Esc 回退栈、六套界面可达、完整键盘流、5 组工具真实算法、插件生命周期闭环、**运行时权限网关与授权语义**、双端白名单对齐、演示态降级、边界与 XSS 防护、data-act 无死按钮」等维度均达到 PRD / 架构验收要求；浏览器实测零外部请求、零未捕获异常。

首轮 2 个源码缺陷（DEF-01 / DEF-02）与 4 项观察项（OBS-01～04）已由工程师修复，QA 用「原 85 条 + 定向 36 条 + 双端 19 状态」共 140 项断言独立复核，**全部通过**。当前仅余 1 项非阻断观察 OBS-06 与第 7 节列出的 V1 级未覆盖项，不影响本次交付。QA 全程遵守「不修改源码」与轮次约束。

---

## 9. 补充核查：工程师「文案微调」后的基线复核（历史快照）

> 本节记录首轮验收后、工程师修复缺陷前的「文案微调」核对过程，属历史审计轨迹；当时判定为 `SOURCE_BUG`，最终结论已由第 10 节的修复后回归转为 `NOONE`。

QA 报告出具后，工程师补充说明：`prototype/index.html` 曾对「设置 → 快捷键 Tab → 被系统或其他应用占用（提示区）」做过一次平台无关文案统一，并要求以最新版为准。QA 未直接采信该自检结论，而是重新核对当前文件指纹并补跑双端复验。

### 9.1 基线一致性（确认「微调已在最终验证版本内」）

| 核对项 | 当前文件实测 | 最终 85 条用例验证基线 | 结论 |
|---|---|---|---|
| 文件字节数 | 202,038 bytes | 202,038 bytes | 一致 |
| MD5 | `3d2f9b6e4aee991e7c244d49f41e650f` | `3d2f9b6e4aee991e7c244d49f41e650f` | 一致 |
| 内嵌脚本长度 | 139,046 chars | 139,046 chars | 一致 |
| 逻辑行数 / 字符数 | 3,126 行 / 184,388 chars | 3,126 行 / 184,388 chars | 一致 |
| 第 1890 行提示区文案 | 平台无关统一文案「常见系统占用：输入法切换、传统窗口的系统菜单、系统全局搜索，以及部分输入框会插入不换行空格（NBSP）。呼出键已通过事件拦截避免字符副作用。」 | 同左 | 已包含在验证基线内 |

结论：**当前文件与 `tests/results.json` 所对应的被测版本为同一字节内容**，该文案微调已在最终 85 条用例验证范围内，无需重跑全量用例，报告第 0、3、4、8 节结论不变。

### 9.2 19 个界面状态双端复验（`tests/parity-supplement.js`）

为避免「只改了文案却引入新的平台分叉」，对以下 19 个状态逐一在 `win` / `mac` 下做「DOM 结构指纹（标签 / class / data-act）+ 归一化文案」比对，白名单 #5 的系统权限卡片按数据项裁剪后参与对比：

| 分组 | 覆盖状态 |
|---|---|
| E 设置 | `E-general` / `E-hotkeys` / `E-plugins` / `E-about` |
| F 引导 | `F-step1` / `F-step2` / `F-step3` / `F-gallery` |
| B 面板 | `B-normal` / `B-edit` |
| C 工具 | `C-grid` / `C-page-calc` |
| D 市场 | `D-discover` / `D-installed` / `D-updates` |
| A 主浮层 | `A-A1` / `A-A3` / `A-A4` / `A-TRIGGER` |

结果：**19 / 19 状态通过，白名单外差异 0，退出码 0**。其中 `E-hotkeys`「被系统或其他应用占用」提示区两端原文完全一致，确认工程师本次统一生效；第 6 节「双端白名单外差异 0」的结论在更细粒度上再次成立。

### 9.3 对工程师自检结论的独立回应

| 工程师说法 | QA 独立核对 |
|---|---|
| 「当前环境无浏览器自动化，建议双击打开目视复核」 | **不成立**。环境中存在系统 Chrome，本次全部用例均以 `--headless=new` + CDP 真实浏览器执行（含 `file://` 加载、点击、键盘、拖拽、截图 10 张），见第 1 节。像素级逐像素 diff 基线仍未建立，此项维持第 7 节遗留风险 #1 |
| 「已知限制均为原型模拟，避免误判为 bug」 | **已确认无一项被误判**。本次 2 个失败用例均与模拟能力无关，是独立的源码实现缺陷 |
| 「自检 IS_PASS: YES / 0 失败」 | **不采信为验收结论**。QA 独立用例 `P08`、`E07` 仍失败，详见第 4 节 DEF-01 / DEF-02 |
| 「全文件 UI 层按平台文案分支为 0 处」 | **复核通过**。静态 38 处平台引用点 + 动态 19 状态比对，白名单 4 类允许差异之外无分叉 |

### 9.4 补充核查后的最终判定

> **当时的判定为 `SOURCE_BUG`（该节属历史快照）。** 此后工程师已完成 DEF-01 / DEF-02 与 OBS-01～04 的修复，QA 已按第 10 节完成修复后回归并转为 **`NOONE` / 全部通过**。本节保留用于说明「文案微调→基线核对」的审计链路，当前有效结论以第 0、8、10 节为准。

---

## 10. 修复后回归验证（最终判定）

### 10.1 被测基线

| 项 | 首轮验收版本 | 修复后回归版本 |
|---|---|---|
| 文件字节数 | 202,038 bytes | **208,228 bytes** |
| MD5 | `3d2f9b6e4aee991e7c244d49f41e650f` | **`1fc47e74727696c2fa21a9c7a104d293`** |
| 逻辑行数 / 字符数 | 3,126 行 / 184,388 chars | 3,242 行 / 189,504 chars |
| 内嵌脚本 | 139,046 chars | 143,885 chars |
| 新增内容 | — | 「2.5 运行时权限网关」、`:root --chip-group-4`、`.tool-cell` 尺寸引用、`doUninstall` 清理、`TRANSITIONS.HIDDEN→TOOLPAGE`、`toast()` 类名 |

QA 未采信工程师「85/85 自检」结论，自行复跑并新增定向用例；三条脚本均以系统 Chrome `--headless=new` + CDP 驱动 `file://`，零第三方依赖。

### 10.2 三条脚本执行结果

| 脚本 | 用例数 | 通过 | 失败 | 通过率 | 退出码 | 结果文件 |
|---|---:|---:|---:|---:|---:|---|
| `tests/prototype-smoke.js`（原 85 条，未改动） | 85 | **85** | **0** | **100.0%** | 0 | `tests/results.json` |
| `tests/fix-regression.js`（新增定向回归） | 36 | **36** | **0** | **100.0%** | 0 | `tests/fix-regression-results.json` |
| `tests/parity-supplement.js`（双端 19 状态） | 19 | **19** | **0** | **100.0%** | 0 | 控制台 |
| **合计** | **140** | **140** | **0** | **100.0%** | — | — |

原脚本回归明细：`P08` / `E07` 由 FAIL 转 PASS；原 83 条零回归；`K15 forced=0`、`D05 白名单外差异 0`、`E08 尺寸`、`X01 0 异常`、`X02 0 外部请求` 全部保持 PASS；`S05/S06/S07/S08` 语义色与尺寸 token 仍 24/24、19/19。

### 10.3 定向回归 36 条分组统计

| 分组 | 用例数 | 通过 | 失败 | 覆盖要点 |
|---|---:|---:|---:|---|
| R1 静态 / 网关结构 / 零回归 | 6 | 6 | 0 | 8 个网关符号、9 条调用链、零外链、`:root` 色值唯一源、HIDDEN 迁移表、`node --check` |
| R2 DEF-02 Toast 计算样式 | 4 | 4 | 0 | `ok/warn/err` 独立类 + `getComputedStyle` 圆点色实测（`rgb(24,160,88)` / `rgb(240,160,32)` / `rgb(229,72,77)`）+ 默认强调色 |
| R3 DEF-01 权限网关 / 授权语义 | 11 | 11 | 0 | `CAPABILITY_PERMS` 六项含 `screenshot→screen.capture` 别名、撤销拦截、恢复放行、拒绝授权、「仅本次」用过即失效、渲染期不消耗、撤销作废在飞授权、未装/未启用不误伤 |
| R4 三条执行链拦截 | 7 | 7 | 0 | 搜索结果 `executeResult`；触发词 `scheduleTriggerTranslate` / `triggerCopyValue` / `trig-clip`；工具内页 `updateTranslateImmediate` / `scheduleTranslate` / `copyToolResult` / `tool-clip-copy` / `onPickHover` |
| R5 OBS-01~04 落地 | 4 | 4 | 0 | `--chip-4=rgb(155,108,246)`；9 格 × 112×96 / 420×360 无溢出 / head 30 foot 22；卸载 9 项清理；`forced=0` |
| R6 全局回归 / 双端 | 4 | 4 | 0 | 安装「仅本次」全链路 grants 写入、Esc 两条回退链、19 状态双端白名单外差异 0、零异常零外部请求 |
| **合计** | **36** | **36** | **0** | — |

### 10.4 缺陷修复逐项验收

| 编号 | 工程师修复 | QA 独立验证 | 结论 |
|---|---|---|---|
| DEF-01 | 新增网关 `guard` / `guardCapability` / `guardPeek` / `guardCapabilityPeek` / `denyFeedback` / `denyIfBlocked`；撤销与拒绝共用 `p.revoked`；`store.grants` 承载「仅本次」；三条链接入 | P08 PASS；R11–R22、R33 共 12 条断言：撤销后 `guard=false`、执行给出「能力已拦截 + 指路恢复」、`restorePerm` 后放行、deny 共用 revoked、once 首次放行二次拦截且 grants 清空、渲染期不提前消耗 | **通过** |
| DEF-02 | `toast` className 改为独立 kind 类 | E07 PASS；R07–R10 计算样式级实测三色与默认色 | **通过** |
| OBS-01 | `--chip-group-4:#9B6CF6`、`--chip-4:var(--chip-group-4)` | R04 / R29 / 原脚本 S08（分组 5 色引用语义变量） | **通过** |
| OBS-02 | `.tool-cell` 引用 `--cell-w/--cell-h`；C 宫格 head/foot 30/22、gap 4、padding 4，外框 420×360 不变 | R30 实测 9 格 112×96、`#c-grid` 无溢出；原脚本 E08 关键尺寸仍 PASS | **通过** |
| OBS-03 | `doUninstall` 清理 trigger / pinList / hotkeys / grants / revoked / pinned | R31 实测 9 项全清；原脚本 P10 / P11 仍 PASS | **通过** |
| OBS-04 | `TRANSITIONS.HIDDEN` 补 `TOOLPAGE` | R05 / R32 / 原脚本 K15 `forced=0` | **通过** |

工程师说明的两点经复核成立：① PRD 5.7 的 `48/36` 是 B 面板 head/foot 规格，未规定 C 宫格 head/foot，外框 `420×360` 未变；② 原型权限 ID 为 `screen.capture`（PRD 4.3 六类之一），`CAPABILITY_PERMS.screenshot` 作为能力别名正确映射，取色能力同样受网关约束（R20、R28 实测）。

### 10.5 回归新发现（非阻断）

| 编号 | 现象 | 影响评估 | 处置 |
|---|---|---|---|
| **OBS-06 / P3** | 仅撤销 / 拒绝插件某一项权限时，`executeResult` case `'plugin'` 按**全部声明权限**做全量校验，会连带拦截该插件的「通用唤起入口」。R21 实测：仅撤销 `clipboard.read` → 通用入口被拦截，但 `guardCapabilityPeek('translate')=true`，触发词 `fy` 仍正常进入翻译 | 与授权弹窗文案「拒绝某项不影响插件其他功能」表述不一致；但 PRD 6.5 只要求「受限能力被拦截」，当前属更严格实现，不构成需求违背；插件结果项未携带 `capability` 字段，属原型建模粒度限制 | **不阻断交付**。建议 V1 给结果项附加 `capability` 并改为按能力校验，或同步修订弹窗文案；按轮次约束本次不再开修复轮次 |

**处置状态（与工程师对齐后补充）**：工程师已就 OBS-06 作出工程判断——**本轮不改文件，当前指纹仍为 `208,228 bytes / md5 1fc47e74727696c2fa21a9c7a104d293`**；并把两个选项提交 `team-lead` 定方向：A = 给插件结果项加 `capability` 并按能力校验；B = 把 `openAuthModal`（约 L2523）文案改为「拒绝某项将限制该插件的对应能力」。理由：更严格实现不违背 PRD、根因是建模粒度而非网关判定错误、为一条 P3 表述改文件会让 140 项结果与报告指纹全部过期。

QA 对「选项 B 零逻辑回归风险」这一说法做了独立复核，结论成立：

| 脚本 | 与该文案 / 授权弹窗的耦合 | 选项 B 影响 |
|---|---|---|
| `tests/prototype-smoke.js` | P02 只断言 `auth-deny` / `auth-once` / `auth-allow` 三个 `data-act` 按钮存在（L714–L717），**不断言弹窗文案** | 无 |
| `tests/fix-regression.js` | 36 条断言中**无任何**依赖「拒绝某项…」字面量；R21 为纯诊断用例（不含 `OK` / `EQ`，只记录实际行为）；R33 仅用 `#modal-layer [data-perm]` 取勾选框 | 无 |
| `tests/parity-supplement.js` | 全文 0 处 `modal-layer` 引用，快照对象为 `#view-a`~`#view-f`，授权弹窗不在快照范围；且两端同改 | 无 |

**若主理人裁定修订**：选项 A 会改动 `executeResult` 执行链，需重跑 `prototype-smoke.js`（尤其 R13 / R22 对应的 P08）与 `fix-regression.js` 的 R13 / R22；选项 B 仅改文案，三条脚本逻辑均无回归，但**任一改动都会产生新 md5**，届时须由 QA 重跑 85 + 36 + 19 并更新本报告第 10.1 节基线表与 `tests/fix-regression-results.json` 的 `sourceMd5`。在收到决定前，报告与结论保持不变。

### 10.6 测试脚本自身缺陷（TEST_BUG，自行修复）

定向回归首跑 33/36，3 个失败均为测试脚本缺陷，非产品缺陷，QA 已自行修正后复跑 36/36：

| 用例 | 根因 | 修正 |
|---|---|---|
| R15 / R33 | 浏览器表达式返回 `Promise`，却被 `JSON.stringify` 包裹 → 序列化成 `{}`，字段全为 `undefined` | 改用 `ex()` + CDP `awaitPromise:true` 直接取 resolved value |
| R17 | 断言把「grant 被删除」写成期望 `undefined`，而表达式用 `|| null` 归一 | 断言改为 `null` |

### 10.7 最终判定

> **路由 `NOONE` · 140 项断言全部通过 · 原型可交付。**

DEF-01（P1）、DEF-02（P3）与 OBS-01～04 均已修复并独立验证；双端 PRD 6.6 白名单外差异 0；零外部请求、零未捕获异常。当前仅余 OBS-06（非阻断 P3 观察）与第 7 节 V1 级未覆盖项（像素级视觉回归、性能指标、持久化、原生系统级行为、插件真实运行时隔离、多分辨率 / 深色主题）。QA 未修改 `prototype/index.html`。

复跑命令：
```bash
node deskkit/tests/prototype-smoke.js
node deskkit/tests/fix-regression.js
node deskkit/tests/parity-supplement.js
```

---

## 附录：交付物索引

| 路径 | 内容 |
|---|---|
| `tests/prototype-smoke.js` | 85 条用例的独立验收脚本（CommonJS，零依赖，可直接 `node` 运行）；修复后复跑 85/85 |
| `tests/fix-regression.js` | 第 10 节定向回归脚本：36 条断言覆盖权限网关 / 授权语义 / 三条执行链 / OBS 落地 / 双端（零依赖，退出码即结论） |
| `tests/parity-supplement.js` | 19 个界面状态 Win/mac 结构 + 文案比对（零依赖，退出码即结论） |
| `tests/results.json` | 85 条逐用例结构化结果（修复后为全通过版本） |
| `tests/fix-regression-results.json` | 36 条定向回归逐用例结果（含被测源码 MD5） |
| `tests/platform-refs.json` | 38 处平台引用点逐条明细 |
| `tests/screenshots/*.png` | 10 张真实 Chrome 截图（A1 / A3 / A4 / 触发词 / 工具内页 / 断网市场 / 超时降级 / macOS / 关于页 / 授权弹窗） |
| `docs/03-QA-Report.md` | 本报告 |
