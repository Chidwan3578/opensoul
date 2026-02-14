import { tabFromPath, type Tab } from "./navigation.ts";
import { connectGateway } from "./app-gateway.ts";
import {
  startLogsPolling,
  startNodesPolling,
  stopLogsPolling,
  stopNodesPolling,
  startDebugPolling,
  stopDebugPolling,
} from "./app-polling.ts";
import { observeTopbar, scheduleChatScroll, scheduleLogsScroll } from "./app-scroll.ts";
import {
  applySettings,
  applySettingsFromUrl,
  attachThemeListener,
  detachThemeListener,
  inferBasePath,
  setTab,
  setTheme,
  syncTabWithLocation,
  syncThemeWithSettings,
} from "./app-settings.ts";
import {
  attachDesktopBridgeListener,
  detachDesktopBridgeListener,
  isDesktopShell,
  sendShellReady,
  type DesktopInitPayload,
} from "./desktop-bridge.ts";
import type { ThemeMode } from "./theme.ts";

type LifecycleHost = {
  basePath: string;
  tab: Tab;
  chatHasAutoScrolled: boolean;
  chatManualRefreshInFlight: boolean;
  chatLoading: boolean;
  chatMessages: unknown[];
  chatToolMessages: unknown[];
  chatStream: string;
  logsAutoFollow: boolean;
  logsAtBottom: boolean;
  logsEntries: unknown[];
  popStateHandler: () => void;
  topbarObserver: ResizeObserver | null;
};

export function handleConnected(host: LifecycleHost) {
  host.basePath = inferBasePath();
  applySettingsFromUrl(host as unknown as Parameters<typeof applySettingsFromUrl>[0]);
  syncTabWithLocation(host as unknown as Parameters<typeof syncTabWithLocation>[0], true);
  syncThemeWithSettings(host as unknown as Parameters<typeof syncThemeWithSettings>[0]);
  attachThemeListener(host as unknown as Parameters<typeof attachThemeListener>[0]);
  window.addEventListener("popstate", host.popStateHandler);

  // Initialize desktop bridge if running inside WPF shell
  if (isDesktopShell()) {
    initDesktopBridge(host);
  }

  connectGateway(host as unknown as Parameters<typeof connectGateway>[0]);
  startNodesPolling(host as unknown as Parameters<typeof startNodesPolling>[0]);
  if (host.tab === "logs") {
    startLogsPolling(host as unknown as Parameters<typeof startLogsPolling>[0]);
  }
  if (host.tab === "debug") {
    startDebugPolling(host as unknown as Parameters<typeof startDebugPolling>[0]);
  }
}

export function handleFirstUpdated(host: LifecycleHost) {
  observeTopbar(host as unknown as Parameters<typeof observeTopbar>[0]);
}

export function handleDisconnected(host: LifecycleHost) {
  window.removeEventListener("popstate", host.popStateHandler);
  stopNodesPolling(host as unknown as Parameters<typeof stopNodesPolling>[0]);
  stopLogsPolling(host as unknown as Parameters<typeof stopLogsPolling>[0]);
  stopDebugPolling(host as unknown as Parameters<typeof stopDebugPolling>[0]);
  detachThemeListener(host as unknown as Parameters<typeof detachThemeListener>[0]);
  detachDesktopBridgeListener();
  host.topbarObserver?.disconnect();
  host.topbarObserver = null;
}

/**
 * Initialize the desktop bridge for WPF shell integration.
 * Handles host.init (gateway URL, theme), host.navigate, host.focus, etc.
 */
function initDesktopBridge(host: LifecycleHost) {
  attachDesktopBridgeListener({
    onInit: (payload: DesktopInitPayload) => {
      // Apply gateway URL and token from WPF shell if provided
      const settingsHost = host as unknown as Parameters<typeof applySettings>[0];
      if (payload.gatewayUrl || payload.token) {
        const next = { ...settingsHost.settings };
        if (payload.gatewayUrl) {
          next.gatewayUrl = payload.gatewayUrl;
        }
        if (payload.token) {
          next.token = payload.token;
        }
        if (payload.settings?.sessionKey) {
          next.sessionKey = payload.settings.sessionKey;
          next.lastActiveSessionKey = payload.settings.sessionKey;
        }
        applySettings(settingsHost, next);

        // Reconnect gateway with new URL/token
        connectGateway(host as unknown as Parameters<typeof connectGateway>[0]);
      }

      // Apply theme from shell
      if (payload.theme) {
        const themeMode = payload.theme as ThemeMode;
        setTheme(settingsHost, themeMode);
      }
    },

    onThemeChanged: (theme: string) => {
      const settingsHost = host as unknown as Parameters<typeof setTheme>[0];
      setTheme(settingsHost, theme as ThemeMode);
    },

    onNavigate: (tab: string) => {
      // Validate tab name before navigation
      const validTab = tabFromPath(`/${tab}`) as Tab | null;
      if (validTab) {
        setTab(host as unknown as Parameters<typeof setTab>[0], validTab);
      }
    },

    onFocus: (target: string) => {
      // Focus specific elements in the UI
      if (target === "chat-input") {
        const input = document.querySelector<HTMLTextAreaElement>(".chat-compose textarea");
        input?.focus();
      } else if (target === "search") {
        const input = document.querySelector<HTMLInputElement>(".search-input, .command-input");
        input?.focus();
      }
    },

    onFileDrop: (files) => {
      // TODO: Integrate with chat attachment system when available
      console.log("[desktop-bridge] File drop received:", files.length, "files");
    },

    onWindowState: (state: string) => {
      // Track window focus state for notification decisions
      (host as unknown as Record<string, unknown>)._windowFocused = state === "focused";
    },

    onCommandPalette: () => {
      // Toggle the command palette overlay in the web UI
      const existing = document.querySelector<HTMLElement>(".command-palette");
      if (existing) {
        // If already open, close it
        existing.remove();
        return;
      }
      // Dispatch a custom event that the UI can listen for
      window.dispatchEvent(new CustomEvent("opensoul:command-palette"));
    },
  });

  // Signal to WPF shell that Control UI is ready
  sendShellReady();
}

export function handleUpdated(host: LifecycleHost, changed: Map<PropertyKey, unknown>) {
  if (host.tab === "chat" && host.chatManualRefreshInFlight) {
    return;
  }
  if (
    host.tab === "chat" &&
    (changed.has("chatMessages") ||
      changed.has("chatToolMessages") ||
      changed.has("chatStream") ||
      changed.has("chatLoading") ||
      changed.has("tab"))
  ) {
    const forcedByTab = changed.has("tab");
    const forcedByLoad =
      changed.has("chatLoading") && changed.get("chatLoading") === true && !host.chatLoading;
    scheduleChatScroll(
      host as unknown as Parameters<typeof scheduleChatScroll>[0],
      forcedByTab || forcedByLoad || !host.chatHasAutoScrolled,
    );
  }
  if (
    host.tab === "logs" &&
    (changed.has("logsEntries") || changed.has("logsAutoFollow") || changed.has("tab"))
  ) {
    if (host.logsAutoFollow && host.logsAtBottom) {
      scheduleLogsScroll(
        host as unknown as Parameters<typeof scheduleLogsScroll>[0],
        changed.has("tab") || changed.has("logsAutoFollow"),
      );
    }
  }
}
