# Pegged — UI Design Spec (Windows 11 Fluent Design 2.0)

> 可视化看板：`docs/ui-design-showcase.html`（浏览器双击即可预览，含深浅色 Token 与全部 7 大组件）。
> 实现：`src/Pegged/Themes/FluentTheme.xaml` + 各 View/Control。

## 1. 设计语言

- **基底**：Win11 Fluent 2.0。亚克力（Acrylic）毛玻璃 + Mica 质感；贯穿高光挂绳；卡片为"拍立得"式白边相框。
- **圆角**：卡片 16px（连续圆角），按钮/胶囊 8px，图标圆 50%。
- **阴影**：卡片静止 0/5/10px 黑 18%；悬停加深至 0/8/14px 黑 26%。
- **动效曲线**：Fluent 标准 `cubic-bezier(0.16, 1, 0.3, 1)`（Decelerate）；弹簧滑出用 stiffness 46 / damping 2.6 的物理参数（WPF 中由自研积分器实现，见 TECHNICAL_DECISIONS §2.2）。
- **Token**：颜色/材质/字号/圆角/阴影全部以 `DynamicResource` 令牌化，深浅主题一套代码切换。

## 2. 深浅色 Token

| Token | Light | Dark |
|---|---|---|
| `Bg.Acrylic` | `#F4F6FB @ 78%` | `#20242B @ 72%` |
| `Bg.Card` | `#FEFEFE @ 88%` | `#2A2E36 @ 86%` |
| `Stroke.Card` | 白 85% → 白 18% 渐变（顶亮底暗） | 白 16% → 白 6% |
| `Rope.Core` | `#787E8A` | `#B9BEC9` |
| `Clothespin.Wood` | `#EEC498 → #D69E64 → #966034` | `#D8A876 → #B9854F → #7A4F2C` |
| `Accent` | `#0F6CBD`（Win11 蓝） | `#4CC2FF` |
| `Text.Primary` | `#1A1B1F` | `#FFFFFF` |
| `Text.Secondary` | `#5C5F66` | `#C9CDD4` |
| `Toast.Success` | `#0F7B0F` 底 / 白字 | `#6CCB5F` 底 / 深字 |
| `Crop.Mask` | 黑 55% | 黑 62% |
| `Crop.Handle` | 白 + 黑描边 | 白 + 黑描边 |

## 3. 七大组件

### 3.1 顶部收起态微光感应条（Collapsed Edge Pill）
- 定位：屏幕物理顶部 4px，宽 120px 居中，`WS_EX_NOACTIVATE|TOOLWINDOW`。
- 视觉：`Accent` 渐变（`#4CC2FF → #7B61FF`）水平流光，透明度呼吸 35%↔75%，周期 2.4s；两端渐隐。
- 交互：光标进入 250ms → 展开舱落下；不拦截点击。

### 3.2 展开态顶部悬挂舱（Expanded Hanging Dock）
- 高度 210px（含照片区），贴顶全屏宽，弹簧动画落下。
- **亚克力**：`AcrylicHost`（Win11 亚克力笔刷：噪声 + 高斯模糊 +  tint `Bg.Acrylic`）。
- **挂绳**：从屏幕两端向中心下垂的二次贝塞尔（矢高 ≤30px），三笔叠加：阴影 / 中灰芯 / 顶部高光；两端 8% 渐隐。
- **顶部快捷操作栏**（悬停舱体时淡入）：`一键清空`（垃圾桶图标）、`固定图钉`（切换 pinned 态）、`设置`（齿轮）——胶囊毛玻璃按钮，hover 提亮 8%。

### 3.3 精致夹子与卡片组件（Clothespin & Media Card）
- **夹子**：9×26px 木色水平渐变胶囊 + 顶部系绳槽（5×1.4px 深色胶囊）+ 金属弹簧箍（13×13px，`#E2E8F0→#949EAC`）。三态：
  - 默认：原样；
  - 悬停张开：弹簧箍下移 2px，两条木片间距 +2px（形变 120ms）；
  - 夹紧下压：卡片 `scaleEffect(0.95)` 锚点顶部（ease-in 450ms）。
- **卡片**：照片等比适配 150×104 内；白边 inset 4px，圆角 16/12（同心）；0.5px 白色描边；静止阴影 0/5px/10px 18%。
- **视频**：时长胶囊 + 28px 播放圆角标角（玻璃底）居中。
- **悬停浮层**：左上角 `×`（20px 圆钮，搁置在绳上图显示 xmark，其它显示 eye.slash）；底部浮出复制/裁剪/删除三枚 28px 圆钮。
- **单击反馈**：`Copied!` 胶囊徽章（11px 半粗，`Toast.Success`，勾图标）从卡片底部弹起 16px → 停留 1.2s → 淡出；同时卡片被轻推 3° 摆动。

### 3.4 空状态引导视图（Empty State）
- 绳索仍渲染并做 ±1.6°~3.4° 的微风轻摆（每 7~16s 一次）。
- 中央胶囊提示（12px 中粗）：`Take a screenshot and it will hang here`（简中：`截一张图，它会挂到这里`）。
- 下方小字快捷键说明 + 暗示"也可直接复制图片"。
- 首启 5s 强制展开一次作为引导。

### 3.5 内联快速裁剪弹窗（Quick Crop Overlay）
- 触发：悬停浮层裁剪钮 / 右键菜单 "裁剪"。
- 全屏 Overlay：冻结桌面背景 + `Crop.Mask` 遮罩，中央图片原比例显示。
- **九宫格**：4px 白色网格线 33% 分隔；选框 8 手柄（角 12px、边 8px），白底黑描边；选框外实时像素尺寸标签（`1080 × 720`）随选框移动。
- **毛玻璃浮动栏**：底部居中胶囊，`取消`（次级）/ `确认`（Accent 填充），`cubic-bezier(0.16,1,0.3,1)` 滑入。

### 3.6 现代化设置中心窗口（Fluent Settings Window）
- 720×560，Mica 底，左栏 240px 卡片式导航（图标 + 标题 + 副标题），选中左侧 3px Accent 指示条 + 卡片底 `Card.Background`。
- 页面：`常规`（语言、声音、保留数量、尺寸 Small/Medium/Large）、`捕获源`（截图目录、额外监控文件夹 ≤3、Inbox 模式开关与还原）、`外观与显示器`（主题 Auto/Light/Dark、悬停停顿时长、全屏应用上显示、来自菜单栏的展开）、`快捷键绑定`（当前快捷键 + 录制框：Esc 取消 / Delete 清除）、`关于`（版本、开源许可、Logo）。
- 页头 28px 半粗标题 + 副标题；设置行 56px 高，hover `Subtle Fill (secondary)`。

### 3.7 系统托盘右键菜单（Fluent Context Menu）
- 原生菜单（保证系统级圆角/阴影/动画），18px 行高，图标 16px 与文本基线对齐。
- 结构：切换显示/隐藏（附快捷键）│ 一键清空 │ 接管截图 ✓ │ 挂复制图 ✓ │ 展开条件 ▸ │ 监控文件夹 ▸ │ 保留数量 ▸ │ 尺寸 ▸ │ 快捷键 ▸ │ 打开截图目录 │ 声音 ✓ │ 开机自启 ✓ │ 检查更新 │ 退出。
- 图标态：接管剪贴板时托盘图标填充 tshirt；`ToolTip` 播报当前状态。

## 4. 动效一览

| 场景 | 参数 |
|---|---|
| 舱体展开 | 弹簧 stiffness 46 / damping 2.6 / mass 1（自研积分器，约 420ms 收敛） |
| 舱体收起 | ease-in 220ms |
| 卡片挂上 | 初角 16°，stiffness 46 / damping 2.6 |
| 微风 | 随机 1.6°~3.4°，delay 0~350ms 错峰 |
| 复制徽章 | 弹起 ease-out 180ms，消失 200ms |
| 悬停浮层 | 150ms 淡入 + scale 0.6→1 |
| 截图飞入 | 650ms easeInOutCubic + 30px 弧线，玻璃框/夹子 35%~100% 渐显 |
| 丢弃掉落 | 550ms ease-in cubic，+520px 坠落，角速度加速旋转，渐隐 |
