/**
 * Desktop Bridge Client - Web side of the WPF ↔ WebView2 communication layer.
 *
 * When the Control UI runs inside the WPF shell (WebView2), this module provides:
 * - Detection of desktop environment
 * - Receiving messages from the WPF shell (host.init, host.navigate, etc.)
 * - Sending messages to the WPF shell (shell.ready, shell.connectionStateChanged, etc.)
 * - Automatic integration with the existing app lifecycle
 *
 * When running in a regular browser, all functions gracefully no-op.
 */

import type { Tab } from "./navigation.ts";
import type { ResolvedTheme } from "./theme.ts";

// ═══════════ TYPE DEFINITIONS ═══════════

/** Bridge message envelope matching WPF BridgeService protocol. */
interface BridgeMessage {
  type: string;
  payload?: unknown;
  id?: string;
}

/** Configuration received from WPF shell on initialization. */
export interface DesktopInitPayload {
  theme?: string;
  gatewayUrl?: string;
  token?: string;
  settings?: {
    sessionKey?: string;
    historyLimit?: number;
  };
  platform?: string;
}

/** File drop info from WPF native drag-and-drop. */
export interface DesktopFileDropInfo {
  name: string;
  path: string;
  size: number;
}

/** Bridge API injected by WPF shell's GetBridgeInitScript(). */
interface DesktopBridgeApi {
  send: (type: string, payload?: unknown) => void;
  on: (type: string, handler: (payload: unknown) => void) => void;
  off: (type: string) => void;
  isDesktop: boolean;
  platform: string;
}

// ═══════════ DETECTION ═══════════

/** Check if running inside WPF WebView2 shell. */
export function isDesktopShell(): boolean {
  return !!(window as unknown as { __opensoul_bridge?: DesktopBridgeApi }).__opensoul_bridge
    ?.isDesktop;
}

/** Get the bridge API, or null if not in desktop shell. */
function getBridge(): DesktopBridgeApi | null {
  return (
    (window as unknown as { __opensoul_bridge?: DesktopBridgeApi }).__opensoul_bridge ?? null
  );
}

// ═══════════ SEND TO WPF SHELL ═══════════

/** Notify the WPF shell that the Control UI is fully initialized. */
export function sendShellReady(): void {
  const bridge = getBridge();
  if (!bridge) return;
  bridge.send("shell.ready", { version: "1.0" });
}

/** Notify the WPF shell about gateway connection state changes. */
export function sendConnectionStateChanged(state: "connected" | "disconnected" | "degraded"): void {
  const bridge = getBridge();
  if (!bridge) return;
  bridge.send("shell.connectionStateChanged", { state });
}

/** Notify the WPF shell about theme changes (from web UI toggle). */
export function sendThemeChanged(theme: ResolvedTheme): void {
  const bridge = getBridge();
  if (!bridge) return;
  bridge.send("shell.themeChanged", { theme });
}

/** Notify the WPF shell about active tab changes. */
export function sendTabChanged(tab: Tab, title: string): void {
  const bridge = getBridge();
  if (!bridge) return;
  bridge.send("shell.tabChanged", { tab, title });
}

/** Request a native Windows toast notification. */
export function sendNotify(title: string, body: string, tag?: string, action?: string): void {
  const bridge = getBridge();
  if (!bridge) return;
  bridge.send("shell.notify", { title, body, tag, action });
}

/** Request the WPF shell to open a URL in the default browser. */
export function sendOpenExternal(url: string): void {
  const bridge = getBridge();
  if (!bridge) return;
  bridge.send("shell.openExternal", { url });
}

/** Update the tray icon badge count. */
export function sendBadgeCount(count: number): void {
  const bridge = getBridge();
  if (!bridge) return;
  bridge.send("shell.badge", { count });
}

/** Request a gateway action (restart or stop) from WPF process manager. */
export function sendGatewayAction(action: "restart" | "stop"): void {
  const bridge = getBridge();
  if (!bridge) return;
  bridge.send("shell.gatewayAction", { action });
}

// ═══════════ RECEIVE FROM WPF SHELL ═══════════

/** Callback type for desktop bridge events. */
export type DesktopBridgeListener = {
  onInit?: (payload: DesktopInitPayload) => void;
  onThemeChanged?: (theme: string) => void;
  onNavigate?: (tab: string) => void;
  onFocus?: (target: string) => void;
  onFileDrop?: (files: Array<DesktopFileDropInfo>) => void;
  onWindowState?: (state: string) => void;
  onSettingsChanged?: (settings: Record<string, unknown>) => void;
  onCommandPalette?: () => void;
  onExecApprovalResult?: (requestId: string, approved: boolean, remember: boolean) => void;
  onDevicePairResult?: (requestId: string, approved: boolean) => void;
};

let currentListener: DesktopBridgeListener | null = null;

/**
 * Attach a listener for all desktop bridge events.
 * Only one listener at a time; calling again replaces the previous one.
 */
export function attachDesktopBridgeListener(listener: DesktopBridgeListener): void {
  const bridge = getBridge();
  if (!bridge) return;

  currentListener = listener;

  // Register handlers for each message type from WPF
  bridge.on("host.init", (payload) => {
    listener.onInit?.(payload as DesktopInitPayload);
  });

  bridge.on("host.themeChanged", (payload) => {
    const data = payload as { theme?: string } | undefined;
    if (data?.theme) {
      listener.onThemeChanged?.(data.theme);
    }
  });

  bridge.on("host.navigate", (payload) => {
    const data = payload as { tab?: string } | undefined;
    if (data?.tab) {
      listener.onNavigate?.(data.tab);
    }
  });

  bridge.on("host.focus", (payload) => {
    const data = payload as { target?: string } | undefined;
    if (data?.target) {
      listener.onFocus?.(data.target);
    }
  });

  bridge.on("host.fileDrop", (payload) => {
    const data = payload as { files?: Array<DesktopFileDropInfo> } | undefined;
    if (data?.files) {
      listener.onFileDrop?.(data.files);
    }
  });

  bridge.on("host.windowState", (payload) => {
    const data = payload as { state?: string } | undefined;
    if (data?.state) {
      listener.onWindowState?.(data.state);
    }
  });

  bridge.on("host.settingsChanged", (payload) => {
    if (payload && typeof payload === "object") {
      listener.onSettingsChanged?.(payload as Record<string, unknown>);
    }
  });

  bridge.on("host.commandPalette", () => {
    listener.onCommandPalette?.();
  });

  bridge.on("host.execApprovalResult", (payload) => {
    const data = payload as { requestId?: string; approved?: boolean; remember?: boolean };
    if (data?.requestId != null) {
      listener.onExecApprovalResult?.(data.requestId, !!data.approved, !!data.remember);
    }
  });

  bridge.on("host.devicePairResult", (payload) => {
    const data = payload as { requestId?: string; approved?: boolean };
    if (data?.requestId != null) {
      listener.onDevicePairResult?.(data.requestId, !!data.approved);
    }
  });
}

/** Detach the current desktop bridge listener. */
export function detachDesktopBridgeListener(): void {
  const bridge = getBridge();
  if (!bridge || !currentListener) return;

  bridge.off("host.init");
  bridge.off("host.themeChanged");
  bridge.off("host.navigate");
  bridge.off("host.focus");
  bridge.off("host.fileDrop");
  bridge.off("host.windowState");
  bridge.off("host.settingsChanged");
  bridge.off("host.commandPalette");
  bridge.off("host.execApprovalResult");
  bridge.off("host.devicePairResult");

  currentListener = null;
}
