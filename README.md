# Pegged

**A clothesline for your screenshots. Native to Windows 11.**

Pegged is a Win11 native port of [alejandrobujan/tendedero](https://github.com/alejandrobujan/tendedero) v1.5.0 (macOS Swift), built end-to-end on WPF + .NET 8 with Fluent Design 2.0. Move the cursor to the top of any monitor: the line drops down. Take a screenshot, copy an image, or drop a file on the line — it pegs up and stays there until you decide what to do with it.

![Showcase](docs/ui-design-showcase.html)

---

## Highlights

- **Always-on-top, focus-less, click-through line** — a transparent strip along the top of any monitor; never takes the focus, lets every click through to whatever is underneath unless the pointer is on a card.
- **Nothing but the clothesline** — no dock, no acrylic slab, no panel: on screen there is only the highlighted rope, the photos hanging from it, and a pill toolbar that fades in when you hover. Everything else is fully transparent, pixel for pixel.
- **Multi-monitor support** with per-monitor DPI (`GetDpiForMonitor` + `PerMonitorV2`).
- **Real physics** — every card is a `Pendulum` (gravity-driven) and a `Spring` (hover/press/lift), driven by a single 60 fps `CompositionTarget.Rendering` loop.
- **Screenshots, just by happening** — `FileSystemWatcher` with `FileShare.ReadWrite` + exponential backoff + magic-number validation so the watcher does not flicker on partial writes.
- **Clipboard listener** — every image you copy on Windows lands on the line. Privacy filter: text/email/file-list formats are ignored; image formats only. Self-write loop is blocked.
- **Drag & drop** — OLE `CF_HDROP` + `FileGroupDescriptor`/`FileContents`, with Copy vs. Move semantics: dragging out with "Move" takes the file down from the line.
- **Built-in crop** — a modal crop overlay with 8 handles, dimmed mask, live pixel size, PNG/JPEG export.
- **10 languages** (en + zh-Hans + es + fr + ja + ko + pt-BR + tr + az), with `ResourceManager`-backed `StrExtension` for XAML.
- **System tray**, global hotkey (`RegisterHotKey`, default `Ctrl+Alt+P`), launch-at-login via `HKCU\…\Run`.
- **Light / dark / auto** theme via two `ResourceDictionary` swap-in swaps.

## Quick start

### Requirements

- Windows 11 21H2 or later
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (`x64`)

### Install

1. Download `Pegged-win-x64.zip` from the [Releases page](https://github.com/dk900912/Pegged/releases).
2. Extract to any folder.
3. Make sure the .NET 8 Desktop Runtime is installed.
4. Double-click `Pegged.exe`.

Pegged stays in the system tray. The default hotkey is **Ctrl+Alt+P** to drop the line down. Move the cursor to the top of any monitor to wake it up.

### Build from source

```powershell
# restore + publish
git clone https://github.com/dk900912/Pegged
cd Pegged
dotnet publish src/Pegged -c Release -r win-x64 --self-contained false -o publish
# If your SDK lives outside PATH:
$env:DOTNET_ROOT = "$env:USERPROFILE\dotnet"
.\publish\Pegged.exe
```

## Project layout

```
Pegged/
├── AGENTS.md            # Project constitution & execution rules (original)
├── README.md            # ← you are here
├── PORTING_CHECKLIST.md # 4 phases × 6 modules, all ticked
├── docs/
│   ├── TECHNICAL_DECISIONS.md   # 96 decisions on the macOS → Win11 port
│   ├── UI_DESIGN_SPEC.md        # 7-component Fluent 2 design spec
│   └── ui-design-showcase.html  # interactive board
├── assets/              # logo.svg / logo.png / app.ico (16–256)
├── scripts/
│   ├── render-logo.py   # Pillow, 1024-supersampled Fluent mark
│   ├── gen-localization.py
│   └── push-via-api.py  # GitHub Git Data API transport (proxy-friendly)
└── src/Pegged/
    ├── Core/            # AppPaths, Settings, CaptureStore, Physics, MonitorService,
    │                    # DragService, WatchService, ClipboardWatcher, ThemeManager,
    │                    # HotkeyParser, HotKeyService, TrayService, StartupService,
    │                    # ThumbnailService, ShellRecycle, SoundService, LineController
    ├── Views/           # LineWindow, EdgePillWindow, CardView, CardStrip,
    │                    # SettingsWindow, PreviewWindow, CropWindow, ToggleSwitch
    ├── Themes/          # FluentTheme.xaml + FluentTheme.Dark.xaml
    ├── Localization/    # Loc, StrExtension, Resources/Strings*.resx
    └── Resources/       # Strings.resx + 9 culture satellite resources
```

## Credits

- Original macOS app [alejandrobujan/tendedero](https://github.com/alejandrobujan/tendedero) (MIT) — design, capture flow, the 6 challenges, the 7 UI components.
- Port by [dk900912](https://github.com/dk900912).

## License

MIT — same as the original.
