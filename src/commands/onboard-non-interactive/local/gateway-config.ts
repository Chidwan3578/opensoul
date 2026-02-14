import crypto from "node:crypto";
import type { OpenSoulConfig } from "../../../config/config.js";
import type { RuntimeEnv } from "../../../runtime.js";
import type { OnboardOptions } from "../../onboard-types.js";

/**
 * Result of applying gateway configuration.
 */
export interface GatewayConfigResult {
  nextConfig: OpenSoulConfig;
  port: number;
  bind: string;
  gatewayToken?: string;
  authMode?: string;
  tailscaleMode?: string;
}

/**
 * Parameters for applying gateway configuration non-interactively.
 */
export interface ApplyNonInteractiveGatewayConfigParams {
  nextConfig: OpenSoulConfig;
  opts: OnboardOptions;
  runtime: RuntimeEnv;
  defaultPort: number;
}

/**
 * Applies gateway configuration from non-interactive CLI flags.
 * Returns the updated config and resolved gateway params, or
 * `undefined` if configuration is invalid.
 */
export function applyNonInteractiveGatewayConfig(
  params: ApplyNonInteractiveGatewayConfigParams,
): GatewayConfigResult | undefined {
  const { nextConfig, opts, defaultPort } = params;

  const port = opts.gatewayPort ?? defaultPort;
  const bind = opts.gatewayBind ?? "loopback";
  const tailscaleMode = opts.tailscale ?? "off";

  // Determine auth mode and token.
  let authMode = opts.gatewayAuth as string | undefined;
  let gatewayToken = opts.gatewayToken;
  const gatewayPassword = opts.gatewayPassword;

  // When binding to LAN/custom/tailnet without explicit auth, default to token auth for security.
  if (!authMode && bind !== "loopback") {
    authMode = "token";
  }

  // Auto-generate token if token auth mode but no token provided.
  if (authMode === "token" && !gatewayToken) {
    gatewayToken = crypto.randomBytes(24).toString("base64url");
  }

  // Build the gateway auth config.
  const gatewayAuth: Record<string, unknown> = {};
  if (authMode) {
    gatewayAuth.mode = authMode;
    if (authMode === "token" && gatewayToken) {
      gatewayAuth.token = gatewayToken;
    }
    if (authMode === "password" && gatewayPassword) {
      gatewayAuth.password = gatewayPassword;
    }
  }

  const updatedConfig: OpenSoulConfig = {
    ...nextConfig,
    gateway: {
      ...nextConfig.gateway,
      mode: "local",
      port,
      bind,
      ...(Object.keys(gatewayAuth).length > 0 ? { auth: gatewayAuth } : {}),
    },
  };

  return {
    nextConfig: updatedConfig,
    port,
    bind,
    gatewayToken,
    authMode,
    tailscaleMode,
  };
}
