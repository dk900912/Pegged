# Project Constitution & Autonomous Execution Rules (AGENTS.md)

## 0. 状态感知与进度恢复协议（每次响应前必读）
你正在执行将 `https://github.com/alejandrobujan/tendedero` (`v1.5.0`) 100% 原生重构至 Windows 11 平台并重塑品牌的完整交付任务。
在执行任何操作前，先通过文件系统判断当前所处阶段：
1. 若本地不存在 `PORTING_CHECKLIST.md`：从 **Phase 1** 开始初始化。
2. 若本地已存在 `PORTING_CHECKLIST.md`：读取其中第一个未勾选的 `[ ]` 条目，从中断处继续向下执行，**严禁重复执行已完成的建仓或初始化步骤**。
3. 只有在 `PORTING_CHECKLIST.md` 全部为 `[x]` 且完成 **Phase 4** 最终推送与终端汇报后，才允许停止工作。

---

## 1. ⛔ 核心执行铁律（永不妥协）
- **严禁中途停机请示**：所有品牌命名、Logo 视觉、UI 细节、技术选型及编译报错修复均由你自主拍板并落地，绝不向用户提问“是否继续”或“方案是否可行”。
- **严禁留白与伪代码**：所有 C# 逻辑与 WPF XAML 样式必须 100% 完整实现，严禁出现 `// TODO`、`// 省略` 或简化版占位实现。
- **难点与决策全程留痕**：迁移过程中的所有跨平台难点、备选方案对比、最终决策及编译排障过程，必须实时维护在 `docs/TECHNICAL_DECISIONS.md` 中。
- **模块级编译与 Git 闭环**：每完成一个阶段或模块，必须执行 `dotnet build` 确保 `0 Error`，更新 `PORTING_CHECKLIST.md` 与 `docs/TECHNICAL_DECISIONS.md`，并立即执行 `git add -A`、`git commit` 和 `git push origin main`。
- **参考源码隔离**：原版 macOS 源码必须存放在 `_reference_macos/` 目录并写入 `.gitignore`，严禁推送到远端 GitHub 仓库。

---

## 2. 🏗️ 钦定技术栈与 6 大跨平台攻关规范
采用 **C# (.NET 8) + WPF + Win32 P/Invoke** 开发，必须完整攻克并在 `docs/TECHNICAL_DECISIONS.md` 记录以下核心难点：
1. **无焦点置顶与多屏边缘唤醒**：结合 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` 与顶部热区探测，交互时不抢占当前活动窗口焦点，支持多显示器 PerMonitorV2 DPI 无缝切换。
2. **高帧率弹簧与钟摆物理引擎**：基于 `CompositionTarget.Rendering` 实现胡克定律弹簧阻尼滑出动画，以及卡片挂上/拖拽时的物理钟摆摇晃（Pendulum Swing）惯性动效。
3. **截图文件锁竞争处理**：针对 Windows 截图异步落盘延迟，在 `FileSystemWatcher` 中实现独占锁检测与指数退避重试，杜绝读取半截图片或触发 `IOException`。
4. **剪贴板监听与隐私过滤**：使用 `AddClipboardFormatListener` 捕获复制图像（含透明通道转换），过滤密码管理器内容及自身复制引发的回环。
5. **OLE 原生拖拽语义区分**：通过 `DragDrop.DoDragDrop` 精确区分拖入聊天/文档软件（`Copy`，保留在绳上）与拖入资源管理器文件夹（`Move`，移动文件并从绳上取下）。
6. **内联快速裁剪与系统标注联动**：内置轻量级框选裁剪弹窗，并支持拉起系统画图/截图工具。

---

## 3. 🎨 全套 UI 视觉系统规范（Windows 11 Fluent Design 2.0）
必须在 `docs/UI_DESIGN_SPEC.md`、单文件可视化看板 `docs/ui-design-showcase.html` 以及 WPF `Themes/FluentTheme.xaml` 中完整实现以下 7 大组件（支持深浅色主题）：
1. **顶部收起态微光感应条（Collapsed Edge Pill）**：顶部边缘 4px 呼吸感流光提示条。
2. **展开态顶部悬挂舱（Expanded Hanging Dock）**：亚克力毛玻璃背景、贯穿式高光挂绳、顶部快捷操作栏（一键清空、固定图钉、设置入口）。
3. **精致夹子与卡片组件（Clothespin & Media Card）**：夹子多状态（默认、悬停张开、夹紧下压）、拍立得圆角卡片、视频时长与播放角标、悬停操作浮层（复制、裁剪、删除）、单击“Copied!”微交互反馈徽章。
4. **空状态引导视图（Empty State）**：绳索轻摆与快捷键/截图引导插画。
5. **内联快速裁剪弹窗（Quick Crop Overlay）**：九宫格裁剪框、像素尺寸实时显示、毛玻璃确认/取消浮动栏。
6. **现代化设置中心窗口（Fluent Settings Window）**：Win11 风格卡片式导航界面（常规、捕获源、外观与显示器、快捷键绑定、关于）。
7. **系统托盘右键菜单（Fluent Context Menu）**：圆角阴影与图标对齐的原生托盘菜单。

---

## 4. 🔄 标准自动化阶段定义

### [Phase 1] 源码审计、全新品牌/Logo 生成与 GitHub 建仓
1. 克隆 `https://github.com/alejandrobujan/tendedero` (`v1.5.0`) 到 `_reference_macos/`，完整拆解所有 `.swift` 源码与多语言词条。
2. 自主构思简短高级的英文 App 名称（先通过 `gh repo view` 确认当前 GitHub 账号下无同名冲突）。
3. 生成矢量 `assets/logo.svg`，编写自动化脚本渲染出 `assets/logo.png` (256x256) 与标准多尺寸 `assets/app.ico` (16/32/48/256px)，绑定至 `.csproj`。
4. 配置 `.gitignore`（忽略 `_reference_macos/`、`bin/`、`obj/`、`.vs/`），创建包含 Phase 1 至 Phase 4 详细勾选条目的 `PORTING_CHECKLIST.md`。
5. 执行 `git init`、首次 Commit，并调用 `gh repo create <NewAppName> --public --source=. --remote=origin --push` 完成建仓与首次推送。

### [Phase 2] 全套 UI 视觉看板与跨平台难点决策建档
1. 撰写 `docs/TECHNICAL_DECISIONS.md`：以表格和架构图对比 macOS 原版机制、Windows 11 阻碍、备选方案优劣及最终工程决策。
2. 撰写 `docs/UI_DESIGN_SPEC.md` 并编写单文件交互式预览画板 `docs/ui-design-showcase.html`（内嵌 CSS/SVG，完整可视化渲染上述 7 大 UI 组件、深浅色 Token 与动画曲线，确保浏览器双击即可直观预览）。
3. 勾选 `PORTING_CHECKLIST.md` 对应项，执行 `git add`、`git commit` 与 `git push`。

### [Phase 3] 模块化开发、XAML 落地、编译自愈与 Git 闭环
严格按顺序逐一闭环以下 6 个模块（每完成一个模块必须：`dotnet build` 零报错 -> 追加实战排障记录至 `docs/TECHNICAL_DECISIONS.md` -> 勾选 `PORTING_CHECKLIST.md` -> `git commit & push`）：
- **Module 1**: 项目脚手架、配置持久化、多语言词条完整迁移（含简中、英、西等）及全套 `Themes/FluentTheme.xaml` 样式字典落地。
- **Module 2**: 多显示器顶部边缘探测、无焦点透明置顶窗口与 60fps 弹簧展开/收起物理动画。
- **Module 3**: 挂绳、夹子、图片/视频卡片 UI 渲染及挂上/拖拽时的钟摆摇晃物理引擎。
- **Module 4**: 截图目录监控器（含文件锁重试策略）与剪贴板图像监听器（含隐私过滤与防回环）。
- **Module 5**: 原生 OLE 拖拽引擎（区分复制到应用 vs 移动到文件夹）、单击复制反馈、双击预览与内置快速裁剪窗口。
- **Module 6**: Fluent 设置中心窗口、系统托盘菜单、全局热键 (`RegisterHotKey`) 与开机自启管理。

### [Phase 4] 打包发布、文档脱敏与全景复盘汇报
1. 执行 `dotnet publish -c Release -r win-x64 --self-contained false -o ./publish` 生成最终可执行程序。
2. 删除 `_reference_macos/` 临时目录，将 `AGENTS.md` 更新为新项目日常维护规范（移除迁移引导指令），完善 `README.md`（包含新品牌 Logo、功能特性、UI 看板说明与构建指南），执行最终 `git commit & push`。
3. 在对话框中向用户输出完整复盘报告：
   - 新 App 名称、Logo 设计理念、GitHub 仓库链接、`.exe` 路径与 `docs/ui-design-showcase.html` 路径。
   - 核心技术难点与工程决策逐条复盘。
   - 全套 7 大 UI 组件视觉架构与交互细节总览。
