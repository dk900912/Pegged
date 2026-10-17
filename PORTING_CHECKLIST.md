# Pegged — Windows 11 Porting Checklist

100% 原生重构自 `https://github.com/alejandrobujan/tendedero` (v1.5.0, macOS/Swift)。
技术栈：C# (.NET 8) + WPF + Win32 P/Invoke。新品牌：**Pegged**。
规则见 `AGENTS.md`：逐项闭环，每模块 `dotnet build` 0 Error 后 commit & push。

## Phase 1 — 源码审计、品牌/Logo 生成与 GitHub 建仓
- [x] 克隆参考源码 v1.5.0 至 `_reference_macos/` 并完整审计（18 个 Swift 源文件、10 语言词条）
- [x] 构思新 App 名称 **Pegged**（`gh repo view dk900912/*` 确认账号下无同名冲突）
- [x] 生成 `assets/logo.svg`、`assets/logo.png` (256x256)、`assets/app.ico` (16/24/32/48/64/256px)
- [x] 编写可复现渲染脚本 `scripts/render-logo.py`
- [x] 配置 `.gitignore`（`_reference_macos/`、`bin/`、`obj/`、`.vs/`）
- [x] 创建本 `PORTING_CHECKLIST.md`
- [x] `git init` + 首次 Commit + `gh repo create dk900912/Pegged --public --push`

## Phase 2 — 技术决策建档与 UI 设计看板
- [x] `docs/TECHNICAL_DECISIONS.md`（6 大跨平台难点对比与决策）
- [x] `docs/UI_DESIGN_SPEC.md`
- [x] `docs/ui-design-showcase.html`（单文件可视化看板，7 大组件 + 深浅色 Token + 动画曲线）
- [x] git commit & push

## Phase 3 — 模块化开发（每模块 build 0 Error → 记录 → 勾选 → commit & push）
- [x] Module 1: 项目脚手架、配置持久化、多语言词条完整迁移（简中/英/西等 10 语言）、`Themes/FluentTheme.xaml`
- [ ] Module 2: 多显示器顶部边缘探测、无焦点透明置顶窗口、60fps 弹簧展开/收起动画
- [ ] Module 3: 挂绳、夹子、图片/视频卡片渲染、钟摆摇晃物理引擎
- [ ] Module 4: 截图目录监控器（文件锁指数退避重试）+ 剪贴板图像监听器（隐私过滤与防回环）
- [ ] Module 5: OLE 拖拽引擎（Copy/Move 语义区分）、单击复制反馈、双击预览、快速裁剪窗口
- [ ] Module 6: Fluent 设置中心窗口、系统托盘菜单、`RegisterHotKey` 全局热键、开机自启

## Phase 4 — 打包发布、文档脱敏与复盘
- [ ] `dotnet publish -c Release -r win-x64 --self-contained false -o ./publish`
- [ ] 删除 `_reference_macos/`，`AGENTS.md` 改为日常维护规范，完善 `README.md`
- [ ] 最终 git commit & push
- [ ] 输出完整复盘报告
