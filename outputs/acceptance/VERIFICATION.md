# 真实端到端验收（2026-10-18 第二轮）

> 上一轮报告把"编译通过 + 有截图"当成端到端通过，属于过度声明。本轮从资源管理器**真实双击
> `publish/Pegged.exe`** 开始，用 UI 自动化断言窗口几何与交互结果。

## 执行方式

- 启动：`scripts/e2e/launch_probe.py` — 打开资源管理器窗口，定位 `Pegged.exe` 列表项，
  `double_click_input()`（真实鼠标双击），并**双击两次**验证单实例。
- 断言全部基于 `GetWindowRect` 物理坐标 / UIA 窗口文本，不依赖"进程是否在跑"。
- 结果：`outputs/acceptance/launch-probe.json`（证据 JSON，截图不入库）。

## 通过（8/8）

| 检查 | 结果 | 实测值 |
|---|---|---|
| `single_instance` | PASS | 双击两次，`processes == 1` |
| `line_window_exists` | PASS | 标题 `Pegged` 窗口存在 |
| `line_full_width` | PASS | 宽 1920 = 屏幕宽 |
| `line_rests_above_screen` | PASS | 收起态 `top=-308`，完全藏在屏幕上方 |
| `revealed_at_top_edge` | PASS | 光标移到顶边后展开并稳定在 `top=0` |
| `revealed_height` | PASS | 293px（含工具栏） |
| `pill_exists` / `pill_top_centred` | PASS | `(860,-1)-(1060,14)`，水平居中于 960 |
| `no_runtime_error_dialog` | PASS | 无 .NET 运行时错误弹窗 |

空状态截图（`02-revealed.png`，本机留档）确认：挂绳横跨顶部、亚克力带、
中文空状态提示"截一张图，它会挂到这里"。

## 本轮实测发现并已修复的阻断/行为缺陷

1. **双击启动后窗口不在屏幕顶部**（实测 `(96,96)` / `(32,32)`）— `PlaceOn()` 早于 `Show()`，
   `MoveTo()` 在句柄为 0 时静默返回。已修：显式 PerMonitorV2 + 句柄就绪后补定位 + 用目标屏 scale。
2. **默认设置下挂绳永不展开** — `IsFullScreen()` 枚举全屏窗口，Windows 常驻的
   Program Manager / IME 宿主永远铺满屏幕。已修：只判断前台窗口 + 跳过 cloaked/toolwindow。
3. **重复启动产生第二套窗口** — 已修：命名 Mutex 单实例。
4. **裁剪窗口 8 个手柄不可拖**（父级 `IsHitTestVisible=false`）+ 索引越界崩溃 — 已重写 `CropWindow`。
5. **拖拽按 Esc 会删除卡片**（取消被当成 Move）— 已修。
6. **全局快捷键随设置窗口关闭失效** + 失败时 hook 泄漏 — 已收归 `App` 常驻。
7. **空挂绳微风不播放**（`OnFrame` 早退）— 已修。
8. 感应条呼吸参数（4s/0.55-0.90 → 2.4s/0.35-0.75）、首启 4s→5s、`PlaceOn` 用物理 Bounds、
   裁剪输出扩展名归一化（webp 不会再写成 PNG 内容）。

已发布：`dotnet publish -c Release` → `publish/`，0 error 0 warning。

## 仍未完成 / 未验证（不得声称已完成）

- 截图"飞入"动画（`Flying` 初值在 `CardView` 订阅前已设置）——**未修复**。
- `CaptureStore.Prune` 已定义但全项目无调用：外部删除/移动文件后卡片不会自动脱落，需重启。
- 卡片照片 `UniformToFill` 会裁切图片，规范要求完整适配；白边 inset、衣夹尺寸与规范不一致。
- 点击复制未触发 3° 摆动；深色主题成功提示白字对比不足。
- 暗色卡片边框 `Brush.CardEdge` 未在 dark 主题覆盖。
- 剪贴板监听：隐私过滤（`ExcludeClipboardContentFromMonitorProcessing`）、原子写入、自身回环的
  加固尚未实现；`LineView` 复制是写文件路径而非图像数据（复制结果不是"图片"）。
- 真亚克力 / Mica 未实现（目前只是半透明色块，无 DWM backdrop 调用）。
- 语言切换只影响托盘与设置窗，`loc:Str` 是一次性文本，已打开的界面不刷新。
- 设置中心缺：额外监控文件夹 ≤3、Inbox 还原、录制中 Esc/Delete 状态提示、开源许可、Logo。
- 托盘菜单缺：接管剪贴板、展开条件、监控文件夹、保留数量、尺寸、快捷键等入口，ToolTip 恒为 "Pegged"。
- 多显示器不同缩放的命中/移动、`WM_NCHITTEST` 点击穿透、剪贴板/裁剪/拖拽的交互结果均未做实机验证。
