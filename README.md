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

| 端 | 栈 | 目录 |
|---|---|---|
| Windows | C++20 / Dear ImGui / DirectX 11 | `apps/windows` |
| macOS | Swift + AppKit | `apps/macos` |

没有插件运行时，没有 WebView，没有共享 Rust 核心。仓库里只有这一份 Windows 工程。

## macOS

```bash
apps/macos/build-app.sh
open apps/macos/build/DeskKit.app
```

第一次会打开欢迎窗口。点「授予辅助功能」，在系统设置里允许 DeskKit，然后点「开始使用」。之后用 `⌥+Space` 呼出。菜单栏上的 D 也可以打开。日志在 `~/Library/Application Support/DeskKit/logs/deskkit.log`。

## Windows

在 `apps/windows` 用 CMake 生成。需要能编译 C++20 的工具链，以及 DirectX 11 的库（Windows SDK 或 MinGW 自带）。

```powershell
cmake -S apps/windows -B apps/windows/build -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build apps/windows/build --target deskkit
apps/windows/build/deskkit.exe
```

逻辑测试不弹出窗口：

```powershell
cmake --build apps/windows/build --target deskkit_tests
apps/windows/build/deskkit_tests.exe
```

从仓库根目录启动，程序才能找到 `contracts/`。启动后不显示窗口。按 `Alt+Space` 呼出，再按一次或逐级 `Esc` 退回后隐藏。右键托盘图标可以退出。日志写在 `%APPDATA%\DeskKit\logs\deskkit.log`。

便携包：

```powershell
.\scripts\publish-win.ps1 -Zip
```
