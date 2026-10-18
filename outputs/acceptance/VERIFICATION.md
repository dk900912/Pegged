# 真实端到端验收（2026-10-18 第三轮）

> 本轮的判断标准只有一条：**能不能被自动化的断言证伪**。凡是"我改好了"但拿不出断言的，
> 一律写在最后"仍未验证"里，不计入通过。

## 执行方式

| 脚本 | 做什么 | 结果 |
|---|---|---|
| `scripts/e2e/launch_probe.py` | 打开资源管理器、定位 `publish/Pegged.exe`、`double_click_input()` **双击两次**（验单实例），光标移到顶边，轮询 `GetWindowRect` 直到两次采样一致 | **9/9 PASS** |
| `scripts/e2e/interaction_probe.py` | 直接向窗口发 `WM_NCHITTEST` 询问命中结果（Windows 派发点击前问的就是这个）；再真实 `mouse_event` 点齿轮；再用 UI 自动化切语言 | **5/5 PASS** |

证据：`outputs/acceptance/launch-probe.json`、`outputs/acceptance/interaction-probe.json`。

## 通过（14 项）

| 检查 | 结果 | 实测值 |
|---|---|---|
| `single_instance` | PASS | 双击两次，进程数 1 |
| `line_window_exists` / `line_full_width` | PASS | `(0,0)-(1920,293)`，宽 1920 = 屏幕宽 |
| `line_rests_above_screen` | PASS | 收起态 `top=-308`，完全藏在屏幕上方 |
| `revealed_at_top_edge` | PASS | 展开后稳定在 `top=0`（连续两次采样一致） |
| `revealed_height` | PASS | 293px（含工具栏） |
| `pill_exists` / `pill_top_centred` | PASS | `(860,-1)-(1060,14)`，水平居中 |
| `no_runtime_error_dialog` | PASS | 无 .NET 运行时错误弹窗 |
| `click_through_empty` | PASS | 舱体空白处 `WM_NCHITTEST → HTTRANSPARENT(-1)` |
| `toolbar_clickable` | PASS | 工具栏横向 889–1030 全部 `HTCLIENT(1)` |
| `settings_opened` | PASS | 真实点击齿轮 → `(160,160)-(1060,860)` = 720×560 DIP |
| `language_refresh` | PASS | 设置窗不重开，导航文案 `捕获源` → `Capture sources` |

另有两项通过人工看图确认（截图留在 `outputs/acceptance/shots/`，不入库）：

- **裁剪浮层**（`crop.png`）：1920×1200 全屏遮罩、图片按原比例居中、33% 九宫格、
  4 个角手柄（12px）+ 4 个边手柄（8px）全部可见、选框上方实时尺寸 `480 × 720`、
  底部毛玻璃栏（尺寸 + 取消 + Accent 填充的"应用裁剪"）。
- **深色主题**（`dark.png`）：舱体为深色雾面（`Bg.Acrylic` 72%）、卡片深色相框、
  白边描边可见、挂绳/工具栏/衣夹配色随主题切换。

## 本轮实测发现并已修复的缺陷（10 项）

1. **舱体"亚克力"是假模糊** — 只有一个半透明色块。分层窗口拿不到 DWM backdrop，改为
   自建材质：舱体还在屏幕外时抓一次背后桌面 → 降采样 + 两遍盒式模糊 + 双三次放大 →
   `Bg.Acrylic` 78% 着色 → 逐像素噪点。截图可见 Explorer 内容在舱体后变成柔和的雾面。
2. **第一版抓屏模糊是纯噪点 / 全透明** — `GetHbitmap` 丢 alpha；GDI+ 往 32bppPArgb 上画
   忽略笔刷 alpha，噪点 `TextureBrush` 把整张图盖死。改 `LockBits` + `Bgr32` + 逐像素噪点。
3. **快捷操作栏（清空/固定/设置）完全点不动** — 整窗 `WS_EX_TRANSPARENT` 把自己的按钮
   一起穿透了。改 `WM_NCHITTEST` 逐点判定：只有卡片和可见的工具栏是 `HTCLIENT`。
   副作用修好：空舱体区域不再挡住下面应用的点击。
4. **卡片照片被裁切、没有白边** — `UniformToFill` → `Uniform`；加 4px 白边相框，
   外圆角 16 / 内圆角 12 同心。
5. **衣夹尺寸与规范不符** — 改为 9×26 木色胶囊 + 5×1.4 系绳槽 + 13×13 金属箍，
   悬停时箍下移 2px、两木片间距 +2px。
6. **托盘 ToolTip 恒为 "Pegged"、剪贴板态无图标** — ToolTip 播报"挂了 N 张 / 空"+剪贴板
   状态；接管剪贴板时图标右下角出现实心 Accent 圆点。菜单补齐剪贴板/展开条件/保留数量/尺寸。
7. **设置中心缺 Inbox 模式** — 用 Windows 的 Screenshots 已知文件夹做等价实现，保存原路径、
   关闭或退出时还原，失败回滚开关。
8. **切换语言后已打开界面不刷新** — `{loc:Str}` 从"一次性字符串"改为"指向
   `Strings.Current[Key]` 的绑定"，一次 `PropertyChanged("Item[]")` 全部重读。
9. **语言下拉框显示 `LangItem { Name = …, Culture = … }`** — `record` 的自动 `ToString()`
   被 ComboBox 当作自动化名。四个选项类型全部 `override ToString()`。
10. **验收脚本自己的坐标错乱** — 探针 DPI-unaware，`GetWindowRect` 是缩放后的值而
    `SetCursorPos` 是物理像素，点击落到了穿透区。探针启动时 `SetProcessDpiAwareness(2)`。

### 加完 crash.log 之后立刻抓到的两个静默闪退（本轮修复）

| 缺陷 | 根因 | 修法 |
|---|---|---|
| 下架/清空偶发闪退，日志一片空白 | `CaptureStore.Drop` 的收尾在 `Task.Delay().ContinueWith(..., TaskScheduler.Current)` 里；`Drop` 从非 UI 线程调用时延续落到线程池，`Changed` 从后台线程触发，`LineWindow.UpdateHint()` 碰 `CardLayer.Children` 抛跨线程异常，又被当作未观察任务异常吞掉 | `CaptureStore.OnChanged()` 统一 `Dispatcher.BeginInvoke` |
| **每来一张新截图**都触发一次跨线程异常 | `ScreenshotWatcher.TryHangAsync` 在 watcher 线程上 `Hang()` 后直接 `Peek()`，而 `Peek → Reveal → PlaceOn` 会设置窗口 `Width`/`Height` | `LineController.Peek()` 入口判断 `Dispatcher.CheckAccess()`，否则 `BeginInvoke` |

压测脚本（连续挂 3 张 → 点卡片 `×` 下架 → 点工具栏清空 → 外部删除文件触发剪枝）跑完后
`%APPDATA%\Pegged\crash.log` **保持为空**；修复前同样的操作会稳定留下两条栈。

另外：未处理异常现在写 `%APPDATA%\Pegged\crash.log`（覆盖层应用崩溃后什么都不留）。

## 仍未验证（不得声称已完成）

- **多显示器 + 混合缩放**：本机只有一块 1920×1200 @125% 的屏幕，跨屏移动、不同缩放下的
  命中/移动只能靠代码逻辑保证（`MoveTo` 用目标屏 `scale`、`MonitorService.Physical` 比较），
  **没有实机断言**。
- **托盘 ToolTip 与图标态**：`NotifyIcon` 的 ToolTip 文本和 HICON 无法通过 UI 自动化读回，
  只做了代码路径验证，**没有截图级证据**。
- **Inbox 模式的实际重定向**：会修改系统 Screenshots 已知文件夹，在验收环境里没有真的
  打开再还原一遍（避免留下副作用），**只有代码路径与回滚逻辑**。
- **裁剪浮层 8 手柄的拖动**：手柄与九宫格已按规范渲染（见 `crop.png`），但"按住手柄
  拖动改变选框"这一步本轮没有做鼠标断言（上一轮修的是 `IsHitTestVisible` 与索引越界）。
- **OLE 拖出到资源管理器**：Copy/Move 语义已按规范修正，但没有自动化断言（依赖真实
  shell 拖放目标）。
- **飞入动画（650ms easeInOutCubic + 30px 弧线）**：仍是弹簧近似，没有按规范曲线重写。
