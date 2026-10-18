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
- [x] Module 2: 多显示器顶部边缘探测、无焦点透明置顶窗口、60fps 弹簧展开/收起动画
- [x] Module 3: 挂绳、夹子、图片/视频卡片渲染、钟摆摇晃物理引擎
- [x] Module 4: 截图目录监控器（文件锁指数退避重试）+ 剪贴板图像监听器（隐私过滤与防回环）
- [x] Module 5: OLE 拖拽引擎（Copy/Move 语义区分）、单击复制反馈、双击预览、快速裁剪窗口
- [x] Module 6: Fluent 设置中心窗口、系统托盘菜单、`RegisterHotKey` 全局热键、开机自启

## Phase 4 — 打包发布、文档脱敏与复盘
- [x] `dotnet publish -c Release -r win-x64 --self-contained false -o ./publish`
- [x] 删除 `_reference_macos/`，`AGENTS.md` 改为日常维护规范，完善 `README.md`
- [x] 最终 git commit & push
- [x] 输出完整复盘报告

## Phase 5 — UI 设计目标与实际效果核对（追加验证）
- [x] 端到端自动测试：启动 → 边缘唤醒 → 挂卡 → 悬停 → 复制 → 预览 → 裁剪 → 设置 → 收回
- [x] 修复：双击预览窗口被遮挡（改 CenterScreen + Topmost，移除 Owner 绑定）
- [x] 修复：卡片悬停镀铬不完整（添加 Copy/Crop/Delete 底部操作行 + 衣夹悬停张开动画）
- [x] 修复：裁剪窗口非全屏遮罩（重写为 Maximized + 33% 九宫格白线 + 底部浮动操作栏）
- [x] 修复：设置窗口尺寸与导航不符合规范（720×560 / 240px 左栏 / 图标+副标题导航 / 28px 页头）
- [x] 修复：空状态绳子无微风 + 边缘胶囊无流光（添加 `_breezePhase` 摆动 + 流光 GradientStop 滑动）
- [x] 重新发布 Release 构建（验证 0 Error 0 Warning）

## Phase 6 — 真实端到端验收与闭环（第三轮）
- [x] `scripts/e2e/launch_probe.py`：资源管理器真实双击 exe，断言单实例 / 贴顶 / 全宽 / 收起藏于屏幕上方 / 展开 293px / 感应条居中（9/9 PASS）
- [x] `scripts/e2e/interaction_probe.py`：`WM_NCHITTEST` 逐点穿透、工具栏可点、设置中心打开（720×560）、语言实时刷新（5/5 PASS）
- [x] 修复：舱体亚克力改为真实"抓屏 + 高斯模糊 + 着色 + 噪点"（`Core/AcrylicBackdrop.cs`）
- [x] 修复：`WM_NCHITTEST` 逐点点击穿透（取代整窗 `WS_EX_TRANSPARENT`），快捷操作栏恢复可点
- [x] 修复：卡片白边 4px / 圆角 16-12 同心 / 照片 `Uniform` 等比适配
- [x] 修复：衣夹 9×26 + 5×1.4 系绳槽 + 13×13 弹簧箍，悬停张开按规范
- [x] 修复：托盘 ToolTip 播报状态 + 剪贴板图标态 + 补全菜单入口（剪贴板/展开条件/保留数量/尺寸）
- [x] 新增：设置中心 Inbox 模式（重定向 Screenshots 已知文件夹）+ 还原按钮
- [x] 修复：`{loc:Str}` 改为活绑定，切换语言时已打开界面即时刷新
- [x] 修复：`LangItem` 等选项类型 `ToString()` 导致下拉框/UIA 显示 `record` 全字段
- [x] 新增：未处理异常写 `%APPDATA%\Pegged\crash.log`
- [x] 剪贴板监听加固：隐私格式跳过、64MB 上限、SHA256 去重、`.part` 原子落盘
- [x] 外部删除/移动文件后 `CaptureStore.Prune()` 已由 `FileSystemWatcher` 触发
- [x] 重新发布 Release 构建（0 Error 0 Warning）
- [x] 修复：`CaptureStore.OnChanged()` 与 `LineController.Peek()` 的跨线程访问（后台线程改 WPF 窗口导致静默闪退）
- [x] 压测：连续挂 3 张 + 单张下架 + 全部清空 + 外部删除触发剪枝，`%APPDATA%\Pegged\crash.log` 保持为空

## Phase 6 — 全量交互走查（第四轮，真跑真点）
- [x] 新增 `scripts/e2e/audit_walkthrough.py`：四区 33 项真实交互断言（dock 8 / card 11 / crop 4 / settings 9 / 无崩溃 1）
- [x] 修复：主题切换后舱体玻璃不变色（`AcrylicMaterial` 缓存 + 主题变化就地重上色 + alpha 取调色板）
- [x] 修复：卡片尺寸改变后舱体高度不变（`LineController` 订阅 `Settings.Saved` 重排）
- [x] 修复：`ToggleSwitch` 双 automation id（外层取消 automation peer）
- [x] 修复：Inbox 模式在无 `FOLDERID_Screenshots` 的系统上弹模态框并吞掉下一次点击 → 探测 + 置灰 + 行内说明
- [x] 修复：预览窗口标题显示 `Preview.Title`（补 en/zh 资源键）
- [x] 修复：裁剪浮层「取消 / 应用裁剪」补 `AutomationId`，手柄拖拽首次被真实验证（`608×384 → 549×337`）
- [x] 修复：验收脚本三处自欺（按句柄判定窗口、探针收尾关窗、按内容而非标题找设置窗）
- [x] 清理：移除全部 `InputTrace` 追踪与 `PEGGED_TRACE`，删除 12 个一次性 `diag_*.py`
- [x] 交付：`docs/REVIEW-CHECKLIST.md`（审查清单：缺陷、证据、覆盖矩阵、未验证项）
- [x] 回归：`launch_probe` 9/9、`interaction_probe` 5/5、`audit_walkthrough` 33/33，`crash.log` 为空
- [x] 重新发布 Release 构建（0 Error 0 Warning）

## Phase 6 — 用户报障修复（挂 2 张以上时第一张很难点选复制）
- [x] 复现：新增 `scripts/e2e/repro_first_card_click.py`（逐点真点 + 剪贴板断言，2 张 / 3 张×网格）
- [x] 定位：`UpdateHint()` 只置 `Opacity=0`，而 WPF 中透明元素**依然吃点击**；居中提示正好压在第一张卡上
- [x] 修复：`Hint` 改 `Visibility` 显隐 + `IsHitTestVisible=False`；`NameBar`/`ActionRowBg` 同源收紧
- [x] 回归：新增 `card.first_card_clickable`；audit 34/34、launch 9/9、interaction 5/5，crash.log 为空
- [x] 清理：删除 `diag_line_tree.py` / `diag_point_owner.py` 一次性脚本
- [x] 交付：`docs/REVIEW-CHECKLIST.md` 增补 1.1 节；`TECHNICAL_DECISIONS.md` 第 88 条
