# 08 原型与 QA 基线迁移影响评估（QA 独立 · 只读）

> **阅读注意：** 本文评估的是 `docs/07` 的 Rust + Win32 方案。Windows 现为 C# / .NET 8 + WPF，见 [`docs/10-Current-Architecture.md`](../10-Current-Architecture.md)。「零 WebView、不复用 HTML 原型测试」仍然成立；文中的 Rust Core / C ABI 不再是现行实现。

> 文档版本：**v1.4**（v1.0 定稿 → v1.1 按 `team-lead` 任务 #9 补充约束与架构师 #8 §8 影响清单增量对齐 → v1.2 同步架构师 #10 定稿状态与 `degradationSteps` 字段 → v1.3 按用户第二轮拍板 D1/D2 同步 §7 披露分档判定 → v1.4 同步架构师 #11 定稿；五次均为增量，**未重写全文、未改变任何分类数量与结论**）｜ 撰写：严过关（Yan）· QA 工程师 ｜ 团队：software-deskkit
> 上游：`docs/06-Incremental-PRD.md`（增量 PRD，319 行）、`docs/01-PRD.md`、`docs/02-Architecture.md`、`docs/03-QA-Report.md`、`docs/07-New-Architecture.md`（架构师 #8 产出、**#10 与 #11 已先后按两轮用户拍板修订定稿**；本评估只读引用其 §8 口径与章节结论，**不锁定其行数/md5**）、`prototype/index.html`、`tests/` 三个脚本与结果
> **性质**：独立、只读的 **QA 资产迁移影响评估**。只判断「旧原型与 140 条断言里哪些能带过去」，供 `team-lead` 并入 Cursor 交接包改写方案。
> **边界**：仅新建本文件。**未修改** `prototype/index.html`、`tests/prototype-smoke.js`、`tests/fix-regression.js`、`tests/parity-supplement.js`、`tests/results.json`、`tests/fix-regression-results.json`、`tests/platform-refs.json`、`tests/screenshots/*.png`、`docs/03-QA-Report.md`；`docs/07-New-Architecture.md` 只读引用、未触碰；未创建任何 `prototype-v2/` / `tests-v2/` 文件。**不含技术选型结论，不含 NFR-03 性能测量方法论 / 采样脚本**（归架构师 `docs/07 §5`）；§7 仅写 **QA 侧对披露分档结果的判定与归档**。
> 核实日期：2026-11-16（v1.1 / v1.2 / v1.3 / v1.4 增量对齐同日）
> **v1.1 变更点**：① §7 性能项按用户 P2 改为「双阈值 + 触发条件 + QA 判定/归档」；② 全文与架构师 #8 §8 八项口径对齐（新增 §1.4），消除「改造 = 改冻结文件」「重拍 = 删原图」「脚本改造 = 改冻结脚本」三处表述冲突；③ §5.3 按「零 WebView / 原生 UI 无 DOM」改为归一化布局 JSON + 截图 diff（G5'）；④ 修正 §3.4 `handleAct` case 分布为 27/8/74；⑤ QA 报告 V2 编号统一为 `docs/09-QA-Report-V2.md`。
> **v1.2 补订点（架构师 #10 定稿同步，纯状态/字段对齐）**：⑥ 将 v1.1 中 4 处把架构师任务 #10 描述为未完成的时态全部更新为「**#10 已 completed、`docs/07` 已定稿**」（架构师回执：`wc -l` 1033 行 / 113,019 bytes / md5 `a67fe6e9137ecc8e01a6536d84a70668`；本评估仍**不锁定**该指纹，仅引用章节口径）；⑦ §1.4 补记 `docs/07` 单阈值表述已在 #10 全文清零、旧编号 `08-QA-Report-V2.md` 4 处已改 09、§8.1 已登记本评估文档；⑧ §7 NFR-03 归档字段补齐架构 §5.4 新增的 `degradationSteps`。**140 条断言四类数量 74/34/32 + 需新增 24、双端口径、10 张截图判定、最大风险点均未变**。
> **v1.3 变更点（用户第二轮拍板 D1 / D2 同步）**：⑨ **D1 彻底删除** R1-B e) 中要求 V1 阶段把实测值重新压到 ≤80MB、复原默认阈值、否则不予发布的整条条款，**且不降级保留为建议项**；⑩ **D2 取消一切以内存数值为条件的发布阻断出口**——实测值无论多高都如实记录归档并放行 W0，`>150MB` 改为「仍放行 W0、升级披露」，只靠发布说明与「关于」页披露；150MB 从发布硬线改为**披露分档线**；⑪ §7 G 组判定结论由「PASS / 失败 / 阻断三态」改为 **PASS（默认线）/ PASS（备案线）/ PASS（超线·升级披露）三档全放行** + 一个正交的「**归档无效 · 退回补正**」（仅凭证 / 哈希 / 字段 / 披露缺失时触发，不影响 W0 放行）；⑫ 归档字段补 `disclosureLevel`，实测值如实归档、不裁剪；⑬ §0 / §1.4 / §8.1 同步清理旧的阻断类与复原类措辞。**140 条断言四类数量 74/34/32 + 需新增 24、附录 A 的 9 条指纹、附录 B 只读命令、双端口径、10 张截图判定、§3 原型改造清单、最大风险点均未变。**
> **v1.4 补订点（架构师 #11 定稿同步，纯时态 / 状态对齐）**：⑭ 将 v1.3 中把架构师任务 #11 描述为未完成的时态更新为「**#11 已 completed、`docs/07` 已定稿**」，并在 §1.4 补记架构师回执的 4 点定稿口径（D2 取消发布阻断、G-NFR03 改称「实测 · 归档 · 披露闸门（非阻断）」、`disclosureLevel: standard|escalated` 落点、**R1-B 细则由 a–e 收为 a–d**）；⑮ 同步头部上游标注与 §8.1 边界表的版本自称。**纯状态同步，§7 三档判定、§2 分类、附录 A/B、双端口径、10 张截图判定、最大风险点全部未变；#12 已清零的 10 词不得重新引入。**

---

## 0. 结论先行

| # | 结论 |
|---|---|
| 1 | **140 条断言的处置**：保留可复用 **74** / 需改写 **34** / 必须删除 **32**（合计 140）；另**需新增 24 条**（不属于 140，是迁移后必须补齐的新断言）。四类数量小计见 §2.5。 |
| 2 | `fix-regression.js` 36 条中 **15 条必须删除、8 条需改写、13 条仍可复用**。可复用的是「与插件无关的静态/视觉/状态机」部分：R03/R04/R05/R06、R07–R10 Toast 三色 `getComputedStyle`、R29 分组第 4 色 `#9B6CF6`、R30 工具宫格 112×96、R32 `HIDDEN→TOOLPAGE` 合法迁移、R34 Esc 两条回退链、R36 零异常零外链；**三条执行链的纯内置工具部分**（R23–R28）判为「需改写」而非删除，把「被拦截」断言翻转为「正常执行成功」断言。 |
| 3 | `prototype/index.html` 现有 **103 个字面 `data-act`** 中：插件链 **23 个必须删除**、**8 个改义/迁移**、**72 个原样保留**；另有 **9 个动态生成的 action**（不在 103 字面集里）出现在分发逻辑中，其中 **5 个必须删除**（合计删除 28 个 action 名）。`handleAct` **109 个 case** 中 **27 个删除、8 个改义、74 个保留**（`perm-` 是字符串拼接前缀不是 case；`trigger-edit` / `f-preset` 由 `handleAct` 外的独立 `if` 分支处理，**三者**造成 action 名与 case 数的差额：`103 − 3 + 9 = 109`）。名单见 §3.4。 |
| 4 | 冻结基线双口径实测一致：Node 解码读原始 HTML = **208,228 bytes / md5 `1fc47e74727696c2fa21a9c7a104d293`**；物理 shell 因 `%TSD-Header-###%` 包装头 = **212,992 bytes / md5 `bee380603a586281c1745bb53a866c63`**。同一文件两层口径，**140 项断言全通过、`NOONE` / 可交付**的结论没有被本任务改动。**现在不要动文件**；新基线重建节点与口径见 §4。 |
| 5 | `docs/03-QA-Report.md` 判定：**冻结归档为历史版本，不改写**。`tests/platform-refs.json` 的「38 处平台引用 / 7 处等值分支 / 越界 0」**对冻结基线仍是有效历史事实，但对新方向不再具有验收效力**，须重建后重新证明（§6）。 |
| 6 | **最大风险点 ①（已由架构定案确认为必现）**：74 条「保留可复用」是**意图层**保留，其中 S01–S09、V01–V03、R03/R04/R06、X01/X02 等约 20 条是对「单文件 HTML 原型 + CDP `Fetch.fulfillRequest` 注入 + `:root` CSS token」的**实现级**断言。架构 #8 已定「共享 Rust Core + 各端原生 UI、**零 WebView**」，这些断言的静态扫描方式、DOM 选择器、注入机制**确定失效**，必须按新产物（归一化布局 JSON / 构建产物哈希 / 主题资源清单）重写，不能零改动复制；`tests/screenshots/` 的 Chrome CDP 采集方式同样不再适用（§5.3）。 |
| 7 | **最大风险点 ②**：新方向允许「阶段性差异清单」，但 4 类结构仍要求任何时刻 100% 对齐；直接沿用 D05 / R35 / parity 的「白名单外差异 0」会**把合法阶段性差异误判为缺陷，或把越界差异藏进清单放行**。验收逻辑必须加「清单内差异掩码 + 类别/到期日校验」，这是本轮最容易被低估的改写点。 |
| 8 | **与架构 #8 §8 影响清单逐条对齐后无冲突**（详见 §1.4）：冻结基线不重跑、`prototype/index.html` 与三个脚本/结果文件/10 张截图全部冻结保留、`docs/03-QA-Report.md` 冻结归档、OBS-06 backlog 作废、新方向基线隔离另建 `prototype-v2/` + `tests-v2/` + `docs/09-QA-Report-V2.md`。§3「改造清单」是**面向新 v2 产物的语义迁移映射**，不是对冻结文件的编辑指令。NFR-03 按用户 P2 + 第二轮拍板采用**披露分档**（80MB 默认目标 / 150MB 披露分档线），**三档结果全部放行 W0**，超线仅升级披露；QA 侧只判定与归档，不写测量方法论（§7）。 |

---

## 1. 评估范围、依据与方法

### 1.1 与 QA 资产直接相关的 8 项方向变更（据 `docs/06-Incremental-PRD.md`）

| # | 变更 | 出处 | 对 QA 资产的影响面 |
|---|---|---|---|
| 1 | D 插件市场 / 已装管理整页删除，含 5 类弹层；「触发词冲突」提示移入设置-快捷键 | §5.1 | `view-d`、`renderD()`、`openView('D')`、V04/V07/M04/SHOT/D 系 action 与 3 个 parity 状态 |
| 2 | E 设置 4 Tab → 3 Tab；「关于」新增「双端功能对齐状态」「阶段性差异清单」 | §5.1 / §4.3 / §7 | `e-tab` 取值、`renderE()` 分支、E06、E-about 快照、关于页文案与版本号断言 |
| 3 | 结果分组 6 → 4 类，删「插件」「计算」独立组（计算并入内置工具） | §5.1 / §2.1 DK-P0-02 | `buildResults()` 的 `order`/`map`、Q02、A-A3 快照 |
| 4 | 触发词 6 → 5 个（`ocr` 删除），全部改指内置工具，改称「工具专属态」 | §5.1 / §2.1 DK-P0-12 | `TRIGGER_DEFS`、Q03、V06、T10、A-TRIGGER 快照、E06 |
| 5 | 空态 6 → 3 类；A4 三出口删「在插件市场搜索」 | §5.1 / §5.2 | `EMPTY_STATES`、E01/E02/V07/Q04、F-gallery 快照、3 张截图 |
| 6 | 键盘：删「搜索框敲插件名回车即安装」；`Ctrl/⌘+K` 菜单删插件项；其余 12 项保留 | §5.2 | K08、`openActionMenuAt()`；**原型中并不存在「敲插件名即安装」入口**（见 §3.2），需补负向断言 |
| 7 | C 宫格「＋ 添加」改义为「自定义宫格排序 / 显示隐藏内置工具」，二维码占位保留 | §5.1 / §3.1 | `tool-add` / `openToolAddModal()` / `tool-pin-plugin`、R31 可迁移部分 |
| 8 | F Step3 保留平台系统权限清单、删预置/示例插件选择、保留预置 5 组工具包 | §5.1 / §2.1 DK-P0-16 | `renderF()` step3、`f-preset`；**原型 Step3 已无插件预置项**（见 §3.3） |

**不变项**（`team-lead` 给定，作为「保留可复用」判据）：A 四态、Esc 五级回退栈、`TRANSITIONS` / `STATE_DEPTH`、B 分组拖拽与 8 项右键菜单、C 3×3 宫格 112×96 与 6 个工具内页、PRD 5.7 尺寸表 / 5.8 配色 token（含分组第 4 色 `#9B6CF6`）、13 项平台差异白名单。

### 1.2 冻结基线实测（本任务只做只读校验）

| 口径 | 字节 | MD5 | 说明 |
|---|---:|---|---|
| Node `fs.readFileSync` 解码后原始 HTML | 208,228 | `1fc47e74727696c2fa21a9c7a104d293` | QA 报告、两份 results.json、三个脚本的验收对象；Chrome CDP `Fetch.fulfillRequest` 注入的正是这份内容 |
| 物理 shell（`stat -f%z` / `md5 -q`） | 212,992 | `bee380603a586281c1745bb53a866c63` | 工作区 `%TSD-Header-###%` 包装头所致，**不是源码内容**，仅用于交付物完整性核对 |

`JS .length=189,504` / 码点 `189,499`（差 5 为 emoji surrogate pair）、LF 计数 3,241、内嵌 `<script>` 143,885 chars。两份结果文件当前均为全通过：`tests/results.json` 85/85、`tests/fix-regression-results.json` 36/36 且 `sourceMd5=1fc47e…`；`tests/parity-supplement.js` 19/19 白名单外差异 0。**合计 140/140，结论 `NOONE` / 可交付，未被本任务改动。**

### 1.3 方法

静态提取（Node 只读扫描源码与三个脚本）＋ 对既有结果文件的只读复核；**本任务不编写、不运行任何新用例，不重跑三条脚本**（那是原型改造后的回归工作）。所有分类均给出用例编号 / 函数名 / 行号与一句可核查理由。评估前后对 9 个冻结文件做了 md5 对比（附录 A），确认零改动。v1.1 增量对齐阶段再次复算 9 个文件 md5，仍全部一致（含 `docs/07` 只读引用未写入）。

### 1.4 与架构师 #8 §8「既有交付物影响清单」及用户 P1 / P2 的口径对齐（v1.1 增量）

> 架构师 2026-11-16 产出 `docs/07-New-Architecture.md`（**仅新建该文件、未改任何既有文件**），并同步 §8 的 8 条口径；`team-lead` 同日同步用户 P1（轻量优先，B 方案不变）、P2（80/150 披露分档）。逐条核对如下，**本评估结论与架构侧无冲突**；3 处初稿措辞已按对齐结果收敛。**决策顺序以最后一轮用户拍板为准**（#8 → P2/#10 → 第二轮 D1/D2/#11），见下方 v1.3 注。
>
> **注（v1.2 状态快照，已被第二轮拍板取代）**：架构师任务 #10「按用户拍板修订架构文档并统一 80MB 门槛口径」**已 completed**。QA 侧当时复核其回执：① 单阈值措辞全文清零；② 新增 §0.7 拍板记录与独立 **R1-B 验收放宽备案**（当时口径为 `(80MB,150MB]` 放行 W0、`>150MB` 走最终兜底出口；**只调验收阈值、不改技术栈、不重选型**）；③ 旧编号 `docs/08-QA-Report-V2.md` 4 处（§8.1 / §8.3 / §9 T05 / §12）已全部改为 `docs/09-QA-Report-V2.md`，残留 0；④ §8.1 影响清单已补一行登记 `docs/08-QA-Migration-Assessment.md`（新建 / QA 迁移评估 / 保留 / 不重跑旧基线）。本评估**仍不锁定 `docs/07` 的行数 / md5**（「1034 行」为其脚本 `count("\n")+1` 的计数口径差异，非矛盾），只引用章节口径与用户拍板。
>
> **注（v1.3 · 用户第二轮拍板 D1 / D2，最终口径）**：用户拍板 ① **D1 删除** R1-B e) 中「V1 阶段重新压到 ≤80MB、复原默认阈值、否则不予发布」的整条机制（不降级保留为建议项）；② **D2 无论多高都放行**，取消以内存数值为条件的发布阻断出口，实测值无论多高都如实记录归档并放行 W0，只靠发布说明与「关于」页披露。架构师 **#11 已 completed、`docs/07` 已按此定稿**（**本评估全程未写入 07**），G-NFR03 改为「**实测 · 归档 · 披露闸门（非阻断）**」。故本评估 §7 以「**披露分档 + 三档全放行**」为准：80MB 默认目标 / 150MB 披露分档线 / `>150MB` 仍放行 W0 并升级披露；#10 版本中的兜底与复原条款在 #11 后失效，不再作为判据。为避免文档随上游修订过期，本评估不登记 07 的行数 / md5。
>
> **注（v1.4 · 架构师 #11 定稿回执，QA 逐点核对一致）**：① **D2** `P95 > 150MB` 不再作为发布阻断出口，实测值无论多高均记录归档并放行 W0，只靠发布说明与「关于」页披露；150MB 定稿为**披露分档线**、`P95 ≤ 80MB` 仍为默认目标——与 §7 口径段一致。② **G-NFR03** 断言不再是内存数值判失败，而是「T02 实测报告存在 + 六档降级记录齐全 + `buildSha / contractHash` 绑定 + `measuredP95Mb` / `thresholdUsed` / `fallbackApplied` / `degradationSteps` / `disclosureLevel` 字段齐全 + 发布说明与『关于』页披露文案已生成」，凭证缺失判「归档无效、退回补正」——与本评估 §7 G 组三行逐字段一致。③ **`disclosureLevel: standard|escalated`**：`>150MB` 取 `escalated`，披露文案额外写明「实测 X MB 已超出预定义备案线 150MB」，实测值如实不裁剪；07 落点为 §0.6 R1-B d) / §0.7 / §5.4 / §7.2 / §10.1 Q5 —— 与 §7 归档行字段名及取值一致。④ **R1-B 细则由 a–e 收为 a–d**（D1 删除末条团队治理条款）、§0.7 新增 D1/D2 两行与守门句（两轮拍板均未触发 R1、选型层不放宽）；07 全文旧规则性表述已清零。**分工不变**：采样窗口 / `P95` 统计 / 测量脚本 / 六档降级预案仍归 `docs/07 §5`，本评估只写 QA 侧判定与归档。架构师回执 `docs/07` 为 `wc -l` 1040 行 / 120,681 bytes / md5 `58df8254e44fe793bb35203bc2756d0f`（`count("\n")+1 = 1041` 为计数口径差异、非矛盾）；**本评估仍不锁定该指纹**，只引用章节口径。

| 架构 §8 条目 | 架构侧口径 | 本评估对应结论 | 对齐判定 |
|---|---|---|---|
| §8.1 / §8.3-1 冻结基线 | 140 项断言结果有效；**不发起重跑、不更新基线**；双口径 208,228 / `1fc47e…` 与 212,992 / `bee380…` | §1.2、§0-4、§4.1、附录 A 完全同值；本任务定性为「**只读评估**」，未运行任何用例、未重跑三条脚本（§1.3、§8.1） | ✅ 一致 |
| §8.3-2 原型 | **保留（冻结，历史参考产物）**，不修改、不删、不重跑；语义与新方向不一致 | §3 标题补注「清单是**面向 v2 的语义迁移映射，不是对冻结文件的编辑指令**」；§4.2「现在不要动文件 / 禁止覆盖冻结文件」 | ✅ 一致（措辞收敛） |
| §8.3-3 三个脚本 + 三结果文件 | **保留为历史基线、不重跑、不改**；与旧 DOM id / `data-act` / `store.platform` 强绑定，改了等于同时改动被测对象与判据 | §2 的「需改写 / 必须删除」指**迁移到新 `tests-v2/` 套件时**对断言的处置，不是回写冻结脚本；§4.2 明确新结果写入**新文件** | ✅ 一致（措辞收敛） |
| §8.3-4 10 张截图 | 全部保留不删不改；`06-market-offline` / `07-timeout-degraded` / `10-plugin-auth` 插件场景**语义作废但仅作历史截图保留** | §5 的「必须重拍」= 在**新 `tests-v2/` 截图集**另拍替代状态，**冻结原 10 张不删不覆盖**；已删除「删原图」措辞 | ✅ 一致（措辞收敛） |
| §8.3-5 03 报告 / OBS-06 | 报告**保留为历史基线、不重跑**；**OBS-06 backlog 作废**，记录不删不改，新 backlog 不承接 | §3.5 已判「OBS-06 随插件运行时整体作废，不做修复、不保留观察项」，R21 必须删除；§6.1 报告冻结归档不改写 | ✅ 一致 |
| §8.3-6 旧 §4.4.1 A1–A4 | 静态扫描基线保留但**不再作为新方向断言**；精神迁移为契约驱动 **G4'**（平台引用点 ⊆ `contracts/platform-diffs.json` 白名单、i18n 外零硬编码） | §6.2 判 `platform-refs.json` 的 38/7/0「对冻结基线有效、对新方向无验收效力，须重建后重新证明」；§7 的双端矩阵改为 4 类结构 + 白名单 + 清单掩码 | ✅ 一致 |
| §8.3-7 新基线 | **完全隔离另建** `prototype-v2/` + `tests-v2/` + QA 报告 V2；含 `tests-v2/perf/rss-{win,mac}.json` 首次内存实测、`tests-v2/permission-guide/` 未授权引导断言、双端归一化夹具与截图 diff 基线；**架构侧只给建议、未创建任何 v2 文件** | §4.2「禁止覆盖冻结文件 / 新结果写入新文件」、§5.3 截图 diff 基线、§7 验收矩阵；**本评估同样只给建议、未创建任何 v2 文件** | ✅ 一致 |
| §8.3-8 Cursor 交接包 | **新建 v2 文件**而非重写既有四份，避免破坏双口径可核对性 | §6.1「冻结归档为历史版本，不改写」、§8.1 边界表 | ✅ 一致 |
| 编号同步（`team-lead` / 架构 #10） | 架构 #8 原建议 `docs/08-QA-Report-V2.md`；**#10 已把 4 处旧编号全改为 `docs/09-QA-Report-V2.md`，残留 0** | `docs/08` 已被本评估占用，QA 报告 V2 统一为 **`docs/09-QA-Report-V2.md`**；本评估文件名不变。架构 §8.1 已补一行登记 `docs/08-QA-Migration-Assessment.md`（新建 / QA 迁移评估 / 保留 / 不重跑旧基线） | ✅ 双方编号已一致、无撞号 |

**与 QA 直接相关的三条架构闸门（只写「QA 侧如何判定与归档」，不写测量方法论）**：

| 闸门 | 架构侧定义 | 本评估 QA 侧落点（不越界） |
|---|---|---|
| **G-NFR03**（v1.3） | 空闲 RSS p95；报告绑定 `buildSha + contractHash`；第二轮拍板后为「**实测 · 归档 · 披露闸门（非阻断）**」，80MB 默认目标 / 150MB 披露分档线，三档全放行 W0 | 按**用户 P2 + D1/D2**改写：§7 工程闸门写「QA 侧如何按 `thresholdUsed` / `fallbackApplied` / `measuredP95Mb` / `degradationSteps` / `disclosureLevel` 判定三档结果并归档」；超线档校验披露文案；**采样与测量方法归 `docs/07 §5`，本评估不写** |
| **G5'** | 原生 UI 无 DOM，改为**归一化布局 JSON 逐字段比对 + 截图 diff**，仅白名单 #11 材质允许差异 | §5.3 的 V1 逐像素 diff 基线落地方式改为「布局 JSON + 截图 diff」；§0-6 风险① 与 §8.2 补记「Chrome CDP / DOM 选择器采集链不再适用」 |
| **G9** | 契约哈希不一致拒绝启动 | §7 工程闸门增加「`buildSha + contractHash` 绑定校验」归档项；不定义契约内容 |

> **口径优先级说明（v1.3 收口）**：决策顺序为「#8 单阈值定稿 → 用户 P2 双阈值 + 预定义放宽条件（#10 落地）→ 用户第二轮 D1/D2（#11 落地）」，**以最后一轮用户拍板为准**。本评估 §7 与架构师 #11 修订后的 `docs/07` 同为「披露分档 + 三档全放行 W0」：均不含以内存数值为条件的发布阻断出口，也不含 V1 阶段阈值复原要求；两份文档在「实测报告绑定 `buildSha + contractHash`」以及「分档只调披露口径、不切换技术栈、不重选型、R1 未触发、`<80MB` 仍是选 B 否决 WPF 的硬判据」上完全一致。前两轮的兜底/放宽/复原类措辞属历史状态，已按 §8.1 检索清单逐词清零。

---

## 2. 140 条断言逐条盘点

### 2.1 分类口径（先定义，避免歧义）

| 归类 | 定义 |
|---|---|
| **保留可复用** | 断言意图与实现绑定在新方向下都不变，可原样带入新套件（允许因文件重命名而改路径，但期望值、选择器、setup 不变） |
| **需改写** | 断言意图仍成立，但期望值 / 选择器 / setup / 状态集合必须改（如 6 套→5 套、6 触发词→5、6 空态→3、分组 6→4、Tab 4→3） |
| **必须删除** | 被测对象在新方向下不存在（插件运行时、D 页面、插件类演示态、权限网关），断言无法迁移 |
| **需新增** | 新方向引入、旧套件完全没有的能力/负向断言；**不属于 140**，是迁移后必须补齐的缺口 |

> 说明：前三类是对 **140 条现有断言**的完备划分（74+34+32=140）；第四类「需新增」是对**新套件**的补充清单，数量为 24，故四类数字不相加为 140。此口径在 §2.5 与汇报中保持一致。

### 2.2 `tests/prototype-smoke.js` · 85 条

| 编号 | 用例（简称） | 归类 | 一句理由 |
|---|---|---|---|
| S01 | 单文件无 `<link>` / `<script src>` / `<iframe src>` | 保留可复用 | 单文件离线原型的硬约束不变（§1.1 不变项） |
| S02 | 零网络：无 `http(s)://` 字面量 | 保留可复用 | NFR-04 离线可用不变，且 A4「用浏览器搜索」走系统浏览器不引入字面量 |
| S03 | 无 `fetch(` / `XMLHttpRequest` / `WebSocket` / `EventSource` / `importScripts` | 保留可复用 | 零网络请求仍是硬约束 |
| S04 | CSS 无 `@import` / `url(` 外部资源 | 保留可复用 | 同上，与插件无关 |
| S05 | `:root` 之外无硬编码 hex / rgb/rgba | 保留可复用 | 架构 §10.1「语义变量唯一色源」不变 |
| S06 | PRD 5.7 的 24 个尺寸 token 齐全且值精确 | 保留可复用 | 5.7 尺寸表明确不变（§1.1 不变项） |
| S07 | PRD 5.8 的 15 语义色 + 4 状态色齐全精确 | 保留可复用 | 5.8 配色 token 明确不变 |
| S08 | 分组 5 色均引用语义变量 | 保留可复用 | 含分组第 4 色 `#9B6CF6`，明确不变 |
| S09 | 内嵌脚本 `node --check` 语法通过 | 保留可复用 | 原型仍是单文件 HTML 时的语法闸门 |
| S10 | `data-act` 全覆盖：每字面值有分发分支 | **需改写** | 「提取→比对」逻辑可复用，但期望集合从 103 字面值 / 109 case 变为删除 28 个插件链 action 后的新集合 |
| S11 | 显式状态机 7 态 + Esc 回退栈存在 | 保留可复用 | `TRANSITIONS` / `STATE_DEPTH` / Esc 五级回退栈明确不变 |
| S12 | 平台等值分支仅落在白名单函数集合内 | **需改写** | 白名单函数数组需纳入「阶段性差异清单」的数据渲染函数；7 处等值分支所在函数集合会随 D 删除、关于页新增而变化 |
| S13 | `render*` / `openModal` 文案无平台权限名硬编码 | 保留可复用 | 系统权限仍由 `PLATFORM_PERMS[store.platform]` 数据驱动（NFR-06 改写后依然成立） |
| V01 | `file://` 加载成功，store 初始化 A1 | 保留可复用 | 断言字段为 `view/state/visible/platform/menu/modal/toolmode/activeTool/raw/trigger/payload/flat/sel/historyForced`，不含将被删的 `plugins`/`market`/`grants` |
| V02 | 页面无未捕获 JS 异常 / console error | 保留可复用 | 通用运行时闸门 |
| V03 | 浏览器实际请求只有主文档 | 保留可复用 | 零外部请求硬约束 |
| V04 | 六套界面逐一可打开、可返回 A | **需改写** | `['A','B','C','D','E','F']` → `['A','B','C','E','F']`，D 整页删除 |
| V05 | A1/A2/A3/A4 四态互斥且 state 正确 | 保留可复用 | A 四态明确不变 |
| V06 | TRIGGER 与 TOOLPAGE 可触发且互斥 | **需改写** | 状态机与 `#a-capsule-slot .capsule` / `.excl-wrap` / `view-c.page` 选择器不变，但「插件专属模式」改称「工具专属态」，且触发词集合缩为 5 |
| V07 | A4 三个出口可见可点且各有效 | **需改写** | 当前断言含 `empty-market` 且期望 `view==='D'`；新三出口为「用浏览器搜索 / 清空重来 / 查看全部工具」 |
| K01 | `Alt+Space` / `⌥+Space` 被 `preventDefault` | 保留可复用 | 12 项保留映射之一 |
| K02 | 真实键盘 Alt+Space 切换且不插入空格 | 保留可复用 | 同上 |
| K03 | mac `⌥+Space` 不产生 NBSP 副作用 | 保留可复用 | 同上，且 NFR-06 系统权限引导仍关注此项 |
| K04 | 备用 `Ctrl/⌘+Shift+Space` 呼出并 `preventDefault` | 保留可复用 | 同上 |
| K05 | `↑↓` 与 `Ctrl/⌘+N/P` 移动选中 | 保留可复用 | 同上 |
| K06 | `Enter` 执行 + Toast；`Shift+Enter` 副操作 | 保留可复用 | 同上；新方向需另加「敲文本不得触发安装」负向断言（N12） |
| K07 | `Tab` 补全触发词 / 结果标题 / 切换 A↔C | 保留可复用 | 同上；`tabComplete()` 依赖 `store.triggers`，5 个词仍成立 |
| K08 | `⌘K` 操作菜单 / `⌘,` / `⌘T` / `⇧⌘V` | **需改写** | 4 条快捷键响应仍成立，但需增设「菜单不含卸载 / 权限 / 插件详情」断言（N11） |
| K09 | 数字键 1–9 直达结果 / 宫格工具 | 保留可复用 | C 3×3 宫格不变 |
| K10 | `F1` / `?` 快捷键速查，输入框内 `?` 不抢占 | 保留可复用 | 同上 |
| K11 | 普通字符输入不被全局快捷键劫持 | 保留可复用 | 同上 |
| K12 | Esc 链① `TRIGGER → A4 → A1 → HIDDEN` | 保留可复用 | Esc 五级回退栈明确不变 |
| K13 | Esc 链② `TOOLPAGE → A1 → HIDDEN` | 保留可复用 | 同上 |
| K14 | Esc 栈为空再按 Esc 不崩溃；快速连按稳定 | 保留可复用 | 同上 |
| K15 | 状态机无非法迁移（forced=0） | 保留可复用 | `TRANSITIONS` 不变，`HIDDEN→TOOLPAGE` 合法迁移已落地 |
| Q01 | 拼音/首字母命中 `wx / kzmb / word / fy` | 保留可复用 | 搜索与拼音匹配不变；`fy→翻译` 从插件结果改为内置工具结果，命中关系不变 |
| Q02 | `123*4` 产生计算结果行 = 492 | **需改写** | 断言现依赖 `flat.filter(type==='calc')` 独立结果类型；新方向「计算并入内置工具」，应改为「结果归入内置工具组且含 492」 |
| Q03 | 六个触发词 `fy/js/color/ts/clip/ocr` 胶囊化 | **需改写** | `words` 数组删 `ocr` → 5 个，且需断言 `target` 全部指向内置工具（N04） |
| Q04 | 无匹配进 A4、三出口可点且文案完整 | **需改写** | 出口数仍为 3 但文案与 action 全变（N05） |
| Q05 | 匹配字符高亮 `<mark>` 且首行选中 | 保留可复用 | A3 渲染不变 |
| Q06 | 空输入 / >500 字符不崩溃 | 保留可复用 | 边界用例与插件无关 |
| Q07 | XSS 注入被 `esc()` 转义、无元素注入 | 保留可复用 | 同上；新增「阶段性差异清单」文案后仍应转义 |
| Q08 | 连续切换平台与视图 24 次无异常 | **需改写** | setup 中 `vs=['A','B','C','D','E','F']` 含 D；须改为 5 视图（新数组与 `i%5`） |
| T01 | 计算器 `123*4=492` / `(1+2)*3=9` | 保留可复用 | 5 组内置工具能力边界不变 |
| T02 | 除零防护、非法表达式不崩溃 | 保留可复用 | 同上 |
| T03 | `1km=1000m` / `100cny to usd` | 保留可复用 | 同上 |
| T04 | `#3B6CF6 → rgb(59,108,246)`，HEX↔RGB 往返 | 保留可复用 | 同上 |
| T05 | HEX→HSL→RGB 往返一致（±2/通道） | 保留可复用 | 同上 |
| T06 | 颜色内页三栏 DOM 联动 | 保留可复用 | 6 个工具内页不变 |
| T07 | 10/13 位时间戳 ↔ 日期双向 | 保留可复用 | 同上 |
| T08 | 翻译内置词典 `hello world` 命中 | 保留可复用 | 离线基础翻译降级保留（NFR-04） |
| T09 | 工具内页 UI：计算 / 单位 / 时间戳 | 保留可复用 | 同上 |
| T10 | 触发词 `js` / `color` / `ts` 结果计算 | 保留可复用 | 三个词均在新 5 词集合内，且现已是内置工具 `target` |
| P01 | 详情 → 权限清单可见 | **必须删除** | 插件详情弹层与 6 类插件权限 `PERM_DEFS` 整体作废 |
| P02 | 安装 → 授权弹窗三要素 | **必须删除** | `openAuthModal()` / `auth-allow|once|deny` 作废 |
| P03 | 允许 → 下载校验 4 步 → 完成弹窗 | **必须删除** | `runDownload()` / `openInstallDoneModal()` 作废 |
| P04 | 完成 → installed/enabled/pinned + 触发词注册 | **必须删除** | `doInstallDone()` / `p.trigger` 注册逻辑作废 |
| P05 | 拒绝授权 → 插件其他功能仍可用 | **必须删除** | 授权决策语义作废（OBS-06 随之作废，见 §3.5） |
| P06 | 仅本次授权 `decision=once` | **必须删除** | `store.grants` / `decision` 作废 |
| P07 | 撤销权限 → 写入 `revoked` 且总览显示 | **必须删除** | `revokePerm()` / E「插件」Tab 权限总览作废 |
| P08 | 撤销后立即生效：受限能力被网关拦截 | **必须删除** | `guard()` / `denyFeedback()` 运行时权限网关整体作废 |
| P09 | 禁用插件 → 不再出现在搜索结果 | **必须删除** | `pluginToggle()` / 插件结果组作废 |
| P10 | 卸载二次确认 + 清理触发词/固定项 | **必须删除** | `openUninstallModal()` / `doUninstall()` 作废（清理逻辑的可迁移部分另见 R31→N23） |
| P11 | 卸载后搜索不命中；重复安装不产生重复条目 | **必须删除** | 同上 |
| M01 | 插件超时：结果区「暂时不可用」+ 三出口 | **必须删除** | `store.demo.timeout` 插件超时演示态作废（NFR-09 改写，见 N15） |
| M02 | 超时态下主浮层仍可用（NFR-09） | **需改写** | 「主浮层不崩、输入框可用、其他结果行仍在」的隔离意图保留，触发器由「插件超时」改为「单个内置工具异常」演示态 |
| M03 | 超时三出口：重试 / 禁用 / 卸载 | **必须删除** | 三出口均为插件动作，整体作废 |
| M04 | 断网：市场降级为本地已装视图 + 提示 | **需改写** | NFR-04 改写为「全部内置工具离线可用 + 断网降级提示保留」；须删除 `#d-content .offline-banner` / `d-offline-installed` / `startInstall` 相关断言 |
| D01 | 静态列出全部平台引用点（输出 `platform-refs.json`） | **需改写** | 机制与 `platformRefs()` 可复用，但引用点集合须重建（D 视图相关消失、关于页新增双端对齐状态/阶段性差异清单），38 处不再是期望值 |
| D02 | 白名单 #1/#2 修饰键与默认呼出键映射 | 保留可复用 | 13 项白名单继续有效，4 类结构中「快捷键语义」100% 对齐 |
| D03 | 白名单 #4 mac 菜单栏 / Win 任务栏 | 保留可复用 | 同上，常驻入口位置差异仍合法 |
| D04 | 白名单 #5 Step3 / 关于页只列本平台权限 | 保留可复用 | 平台系统权限清单保留（NFR-06 改写为「系统权限透明」） |
| D05 | 六套界面两端结构/文案/尺寸白名单外 0 差异 | **需改写** | 6 视图→5 视图；且必须加入「阶段性差异清单」的掩码与到期日校验，否则会误判（§0 风险②） |
| D06 | `{{PLATFORM}}` 标签列入 `t()` 白名单 | 保留可复用 | 白名单映射不变 |
| E01 | 6 类空态主行动按钮均可点击 | **需改写** | `acts.length` 期望 6 → 3，删 `empty-plugins` / `empty-updates` / `empty-offline` |
| E02 | A4 / 空剪贴板 / 空更新不崩溃 | **需改写** | 含 `store.plugins.forEach(p.update=false)` 与 `#d-content .empty` 断言，须删；保留 A4 与空剪贴板部分 |
| E03 | 动态点击覆盖：每个出现的 `data-act` 无异常 | **需改写** | 机制（25 种界面状态遍点）可复用，但 action 集合与状态集合随删除/新增变化 |
| E04 | 真实鼠标坐标点击触发平台切换 | 保留可复用 | 与插件无关 |
| E05 | B 面板从系统拖入 → 新增快捷项 | 保留可复用 | B 分组拖拽与 8 项右键菜单不变 |
| E06 | E 设置页触发词冲突 / 空值校验 | **需改写** | setup `store.settings.tab='plugins'` 失效；触发词自定义与冲突检测须移入「快捷键」Tab |
| E07 | Toast 类型样式类命中 `.toast.ok/.warn/.err` | 保留可复用 | 与插件无关（R07–R10 另有计算样式级复核） |
| E08 | 关键尺寸 `720 / 420×360 / 1000×680 / 760×520` | **需改写** | `view-d` 的 1000×680 随 D 删除，须从期望表移除；A/C/F 三项不变 |
| SHOT | 输出 10 张关键状态截图 | **需改写** | 截图集合与命名须按 §5 重定（7 张重拍 / 3 张可复用为参考 / 新增 12 张） |
| X01 | 全流程无未捕获异常 / console error | 保留可复用 | 通用运行时闸门 |
| X02 | 全流程无外部网络请求 | 保留可复用 | 零网络硬约束 |

**小计（85）**：保留可复用 **52** / 需改写 **20** / 必须删除 **13**。

### 2.3 `tests/fix-regression.js` · 36 条

| 编号 | 用例（简称） | 归类 | 一句理由 |
|---|---|---|---|
| R01 | 运行时权限网关 8 个函数与常量齐备 | **必须删除** | `guard / guardCapability / guardPeek / guardCapabilityPeek / denyFeedback / denyIfBlocked / permForCapability / CAPABILITY_PERMS` 整体作废 |
| R02 | 三条执行链均接入网关（9 处静态调用点） | **必须删除** | 网关调用点断言失去意义 |
| R03 | 零网络 / 单文件约束未回归 | 保留可复用 | 与 S01–S04 同源，硬约束不变 |
| R04 | `:root` 外无硬编码色值；`--chip-group-4` 被 `--chip-4` 引用 | 保留可复用 | 5.8 token 与分组第 4 色 `#9B6CF6` 明确不变 |
| R05 | `HIDDEN → TOOLPAGE` 为合法迁移（静态） | 保留可复用 | `TRANSITIONS` 明确不变 |
| R06 | 修复后内嵌脚本 `node --check` | 保留可复用 | 语法闸门不变 |
| R07 | Toast `ok` 独立类 + 圆点计算色 `var(--success)` | 保留可复用 | `getComputedStyle` 三色实测与插件无关，可直接带入 |
| R08 | Toast `warn` 独立类 + `var(--warning)` | 保留可复用 | 同上 |
| R09 | Toast `err` 独立类 + `var(--danger)` | 保留可复用 | 同上 |
| R10 | 无 kind 的 Toast 用默认强调色 | 保留可复用 | 同上 |
| R11 | `CAPABILITY_PERMS` 六项映射 + `screenshot` 别名 | **必须删除** | 能力→插件权限映射作废；`screen.capture` 仅作为 macOS 系统权限保留在 `PLATFORM_PERMS` |
| R12 | 撤销 `net.fetch` 后 `guard` / `guardPeek` 同时拒绝 | **必须删除** | 网关判定作废 |
| R13 | 撤销后执行插件结果被拦截并指路「恢复」 | **必须删除** | `executeResult` 插件分支 + `denyFeedback()` 作废 |
| R14 | `restorePerm` 恢复后放行 | **必须删除** | 作废 |
| R15 | 拒绝授权（deny）与撤销共用 `p.revoked` | **必须删除** | 作废 |
| R16 | 「仅本次」首次放行并消耗 grant、二次拦截 | **必须删除** | `store.grants` 作废 |
| R17 | 渲染期 `guardPeek` 不消耗一次性授权 | **必须删除** | 作废 |
| R18 | 撤销立即作废在飞的一次性授权 | **必须删除** | 作废 |
| R19 | 未安装 / 未启用插件不参与拦截 | **必须删除** | 作废 |
| R20 | `screenshot` 别名受网关约束 | **必须删除** | 作废 |
| R21 | 诊断：单项拒绝连带拦截通用入口（OBS-06） | **必须删除** | OBS-06 随权限网关一并作废，处置结论见 §3.5 |
| R22 | 链①搜索结果 `executeResult case 'plugin'` 拦截 | **必须删除** | 插件结果类型与拦截逻辑作废 |
| R23 | 链②触发词 `fy` 翻译输出降级文案、无重复 Toast | **需改写** | 纯内置工具部分可复用：改写为「`fy` 触发词视图正常输出译文、渲染期无重复 Toast」，删除「权限已撤销，需重新申请」降级文案断言 |
| R24 | 链②`trig-copy` 复制译文被拦截 | **需改写** | 改写为「`triggerCopyValue('fy',…)='你好，世界'` 且 `copyText` 被调用」，断言方向从 deny 翻转为 success |
| R25 | 链②`trig-clip` 剪贴板能力被拦截 | **需改写** | 改写为「`trig-clip` 复制剪贴板记录成功」，删除 `denyIfBlocked('clipboard')` 依赖 |
| R26 | 链③`copyToolResult`（翻译/取色）被拦截 | **需改写** | 改写为「翻译 / 取色内页 `copyToolResult()` 复制结果成功」，保留两内页覆盖 |
| R27 | 链③`tool-clip-copy` 被拦截 | **需改写** | 改写为「剪贴板内页 `tool-clip-copy` 复制成功」 |
| R28 | 链③`onPickHover` 取色受 `screen.capture` 约束 | **需改写** | 改写为「`onPickHover` 正常更新 `store.tools.pickedHex` 与 `updatePickerReadout()`」；mac 未授权引导另由 N14 覆盖 |
| R29 | OBS-01：`--chip-group-4` 计算色 `rgb(155,108,246)` | 保留可复用 | 5.8 分组第 4 色明确不变 |
| R30 | OBS-02：`.tool-cell` 112×96 / 9 格 / 420×360 无溢出 | 保留可复用 | C 3×3 宫格与尺寸明确不变；「＋」改义后格数仍为 9 |
| R31 | OBS-03：卸载清理 9 项状态 | **需改写** | 插件卸载作废，但「`trigger` / `pinList` / `hotkeys` 清理」可迁移到「隐藏内置工具后从 pinList、数字键绑定、触发词表移除，且宫格仍 9 格无溢出」（N23）；`grants` / `revoked` / `installed` / `enabled` 四项删除 |
| R32 | OBS-04：`HIDDEN→TOOLPAGE` 不产生 forced | 保留可复用 | `TRANSITIONS` 不变，状态机基线不变 |
| R33 | 安装全链路 grants 写入且持久状态受限 | **必须删除** | `startInstall → auth → runDownload` 作废 |
| R34 | Esc 逐级回退两条链未回归 | 保留可复用 | Esc 五级回退栈明确不变 |
| R35 | 19 个界面状态双端白名单外差异 0 | **需改写** | 状态集合须重建（删 `E-plugins` 与 D 三状态、改 `E-about` / `F-gallery` / `C-grid` / A 三态），且需加入阶段性差异清单掩码 |
| R36 | 全流程零未捕获异常 / 零外部请求 | 保留可复用 | 通用运行时闸门 |

**小计（36）**：保留可复用 **13** / 需改写 **8** / 必须删除 **15**。

### 2.4 `tests/parity-supplement.js` · 19 个界面状态

| 状态 | setup 要点 | 归类 | 一句理由 |
|---|---|---|---|
| `E-general` | `settings.tab='general'` | 保留可复用 | 「常规」Tab 保留，仅需并入「主程序自动更新」行；双端对比逻辑不变 |
| `E-hotkeys` | `settings.tab='hotkeys'` | 保留可复用 | 「快捷键」Tab 保留；触发词冲突提示移入后需并入快照（不改对比机制） |
| `E-plugins` | `settings.tab='plugins'` | **必须删除** | 「插件」Tab 整页删除 |
| `E-about` | `settings.tab='about'` | **需改写** | 关于页新增「双端功能对齐状态」「阶段性差异清单」，且版本号文案由「同一版本号/同一发布日」改为分阶段规则；白名单 #5 权限卡片裁剪逻辑保留 |
| `F-step1` | `guide.step=1` | 保留可复用 | Step1 不变 |
| `F-step2` | `guide.step=2` | 保留可复用 | Step2 不变 |
| `F-step3` | `guide.step=3` | 保留可复用 | Step3 平台系统权限清单 + 预置 5 组工具包保留；**原型本就没有「预置插件/示例插件」选择**，无需改动，仅需复核 `PLABELS` 白名单裁剪仍生效 |
| `F-gallery` | `guide.gallery=true` | **需改写** | 空态 6 类 → 3 类，快照 DOM 结构与文案变化 |
| `B-normal` | `panel.edit=false` | 保留可复用 | B 快捷启动面板完全不变 |
| `B-edit` | `panel.edit=true` | 保留可复用 | 编辑态 / 拖拽态 / 8 项右键菜单不变 |
| `C-grid` | `tools.mode='grid'` | **需改写** | 9 格与 112×96 不变，但第 9 格 `tool-add` 由「添加插件」改义为「宫格排序 / 显示隐藏」，`data-act` 与弹层内容变化 |
| `C-page-calc` | `openToolPage('calc','C')` | 保留可复用 | 6 个工具内页不变 |
| `D-discover` | `market.tab='discover'` | **必须删除** | D 整页删除 |
| `D-installed` | `market.tab='installed'` | **必须删除** | 同上 |
| `D-updates` | `market.tab='updates'` | **必须删除** | 同上 |
| `A-A1` | `setQuery('')` | 保留可复用 | A1 默认态不变（但整页截图含顶部控制台 D 入口，见 §5） |
| `A-A3` | `setQuery('word')` | **需改写** | 结果分组 6 → 4 类，`store.launcher.groups` 与归一化文案变化 |
| `A-A4` | `setQuery('zzzznomatch')` | **需改写** | A4 三出口与文案全变 |
| `A-TRIGGER` | `setQuery('fy hello')` | **需改写** | 「插件专属模式」改称「工具专属态」、触发词 6→5、`ocr` 删除；`fy` 本身保留 |

**小计（19）**：保留可复用 **9** / 需改写 **6** / 必须删除 **4**。

### 2.5 四类数量小计

| 归类 | 85 条 smoke | 36 条 fix-regression | 19 个 parity | **合计** |
|---|---:|---:|---:|---:|
| 保留可复用 | 52 | 13 | 9 | **74** |
| 需改写 | 20 | 8 | 6 | **34** |
| 必须删除 | 13 | 15 | 4 | **32** |
| **现有断言合计** | **85** | **36** | **19** | **140** |
| 需新增（不属于 140） | — | — | — | **24** |

> 前三行为 140 条现有断言的完备划分（74+34+32=140）；第四类「需新增」是新套件的补充清单，故四类数字不相加为 140。汇报口径与此表一致。
> **v1.1 补充（v1.3 沿用）**：用户 P2 + 第二轮 D1/D2 拍板的 NFR-03 披露分档、架构 G4'/G5'/G9 闸门的 QA 判定与归档，属**验收矩阵与归档规则**（§7），不是断言条目，因此**不增加 N 系列数量，24 条保持不变**；`tests-v2/perf/rss-{win,mac}.json` 与 `tests-v2/permission-guide/` 是报告/夹具产物，同样不计入断言数。

### 2.6 需新增 24 条（N01–N24）

| 编号 | 新断言 | 一句理由 |
|---|---|---|
| N01 | 结果分组恰为 4 类（应用 / 文件 / 网址 / 内置工具），且 `order`/`map` 不含「插件」「计算」组名 | `buildResults()` L1205–1206 的 6 组收窄为 4 组，需正向锁定 |
| N02 | `123*4` 结果归入「内置工具」组且值 = 492 | 配合 Q02 改写，验证「计算并入内置工具」而非被删 |
| N03 | `TRIGGER_DEFS` 恰 5 个词 `fy/js/color/ts/clip`，不含 `ocr` | 删 `ocr` 的负向断言 |
| N04 | 5 个触发词的 `target` 全部 ∈ `TOOLS.id`，不指向任何插件 ID | 「触发词唤起内置工具」的语义锁定（DK-P0-12 改写） |
| N05 | A4 三出口为「用浏览器搜索 / 清空重来 / 查看全部工具」，且无 `empty-market` | 三出口替换的正向 + 负向断言 |
| N06 | 空态恰 3 类，F gallery 无 `empty-plugins` / `empty-updates` / `empty-offline` | 6→3 的负向断言 |
| N07 | 不存在 `#view-d`、`data-act="view" data-v="D"`，且 5 类弹层 DOM 与函数均不存在 | D 整页 + 5 类弹层删除的负向断言（详情 / 授权 / 更新 / 卸载 / 触发词冲突） |
| N08 | E 恰 3 Tab；「常规」含主程序自动更新、「关于」含系统权限状态 + 双端功能对齐状态 + 阶段性差异清单 | 3 Tab 结构与内容分工（§5.1） |
| N09 | Esc 五级回退链对 5 个触发词逐条验证（工具内页→结果→触发词胶囊→清空→隐藏） | 现有 K12/K13 只覆盖 `fy` 与 `TOOLPAGE` 两条，新方向需覆盖 5 词 |
| N10 | 触发词冲突 / 空值校验在「快捷键」Tab 生效 | E06 迁移后的定位断言 |
| N11 | `Ctrl/⌘+K` 菜单不含「卸载 / 权限 / 插件详情」，保留 5 项（执行/打开、打开所在目录、复制路径、固定、绑定快捷键） | 菜单删项（P1-04） |
| N12 | 输入框敲任意文本 + Enter 不触发安装、不产生网络请求 | 「搜索框敲插件名回车即安装」入口删除的负向断言；原型本无此实现，需显式防回归 |
| N13 | C「＋」位点击后进入宫格排序 / 显示隐藏，9 格数不变、不新增工具、无插件安装入口 | 「＋」改义且不新增扩展能力（Q2 默认零扩展） |
| N14 | 系统权限未授权引导：mac 屏幕录制 `state='未授予'` 时「去授权」按钮存在且点击给明确引导、不静默失败 | NFR-06 改写为「系统权限透明」后的核心新验收点 |
| N15 | 单个内置工具异常演示态：重试 / 返回入口存在，主浮层不崩、其他结果可用 | NFR-09 改写（M01–M03 的替代） |
| N16 | 离线态下 5 组内置工具全部可用 + 明确降级提示 | NFR-04 改写（M04 的替代） |
| N17 | F Step3 无「预置插件 / 示例插件」选择，5 组工具包默认全选 | DK-P0-16 改写的负向 + 正向断言 |
| N18 | 4 类结构 100% 对齐：数据 schema / 快捷键语义 / 24 尺寸 + 19 色值 / 设置项结构（3 Tab、条目、默认值语义） | §4.3「任何差异视为缺陷」的四类结构闸门 |
| N19 | 阶段性差异清单每项带类别、最长间隔（≤8 周 / ≤16 周）、到期日；清单外差异 0 | 新增的唯一合法临时差异来源，必须可核查、可到期 |
| N20 | 版本号规则：W0 `0.9.x-win`、W8 `0.9.x-mac`、V1 起单一版本号 + 同发布日（关于页展示） | §4.4 发布节奏与版本号规则的展示层断言 |
| N21 | 13 项白名单 + 阶段性差异清单为唯一合法差异来源；W0 仅 Win 时跳过 mac 快照并显式标注 | 双端同期放宽后，`parity` 类断言必须先判「是否已发布第二端」 |
| N22 | 新 `data-act` 全集合 100% 有 `handleAct` 分支、无死按钮；且插件链 28 个 action 全部消失 | S10 / E03 改写后的期望集合与负向断言 |
| N23 | 隐藏内置工具后从 `pinList` / 数字键绑定 / 触发词表移除，宫格仍 9 格无溢出 | R31 卸载清理逻辑的可迁移部分 |
| N24 | `prototype-v2/` 双口径哈希登记 + 布局 JSON / 截图 diff 基线状态采集 + `buildSha / contractHash` 绑定（见 §5.3、§7 G9） | 新基线可追溯性与 V1 双端回归落地 |

---

## 3. `prototype/index.html` 改造清单（面向 v2 的语义迁移映射）

> **口径校正（v1.1，与架构 §8.3-2 对齐）**：架构侧已判定 `prototype/index.html` 为「**保留（冻结，历史参考产物）**，不修改、不删、不重跑」。因此本节三组清单**不是对冻结文件下达的编辑指令**，而是交接包用于描述「新方向 `prototype-v2/` 相对旧原型应删除 / 改义 / 原样保留哪些语义」的**差量清单**；行号仅用于定位冻结基线中的证据。**本任务全程未修改该文件**（附录 A 双口径 md5 前后一致）。
>
> 行号基于冻结基线 `md5 1fc47e…`（3,242 行 / LF 3,241）。分「删除 / 改文案或语义 / 原样保留」三组，精确到 DOM 节点、`data-act` action 名、store 字段、状态与函数。

### 3.1 删除

| 类别 | 具体对象 | 位置 / 依据 |
|---|---|---|
| DOM 节点 | `<section id="view-d" class="window hidden">` 整段（含 `#d-search`、`#d-nav`、`#d-content`、`#d-foot`） | L547–558；D 整页删除 |
| DOM 节点 | 顶部控制台 `<button data-act="view" data-v="D">D 插件市场</button>` | L443；`view` action 的 `D` 取值 |
| DOM 节点 | 顶部控制台 `<label><input data-act="demo" data-v="timeout"> 插件超时</label>` | L449；插件超时演示态作废 |
| DOM 节点 | A4 空态按钮 `data-act="empty-market"`「在插件市场搜索」 | L1352 |
| DOM 节点 | F gallery 3 张空态卡片 `empty-plugins` / `empty-updates` / `empty-offline` | L2036–2038 |
| DOM 节点 | 5 类弹层产出点：`openPluginDetail`（详情）、`openAuthModal`（授权，L2513–2527）、`openUpdateModal` + `runUpdate`（更新，L2587–2609）、`openUninstallModal`（卸载，L2612–2618）、`#trigger-conflict` 提示 + `doInstallDone` 冲突分支（触发词冲突，L2558 / L2561–2568） | §5.1；「触发词冲突」提示移入「快捷键」Tab |
| `data-act` / case | 23 个插件链字面 action + 5 个动态生成 action（完整名单见 §3.4） | `handleAct` L2711 起 |
| store 字段 | `store.plugins`（L798）、`store.market`（L794）、`store.grants`（L782）、`store.demo.timeout`（L778） | 插件运行时整体作废 |
| store 字段 | `store.settings.localDev`（L796）、`store.settings.autoUpdate` 保留但改义（见 §3.2） | 「本地开发插件加载」删除 |
| 数据常量 | `PLUGIN_SEED`（L701–710）、`PERM_DEFS` 6 类插件权限（L603–610）、`CAPABILITY_PERMS`（L820–827） | 插件权限模型作废 |
| 函数 | `permById` / `permLabel`（L611–612）、`providersFor` / `guard` / `guardPeek` / `guardCapability` / `guardCapabilityPeek` / `permForCapability` / `denyFeedback` / `denyIfBlocked` / `applyPermDeniedView` / `PERM_DENIED_TEXT`（L815–889 整段「2.5 运行时权限网关」） | 网关整体作废 |
| 函数 | `pluginById`（L901）、`openPluginDetail` / `startInstall` / `openAuthModal` / `runDownload` / `openInstallDoneModal` / `doInstallDone` / `pluginToggle` / `openUpdateModal` / `runUpdate` / `openUninstallModal` / `doUninstall` / `openPluginMore` / `revokePerm` / `restorePerm`（L2488–2675） | 插件生命周期状态机作废 |
| 函数 | `renderD()` / `dNavItem()`（L1821–1901）、`setDemo('timeout',…)` 分支（L2174） | D 渲染与超时演示态作废 |
| 渲染分支 | `buildResults()` 中 `plugins` 数组、`type:'plugin'` 结果项、`plugin-degraded` 降级项、`store.demo.timeout` 分支；`order` / `map` 中的「插件」「计算」组名 | L1190–1215 |
| 渲染分支 | `openActionMenuAt()` 中 `if(it.type==='plugin')` 的「卸载插件」项（L2354）与 else 分支「打开插件市场」（L2357） | `Ctrl/⌘+K` 菜单删插件项 |
| 渲染分支 | `openToolAddModal()` 中「固定已装插件」列表与「打开插件市场」按钮（L2335–2341） | 「＋」改义，见 §3.2 |
| 渲染分支 | E `tab==='plugins'` 整段（L1979–2002，权限总览 / 触发词自定义 / 来源与信任） | 「插件」Tab 删除 |
| 演示态 | `store.demo.timeout` 相关 `#state-chip` 后缀（L1254）、结果区降级行（L1199–1203） | 插件超时演示态作废 |
| 演示态 | `store.demo.offline` 的「市场降级为本地已装视图」分支（L1843–1844、L1900、L2508） | 市场断网降级作废；离线提示本身保留并改义（§3.2） |

### 3.2 改文案或语义

| 对象 | 现在 | 改为 | 依据 |
|---|---|---|---|
| `renderA4()` 三出口（L1347–1355） | `empty-market`「在插件市场搜索」/ `empty-browser` / `clear` | 「用浏览器搜索」/「清空重来」/「查看全部工具」（新 action，如 `empty-all-tools`） | §5.1 A4 三出口 |
| `buildResults()` 的 `order` / `map`（L1205–1206） | `['计算','常用工具','应用','文件','网址','插件']` 与 6 组 map | 4 组：应用 / 文件 / 网址 / 内置工具；`calc` 结果并入「内置工具」 | §5.1 分组 6→4 |
| `TRIGGER_DEFS`（L672–679） | 6 词含 `ocr → target:'ocr'` | 删 `ocr`；5 词 `target` 全部指向 `TOOLS.id`；「插件专属模式」改称「工具专属态」 | §5.1 / DK-P0-12 |
| `renderTriggerView()` 注释与文案（L1357 起） | 「插件独占视图」/ 在线引擎措辞 | 「工具专属态 · 内置工具」；离线基础翻译降级提示保留 | §5.1 / NFR-04 |
| `renderE()` 导航（L1931–1934） | 4 Tab：常规 / 快捷键 / 插件 / 关于与双端信息 | 3 Tab：常规 / 快捷键 / 关于与双端信息 | §5.1 |
| E「常规」Tab（L1936–1962） | 无主程序自动更新 | 并入「主程序自动更新」（原 E plugins Tab L1999 的 `sw('autoUpdate')` 语义） | §5.1 / §2.2 P1-01/02 |
| E「快捷键」Tab（L1963–1978） | 仅快捷键录制 + 系统占用提示 | 并入「触发词自定义 · 冲突检测」（原 E plugins Tab L1993–1997）；触发词冲突提示移入此处 | §5.1 |
| E「关于」Tab（L2003–2022） | 「同一版本号 / 同一发布日 / 同一更新日志」「双端功能对齐状态 100%」 | 改为分阶段规则：新增「双端功能对齐状态」与「阶段性差异清单」（类别 / 最长间隔 / 到期日）展示；系统权限状态并入本页；更新日志去插件措辞（L2009「插件安装授权闭环」） | §4.3 / §4.4 / §5.1 |
| `data-act="d-check-update"`（L1874 / L2018 / L2876） | D「所有插件都是最新版本」空态 + 关于页「检查更新」 | 删除 D 侧用法；保留并改名为「主程序检查更新」，移到「常规」Tab | §5.1 / P1-01 |
| `openToolAddModal()` + `tool-add` / `tool-pin-plugin`（L2334–2342、L1493、L2807） | 「添加工具到宫格」列出已装插件并「打开插件市场」 | 改为「自定义宫格排序 / 显示隐藏内置工具」；删除 `tool-pin-plugin`；不新增工具、不新增扩展能力；二维码 `qr` 占位保留 | §5.1 / §3.1 |
| `store.tools.pinList` / `store.settings.hotkeys` / `store.triggers` 的清理逻辑（`doUninstall` L2619–2634） | 卸载插件时清理 | 迁移为「隐藏内置工具」时清理（对应 N23 / R31 改写） | §1.1 变更 7 |
| `EMPTY_STATES`（L2032–2038）与 `F gallery`（L2040–2045、L2087） | 6 类空态 / 标题「6 类空态」 | 3 类：快捷启动面板无项 / 剪贴板无记录 / 搜索无结果；标题改「3 类空态」 | §5.1 / §5.2 |
| `data-act="demo" data-v="offline"`（L450）与 `setDemo('offline')` | 「市场降级为本地已装视图 + 已装插件仍可用」 | 改为「全部内置工具离线可用 + 明确降级提示」演示态；建议改名为 `offline-tools` | NFR-04 改写 / §5.1 |
| `openActionMenuAt()`（L2345–2361） | 含「卸载插件」「打开插件市场」 | 删除两项，保留执行 / 打开所在目录 / 复制路径 / 固定 / 绑定快捷键 / 复制结果 | §5.2 / P1-04 |
| `#view-nav` 的 `view` action（L439–446） | `data-v` 含 `D` | 删除 `D` 取值，视图集合改为 `A/B/C/E/F` | §5.1 |
| `store.demo.timeout`（L778） | 插件超时开关 | 改为「单个内置工具异常」演示开关（或删除并由 N15 新演示态替代） | NFR-09 改写 |
| 关于页 / 引导页权限文案（L2010、L2070） | 「白名单 #5，仅此项随平台变化」 | 保留（系统权限清单不变），但需新增「未授权给明确引导，不静默失败」的可核查入口 | NFR-06 改写 / N14 |
| 各处「插件」字样（全文 80 处命中） | 如 L606「市场 / 更新服务」、L609「注册插件专属热键」、L1352、L1844、L1900、L2009、L2036/2038 | 逐条改为「内置工具 / 主程序」或删除；`PLATFORM_PERMS`（L615–626）的系统权限名**不改** | §1.1 变更 1–5 |

> **精确说明**：`PRD §5.2` 要求删除「搜索框敲插件名回车即安装」入口，但**冻结原型中不存在该实现**——Enter 走 `executeResult()`（`case 'plugin'` 仅 Toast「已唤起插件」），安装只由 D 页 `data-act="plugin-install"` 触发。因此原型侧无需删除代码，但必须新增 N12 负向断言防回归。同理，Step3 **原型本就没有「预置插件 / 示例插件」选择**（L2069–2080 只有平台权限清单 + 预置 5 组工具包），该项为「原样保留」，见 §3.3。

### 3.3 原样保留

| 类别 | 具体对象 | 理由 |
|---|---|---|
| 状态机 | `TRANSITIONS`（L803–812，含 `HIDDEN:['A1','TOOLPAGE']`）、`STATE_DEPTH`（L813）、`setState()`（L1143）、`rebuildEscStack()`（L1153）、`escBack()`（L1170） | A 四态 + Esc 五级回退栈 + 7 态显式状态机明确不变 |
| A 视图 | `#view-a`、`renderA1/A2/A3`、`renderA4()` 的四态结构/尺寸/配高亮、`#a-capsule-slot .capsule`、`#a-body .excl-wrap` | A 四态结构、尺寸、配高亮不变；仅三出口文案与分组内容改（§3.2） |
| B 视图 | `#view-b`、`GROUP_SEED`（L693–699）、`b-*` action 全组、`openMoveGroupMenu()`、8 项右键菜单、`drop-trigger` / `b-simulate-drop` / `b-remove-outside` | B 分组拖拽与 8 项右键菜单明确不变 |
| C 视图 | `#view-c`、`TOOLS`（L659–669，9 格）、`renderC()`（L1471）、`.tool-cell{width:var(--cell-w);height:var(--cell-h)}`（L282）、`#c-grid` 112×96 / 420×360 / head 30 / foot 22、6 个工具内页、`tool-*` 除 `tool-pin-plugin` 外全组、`qr` 占位 | C 3×3 宫格 112×96 与 6 个工具内页明确不变 |
| 算法 | `calcParse` / `evalExpression` / `convertUnit` / `hexToRgb` / `rgbToHex` / `rgbToHsl` / `hslToRgb` / `tsToDate` / `dateToTs` / `fmtDate` / `translateText` / `DICT` | 5 组内置工具能力边界完全沿用 01-PRD §4.2/5.3 |
| token | `:root` 全部 24 个尺寸 token 与 19 个色值（含 `--chip-group-4:#9B6CF6`、`--chip-4:var(--chip-group-4)`，L25） | 5.7 尺寸表 / 5.8 配色 token 明确不变 |
| 双端 | `TEXT_MAPS`（L629–632）、`M()` / `defaultHotkey()` / `backupHotkey()` / `keycap()` / `t()`（L907–922）、`PLATFORM_PERMS`（L615–626）、`renderResident()` 的 mac 菜单栏 / Win 任务栏（L1258–1259）、`{{PLATFORM}}` 标签 | 13 项平台差异白名单继续有效；系统权限清单保留 |
| 引导 | F Step1 / Step2、Step3 的平台系统权限清单与 `f-preset` 预置 5 组工具包（默认全选） | §5.1；原型已无插件预置项 |
| 通用 | `esc()` 转义、`toast()`（L938，独立 kind 类）、`#toast-layer` CSS `.toast.ok/.warn/.err`（L414–417）、`openModal` / `showPopup` / `handleAct` 事件委托框架、`clip-*` 剪贴板工具链、`about-perm` / `picker-permission` 系统权限引导入口 | 与插件无关，且是「系统权限透明」新验收点的载体 |
| 全局闸门 | 零 `<link>` / `<script src>` / `http(s)://` / `fetch(` / `@import` / `url(`、`node --check` | 单文件离线硬约束不变 |

### 3.4 103 个字面 action / 109 个 handleAct case 的归属

| 组 | 数量 | 名单 |
|---|---:|---|
| **插件链 · 必须删除**（103 字面集内） | **23** | `auth-allow` `auth-once` `auth-deny` `install-done` `uninstall-run` `update-run` `batch-update` `plugin-detail` `plugin-install` `plugin-toggle` `plugin-uninstall` `plugin-update` `plugin-more` `plugin-retry` `plugin-disable` `perm-` `d-tab` `d-cat` `d-offline-installed` `d-offline-retry` `dev-guide` `tool-pin-plugin` `empty-market` |
| **插件链 · 必须删除**（动态生成，不在 103 集内） | **5** | `perm-revoke` `perm-restore` `empty-plugins` `empty-updates` `empty-offline` → **合计删除 28 个 action** |
| **改义 / 迁移** | **8** | `demo`（timeout 分支删、offline 改义）、`d-check-update`（主程序更新移到「常规」）、`trigger-edit`（触发词自定义移入「快捷键」）、`e-tab`（Tab 值 4→3）、`f-gallery`（6→3 空态）、`empty-browser`（A4 三出口改）、`clear`（A4 出口排序/文案改）、`view`（删除 `data-v="D"` 取值）；另有动态 action `tool-add` 一并改义（不计入 103） |
| **原样保留**（内置工具链 / 中性） | **72** | `about-perm` `action-menu` `b-add` `b-cell` `b-del` `b-edit` `b-edit-done` `b-fold` `b-group-menu` `b-more` `b-nav` `b-newgroup` `b-remove-outside` `b-simulate-drop` `cfg-toggle` `cheat` `clip-action` `clip-clear` `clip-dismiss` `clip-pause` `data-manage` `demo-query` `density-chip` `drop-trigger` `f-back` `f-default` `f-preset` `f-record` `f-step` `feedback` `group-create` `group-rename` `guide-restart` `hide` `hotkey-reset` `modal-backdrop` `modal-close` `picker-permission` `platform` `popup-item` `qr-copy` `qr-save` `recent` `recent-num` `record` `rename-confirm` `resident-menu` `result` `result-num` `simulate-copy` `theme-chip` `toggle` `tool-action` `tool-back` `tool-clip-clear` `tool-clip-copy` `tool-clip-pause` `tool-clip-tab` `tool-clip-try` `tool-copy` `tool-history` `tool-open` `tool-pin` `tool-sample` `tool-tr-clear` `tool-tr-paste` `tool-ts-now` `tool-ts-sample` `tool-unit-sample` `trig-clip` `trig-copy` `trig-open-tool` |
| **动态生成但原样保留** | **3** | `empty-b-add` `empty-clip` `empty-noresult`（`tool-add` 已计入「改义」行，不重复计数；9 个动态 action = 5 删除 + 1 改义 + 3 保留） |

`handleAct` 共 **109 个 case**（Node 只读实测：103 字面值去重 103 个、`case 'xxx':` 标签 109 个且互不重复）：其中 **27 个删除、8 个改义、74 个原样保留**（与 §0-3 一致，`27+8+74=109`）。

**核算口径（可复现）**：`103 − 3 + 9 = 109`。
- 103 个字面 `data-act` 中有 **3 个不进入 `handleAct` 的 case 分支**：`perm-` 是字符串拼接前缀（真实 action 为 `perm-revoke` / `perm-restore`，见 L1988 `data-act="perm-'+(revoked?'restore':'revoke')+'"`）、`trigger-edit`（L1996）与 `f-preset`（L2079）由 `handleAct` **外的独立 `if` 分支**处理（L3096–3097）；
- 另有 **9 个动态生成 action** 拥有独立 case 但不在 103 字面集里：`empty-b-add` `empty-clip` `empty-noresult` `empty-offline` `empty-plugins` `empty-updates` `perm-restore` `perm-revoke` `tool-add`（由字符串拼接或空态卡片的 `act:'empty-xxx'` 产生）；
- 上表「删除 23 字面 + 5 动态」是 **action 名口径合计 28 个**；换算成 **case 口径**时，23 字面里的 `perm-` 不是 case（−1），5 个动态全部是 case，故 `23−1+5=27` 个删除 case；「改义 8 字面 + `tool-add`」同理为 `8+1=9` 个改义 action，但其中 `trigger-edit` 不是 case（−1），故 **8 个改义 case**；余下 `109−27−8=74` 个保留 case。两组数字口径不同但互不矛盾，§0-3 用的是 **case 口径（27/8/74）**，§3.4 表格用的是 **action 名口径（28/9/72，其中 72 为 103 字面集内的保留项）**。

迁移后 S10 / E03 / N22 必须同时覆盖「字面 + 动态」两个来源，否则会漏检死按钮。

### 3.5 OBS-06 的处置结论

**OBS-06（单项权限撤销连带拦截插件通用唤起入口）随插件运行时整体作废，不做修复、不保留观察项。** 理由：① 其根因是「插件结果项未携带 `capability` 字段」+ `executeResult` 按全量 `p.perms` 校验，两者都随插件链删除；② 原报告给出的两个选项（A 加 capability / B 改 L2523 文案）中，L2523 授权弹窗文案与整段 `openAuthModal()` 一并删除，选项 B 失去载体；③ 因此 R21 判为「必须删除」，`docs/03-QA-Report.md` 中 OBS-06 条目随报告冻结归档，不再作为遗留风险追踪。若未来 Q2 决定保留极简内置扩展点，需重新评估是否引入新的能力级权限判定——那属于新方向下的新设计，不在本次迁移范围。

---

## 4. md5 双口径的处理方式

### 4.1 两口径的成因与正确用法

| 口径 | 数值 | 用途 |
|---|---|---|
| Node 解码（`fs.readFileSync` 得到 `<!doctype html>` 开头的原始 HTML） | 208,228 bytes / `1fc47e74727696c2fa21a9c7a104d293` | **唯一验收口径**：三个脚本读的是它；CDP `Fetch.fulfillRequest` 注入 base64 的也是它；`tests/fix-regression-results.json` 的 `sourceMd5` 记录的是它 |
| 物理 shell（`stat -f%z` / `md5 -q`） | 212,992 bytes / `bee380603a586281c1745bb53a866c63` | **仅用于交付物完整性 / 防篡改核对**：包含工作区 `%TSD-Header-###%` 包装头，不是源码内容，也不随源码语义变化 |

> 历史教训：工作区 `wc` / `grep` / `file` / `head` / `stat` 看到的是包装体（曾误报 772 行 / `data`），Chrome 原生直读 `file://` 也读不到脚本；因此**所有静态与动态断言必须走 Node 解码口径**，物理口径不得进入断言期望值。两口径并存是同一文件的两层视图，**不是两个版本**，140 项断言全通过的结论没有歧义。

### 4.2 新基线在哪个节点、由谁、以什么口径重建

| 项 | 结论 |
|---|---|
| **旧基线效力（与架构 §8.1 一致）** | v0.9 冻结基线的 **140 项断言结果继续有效、不发起重跑、不更新基线**；双口径 `208,228 / 1fc47e…` 与 `212,992 / bee380…` 是同一文件的两层读取口径，不是两个版本。`prototype/index.html` **保留为冻结历史参考产物，不修改、不删、不重跑**（架构 §8.3-2）；三个脚本与三份结果/引用文件、10 张截图、`docs/03-QA-Report.md` 一律冻结（§8.3-3/4/5/8） |
| **新基线形态（与架构 §8.3-7 一致）** | **完全隔离另建**：`prototype-v2/` + `tests-v2/` + **`docs/09-QA-Report-V2.md`**（编号按 `team-lead` 同步，`docs/08` 已被本评估占用）。`tests-v2/` 至少含：`perf/rss-{win,mac}.json`（首次内存实测结果）、`permission-guide/`（系统权限未授权引导断言）、双端归一化布局夹具、布局 JSON + 截图 diff 基线。**架构侧与本评估本轮均只给建议，未创建任何 v2 文件** |
| **重建节点** | **仅在「`prototype-v2/` 按 §3 语义差量实际落地 + `tests-v2/` 新套件按 §2 分类改写/新增完成 + 首次全量回归通过 + T02 首次内存实测报告产出」之后**。具体为交接包 v2 方案定稿、实现方交付 `prototype-v2/`、QA 完成 `tests-v2/` 三套脚本（对应 smoke / fix-regression / parity）并全部通过的那一次执行；**不是现在，也不是只改文档时** |
| **现在不要动文件** | 本任务及交接包改写阶段**保持冻结**：`prototype/index.html` 双口径、`tests/*`、`docs/03-QA-Report.md` 一律不改；`docs/06-Incremental-PRD.md` 已声明「Cursor 交接包与原型 QA 冻结基线保持有效」，本评估沿用该约定；架构 §8 同样判定不发起重跑 |
| **由谁** | ① 实现方（`software-engineer`）产出 `prototype-v2/` 并同时给出**双口径**字节数 / md5（新产物为原生 UI 时见下条口径换算）；② **QA（严过关）独立复算**，确认与实现方一致后写入**新建**的 `tests-v2/` 结果文件；③ 性能实测报告由架构侧 T02 产出、QA 侧只按 §7 判定与归档（不重新测量）；④ `team-lead` 在交接包登记两个口径、`buildSha + contractHash` 与对应版本号 |
| **以什么口径** | 冻结 v0.9 层：**双口径都登记，以 Node 解码口径为验收依据**，物理 shell 口径只作完整性指纹、不进断言。新 v2 层：架构已定**零 WebView / 各端原生 UI**，不存在「Node 解码单文件 HTML」，故口径替换为**构建产物/资源清单哈希（逻辑内容层）＋ 交付包哈希（物理交付层）**，仍保留两层；布局基线以各端导出的归一化布局 JSON 为准。QA 报告 V2 需同时登记 `buildSha + contractHash`（G9） |
| **禁止覆盖冻结文件** | 新结果只写入 `prototype-v2/` / `tests-v2/` / `docs/09-QA-Report-V2.md`，**不覆盖** `prototype/index.html`、`tests/results.json`、`tests/fix-regression-results.json`、`tests/platform-refs.json`、`tests/screenshots/*.png`、`docs/03-QA-Report.md`；旧文件全部作为 v0.9 插件版原型的历史证据保留 |
| **关联更新** | V2 报告重列基线表（双口径 + `tests-v2/` 三套脚本结果 + `sourceMd5` + `buildSha` / `contractHash` + NFR-03 披露分档三档归档）；S10 / E03 / N22 的期望 action 集合、平台引用点清单（按 G4' 契约白名单重建）、布局 JSON + 截图 diff 基线须按 §3.4 / §5 / §5.3 / §6 在 `tests-v2/` 内重建 |

---

## 5. `tests/screenshots/*.png` 10 张逐张判定

> 全部为 `Page.captureScreenshot({format:'png',fromSurface:true})` 的 **1440×900 整视口**截图（脚本 L223–225），画面包含顶部控制台 `#console`（含「D 插件市场」按钮与「插件超时 / 断网」演示开关）与 `#hintbar`。因此**用作 V1 像素基线时全部需重采**；下表按「主体状态是否仍存在且视觉一致」逐张判定。
>
> **保留声明（v1.1，与架构 §8.3-4 对齐）**：现有 **10 张全部保留、不删、不覆盖、不改名**。「必须重拍」指**在新的 `tests-v2/` 截图集中另拍替代/新状态**，冻结原图继续作为 v0.9 插件版原型的历史证据留档；其中 `06-market-offline.png` / `07-timeout-degraded.png` / `10-plugin-auth.png` 三个插件场景在**新方向下语义作废**，但仅作废其「验收效力」，文件本身仍是有效历史截图。本评估对 10 张 PNG 只做 `readdirSync` + `statSync` 只读列举，未写入任何一张（§8.1 / 附录 A）。

| 截图 | 记录的状态（脚本 L986–995） | 判定 | 理由 |
|---|---|---|---|
| `01-A1-win.png` | `qa.reset(); switchPlatform('win'); openView('A')` | **可复用（仅作状态参考）** | A1 默认态明确不变；但画面含顶部控制台 D 入口与演示开关，作像素基线须重采 |
| `02-A3-results.png` | `setQuery('word')` | **必须重拍** | 结果分组 6→4 类，「插件」「计算」独立组消失，结果行构成变化 |
| `03-A4-empty.png` | `setQuery('zzzznomatch')` | **必须重拍** | A4 三出口文案与 action 全变（`empty-market` 删除） |
| `04-trigger-fy.png` | `setQuery('fy hello world')` | **必须重拍** | 「插件专属模式」改称「工具专属态」，独占视图文案与 `ocr` 触发词相关提示变化 |
| `05-toolpage-calc.png` | `openToolPage('calc','C'); '(1+2)*3'` | **可复用（仅作状态参考）** | 6 个工具内页与 112×96 / 420×360 尺寸明确不变；像素基线须重采以同步控制台条 |
| `06-market-offline.png` | `setDemo('offline',true); openView('D')` | **必须重拍（原状态不存在，改拍替代状态）** | D 整页删除、市场断网降级作废；改拍「离线 · 全部内置工具可用」（N16 / N15） |
| `07-timeout-degraded.png` | `setDemo('timeout',true); setQuery('翻译')` | **必须重拍（原状态不存在，改拍替代状态）** | 插件超时演示态作废；改拍「单个内置工具异常 · 重试/返回」（N15） |
| `08-mac-platform.png` | `switchPlatform('mac'); openView('A')` | **可复用（仅作状态参考）** | mac A1 与常驻入口位置不变；像素基线须重采 |
| `09-settings-about.png` | `switchPlatform('mac'); settings.tab='about'` | **必须重拍** | 关于页新增「双端功能对齐状态」「阶段性差异清单」，版本号文案由「同一版本号/同一发布日」改为分阶段规则 |
| `10-plugin-auth.png` | `startInstall('qrtool')` | **必须重拍（原状态不存在，改拍替代状态）** | 授权弹窗与 5 类弹层作废；改拍「E 3 Tab / 快捷键 Tab 触发词冲突」或「系统权限未授权引导」 |

**小计**：必须重拍 **7**（02/03/04/06/07/09/10，其中 06/07/10 为「原状态不存在 → 在 `tests-v2/` 另拍替代状态，冻结原图保留为历史截图」）；可复用为状态参考 **3**（01/05/08，但作像素基线仍需重采）；**需新增 12 张**（下表）。**10 张冻结原图零删除、零覆盖**。

| 建议新增截图 | 对应新能力 |
|---|---|
| `11-A3-4groups.png` | 4 类结果分组（N01/N02） |
| `12-A4-new-exits.png` | A4 三新出口（N05） |
| `13-trigger-tool-exclusive.png` | 工具专属态（5 词任选其一，N03/N04/N09） |
| `14-C-grid-customize.png` | 「＋」宫格排序 / 显示隐藏（N13/N23） |
| `15-E-3tabs-about-parity.png` | E 3 Tab 与关于页双端对齐状态 + 阶段性差异清单（N08/N19/N20） |
| `16-E-hotkeys-trigger-conflict.png` | 快捷键 Tab 触发词冲突（N10） |
| `17-E-general-autoupdate.png` | 常规 Tab 主程序自动更新（N08） |
| `18-F-step3-system-perms.png` | Step3 系统权限清单 + 5 组工具包（N17） |
| `19-empty-3states.png` | 3 类空态（N06） |
| `20-system-perm-unauthorized.png` | mac 屏幕录制未授权引导（N14） |
| `21-tool-error-retry.png` / `22-offline-builtin-tools.png` | 内置工具异常 / 离线内置工具演示态（N15/N16） |
| `23-win-w0.png` + `24-mac-w8.png` | W0 / W8 双端逐像素 diff 基线对（N21/N24） |

### 5.3 V1「未建立逐像素 diff 基线」在新方向下如何落地

现有遗留风险的准确表述是「**未建立双端逐像素 diff 基线**，原型层以 DOM 结构 + 归一化文案 + `getComputedStyle` 尺寸三重断言替代」。

> **v1.1 前提变更（与架构 G5' 对齐）**：架构 #8 已定「共享 Rust Core + 各端原生 UI、**零 WebView**」，原生 UI **没有 DOM**，因此 v0.9 的「DOM 结构 + 归一化文案 + `getComputedStyle`」三重断言与 Chrome CDP 截图链**整体不再适用**。按 G5' 改为 **归一化布局 JSON 逐字段比对 + 各端原生截图 diff**：布局 JSON 由各端 UI 导出（控件 id / 层级 / 尺寸 / 文案键 / 状态名），QA 侧对 Win 与 mac 的 JSON 逐字段比对，像素层只对截屏做 diff，且**仅白名单 #11（系统材质，如 Win Mica / mac 毛玻璃与 vibrancy）允许差异**。以下步骤按此前提重写。

1. **基线状态清单**（Win 与 mac 各一套，共约 24–30 个状态）：A 四态（A1/A2/A3/A4）；工具专属态 5 个触发词各 1；C 宫格 + 6 个工具内页；B 面板 normal / edit / drag；E 3 Tab；F 3 步引导 + 3 类空态；3 类空态独立页面；系统权限未授权引导页（mac 4 项 / Win 2 项，仅本端）；阶段性差异清单展示页（W0 Win / W8 mac）；常驻入口（Win 任务栏 / mac 菜单栏）。每个状态产出「布局 JSON + 原生截屏」一对基线。
2. **双通道比对与允许差异掩码**：① **布局 JSON 通道**——4 类白名单区域允许字段差异：修饰键文案（`Ctrl/Alt` ↔ `⌘/⌥`）、默认呼出键显示（`Alt + Space` ↔ `⌥ + Space`）、引导 Step3 / 关于页系统权限清单（Win 开机自启 / 文件索引；mac 辅助功能 / 输入监控 / 屏幕录制 / 完全磁盘访问）、常驻入口位置（菜单栏 / 任务栏）；② **截图 diff 通道**——按 G5' **仅白名单 #11 系统材质**允许像素差异。两通道掩码外任何差异判缺陷。
3. **阶段性差异清单特例**：清单内项（平台特有系统能力深度适配 ≤8 周、V1 增强项 ≤16 周）允许临时差异，但**必须带类别、最长间隔、到期日**，且到期后自动转为缺陷；4 类结构（数据 schema / 快捷键语义 / 5.7 尺寸 + 5.8 token / 设置项 3 Tab）**任何时刻不做掩码，布局 JSON 逐字段 0 差异**。
4. **W0 单端期**：只有 Windows 时，mac 侧快照标记为「未发布」，跳过 diff 但在基线清单中显式登记；W8 mac 发布时一次性补齐并重跑全量 diff（N21）。
5. **采集条件固定并归档**：各端固定窗口尺寸 / DPI 缩放 / 系统主题 / 字体渲染 / 动效冻结（Win 与 mac 各自固定一组、跨版本不变），采集工具由架构侧提供（**不再用 `--headless=new` / Chrome CDP**，见上方前提变更）；基线布局 JSON 与 PNG 入库 `tests-v2/`，登记双口径 md5 与 `buildSha + contractHash`（§4、§7 G9），冻结的 `tests/screenshots/` 10 张不参与新基线、不覆盖。

---

## 6. `docs/03-QA-Report.md` 判定效力与 `platform-refs.json`

### 6.1 报告：冻结归档为历史版本，**不改写**

| 理由 | 说明 |
|---|---|
| 结论绑定特定源码 | 第 0/8/10 节明确绑定 `208,228 bytes / md5 1fc47e74727696c2fa21a9c7a104d293`；新方向下 D 删除、插件运行时作废，该 md5 不再存在，报告中的 140/140、`NOONE`、DEF-01/DEF-02「已修复并验证」、OBS-01~06 均**不再构成新产品的验收依据** |
| 关键表述失效 | 「六套界面」「13 项白名单外差异 0」「6 触发词」「6 类空态」「插件生命周期闭环」「运行时权限网关」「同版本号同发布日」等在新方向下均需改写；若就地改写会把历史审计轨迹与方法证据一并抹掉 |
| 但仍有保留价值 | 报告记录了 QA 方法（真实 Chrome headless + CDP / `file://` / `Fetch.fulfillRequest` 注入解码 HTML）、双口径 md5 处理、双端三重断言、两轮测试与智能路由、缺陷修复闭环，是交接包里**唯一可复用的 QA 方法论与证据链** |
| 处置 | 在交接包中标注「**历史版本 · 仅适用于 v0.9 插件版原型（md5 1fc47e…）**」，不修改文件；新方向的 QA 结论另起新报告（如 `docs/09-QA-Report-V2.md`，不在本任务范围），并在新报告里引用本文 §2/§3/§4/§5 作为迁移依据 |
| 一致性 | `docs/06-Incremental-PRD.md` §15 已声明「修改 03-QA-Report.md：❌ 全部未改动」，本评估沿用；`handoff/` 四份亦未触动。架构 §8.3-5 同为「保留为历史基线报告、不重跑」 |
| **OBS-06 backlog（与架构 §8.3-5 对齐）** | 架构侧明确 **OBS-06 backlog 作废**（「单项拒绝连带拦截插件通用唤起入口」是插件权限模型产物，零插件后该路径不存在）。本评估 §3.5 结论一致：**不做修复、不保留观察项、新 backlog 不承接**；但 `docs/03-QA-Report.md` 中 OBS-06 的**记录不删不改**，随报告冻结归档。R21 判「必须删除」 |

### 6.2 `tests/platform-refs.json` 的 38 / 7 / 0 结论是否仍有效

| 项 | 对冻结基线 | 对新方向 |
|---|---|---|
| **38 处平台引用点** | 有效历史事实（实测 38 条，明细含 `TEXT_MAPS` L629、`M/defaultHotkey/backupHotkey/keycap/t` L907–922、`renderResident` L1258–1259、`PLATFORM_PERMS[store.platform]` L2004/2068、`onRecordKey` L2990/2996/2999 等） | **不再具有验收效力**：D 视图相关引用随 `renderD()` 删除、插件网关注释类引用消失，关于页新增「双端功能对齐状态 / 阶段性差异清单」会引入新的数据渲染引用点。须由改造后的 `platformRefs()` 重新扫描并输出**新建**的引用清单（不覆盖冻结文件） |
| **7 处等值分支** | 有效：S12 统计口径为 `store.platform==='win' ?` 三元分支，落在 `defaultHotkey` / `backupHotkey` / `t` / `renderF` / `onRecordKey`×3 共 7 处，全部在白名单函数集合内（另有 1 处 `keycap` L911 的布尔赋值 `var win=store.platform==='win'`，非三元，不计入 7） | **白名单函数集合可作为新原型的起点**（7 处所在函数全部属于保留项），但新增的「阶段性差异清单」若由 `PlatformAdapter` 数据渲染，必须确认不引入白名单外的等值分支；「越界 0」须重新证明，不能继承 |
| **越界 0 结论** | 有效历史结论（D05 / R35 / parity 19 状态均实测 0） | **须重新证明**：差异来源从「4 类白名单」扩展为「13 项白名单 + 阶段性差异清单」，判据本身要改写（§0 风险② / N19/N21） |
| **13 项白名单继续有效** | — | 有效（`docs/06` §4.3 / §5.2 明确），但需新增「阶段性差异清单」作为唯一合法的临时差异来源 |

> **与架构 §8.3-6 / G4' 的衔接**：旧 `docs/02-Architecture.md` §4.4.1 的 A1–A4 静态扫描基线**保留但不再作为新方向断言**；其精神（归属集合为主断言、UI 文案分支 = 0、`forced` 只由窗口导航产生）迁移为契约驱动的 **G4'**：「平台相关引用点 ⊆ `contracts/platform-diffs.json` 白名单、i18n 外零硬编码文案 / 色值 / 修饰键 / 设置键名 / 状态名」。因此上表「38 处引用点」在新方向下不再是固定期望值，而应改为「引用点集合 ⊆ 契约白名单」的集合包含判定；`platform-refs.json` 本身冻结不覆盖，新清单写入 `tests-v2/`。

---

## 7. 新方向下的建议 QA 验收矩阵

> 覆盖功能、交互（含 Esc 回退栈与 5 个触发词）、双端 4 类结构 100% 对齐、阶段性差异清单、3 类空态、系统权限未授权引导，以及 **NFR-03 披露分档的 QA 判定与归档**。
> **边界**：不写性能测量脚本、采样窗口、RSS 采集/统计方法论（归架构师 `docs/07 §5` 与 PRD Q5）；本矩阵只定义「拿到实测/CI 报告后，QA 侧如何判定三档结果、如何归档」。
> **NFR-03 口径（用户 P2 + 第二轮拍板 D1/D2）**：默认目标 `P95 ≤ 80MB`；`80 < P95 ≤ 150MB` 走预定义备案线、**放行 W0**（触发条件：T02 首次实测 + 六档降级全部执行）；`P95 > 150MB` **仍放行 W0、升级披露**，只靠发布说明与「关于」页如实写明实测值。150MB 是**披露分档线**、不是发布硬线；CI 报告带 `fallbackApplied` / `thresholdUsed: 80|150` / `measuredP95Mb` / `degradationSteps` / `disclosureLevel`，实测报告绑定 `buildSha + contractHash`；分档生效**不切换技术栈、不重选型**；G-NFR03 由架构师改为「**实测 · 归档 · 披露闸门（非阻断）**」。本矩阵不含任何以内存数值为条件的发布阻断判据，也不写采样 / `P95` 统计 / 测量脚本。
> **团队解释（`team-lead` 落地细化，v1.3 标注，非用户原话）**：① **取消发布阻断 ≠ 取消 QA 校验**——QA 仍校验凭证、`buildSha + contractHash`、字段完整性与披露文案，只是结论从「失败 / 阻断」变为「**归档无效 · 退回补正**」；内存数值本身**不再产生 FAIL**。② **验收层放宽 ≠ 选型层放宽**——`<80MB` 仍是决定选 B、否决 WPF 的硬判据，R1 未触发；它是默认目标与披露分档基准，**不代表 80MB 目标已放弃、也不代表门槛不再重要**，只是不作为发布阻断条件。

| 组 | 验收项 | 具体判据（可核查） | 主要来源 |
|---|---|---|---|
| **A 功能** | 全局呼出 / 隐藏 | `Alt/⌥+Space` 与备用键 `preventDefault`、无空格/NBSP 副作用；失焦隐藏、多屏边缘内收 | K01–K04 / DK-P0-01/17 |
| | 模糊 + 拼音搜索 | `wx / kzmb / word` 命中；结果恰 4 类（应用/文件/网址/内置工具），无「插件」「计算」独立组；`123*4` 并入内置工具组且 =492 | Q01 / N01 / N02 |
| | 应用 / 文件 / 网址启动 | 三类结果 `executeResult` 分支各自有效，零网络请求 | DK-P0-03 / V03 |
| | 5 组内置工具 | 剪贴板、计算器+单位换算、翻译（离线词典降级）、截图取色（HEX/RGB/HSL）、时间戳+颜色转换；6 个工具内页实测 | T01–T10 / §3.1 |
| | 二维码占位 | `qr` 格保留为 P1 占位，不升优先级、不新增能力 | §3.1 |
| | 分组与拖拽 | B 面板 normal/edit/drag 三态、8 项右键菜单（去插件项后）、从系统拖入新增快捷项 | E04 / E05 / §1.1 不变项 |
| | 主程序自动更新 / 数据本地管理 | 「常规」Tab 含自动更新；`data-manage` 导出/清空可用；云同步默认关闭 | N08 / §5.1 |
| **B 交互** | 12 项键盘映射 | `Alt/⌥+Space`、`↑↓`、`Enter`、`Shift+Enter`、`Tab`、数字 1–9、`Esc`、`⌘T`、`⇧⌘V`、`⌘,`、`⌘N/P`、鼠标行为全部响应且输入框正常输入不被劫持 | K01–K11 |
| | Esc 五级回退栈 | `工具内页 → 结果 → 触发词胶囊 → 清空 → 隐藏` 每次只退一层；链① `TRIGGER→A4→A1→HIDDEN`、链② `TOOLPAGE→A1→HIDDEN`；栈空再按 Esc 不崩；`forced=0` | K12–K15 / R34 / N09 |
| | 5 个触发词 · 工具专属态 | `fy/js/color/ts/clip` 全部胶囊化、切独占视图、与结果列表互斥；`target` 全指向内置工具；无 `ocr`；自定义 + 冲突检测 + 空值校验在「快捷键」Tab | Q03 / V06 / T10 / N03/N04/N10 |
| | `Ctrl/⌘+K` 操作菜单 | 只保留执行/打开、打开所在目录、复制路径、固定、绑定快捷键（+ 计算结果的复制结果）；无卸载/权限/插件详情 | N11 |
| | 负向：无安装入口 | 输入框敲任意文本 + Enter 不触发安装、不产生网络请求；无 `data-act="view" data-v="D"` | N12 / N07 |
| | C「＋」改义 | 点击后进入宫格排序 / 显示隐藏；9 格数不变、不新增工具、无插件安装入口；隐藏后清理 `pinList` / 数字键绑定 / 触发词表 | N13 / N23 |
| **C 演示态 / 稳定性** | 内置工具异常隔离 | 单个内置工具异常有「重试 / 返回」入口，主浮层不崩、输入框可用、其他结果行仍在（NFR-09 改写） | N15 / M02 改写 |
| | 离线可用 | 离线态下 5 组内置工具全部可用 + 明确降级提示；在线翻译增强不可用时走离线词库（NFR-04 改写） | N16 / M04 改写 |
| | 运行时闸门 | 零未捕获异常 / 崩溃日志；零外部请求；按 G4' 校验「i18n 外零硬编码文案 / 色值 / 修饰键 / 设置键名 / 状态名」。v0.9 的 `node --check` / `:root` 扫描属 HTML 原型专用，原生 UI 下改为构建产物 lint + 资源清单校验（§0 风险①） | X01/X02 / S05/S09/R36 改写 / 架构 G4' |
| **D 双端 · 4 类结构 100% 对齐** | 数据 schema | 分组 / 快捷项 / 剪贴板 / 设置的结构与字段两端一致 | N18 / §4.3 |
| | 快捷键语义 | `Alt↔⌥`、`Ctrl↔⌘` 映射与行为一致；12 项映射两端等价 | D02 / N18 |
| | 视觉规范 | 24 个尺寸 token + 19 个色值（含 `#9B6CF6`）两端一致；A/C/E/F 关键尺寸一致 | S06/S07/R29/E08 改写 / N18 |
| | 设置项结构 | 3 Tab、条目、默认值语义两端一致 | N08 / N18 |
| | 13 项白名单 / G5' | 13 项允许差异逐项核对；按 G5' 用**归一化布局 JSON 逐字段比对 + 原生截图 diff**，截图层仅白名单 #11 系统材质允许差异；平台相关引用点 ⊆ `contracts/platform-diffs.json`（G4'）；掩码外任何差异判缺陷 | D02–D06 / N21 / 架构 G4'、G5' |
| **E 阶段性差异清单** | 清单可核查 | 每项含类别（平台特有系统能力 / V1 增强项）、最长间隔（≤8 周 / ≤16 周）、到期日；关于页展示「双端功能对齐状态」 | N19 |
| | 清单外 0 差异 | 归一化后白名单外差异 0；清单内差异到期自动转缺陷 | D05/R35/parity 改写 / N21 |
| | 发布节奏与版本号 | W0 `0.9.x-win`、W8 `0.9.x-mac` 核心 100% 对齐、V1 起单一版本号 + 同发布日；W0 单端期 mac 快照显式标记「未发布」 | N20 / N21 / §4.4 |
| | 系统深度适配 | macOS 辅助功能 / 屏幕录制 / 完全磁盘访问流程、Win Everything 索引桥、高 DPI / 多屏边缘（≤8 周） | §4.2 / N19 |
| **F 空态与引导** | 3 类空态 | 快捷启动面板无项 / 剪贴板无记录 / 搜索无结果，各含「图标 + 一句文案 + 一个主行动按钮」且可点击 | E01/E02 改写 / N06 |
| | A4 三出口 | 「用浏览器搜索 / 清空重来 / 查看全部工具」三个出口可见可点且各有效 | V07/Q04 改写 / N05 |
| | Step3 系统权限 | 按平台只列本平台权限（Win 2 项 / mac 4 项）；无预置/示例插件；预置 5 组工具包默认全选 | D04 / N17 |
| | 系统权限未授权引导 | 未授予时给明确引导（「去授权」按钮 + 明确反馈），不静默失败 / 不黑屏 / 不空结果；mac 屏幕录制 `state='未授予'` 场景必测 | N14 / NFR-06 改写 |
| **G 工程 / 基线** | 事件委托完整 | 新 `data-act` 全集合（字面 + 动态）100% 有 `handleAct` 分支、无死按钮；插件链 28 个 action 全部消失 | S10/E03 改写 / N22 |
| | 负向：零插件 | 无 D 视图、无 5 类弹层、无插件结果组、无 `store.plugins/market/grants`、无权限网关函数 | N07 / §3.1 |
| | 基线可追溯 / G9 | `prototype-v2/` + `tests-v2/` + `docs/09-QA-Report-V2.md` 完全隔离另建；双口径哈希（构建产物/交付包）登记；布局 JSON + 截图 diff 基线按 §5.3 采集；实测报告与 CI 报告校验 `buildSha + contractHash` 一致（G9）；新结果写入新文件不覆盖冻结文件 | N24 / §4 / 架构 G9、§8.3-7 |
| | **NFR-03 · 披露分档判定（QA 侧）** | QA 读取 CI 报告 `thresholdUsed: 80\|150` / `fallbackApplied` / `measuredP95Mb` / `degradationSteps` / `disclosureLevel`（架构 §5.4 统一输出），产出 **PASS 三档（全部放行 W0）** + 一个正交的「**归档无效 · 退回补正**」：① `thresholdUsed=80` 且 `measuredP95Mb ≤ 80` → **PASS（默认线）**；② `measuredP95Mb > 80` 且 `fallbackApplied=true` 且 `thresholdUsed=150` 且 `measuredP95Mb ≤ 150` 且六档降级凭证齐全 → **PASS（备案线，放行 W0）**；③ `measuredP95Mb > 150` → **PASS（超线 · 升级披露），仍放行 W0**，但 QA 须校验：发布说明与「关于」页已写明「实测 X MB 已超出预定义备案线 150MB」、CI 报告带 `disclosureLevel: escalated`、`measuredP95Mb` 如实记录未裁剪；披露缺失则**归档无效 · 退回补正**，不影响 W0 放行。**触发条件凭证校验保留**：`fallbackApplied=true` 必须附「T02 首次实测报告 + 六档降级全部执行记录」，凭证缺失同样判**归档无效 · 退回补正**（不允许跳过降级直接套用 150MB 档）。内存数值本身不产生失败结论；**不定义 `P95` 的采样窗口、样本量、RSS 统计方法**（归 `docs/07 §5`） | 用户 P2 + D2 / PRD §6 NFR-03 / 架构 G-NFR03（非阻断） |
| | **NFR-03 · 报告绑定与数据完整性校验（QA 侧）** | 归档前校验实测报告与 CI 报告的 `buildSha + contractHash` 一致且与本次构建一致（对应架构 G9 契约哈希闸门）；字段 `thresholdUsed` / `fallbackApplied` / `measuredP95Mb` / `degradationSteps` / `disclosureLevel` 缺失、哈希不一致、或报告内部矛盾（`fallbackApplied=true` 但 `thresholdUsed=80`、备案线缺 `degradationSteps`、超线档缺 `disclosureLevel: escalated`、实测值被裁剪）→ **归档无效 · 退回补正**。这是**数据完整性校验、不是发布阻断**：仅要求补正归档材料，不影响 W0 放行，QA 不重新测量、不改写架构侧报告 | 用户 P2 + D2 / 架构 G-NFR03、G9、§5.4 |
| | **NFR-03 · 三档结果归档（QA 侧）** | 按 `tests-v2/perf/rss-{win,mac}.json` 分端归档；每份记录 `buildSha`、`contractHash` 与架构 §5.4 统一输出的字段 `thresholdUsed` / `fallbackApplied` / `measuredP95Mb` / **`degradationSteps`（六档降级执行记录）** / **`disclosureLevel`（`standard` \| `escalated`）**，再加 QA 判定结论、判定时间；**默认线 / 备案线 / 超线三类结果分别留档、不覆盖**，且 `measuredP95Mb` 必须如实归档、不裁剪、不改写，供发布说明与「关于」页披露核对。备案线 PASS 另标注「备案生效 / 技术栈未切换 / 不重选型」，超线 PASS 另标注「披露已升级 / 实测值 X MB」。三类归档均不构成发布判据 | 用户 P2 + D2 / 架构 §8.3-7、§5.4 |
| | 性能测量方法论（**本矩阵不写**） | 采样窗口、进程/线程范围、`P95` 统计口径、六档降级的具体内容与执行脚本、实测报告生成方式，全部归架构师 `docs/07 §5` 与 PRD Q5；QA 侧仅消费其结果 | 边界声明 / `docs/07 §5` |

---

## 8. 边界复核表与最大风险点

### 8.1 边界复核表

| 项 | 状态 | 证据 |
|---|---|---|
| 只新建 `docs/08-QA-Migration-Assessment.md` | ✅ | 本文件；`docs/` 目录评估前无 08 |
| 未修改 `prototype/index.html` | ✅ | 评估前后 md5 均 `1fc47e74727696c2fa21a9c7a104d293`（附录 A） |
| 未修改三个测试脚本 / 两份 results / `platform-refs.json` | ✅ | 评估前后 md5 逐一比对一致（附录 A） |
| 未修改 `tests/screenshots/*.png` 10 张 | ✅ | 仅 `readdirSync` + `statSync` 只读列举 |
| 未修改 `docs/03-QA-Report.md` | ✅ | 冻结归档，md5 `054d149c…` 前后一致 |
| 未修改 `docs/07-New-Architecture.md` | ✅ | 架构师 #8 新建、**#10 / #11 已先后修订定稿**；本评估 v1.4 仅只读引用其 §8 口径与章节结论，未写入，**不锁定其行数 / md5**（避免随对方修订而过期） |
| 未写技术选型结论 / 未写 NFR-03 性能测量脚本 · 采样方法论 | ✅ | §7 只写「披露分档 QA 判定与归档」（字段校验 / 触发条件凭证 / 三档结果分别留档 / 披露文案核对），并单列一行声明采样窗口、`P95` 统计、六档降级脚本归 `docs/07 §5` |
| NFR-03 按用户 P2 + D1/D2 写成披露分档 + 三档全放行 | ✅ | §7 工程闸门 4 行；披露分档 80/150，>150MB 仍放行 W0 并升级披露，**无阻塞规则**；三档结论全为 PASS，正交「归档无效 · 退回补正」不影响 W0 放行；未出现「80MB 不可放宽」「不达标即阻塞」措辞 |
| 与架构 §8 八条影响清单逐条对齐、无结论冲突 | ✅ | §1.4 对齐表（9 行）＋ 三条闸门落点；§3 / §5 / §5.3 / §6.1 措辞已收敛 |
| 10 张截图全部保留不删不覆盖 | ✅ | §5 保留声明；「必须重拍」= 在 `tests-v2/` 另拍，冻结原图零写入 |
| 冻结基线双口径均复核、结论未被改动 | ✅ | §1.2 / §4.1；`stat`/`md5`/Node 解码三方实测；v1.1 二次复算 9 文件 md5 全一致 |
| 140 条逐条给出编号与一句理由、四类小计可核查 | ✅ | §2.2–§2.5，74+34+32=140，另需新增 24 |
| 103 action / 109 case 两套口径全文自洽 | ✅ | §0-3 与 §3.4 统一为 case 口径 27/8/74；Node 只读实测 `103−3+9=109`；动态保留行由 4 修正为 3（去掉与 `tool-add` 的重复计数） |
| 未出现「需重新评估 / 后续再看」类空话 | ✅ | 所有条目给出具体编号、函数名、行号、action 名或期望值 |
| 未运行任何新用例、未重跑三条脚本 | ✅ | 本任务为只读评估，回归留待原型改造后（§4.2） |
| 未创建任何 `prototype-v2/` / `tests-v2/` / V2 报告文件 | ✅ | 与架构 §8.3-7 一致：双方本轮均只给建议、不落盘 v2 文件 |

### 8.2 最大风险点

**风险①（最高，已由架构定案确认为必现）·「保留可复用 74 条」被误当成零改动复制。**
74 条是**意图层**保留，其中 S01–S09、V01–V03、R03/R04/R06、E07、X01/X02 等约 20 条是对「**单文件 HTML 原型 + CDP `Fetch.fulfillRequest` 注入解码内容 + `node --check` + `:root` CSS token + DOM 选择器**」的实现级断言。这一风险在 v1.0 时是「若产物形态改变」的假设，**v1.1 已由架构 #8 定案确认为必现**：共享 Rust Core（C ABI）+ 各端原生 UI（Win Rust/Win32 自绘、mac Swift/AppKit）、**零 WebView**、零插件生态、单进程，用户 P1 确认 B 方案不变。因此这些断言的静态扫描方式、DOM 选择器、`Fetch.fulfillRequest` 注入机制**确定整体失效**，`tests/screenshots/` 的 Chrome CDP 采集链同样失效（§5.3 已按 G5' 改为「归一化布局 JSON 逐字段比对 + 原生截图 diff」）。必须按新产物形态重写为「构建产物/资源清单哈希 + 布局 JSON 字段断言 + 主题 token 清单」等替代闸门。**交接包必须写明：74 条 = 验收意图可带入 ≠ 代码可复制**，否则迁移后首次回归会大面积假阳性。

**风险② · 阶段性差异清单与「白名单外 0 差异」判据冲突。**
新方向一方面允许「平台特有系统能力 ≤8 周、V1 增强项 ≤16 周」的阶段性差异，另一方面要求 4 类结构（数据 schema / 快捷键语义 / 视觉规范 / 设置项结构）任何时刻 100% 对齐、任何差异视为缺陷。若直接把 D05 / R35 / `parity-supplement.js` 的「归一化后白名单外差异 0」搬过去，会**把清单内合法差异误判为缺陷**；若反过来把「清单」当成万能豁免，又会把越界差异藏进去放行。必须在断言层加入：① 清单项类别 / 最长间隔 / 到期日校验（N19）；② 清单内差异掩码且到期自动转缺陷；③ 4 类结构区域永不掩码、逐像素 0 差异；④ W0 单端期跳过第二端快照并显式标注（N21）。这是三种脚本里 D05 / R35 / parity 19 状态、D01、S12 共 6 条改写项的共同核心，也是最容易被低估的改写工作量。

---

## 附录 A · 冻结文件指纹（评估前实测，评估后复核一致）

| 文件 | 字节 | MD5 |
|---|---:|---|
| `prototype/index.html`（Node 解码口径） | 208,228 | `1fc47e74727696c2fa21a9c7a104d293` |
| `prototype/index.html`（物理 shell 口径） | 212,992 | `bee380603a586281c1745bb53a866c63` |
| `tests/prototype-smoke.js` | 76,269 | `363ee5db8f83870c735cde00adb2c67e` |
| `tests/fix-regression.js` | 38,419 | `cbf14340aab810a4614a5242adb50f09` |
| `tests/parity-supplement.js` | 8,764 | `9ce9252f272b9119821add0df01df676` |
| `tests/results.json` | 27,022 | `fcd14caafd16b1dd85af01c004ebcc9d` |
| `tests/fix-regression-results.json` | 12,873 | `f58f09e96c66bc4216288b681a8f4256` |
| `tests/platform-refs.json` | 4,880 | `92c44b108b59d7495cc189ce298f06ab` |
| `docs/03-QA-Report.md` | 49,400 | `054d149c003d9af5ade666b2402f146b` |

## 附录 B · 只读校验命令（供交接包复现，不修改任何文件）

```bash
cd /Users/chengzongxin/WorkBuddy/2026-11-06-16-08-00/deskkit
# 双口径
stat -f "physical_bytes=%z" prototype/index.html && md5 -q prototype/index.html
node -e 'const fs=require("fs"),c=require("crypto");const b=fs.readFileSync("prototype/index.html");console.log(b.length,c.createHash("md5").update(b).digest("hex"))'
# 冻结结果
node -e 'const s=require("./tests/results.json"),r=require("./tests/fix-regression-results.json");console.log(s.pass+"/"+s.total,r.pass+"/"+r.total,r.sourceMd5)'
# 103 字面 action / 109 case / 38 引用点 / 7 等值分支
node -e 'const fs=require("fs");const h=fs.readFileSync("prototype/index.html","utf8");console.log([...new Set([...h.matchAll(/data-act=[\\"\x27]*([A-Za-z0-9_-]+)/g)].map(m=>m[1]))].length)'
```
