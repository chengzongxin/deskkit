# 已归档：Rust + Win32 方案

全文在 [`docs/archive/07-New-Architecture.md`](archive/07-New-Architecture.md)。其中「不能用 WPF」「共享 Rust Core」已被后来的决定覆盖。

现行架构见 [`docs/10-Current-Architecture.md`](10-Current-Architecture.md)：Windows 为 C++20 / Dear ImGui / DirectX 11，工程在 `apps/windows`。macOS 为 Swift + AppKit，不设 Rust 核心。产品范围仍以 [`docs/06-Incremental-PRD.md`](06-Incremental-PRD.md) 为准。
