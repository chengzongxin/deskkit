# 已归档：旧原型断言迁移评估

全文在 [`docs/archive/08-QA-Migration-Assessment.md`](archive/08-QA-Migration-Assessment.md)。正文按 Rust + Win32 方案写成，其中「80MB 否决 WPF」不再有效。

仍成立的结论：

- 不要改 `prototype/index.html` 和 `tests/`，也不要重跑那 140 条断言。
- 这 140 条里，意图上可保留 74、需改写 34、必须删除 32，另需新增 24。删除的是插件市场、插件权限和断网市场。
- 新界面没有 DOM。不要把 Chrome CDP 和 `getComputedStyle` 套到 WPF / AppKit 上。

现行架构见 [`docs/10-Current-Architecture.md`](10-Current-Architecture.md)。
