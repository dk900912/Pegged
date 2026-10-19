# scratch — 两个最小复现工程

不是产品代码，是排障时用来**证明**根因的最小工程。留着是因为它们各自复现一个非常容易再踩的坑，重写一次要花半小时。

| 目录 | 复现什么 | 结论 |
|---|---|---|
| `themetest/` | 浅色字典里写 `<SolidColorBrush Color="{DynamicResource Palette.Card}"/>`，再把暗色字典 merge 到顶层并重定义 `Palette.Card` | 画刷**不会**重算——嵌套 `DynamicResource` 只在解析该字典时求值一次。所以 `Themes/FluentTheme.Dark.xaml` 必须整份重定义所有 `Brush.*`（TECHNICAL_DECISIONS 90） |
| `trayprobe/` | 反射加载 `publish/Pegged.dll`，用 `RuntimeHelpers.GetUninitializedObject` 造一个不跑 `OnStartup` 的 `App`，直接调 `TrayService.BuildMenu()` 并把 `ContextMenuStrip` 渲染成 PNG | 用于核对托盘菜单的图标字形与项目顺序。**不要**改成 `new App()`——那会把真实的启动流程跑进探针进程，然后因为 pack URI 解析失败写一条假的 `crash.log` |

`bin/`、`obj/`、`run/` 都是构建输出，已忽略。
