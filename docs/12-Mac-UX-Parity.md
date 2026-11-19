# macOS 快捷启动交互补齐（待同步 Windows）

> 日期：2026-11-19
> 状态：macOS 与 Windows 都已按下面的行为实现。
> 上一批已经在 Windows 上有的能力（移动到分组、新建分组在侧栏底部、去掉底部「完成」）保持不变。

色值和时长只读 `contracts/tokens.json`、`contracts/layout.json`。提示文案只读 `contracts/i18n.json`。

## 这次在 macOS 上做了什么

| # | 行为 | macOS 落点 | Windows 还缺什么 |
|---|---|---|---|
| 1 | 切换分组、折叠、搜索时，窗口停在原地，内容区和侧栏的滚动位置保留 | `ShellUI.renderGroups` | `GroupsWindow.Render` 记下 `VerticalOffset`，画完后 `ScrollToVerticalOffset` |
| 2 | 窗口出现用 120ms 淡入，隐藏用 150ms 淡出 | `fadeIn` / `fadeOut` | `Chrome.FadeIn` / `FadeOut`，时长 `motionMs.show`、`motionMs.blurHide`。后一次动画使前一次收尾失效。截图前把透明度拉回 1 |
| 3 | 长按约 0.25 秒后图标抬起（放大 1.05、透明度 0.92），光标变成抓取，旁边出现提示 | `HitRow` + `trackShortcutMove` | 按住 250ms 后 `DoDragDrop`，`Lift` 放大，`GiveFeedback` 用手型光标，旁边显示提示 |
| 4 | 鼠标停在侧栏行或快捷项上时有浅底 | 侧栏 `accent.soft` 45%，快捷项 `bg.hover` | 侧栏 `MouseEnter` 用 `accent.soft` 的 45% 透明度，快捷项用 `bg.hover`。选中行保持选中色 |
| 5 | 编辑模式的抖动更小，并且会停 | `wobble`：左右约 0.86 度，0.14 秒，来回 6 次 | `GroupsWindow.Wobble`，同样幅度和次数 |
| 6 | 快捷启动里，上下键换分组，回车打开当前视图里的第一个快捷项 | `handleGroupsKey` | `OnPreviewKeyDown`。搜索框或重命名框有焦点时不抢走上下键和回车。Esc 先取消重命名，再关闭 |
| 7 | 编辑模式下，在同一分组里拖到另一个格子上可以改顺序。拖到别的分组仍然是移动 | `Session.reorderShortcut` | `Session.ReorderShortcut`。格子 `Tag` 为 `cell\|分组id\|快捷项id`。中线以上排到前面。载荷仍是 `deskkit-shortcut` |
| 8 | 新建分组追加在列表末尾，并立刻进入重命名、焦点在输入框 | `addGroup` 设置 `renamingGroupId` | `AddGroup` 在 `Render()` 之前设 `renaming`，输入框 `Loaded` 时获得焦点 |

## 提示文案

已写入 `contracts/i18n.json`：

| 键 | 文案 |
|---|---|
| `shortcut.dropMove` | 松手移动到「{name}」 |
| `shortcut.dropAfter` | 松手排到后面 |
| `shortcut.dropBefore` | 松手排到前面 |

`{name}` 换成目标分组名。没有落在可放置目标上时，提示隐藏。

## 拖动手势（两端要对齐）

1. 按下后 0.25 秒内松开：当成点击。非编辑模式打开该项；编辑模式不打开（删除按钮仍只删除）。
2. 按住超过 0.25 秒：进入拖动。编辑模式下，指针在同一分组的另一个格子上，提示「排到前面 / 后面」，松手调用组内排序。
3. 指针在其他分组的侧栏行或分组卡片上：提示「松手移动到「名称」」，松手调用移动。不能移到自己所在的分组，也不能放到「全部」。
4. 松手时没有目标：图标回到原样，数据不改。
5. 右键「移动到分组」保持现有子菜单，不替代拖动。

## Windows 落点

- `apps/windows/src/DeskKit.App/GroupsWindow.cs`：滚动位置、悬停、长按反馈、键盘、新建后聚焦、编辑态组内拖动。
- `apps/windows/src/DeskKit.App/Session.cs`：`ReorderShortcut`。先从该分组的 `itemIds` 里拿掉，再按锚点插入；锚点找不到就不动。
- `apps/windows/src/DeskKit.App/Chrome.cs` 与 `Shell.cs`：搜索窗和快捷启动的 120ms / 150ms 淡入淡出。
- 提示文案用上面的三个键，不写死在 C# 里。

## macOS 上怎么验收

1. 快捷启动滚到下面，再点另一个分组：窗口不跳回左上角，列表不回到最顶。
2. 呼出搜索窗、打开和关闭快捷启动：能看到短淡入、短淡出，而不是瞬间出现或消失。
3. 长按一个快捷项约四分之一秒：图标略放大，出现抓取光标和提示。点一下仍打开。
4. 鼠标划过「全部」、分组行、快捷项：有浅色悬停。选中分组保持选中色。
5. 点编辑：图标小幅晃几下后停下。删除按钮只删除。
6. 不在输入框里时，上下键换分组，回车打开第一项。搜索或重命名时，方向键留给文字。
7. 编辑模式下，把同一组里的图标拖到另一个图标上半部或下半部，顺序会变。拖到别的分组，该项挪过去。
8. 点「新建分组」：新分组在侧栏底部，名字立刻可改。
