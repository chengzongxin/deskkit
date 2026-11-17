# DeskKit

一个键呼出的桌面启动器。Windows 先行，macOS 随后对齐。

## 开工先读

1. [`docs/10-Current-Architecture.md`](docs/10-Current-Architecture.md)：技术栈、目录、任务顺序。
2. [`docs/06-Incremental-PRD.md`](docs/06-Incremental-PRD.md)：留下和砍掉的功能。
3. [`docs/01-PRD.md`](docs/01-PRD.md) 第 5、6 章：尺寸、配色、键盘。被 06 改写的地方以 06 为准。
4. [`handoff/PROTOTYPE-SPEC.md`](handoff/PROTOTYPE-SPEC.md) 第 2、3、5、7 章：状态机、键盘、色值、工具算法。

`prototype/index.html` 可以双击看布局。它仍包含已取消的插件市场，不能当功能清单。

`docs/archive/` 里是作废的 Tauri 方案和 Rust + Win32 方案。不要按那些文档写代码。

## 技术栈

| 端 | 栈 | 何时 |
|---|---|---|
| Windows | C# / .NET 8 / WPF | 第一版 |
| macOS | Swift + AppKit | 最晚 8 周后对齐核心功能 |

没有插件运行时，没有 WebView，没有共享 Rust 核心。

## 本机环境（2026-11-16）

这台机器是 macOS，已有 Xcode 26.4.1 和 Swift 6.3.1。NET 8 SDK 装在 `~/.dotnet`。如果终端找不到 `dotnet`，先执行：

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

WPF 工程在 Mac 上可以编译，但不能弹出窗口。呼出热键要到 Windows 上验收。

## macOS

```bash
apps/macos/build-app.sh
open apps/macos/build/DeskKit.app
```

第一次会打开欢迎窗口。点「授予辅助功能」，在系统设置里允许 DeskKit，然后点「开始使用」。之后用 `⌥+Space` 呼出。菜单栏上的 D 也可以打开。日志在 `~/Library/Application Support/DeskKit/logs/deskkit.log`。

## 构建

Core 测试在 macOS 上可以跑：

```bash
dotnet test apps/windows/tests/DeskKit.Core.Tests
```

主程序是 WPF，只能在 Windows 上编译和运行：

```bash
dotnet run --project apps/windows/src/DeskKit.App
```

启动后不显示窗口。按 `Alt+Space` 呼出，再按一次或 `Esc` 隐藏。右键托盘图标可以退出。日志写在 `%APPDATA%\DeskKit\logs\deskkit.log`。

## 下一步

T01 的契约、状态机和空浮层已经在仓库里。下一步是 T02：拼音搜索和五组内置工具。
