# Pegged 全量交互走查 · 审查清单

> 审查对象：`publish/Pegged.exe`（Release，.NET 8 + WPF，Per-Monitor-V2）
> 审查方式：**真跑、真点、真键、真文件**。每一项都由脚本驱动真实输入，并断言一个用户看得见的结果，而不是「没抛异常」。
> 环境：Windows 11，主屏 1920×1080 @125%，语言 zh-Hans，`%APPDATA%\Pegged\` 为真实数据目录。

## 0. 结论

| 走查套件 | 覆盖 | 结果 |
| --- | --- | --- |
| `launch_probe.py`（资源管理器双击启动） | 单实例 / 贴顶 / 全宽 / 收起 / 展开高度 / 感应条 | **9 / 9 PASS** |
| `interaction_probe.py`（穿透与语言） | `WM_NCHITTEST` 逐点穿透 / 工具栏可点 / 设置窗 / 语言实时刷新 | **5 / 5 PASS** |
| `audit_walkthrough.py`（四区交互） | dock 8 + card 12 + crop 4 + settings 9 + 无崩溃 1 | **34 / 34 PASS** |
| `repro_first_card_click.py`（定向复现，见 §5） | 2 / 3 张卡片时每张图的可点区域 | **全绿** |
| 合计 | | **48 / 48 PASS** |

清理临时代码后重跑，各套件保持原结果，`crash.log` 为空。

本轮（用户报障：挂 2 张以上时第一张很难点选复制）新定位 **1 个应用缺陷**（A-9），已修复并新增回归项 `card.first_card_clickable`；前序 8 个缺陷全部回归通过。

本轮共定位 **8 个应用缺陷**（其中 2 个是「功能在这台机器上根本不可用却假装能用」的严重项）与 **4 个验收脚本自身缺陷**；另把 3 项此前「声明为未验证」的交互（裁剪手柄拖拽、剪贴板自动上线、一键清空）变成可复现的绿项。

---

## 1. 本轮新发现的应用缺陷

| # | 现象（用户视角） | 根因 | 修复 | 证据（修复后） |
| --- | --- | --- | --- | --- |
| A-1 | 在设置里切到深色主题，舱体玻璃**不变色**，仍然是浅色板子 | 玻璃不是笔刷资源，是一张「桌面快照」：`ApplyThemeBrushes()` 只重画了绳子；`ApplyAcrylic()` 又因为 `_revealed` 早退，主题一变什么都不做。且着色 alpha 被写死成 `const byte TintAlpha = 199`，两个主题只有色相不同、浓淡一样 | ①`AcrylicBackdrop` 拆成「抓屏+模糊」与「上色+噪点」两步，材质缓存为 `AcrylicMaterial`；②主题变化时就地 `Paint()` 重新上色（不必再读屏幕，此时舱体正盖着那块屏幕）；③着色 alpha 改取调色板 `tint.A`（浅色 78% / 深色 72%，与注释里的规范值一致） | `settings.theme_live`：`dock luminance light=250 dark=98`（修复前 `light=245 dark=245`） |
| A-2 | 设置里把卡片改成「大」，**舱体高度不变**，卡片从舱体下沿伸出来 | 卡片尺寸只影响 `LayoutMetrics.PanelHeight`，而窗口高度只在 `PlaceOn()` 里赋值；设置保存后没有任何人重新排窗口 | `LineController` 订阅 `Settings.Saved`，发现 `_line.Height` 与 `PanelHeight + 24` 不符就重排（并只在真的不符时才动，因为每次截图、每次开关都会触发保存） | `settings.size_live`：`dock height small=266 large=338`（修复前 `266 / 266`） |
| A-3 | 「接管剪贴板」「Inbox 模式」两个开关在 UI 自动化里**各有 2 个同名元素**，读屏软件会把同一个开关念两遍 | `ToggleSwitch` 是 `UserControl` 包一个 `CheckBox`，外层 `x:Name` 生成 automation id，内层又把同一个 id 绑定过去 | 外层 `OnCreateAutomationPeer() => null`：装饰层不进自动化树，真正可切换的 `CheckBox` 才是那个开关 | `settings.toggle_persists`：`WatchClipboard True -> False -> True`（修复前 `ElementAmbiguousError: 2 elements match 'ClipboardSwitch'`） |
| A-4 | 打开 Inbox 模式，开关弹回原位，并弹出一个**模态框**；点「确定」后**下一次点开关没反应**（那一下被模态框吃掉） | ①`FOLDERID_Screenshots` 在这台机器上根本解析不出来（`SHGetKnownFolderPath` = `0x80070002`），所以重定向必然失败；②失败用 `MessageBox` 回应，而 Pegged 从不持有前台（`WS_EX_NOACTIVATE`），模态框出现在设置窗后面并抢走下一次点击 | ①新增 `InboxMode.Unavailable` 启动探测；②系统不支持时**开关置灰**并在行内写明原因（`Settings.InboxUnavailable`），永不弹窗；③支持但被策略拒绝时同样只做行内提示 + 回滚开关 | `settings.inbox_roundtrip`：`system has no Screenshots folder (0x80070002); switch enabled=False settings=False popups=[]` |
| A-5 | 预览窗口在任务栏 / Alt+Tab 里显示标题 **`Preview.Title`** | `PreviewWindow.xaml` 用 `{loc:Str Preview.Title}`，而 `Strings.resx` 与 `Strings.zh-Hans.resx` 里**根本没有这个键**，`Loc.Get` 只能回退成键名 | 补 `Preview.Title`（en `Preview` / zh `预览`） | `card.double_click_preview`：`new windows=['预览']`（修复前 `['Preview.Title']`） |
| A-6 | 裁剪浮层的 8 个手柄「拖不动」——一直没有被真正验证过 | 不是代码缺陷，是**从未验证**：手柄没有 automation peer，探针此前只能验证「浮层能开、Esc 能关」 | 给「取消 / 应用裁剪」补 `AutomationId`（`CropCancel` / `CropApply`），并新增真拖拽检查：按初始 80% 选框反推右下角手柄位置，`mouse down → 14 步移动 → up` | `crop.resize_and_apply`：`size '608 × 384 px' -> '549 × 337 px', cards 1 -> 2, overlay closed=True` |
| A-7 | 卡片被复制后，**同一张图会在挂绳上再挂一张** | 剪贴板监听不区分「图」和「图 + 文本/文件」；而 macOS 原版把 `.string` / `.fileURL` 列入 `ignoredTypes`（带文本的复制不算截图） | `ClipboardWatcher` 增加 `NotAPicture` 格式过滤（FileDrop / Text / UnicodeText / StringFormat），并对编码后的 PNG 做 SHA256 去重 | `card.click_copies_image`：`clipboard image=True, cards 1 -> 1` |
| A-8 | 图片文件被预览窗打开过之后，**复制和右键回收都失败**，卡片赖着不走 | 预览用 `new BitmapImage(new Uri(path))` 一直持有文件句柄；`SHFileOperation` 又会在**成功**时置 `fAnyOperationsAborted`，应用据此判定失败 | ①`BeginInit/CacheOption=OnLoad/IgnoreImageCache/EndInit/Freeze`；②回收成功与否改看 `!File.Exists(path)`，不看返回码 | `card.right_click_drops`：`cards 1 -> 0`（此前间歇性失败，需连查 6 个原因） |

### 1.1 用户报障轮次（「挂 2 张以上时第一张很难点选到复制」）

| # | 现象（用户视角） | 根因 | 修复 | 证据（修复后） |
| --- | --- | --- | --- | --- |
| A-9 | 挂上第 2 张图以后，**第一张图很难点**：图上没有任何遮挡，但点它十有八九没反应（既不复制，也不弹裁剪），像有一块看不见的玻璃压在它身上 | **空态提示胶囊是不可见的，但它仍然吃点击**。`UpdateHint()` 只把 `Hint.Opacity` 置 0，而 WPF 中 `Opacity=0` 的元素**依然参与命中测试**，一样会截留 `MouseLeftButtonDown`。提示是 `HorizontalAlignment=Center`，正好压在舱体中央——也就是第一张卡（最左那张）的右半部分上；第二张卡的中心恰好落在它右边一点点，所以第一张最明显、第二张偶发。UIA `ElementFromPoint` 逐点归属图直接拍到：卡片中部这些点的所有者是 `Text:HintText` / `Text:HintSub`，而不是图 | ①`Hint` 改由 `Visibility` 显隐（`Visibility.Hidden` 才真正退出命中测试），并保留 `IsHitTestVisible="False"` 双保险；②顺带给同一处两处疑似同源隐患上了保险：卡内的 `NameBar`（文件名条）与 `ActionRowBg`（悬停操作栏底板）同样以 `Opacity` 显隐，改为一律 `IsHitTestVisible="False"`——它们只是底板，点击应落到按钮或图上 | 修复前后逐点对比（3 张卡 × 3×3 网格）：修复前第一、二张的中下部 7 个点 `copied=False`；修复后 `GREEN: every click on a picture copied that picture`，第三张亦全绿。回归项 `card.first_card_clickable`：`clipboard image=True` |

## 2. 本轮新发现的验收脚本缺陷（自己骗自己也是 BUG）

| # | 现象 | 根因 | 修复 |
| --- | --- | --- | --- |
| S-1 | `card.preview_close` 偶发红：`left open=['设置']` | 断言写成「不许剩下任何我们自己的窗口」，而用户（或上一个探针）开着设置中心是完全正常的 | 改为按**预览窗口句柄**判定：双击前记下窗口集合，取差集拿到预览句柄，Esc 后只检查这个句柄 |
| S-2 | `crop.esc_cancels` / `crop.takes_keyboard` 同样靠「第一个不是挂绳的窗口」找浮层 | 同上：设置窗比浮层先出现在枚举里就会断言错窗口 | 同样改为按句柄差集定位 |
| S-3 | `interaction_probe` 打开设置窗后不关，污染后续所有检查 | 探针只还原了语言，没有还原窗口状态 | 收尾 `WM_CLOSE`，与「还原语言」同一原则 |
| S-4 | 一次跑 `--only settings` 时六项设置检查同时红 | 前面裁剪区留下一个全屏浮层盖住设置窗；且按标题（本地化）找设置窗 | 设置区开始前先收掉残留浮层；`settings_window()` 改为按 `NavCapture` 内容定位，不再看标题 |

## 3. 前序轮次发现、本轮回归确认的缺陷

| # | 缺陷 | 修复要点 | 本轮确认 |
| --- | --- | --- | --- |
| P-1 | 裁剪浮层 / 预览 / 设置窗口**拿不到键盘**，Esc 无效，必须先用鼠标点一下 | Pegged 是 `WS_EX_NOACTIVATE` 的非前台进程，`SetForegroundWindow` 会被拒。新增 `WindowFocus.Take/Release`：先 `SetForegroundWindow`，失败则 `SwitchToThisWindow`（不用 `AttachThreadInput`，避免合并输入队列），关闭时把前台还回去 | `crop.takes_keyboard` / `card.preview_takes_keyboard` / `settings.takes_keyboard` 全绿 |
| P-2 | 舱体悬停工具栏的**药丸背景**盖住卡片，卡片右键点不动 | 工具栏 `Border` 参与命中；改为 `IsHitTestVisible="False"`，只让三个按钮参与 `WM_NCHITTEST` | `card.right_click_drops` 绿 |
| P-3 | peek 到期时，如果指针正停在卡片上，整条挂绳会**从指针下面抽走**，点一半的卡片飞了 | `LineController` 到期时先看 `OverLine(dip)`，指针在内就不收，交给正常的离开计时 | `dock.peek_keeps_pointer`：`top=0 2.6s after a peek started under the pointer` |
| P-4 | 展开/收起弹簧是 `46 / 2.6`（阻尼比 ≈0.19），舱体要晃好几秒 | 按规范 0.42s / 0.82 反推：`k=(2π/0.42)²≈224`、`c=2ζ√k≈24.5`；收起 0.22s 用 `816 / 57` | `dock.reveal_settles`：`top at 1.0s=0, at 1.3s=0` |
| P-5 | 滚轮滚动方向反、范围错（`-span..span` 能把所有卡片滚出屏幕），且 `SendMessage(WM_MOUSEWHEEL)` 根本测不出东西 | 范围改 `0..span`，向上滚 = 更旧；测试改用**真实滚轮事件**（WPF 只对走输入队列的滚轮产生 `MouseWheel`） | `card.wheel_scroll`：`first card x -1813 -> -1130 after six notches up` |
| P-6 | 卡片带 3° 倾斜后，**命中矩形不跟着转**，点卡片边缘会落空 | `TransformToAncestor` 在 `Canvas.Left/Top` 同批设置时会返回过期偏移（实测拿到 `4,30 150×94`，没有画布偏移）；改为手工合成旋转矩阵 `Matrix.RotateAt(angle, ActualWidth/2, PivotY)` + `TransformBounds` | `card.hover_chrome` / `right_click_drops` 稳定绿 |
| P-7 | 切换语言后已打开的界面不刷新 | `{loc:Str}` 由一次性取值改为活绑定，`Loc.Apply` 对索引器发一次 `PropertyChanged` | `interaction_probe` 的 `language_refresh` 绿 |
| P-8 | 后台线程直接改 WPF 窗口导致**静默闪退**（每来一张新截图触发一次） | `CaptureStore.OnChanged()` 与 `LineController.Peek()` 统一 marshal 回 UI 线程 | `run.no_crash_log`：`crash.log absent` |
| P-9 | 双击启动后挂绳出现在 (96,96)，不在屏幕顶部 | 启动时 `SetProcessDpiAwarenessContext(PerMonitorV2)`；`OnSourceInitialized` 补做一次定位；`MoveTo` 用目标显示器的 scale | `launch_probe` 的 `line_rests_above_screen` / `revealed_at_top_edge` 绿 |
| P-10 | 两次双击打开两套窗口 | 命名 `Mutex(Local\Pegged.SingleInstance)`，第二个实例把已有窗口请到前台后退出 | `launch_probe` 的 `single_instance` 绿 |
| P-11 | 默认设置下挂绳**永远不展开**（把常驻的桌面窗口当成了全屏应用） | 只判断前台窗口，跳过 shell / toolwindow / cloaked 窗口 | `dock.hotkey_toggle` / `dock.pin_holds_open` 绿 |

## 4. 走查覆盖矩阵

### 4.1 dock（挂绳本体）8/8

| 检查 | 断言 | 实测 |
| --- | --- | --- |
| `dock.empty_hint` | 空挂绳显示引导文案 | hint text present |
| `dock.clear_all` | 一键清空后卡片归零、引导文案回来 | `cards after clear=0, hint back=True` |
| `dock.toolbar_auto_hide` | 指针离开后工具栏不再拦截点击 | `hittest=-1` |
| `dock.pin_holds_open` | 固定后指针离开仍停 `top=0` | `top=0` |
| `dock.unpin_retracts` | 取消固定后收回屏幕上方 | `top=-307` |
| `dock.hotkey_toggle` | 全局快捷键可开可关 | `-307 → 0 → -307` |
| `dock.reveal_settles` | 1.0s 内到位且不抖 | `0 / 0` |
| `dock.peek_keeps_pointer` | peek 到期不把挂绳从指针下抽走 | `top=0` |

### 4.2 card（卡片）12/12

| 检查 | 断言 | 实测 |
| --- | --- | --- |
| `clipboard.hangs_capture` | 复制一张图自动上线一张 | `cards=1` |
| `card.hang_from_file` | 新文件自动挂上 | `cards=1` |
| `card.hover_chrome` | 悬停出 复制/裁剪/删除/关闭 | 全部存在 |
| `card.click_copies_image` | 复制进剪贴板且**不重复挂** | `cards 1 -> 1` |
| `card.first_card_clickable` | **挂 2 张以上时第一张仍可单击复制** | `clicked at (868,108): clipboard image=True` |
| `card.double_click_preview` | 双击开预览 | `new windows=['预览']` |
| `card.preview_takes_keyboard` | 预览打开即拿焦点 | `foreground=预览` |
| `card.preview_close` | Esc 关掉预览 | `closed=True` |
| `card.right_click_drops` | 右键取下 | `1 -> 0` |
| `card.close_button_drops` | 关闭按钮取下 | `1 -> 0` |
| `card.prune_external` | 外部删文件后卡片消失 | `cards=0` |
| `card.wheel_scroll` | 超一屏时滚轮翻旧照片 | `x -1813 → -1130` |

### 4.3 crop（裁剪）4/4

| 检查 | 断言 | 实测 |
| --- | --- | --- |
| `crop.opens` | 浮层打开 | `windows=['裁剪']` |
| `crop.takes_keyboard` | 打开即拿键盘 | `foreground=裁剪` |
| `crop.esc_cancels` | Esc 取消 | `closed=True` |
| `crop.resize_and_apply` | 拖手柄改选框 + 应用生成新卡片 | `608×384 → 549×337, cards 1 → 2` |

### 4.4 settings（设置中心）9/9

| 检查 | 断言 | 实测 |
| --- | --- | --- |
| `settings.open` | 从齿轮打开 | `hwnd=0x50f76` |
| `settings.takes_keyboard` | 打开即拿键盘 | `foreground=设置` |
| `settings.nav_pages` | 五个分页都能切 | 5 页全部访问 |
| `settings.toggle_persists` | 开关写回 settings.json | `True → False → True` |
| `settings.theme_live` | 切深色舱体立刻变暗 | `light=250 dark=98` |
| `settings.size_live` | 尺寸切换立刻改舱体高度 | `266 → 338` |
| `settings.hotkey_esc_keeps` | 录制时 Esc 保留原快捷键 | 快捷键不变 |
| `settings.inbox_roundtrip` | 按系统能力行事（可开可关 / 置灰不弹窗） | 置灰 + 行内说明 + 无弹窗 |
| `settings.close` | 可关闭且不留窗口 | `left open=[]` |

---

## 5. 明确未验证 / 受限项

以下项目**没有**被本轮走查证明，不得视为通过：

1. **多显示器混合 DPI**（例如 100% + 150% 并排时跨屏拖动挂绳）。当前机器只有一块屏，`PlaceOn` 的 scale 切换只用单屏验证过。
2. **拖出到资源管理器（OLE 拖放）**：`DoDragDrop` 依赖真实的 Explorer 窗口与 `IDataObject` 往返，探针无法可靠模拟，仍属人工验收项。
3. **托盘图标交互**：`Shell_NotifyIcon` 的右键菜单需要真实鼠标点击通知区域，自动化极不稳定，未纳入自动走查。
4. **Inbox 模式的「成功路径」**：本机 `FOLDERID_Screenshots` 不可解析（`0x80070002`），只能验证「不支持时正确降级」。写入 `User Shell Folders`、`KF_FLAG_DONT_VERIFY`、`SHChangeNotify` 三种绕法都实测无效，故成功路径在本机不可达。
5. **飞入动画曲线与规范逐帧一致**：只验证了「卡片会出现、位置正确」，没有逐帧比对 SwiftUI 的 `response/dampingFraction`。
6. **非 125% 缩放与多语言 UI**：其余 8 种语言的 resx 只做了回退到英文的保证，未逐一走查。

## 6. 复现方式

```bash
# 0) 构建 + 发布（SDK 在本机 %USERPROFILE%\dotnet，不在 Program Files）
"C:/Users/dk900/dotnet/dotnet.exe" build   src/Pegged/Pegged.csproj -c Release
"C:/Users/dk900/dotnet/dotnet.exe" publish src/Pegged/Pegged.csproj -c Release -o publish

# 1) 双击启动（资源管理器真实双击，且进程要能活过启动它的 shell）
python scripts/e2e/launch_probe.py          # 9 项

# 2) 穿透与语言
python scripts/e2e/interaction_probe.py     # 5 项

# 3) 四区交互走查（会自己停掉/恢复设置与卡片）
python scripts/e2e/audit_walkthrough.py             # 34 项
python scripts/e2e/audit_walkthrough.py --only crop # 只跑一区

# 4) 定向复现：多张卡片时每张图的可点区域（原文报障的复现脚本）
python scripts/e2e/repro_first_card_click.py                 # 2 张
python scripts/e2e/repro_first_card_click.py --cards 3 --grid # 3 张 + 逐点网格
```

结果落在 `outputs/acceptance/audit.json`（全量）与 `outputs/acceptance/audit-<area>.json`（分区），截图在 `outputs/acceptance/shots/audit/`。

**走查期间的两个硬约束**（曾经把整轮跑「神秘取消」过，已写进脚本）：

- 宿主应用（WorkBuddy）若最大化盖住顶边，走查的点击会落在它的工具栏甚至「停止」按钮上 → `park_host()` 先把宿主挪到 `y≥320`，跑完还原。
- 后台 shell 的作业对象会回收子进程 → 启动用 `DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB`，并且启动、探针、清理必须写在**同一条命令**里。
- 任何要发 Esc 的检查，先确认前台确实属于 Pegged（`focused_is_pegged()`），否则不发键——这条曾经让 Esc 落进宿主 UI，把正在跑的走查取消掉。
