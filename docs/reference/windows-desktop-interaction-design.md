---
summary: "Complete frontend interaction design for OpenSoul Windows Desktop"
read_when:
  - Designing or implementing the Windows desktop app UX
  - Aligning WPF shell, WebView2, system tray, and settings interactions
  - Making architecture decisions for the Windows client
title: "Windows Desktop Interaction Design"
---

# Windows Desktop Interaction Design (v1.0)

> Architecture: **WPF Shell + WebView2 Hybrid** (matches macOS Swift + WebView pattern)

---

## 1) Product Frame

- **Target users:**
  - Solo builders and developers running OpenSoul as an always-on Windows assistant.
  - Operators managing channels, sessions, skills, nodes, and automation from one control point.
  - Windows power users who expect Ctrl-based shortcuts, system tray presence, and native notifications.

- **Core task loop:**
  - Observe status (system tray) → Intervene quickly (tray menu or main window) → Configure safely (settings) → Verify health (overview).

- **Primary platform constraints:**
  - System tray (NotifyIcon) is the persistent entry point, replacing macOS menu bar.
  - WebView2 renders the full Control UI (Lit components from `ui/src/`).
  - WPF shell provides native window chrome, system tray, notifications, dialogs, and OS integration.
  - Bridge layer connects WPF ↔ WebView2 for state sync and native actions.
  - Must support Windows 10 (1903+) and Windows 11.

- **Fastest interaction:**
  - Tray icon single-click → Main window visible (< 200ms).
  - `Ctrl+K` in main window → Command palette (< 100ms).

---

## 2) Architecture Overview

### 2.1 Layer model

```
┌─────────────────────────────────────────────────────────┐
│  Layer 1: WPF Shell (C#/XAML)                           │
│  ┌─────────────┬──────────────────┬──────────────────┐  │
│  │ System Tray  │ Window Chrome    │ Native Dialogs   │  │
│  │ (NotifyIcon) │ (Titlebar, frame)│ (Exec approval,  │  │
│  │              │                  │  device pairing)  │  │
│  └─────────────┴──────────────────┴──────────────────┘  │
│                          │                               │
│  ┌───────────────────────┴───────────────────────────┐  │
│  │ Layer 2: Bridge (WebView2 ↔ WPF)                  │  │
│  │ • postMessage / hostObjects                       │  │
│  │ • State sync (connection, theme, tray)            │  │
│  │ • Native action dispatch                          │  │
│  └───────────────────────┬───────────────────────────┘  │
│                          │                               │
│  ┌───────────────────────┴───────────────────────────┐  │
│  │ Layer 3: WebView2 (Control UI - Lit/Vite)         │  │
│  │ • All views: Chat, Overview, Channels, Agents...  │  │
│  │ • Design tokens, theme, navigation                │  │
│  │ • Gateway WebSocket connection                    │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

### 2.2 Responsibility split

| Responsibility | WPF Shell | WebView2 (Control UI) |
| --- | --- | --- |
| Window frame and titlebar | ✓ Custom chrome | — |
| System tray icon and menu | ✓ NotifyIcon | — |
| Gateway process management | ✓ GatewayProcessManager | — |
| Gateway WebSocket connection | — | ✓ Existing gateway.ts |
| All view rendering | — | ✓ Lit components |
| Theme management | ✓ System theme detection | ✓ CSS tokens + theme.ts |
| Native notifications | ✓ Toast notifications | Bridge triggers |
| Exec approval dialog | ✓ Native WPF dialog | Bridge triggers |
| Device pairing dialog | ✓ Native WPF dialog | Bridge triggers |
| Settings (Windows-specific) | ✓ Native settings window | — |
| Keyboard shortcuts (global) | ✓ Hotkey registration | — |
| Keyboard shortcuts (in-app) | — | ✓ Existing key handlers |
| Auto-start / launch behavior | ✓ Registry / Task Scheduler | — |
| File drag-and-drop | ✓ WPF DragDrop → Bridge | ✓ Process in chat |

### 2.3 Data flow

```
Gateway (Node.js process or remote)
    ↕ WebSocket
Control UI (WebView2)
    ↕ postMessage bridge
WPF Shell
    ↕ Native APIs
Windows OS (tray, notifications, hotkeys, auto-start)
```

---

## 3) Surface Model (Windows Adaptation)

| # | Surface | macOS equivalent | Windows implementation |
| --- | --- | --- | --- |
| A | **Tray Quick Surface** | Menu bar app | System tray NotifyIcon + context menu |
| B | **Desktop Workbench** | Floating window | Main WPF window with WebView2 |
| C | **Settings Center** | SettingsRootView | Separate WPF settings window |
| D | **Native Dialogs** | Alert panels | WPF modal dialogs |

### 3.1 Surface A: Tray Quick Surface

The system tray is the persistent presence of OpenSoul on Windows.

**Tray icon states:**
| State | Icon | Tooltip |
| --- | --- | --- |
| Active (connected) | `tray-active.ico` (accent color) | "OpenSoul - Connected" |
| Idle (disconnected) | `tray-idle.ico` (neutral gray) | "OpenSoul - Disconnected" |
| Paused | `tray-paused.ico` (amber) | "OpenSoul - Paused" |
| Error/degraded | `tray-error.ico` (red) | "OpenSoul - Error: {reason}" |

**Tray context menu structure:**

```
┌────────────────────────────────────┐
│  ● OpenSoul Active     [toggle]   │
│  ─────────────────────────────────  │
│  ▶ Open Dashboard       Ctrl+D    │
│  ▶ Open Chat            Ctrl+Shift+C │
│  ─────────────────────────────────  │
│  ⚡ Gateway: Connected  [status]   │
│  🔄 Heartbeat: 2s ago   [status]   │
│  📋 Pending approvals: 0 [status]  │
│  ─────────────────────────────────  │
│  ▶ Quick Actions                   │
│  │  ├ Toggle Camera               │
│  │  ├ Toggle Voice Wake           │
│  │  ├ Toggle Canvas               │
│  │  └ Select Microphone    ▸      │
│  ─────────────────────────────────  │
│  ⚙ Settings...          Ctrl+,   │
│  ℹ About OpenSoul                 │
│  🐛 Debug Console                 │
│  ─────────────────────────────────  │
│  ✕ Quit                 Ctrl+Q   │
└────────────────────────────────────┘
```

**Tray interaction rules:**
- Single-click: Toggle main window visibility (show/hide).
- Double-click: Open main window and focus chat.
- Right-click: Open context menu.
- Left-click when main window is open: Minimize to tray.
- Balloon notification click: Open relevant view.

### 3.2 Surface B: Desktop Workbench (Main Window)

The main window hosts the full Control UI via WebView2 with a custom WPF titlebar.

**Window behavior:**
- Default size: 1360×860, min 1100×720.
- Remember position, size, and maximized state across sessions.
- Close button minimizes to tray (configurable; show notice on first close).
- Mica/Acrylic backdrop on Windows 11 (optional, configurable).
- Custom titlebar with: brand mark, window controls (minimize/maximize/close), connection status indicator, and theme toggle.

**Shell layout:**

```
┌──────────────────────────────────────────────────────────────┐
│  [≡] OpenSoul    ● Connected    ☀/🌙  [─][□][×]            │  ← WPF Titlebar
├──────────────────────────────────────────────────────────────┤
│                                                              │
│                   WebView2 (Control UI)                      │
│                                                              │
│  ┌──────────┬───────────────────────────────────────────┐   │
│  │          │                                           │   │
│  │  Nav     │  Content                                  │   │
│  │  sidebar │  (Chat / Overview / Channels / ...)       │   │
│  │          │                                           │   │
│  │  Assist  │                                           │   │
│  │  Operate │                                           │   │
│  │  Build   │                                           │   │
│  │  System  │                                           │   │
│  │          │                                           │   │
│  └──────────┴───────────────────────────────────────────┘   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

**Custom titlebar spec:**
- Height: 36px (Windows 11 standard).
- Left: App icon (16×16) + "OpenSoul" brand text.
- Center: Connection status pill (`● Connected` / `◌ Disconnected` / `⚠ Degraded`).
- Right: Theme toggle button + native window controls (minimize/maximize/close).
- Draggable area: Entire titlebar except interactive elements.
- Double-click titlebar: Maximize/restore.

### 3.3 Surface C: Settings Center

A separate WPF window for Windows-specific settings. App-level settings (channels, agents, etc.) are in the Control UI.

**Settings window layout:**

```
┌────────────────────────────────────────────────────┐
│  ⚙ OpenSoul Settings                    [×]       │
├──────────────┬─────────────────────────────────────┤
│              │                                     │
│  General     │  [Section content]                  │
│  Connection  │                                     │
│  Appearance  │                                     │
│  Shortcuts   │                                     │
│  Privacy     │                                     │
│  Advanced    │                                     │
│  About       │                                     │
│              │                                     │
└──────────────┴─────────────────────────────────────┘
```

**Settings groups:**

**General:**
- Start OpenSoul at Windows login (auto-start toggle).
- Show in taskbar (vs tray-only mode).
- Close to tray behavior (toggle + "Don't show this again" for first-close notice).
- Default session key.
- History message limit.

**Connection:**
- Connection mode: Local / Remote.
- Remote URL, Token, Password, Device Token.
- Auto-connect on launch.
- Connection timeout.
- Gateway health polling interval.

**Appearance:**
- Theme: System / Light / Dark.
- Window opacity (90%-100%).
- Enable Mica/Acrylic effect (Windows 11).
- UI scale factor (80%-150%).
- Font: System default / Custom.

**Shortcuts:**
- Global hotkeys table (configurable):
  | Action | Default | Scope |
  | --- | --- | --- |
  | Toggle main window | `Ctrl+Shift+O` | Global |
  | Open chat | `Ctrl+Shift+C` | Global |
  | Open dashboard | `Ctrl+D` | Window |
  | Command palette | `Ctrl+K` | Window |
  | Switch to Assist | `Ctrl+1` | Window |
  | Switch to Operate | `Ctrl+2` | Window |
  | Switch to Build | `Ctrl+3` | Window |
  | Switch to System | `Ctrl+4` | Window |
  | Quick settings | `Ctrl+,` | Window |
  | Quit | `Ctrl+Q` | Window |

**Privacy:**
- Send anonymous usage analytics (toggle).
- Clear local chat cache.
- Clear saved credentials.

**Advanced:**
- Gateway binary path (override).
- Node.js path (override).
- Custom state directory.
- Enable debug console (shows gateway events panel).
- Enable verbose logging.
- Reset all settings to default.

**About:**
- Version, build info, update check.
- License, links.

### 3.4 Surface D: Native Dialogs

**Exec Approval Dialog:**

```
┌───────────────────────────────────────────────┐
│  ⚠ OpenSoul - Command Approval                │
├───────────────────────────────────────────────┤
│                                               │
│  A command is requesting execution approval.  │
│                                               │
│  ┌─────────────────────────────────────────┐  │
│  │ Command: rm -rf /tmp/build              │  │
│  │ Working dir: ~/projects/myapp           │  │
│  │ Reason: Clean build artifacts           │  │
│  │ Risk: ⚠ medium                          │  │
│  └─────────────────────────────────────────┘  │
│                                               │
│  ☐ Remember this decision for similar cmds    │
│                                               │
│  ┌──────────┐  ┌──────────────────────────┐  │
│  │  Reject  │  │  ✓ Approve and Execute   │  │
│  └──────────┘  └──────────────────────────┘  │
└───────────────────────────────────────────────┘
```

- Dialog appears above all windows (topmost).
- Sound alert on appearance.
- Default focus on "Reject" (safe default).
- Shows risk level with color coding (low=green, medium=amber, high=red).
- "Remember" checkbox for whitelist/blacklist.
- 60-second auto-reject timeout with countdown display.

**Device Pairing Dialog:**

```
┌───────────────────────────────────────────────┐
│  🔗 OpenSoul - Device Pairing Request          │
├───────────────────────────────────────────────┤
│                                               │
│  A new device wants to pair with your node.   │
│                                               │
│  ┌─────────────────────────────────────────┐  │
│  │ Device: iPhone 16 Pro                   │  │
│  │ Platform: iOS 19                        │  │
│  │ IP: 192.168.1.42                        │  │
│  │ First seen: just now                    │  │
│  └─────────────────────────────────────────┘  │
│                                               │
│  ┌──────────┐  ┌──────────────────────────┐  │
│  │  Reject  │  │  ✓ Approve Pairing       │  │
│  └──────────┘  └──────────────────────────┘  │
└───────────────────────────────────────────────┘
```

---

## 4) Detailed Screen Specs

### 4.1 Startup Flow

```
App.xaml.cs → MainWindow
    │
    ├─[1] Show splash (200ms, app icon + "Loading...")
    │
    ├─[2] Initialize WPF shell
    │     ├ Create NotifyIcon (tray-idle.ico)
    │     ├ Register global hotkeys
    │     ├ Load AppSettings
    │     └ Apply theme from settings
    │
    ├─[3] Initialize WebView2
    │     ├ Create WebView2 environment
    │     ├ Navigate to Control UI (local file or dev server)
    │     ├ Inject bridge scripts
    │     └ Wait for DOMContentLoaded
    │
    ├─[4] Connect to gateway
    │     ├ If auto-connect: start connection
    │     ├ Update tray icon state
    │     └ Push connection info to WebView2
    │
    └─[5] Ready
          ├ Hide splash, show main content
          ├ If first launch: show onboarding overlay in WebView2
          └ If auto-connected: load chat history
```

**First launch experience:**
- On very first launch, show a slim onboarding banner in WebView2:
  "Welcome to OpenSoul. Your gateway is starting..." → "Connected! Start chatting."
- Don't show the full onboarding wizard (that's handled by the CLI setup).
- Show "Close to tray" notice on first window close.

### 4.2 Screen B-1: Chat (Assist Domain)

Rendered entirely in WebView2, using existing `views/chat.ts`.

**Windows-specific enhancements:**
- Native file drop: WPF intercepts file drop on WebView2, sends file paths via bridge.
- Native clipboard: Enhanced paste handling for images via bridge.
- Send shortcut: `Ctrl+Enter` (consistent with current behavior).
- Focus chat input: `Ctrl+L` or `/` when not focused.
- Notification on new assistant message when window is minimized: native Windows toast.

### 4.3 Screen B-2: Overview (System Domain)

Rendered in WebView2, using existing `views/overview.ts`.

**Windows-specific enhancements:**
- System tray status mirrors overview health.
- Gateway restart button triggers bridge → WPF → `GatewayProcessManager.RestartAsync()`.

### 4.4 Screen B-3: Channels (Operate Domain)

Rendered in WebView2, existing `views/channels.ts`.

**No Windows-specific changes needed.** All channel operations go through the gateway.

### 4.5 Screen B-4: Other Views

All remaining views (Instances, Sessions, Usage, Cron, Agents, Skills, Nodes, Config, Debug, Logs) render in WebView2 using existing Lit view modules. No Windows-specific modifications needed for v1.

---

## 5) WebView2 Bridge Protocol

### 5.1 Message format

All bridge messages use `window.chrome.webview.postMessage()` (WebView2 → WPF) and `webView.CoreWebView2.PostWebMessageAsJson()` (WPF → WebView2).

```typescript
interface BridgeMessage {
  type: string;       // Message type identifier
  payload?: unknown;  // Type-specific payload
  id?: string;        // Optional correlation ID for request/response
}
```

### 5.2 WebView2 → WPF messages

| Type | Payload | Purpose |
| --- | --- | --- |
| `shell.ready` | `{ version: string }` | Control UI initialized, ready for bridge |
| `shell.connectionStateChanged` | `{ state: "connected" \| "disconnected" \| "degraded" }` | Update tray icon and titlebar |
| `shell.notify` | `{ title, body, tag?, action? }` | Trigger native Windows toast notification |
| `shell.themeChanged` | `{ theme: "light" \| "dark" }` | Sync theme to WPF titlebar |
| `shell.tabChanged` | `{ tab: string, title: string }` | Update window title |
| `shell.requestExecApproval` | `{ requestId, command, cwd, reason, riskLevel }` | Show native exec approval dialog |
| `shell.requestDevicePair` | `{ requestId, deviceName, platform, ip }` | Show native device pairing dialog |
| `shell.openExternal` | `{ url: string }` | Open URL in default browser |
| `shell.badge` | `{ count: number }` | Update tray icon badge/overlay |
| `shell.gatewayAction` | `{ action: "restart" \| "stop" }` | Control local gateway process |

### 5.3 WPF → WebView2 messages

| Type | Payload | Purpose |
| --- | --- | --- |
| `host.init` | `{ theme, gatewayUrl, token, settings }` | Initial config on bridge ready |
| `host.themeChanged` | `{ theme: "light" \| "dark" }` | System theme change |
| `host.navigate` | `{ tab: string }` | Navigate to tab (from tray menu) |
| `host.focus` | `{ target: "chat-input" \| "search" }` | Focus specific element |
| `host.execApprovalResult` | `{ requestId, approved, remember }` | Exec approval dialog result |
| `host.devicePairResult` | `{ requestId, approved }` | Device pairing dialog result |
| `host.fileDrop` | `{ files: Array<{ name, path, size }> }` | Files dropped on window |
| `host.windowState` | `{ state: "focused" \| "blurred" \| "minimized" }` | Window state change |
| `host.settingsChanged` | `{ ...settings }` | Settings updated from native settings window |

### 5.4 Bridge initialization sequence

```
WPF                          WebView2 (Control UI)
 │                                │
 │  ← DOMContentLoaded ─────────  │
 │                                │
 │  ← shell.ready ──────────────  │
 │                                │
 │  ── host.init ──────────────→  │
 │     (theme, gateway, token)    │
 │                                │
 │  ← shell.connectionState ───  │
 │     (after gateway connect)    │
 │                                │
 │  ← shell.themeChanged ──────  │
 │                                │
 │  (bidirectional messaging)     │
```

---

## 6) Component and State Matrix

### 6.1 WPF Shell components

| Component | States | Behavior |
| --- | --- | --- |
| **System tray icon** | idle, active, paused, error | Animated transition between states; tooltip updates |
| **Tray context menu** | enabled/disabled per connection state | Grayed items when disconnected |
| **Custom titlebar** | normal, maximized, focused, unfocused | Adjust padding for maximized; dim unfocused |
| **Connection status pill** | connected (green), disconnected (gray), degraded (amber), connecting (pulse) | Click opens overview |
| **Theme toggle** | light, dark | Animated sun/moon icon swap |
| **Window controls** | normal, hover, pressed | Accent color on close hover (red) |
| **Splash overlay** | visible, fading, hidden | 200ms fade out on ready |

### 6.2 WebView2 components (existing, enhanced)

| Component | Enhancement for Windows |
| --- | --- |
| **Nav sidebar** | Receive `host.navigate` for tray-triggered navigation |
| **Chat compose** | Accept `host.fileDrop` for drag-and-drop attachments |
| **Command palette** | Triggered by WPF `Ctrl+K` forwarding |
| **Status indicators** | Send `shell.connectionStateChanged` on every transition |
| **All views** | No shadow DOM (light DOM already) for seamless font inheritance |

### 6.3 Notification behavior matrix

| Event | In-app (WebView2) | Minimized/tray | Focus steal? |
| --- | --- | --- | --- |
| Chat message received | Scroll + highlight | Toast notification | No |
| Exec approval requested | Overlay banner | Toast + bring to front | Yes (topmost dialog) |
| Device pairing requested | Overlay banner | Toast + bring to front | Yes (topmost dialog) |
| Gateway connected | Status pill update | Tray icon change | No |
| Gateway disconnected | Status pill + banner | Tray icon + toast | No |
| Gateway error | Error banner | Tray icon (error) + toast | No |

---

## 7) Visual Design Language

### 7.1 Design direction

Hybrid of **Cursor** (speed, density, keyboard-first) and **Notion** (calm whitespace, readable hierarchy).

- Dark-first with full light theme parity.
- Custom titlebar matches app theme, not Windows system chrome.
- Smooth, subtle animations (no decorative motion on critical paths).
- High information density in tables and lists; generous spacing in chat and settings.

### 7.2 Color tokens

Inherit from existing Control UI `base.css` tokens. WPF shell uses matching colors:

**Dark theme (default):**

| Token | CSS var | WPF Brush | Value |
| --- | --- | --- | --- |
| Background | `--bg` | `ShellBgBrush` | `#0F1117` |
| Titlebar | `--chrome` | `TitlebarBrush` | `#161B22` |
| Panel | `--panel` | `PanelBrush` | `#1A1F28` |
| Border | `--border` | `BorderBrush` | `#2D333B` |
| Text primary | `--text` | `TextBrush` | `#E6EDF3` |
| Text muted | `--muted` | `MutedTextBrush` | `#768390` |
| Accent | `--accent` | `AccentBrush` | `#FF5C5C` |
| Success | `--ok` | `SuccessBrush` | `#3FB950` |
| Warning | `--warn` | `WarningBrush` | `#D29922` |
| Danger | `--danger` | `DangerBrush` | `#F85149` |

**Light theme:**

| Token | CSS var | WPF Brush | Value |
| --- | --- | --- | --- |
| Background | `--bg` | `ShellBgBrush` | `#FFFFFF` |
| Titlebar | `--chrome` | `TitlebarBrush` | `#F6F8FA` |
| Panel | `--panel` | `PanelBrush` | `#FFFFFF` |
| Border | `--border` | `BorderBrush` | `#D1D9E0` |
| Text primary | `--text` | `TextBrush` | `#1F2328` |
| Text muted | `--muted` | `MutedTextBrush` | `#656D76` |
| Accent | `--accent` | `AccentBrush` | `#DC2626` |
| Success | `--ok` | `SuccessBrush` | `#1A7F37` |
| Warning | `--warn` | `WarningBrush` | `#9A6700` |
| Danger | `--danger` | `DangerBrush` | `#CF222E` |

### 7.3 Typography

| Level | Font | Size | Weight | Usage |
| --- | --- | --- | --- | --- |
| Display | Segoe UI Variable Display | 20px | SemiBold | Window titles, section headers |
| Title | Segoe UI Variable Text | 16px | SemiBold | Card titles, dialog headers |
| Body | Segoe UI Variable Text | 14px | Regular | Default text, form labels |
| Small | Segoe UI Variable Small | 12px | Regular | Captions, timestamps, tooltips |
| Mono | Cascadia Mono | 13px | Regular | Code, paths, IDs, JSON |

Note: WebView2 inherits Space Grotesk + JetBrains Mono from Control UI CSS. WPF shell uses Segoe UI family for native consistency.

### 7.4 Spacing and sizing

- 4px base unit, scale: 4 / 8 / 12 / 16 / 24 / 32 / 48.
- Titlebar height: 36px.
- Tray menu item height: 32px.
- Settings nav item: 36px.
- Button heights: 32px (compact), 36px (default), 40px (prominent).
- Border radius: 6px (small), 8px (medium), 12px (large).
- Content padding: 16px default, 24px for settings.

### 7.5 Motion

| Transition | Duration | Easing | Notes |
| --- | --- | --- | --- |
| Window show/hide | 150ms | ease-out | Fade + slight scale (0.98→1.0) |
| Theme switch | 200ms | ease-in-out | Cross-fade all colors |
| Tray icon state | 300ms | ease-out | Icon crossfade |
| Dialog appear | 180ms | ease-out | Fade + translate-y (-8→0) |
| Dialog dismiss | 120ms | ease-in | Fade + translate-y (0→8) |
| Splash → content | 250ms | ease-out | Fade out splash, reveal WebView |
| Tooltip | 100ms | ease-out | Fade in |

### 7.6 Shadows and elevation

- Titlebar: `0 1px 0 var(--border)` (single line separator).
- Dialogs: `0 8px 24px rgba(0,0,0,0.4)` (dark), `0 8px 24px rgba(0,0,0,0.15)` (light).
- Tray menu: System default (DWM shadow).
- Settings window: Standard WPF window shadow.

---

## 8) Keyboard Model

### 8.1 Global hotkeys (registered via Win32 API)

| Shortcut | Action |
| --- | --- |
| `Ctrl+Shift+O` | Toggle main window visibility |
| `Ctrl+Shift+C` | Open main window and focus chat input |

### 8.2 Window-scoped shortcuts (handled in WPF or forwarded to WebView2)

| Shortcut | Action | Handler |
| --- | --- | --- |
| `Ctrl+K` | Open command palette | Forward to WebView2 |
| `Ctrl+1` | Switch to Assist domain | Forward to WebView2 |
| `Ctrl+2` | Switch to Operate domain | Forward to WebView2 |
| `Ctrl+3` | Switch to Build domain | Forward to WebView2 |
| `Ctrl+4` | Switch to System domain | Forward to WebView2 |
| `Ctrl+,` | Open settings window | WPF handler |
| `Ctrl+Q` | Quit application | WPF handler |
| `Ctrl+L` | Focus chat input | Forward to WebView2 |
| `Ctrl+Shift+L` | Focus logs view | Forward to WebView2 |
| `F5` | Refresh current view | Forward to WebView2 |
| `F11` | Toggle fullscreen | WPF handler |
| `Escape` | Close dialog / deselect | Forward to WebView2 |
| `Ctrl+Shift+I` | Toggle DevTools (debug only) | WPF handler |

### 8.3 Focus management

- Tab order: Titlebar controls → Nav sidebar → Content area.
- `Ctrl+Tab`: Cycle between open panels (if tabbed).
- `Alt+←/→`: Navigate back/forward in view history.
- Focus trap in dialogs (exec approval, device pairing).
- WebView2 focus restore on window re-focus.

---

## 9) Accessibility Baseline

- Visible focus indicators for all interactive elements (2px accent ring).
- Titlebar buttons: 44×36px minimum hit target.
- Tray menu items: 32px height, full-width click area.
- Dialog buttons: 40px height minimum.
- Contrast: 4.5:1 for body text, 3:1 for large text and icons.
- Screen reader support:
  - Titlebar: `AutomationProperties.Name` on all controls.
  - Tray: `NotifyIcon` tooltip text.
  - Dialogs: `AutomationProperties.LiveSetting="Assertive"` for approval dialogs.
  - WebView2: Inherits web accessibility from Control UI (ARIA labels, semantic HTML).
- Reduced motion: Respect `prefers-reduced-motion` in WebView2; disable WPF animations when `SystemParameters.MinimizeAnimation` is false.

---

## 10) Error Handling and Edge Cases

### 10.1 WebView2 unavailable

If WebView2 runtime is not installed:
- Show a centered WPF panel with:
  "WebView2 Runtime Required"
  "OpenSoul needs the Microsoft Edge WebView2 Runtime to display its interface."
  [Download WebView2 Runtime] button (opens evergreen bootstrapper URL).
  [Quit] button.
- Do not crash. Log the error.

### 10.2 Gateway process failure

- If local gateway fails to start:
  - Tray icon → error state.
  - WebView2 shows disconnected state (existing behavior).
  - Toast notification: "Gateway failed to start. Check settings."
- If gateway crashes mid-session:
  - Auto-reconnect with exponential backoff (existing `GatewayChannel` behavior).
  - Tray icon → error state during reconnection.
  - Banner in WebView2: "Reconnecting to gateway..."

### 10.3 Node.js not found

- Show native dialog:
  "Node.js 22+ Required"
  "OpenSoul needs Node.js 22 or later. Install from nodejs.org."
  [Open nodejs.org] [Use Remote Mode] [Quit]

### 10.4 Theme mismatch

- On system theme change (Windows dark/light switch):
  - If user theme setting is "System": push `host.themeChanged` to WebView2.
  - Update WPF titlebar brushes.
  - 200ms cross-fade transition.

### 10.5 High DPI and scaling

- WPF: Per-monitor V2 DPI awareness (already in `app.manifest`).
- WebView2: Inherits DPI from WPF host; CSS `rem`-based sizing scales correctly.
- Tray icons: Provide 16, 24, 32, 48px versions for different DPI scales.

---

## 11) Rollout Phases (Windows-Specific)

### Phase W1: Shell Migration (Current)

- [ ] Replace MainWindow with custom-chrome WPF shell + WebView2.
- [ ] Implement system tray with state-aware icons and context menu.
- [ ] Create bridge protocol (postMessage-based).
- [ ] Load Control UI in WebView2 with gateway connection.
- [ ] Migrate connection management from XAML to bridge.
- [ ] Implement close-to-tray behavior.

### Phase W2: Native Integration

- [ ] Native exec approval and device pairing dialogs.
- [ ] Windows toast notifications for key events.
- [ ] File drag-and-drop → bridge → chat attachments.
- [ ] Global hotkeys (Ctrl+Shift+O, Ctrl+Shift+C).
- [ ] Settings window (General, Connection, Appearance).

### Phase W3: Polish and Optimization

- [ ] Theme sync (system → WPF → WebView2).
- [ ] Window state persistence (position, size, maximized).
- [ ] Splash screen and startup optimization.
- [ ] Mica/Acrylic backdrop on Windows 11.
- [ ] Settings: Shortcuts, Privacy, Advanced sections.
- [ ] Auto-update integration (Squirrel or custom).

### Phase W4: Feature Parity

- [ ] Keyboard shortcut overlay (show all shortcuts).
- [ ] Command palette integration via bridge.
- [ ] Tray quick actions (camera, voice wake, canvas toggles).
- [ ] Deep link support (`opensoul://` protocol handler).
- [ ] Installer improvements (MSIX or MSI).

---

## 12) File Structure (Target)

```
apps/windows/src/OpenSoul/
├── App.xaml / App.xaml.cs                     # App entry
├── MainWindow.xaml / MainWindow.xaml.cs        # Shell with WebView2
├── Windows/
│   ├── SettingsWindow.xaml / .xaml.cs          # Settings center
│   ├── ExecApprovalDialog.xaml / .xaml.cs      # Exec approval
│   └── DevicePairingDialog.xaml / .xaml.cs     # Device pairing
├── ViewModels/
│   ├── MainViewModel.cs                        # Main window state (MVVM)
│   ├── SettingsViewModel.cs                    # Settings state
│   └── TrayViewModel.cs                        # Tray state and commands
├── Services/
│   ├── BridgeService.cs                        # WebView2 ↔ WPF bridge
│   ├── ThemeService.cs                         # Theme detection and sync
│   ├── HotkeyService.cs                        # Global hotkey registration
│   ├── NotificationService.cs                  # Windows toast notifications
│   └── WindowStateService.cs                   # Position/size persistence
├── Themes/
│   ├── Dark.xaml                                # Dark theme resource dict
│   └── Light.xaml                               # Light theme resource dict
├── Converters/
│   └── BoolToVisibilityConverter.cs            # Common converters
├── Resources/
│   ├── opensoul.ico                            # App icon
│   ├── tray-idle.ico / tray-active.ico / ...   # Tray icons
│   └── splash.png                              # Splash image
├── AppSettings.cs                              # Settings model (extended)
├── AppSettingsStore.cs                          # Settings persistence
└── app.manifest                                # DPI, UAC, compat
```

---

## 13) Acceptance Criteria

### Shell and window
- [ ] App starts and shows Control UI in WebView2 within 3 seconds.
- [ ] Custom titlebar is draggable, maximize/restore on double-click works.
- [ ] Window close minimizes to tray; can be disabled in settings.
- [ ] Window position and size are restored on next launch.
- [ ] Theme toggle switches both WPF chrome and WebView2 theme.

### System tray
- [ ] Tray icon reflects connection state (4 states).
- [ ] Single-click toggles window visibility.
- [ ] Right-click shows context menu with all specified items.
- [ ] Context menu items are enabled/disabled based on connection state.

### Bridge
- [ ] WebView2 Control UI connects to gateway and renders all views.
- [ ] Connection state changes in WebView2 update tray icon within 1 second.
- [ ] Tray "Open Chat" navigates WebView2 to chat tab.
- [ ] Theme changes propagate bidirectionally.

### Notifications and dialogs
- [ ] Exec approval shows native dialog with command details.
- [ ] Device pairing shows native dialog with device info.
- [ ] Toast notifications appear when window is minimized.
- [ ] Clicking a toast notification opens the relevant view.

### Keyboard
- [ ] `Ctrl+Shift+O` toggles window from any app.
- [ ] `Ctrl+K` opens command palette in WebView2.
- [ ] `Ctrl+1..4` switches domains.
- [ ] All listed shortcuts work as specified.

### Performance
- [ ] Cold start to interactive: < 3 seconds.
- [ ] WebView2 navigation between tabs: < 200ms.
- [ ] Memory usage: < 300MB baseline (WebView2 included).
- [ ] System tray response: < 100ms for menu display.
