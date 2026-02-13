import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import type { OpenSoulConfig } from "./types.js";

/**
 * Nix mode detection: When OPENSOUL_NIX_MODE=1, the gateway is running under Nix.
 * In this mode:
 * - No auto-install flows should be attempted
 * - Missing dependencies should produce actionable Nix-specific error messages
 * - Config is managed externally (read-only from Nix perspective)
 */
export function resolveIsNixMode(env: NodeJS.ProcessEnv = process.env): boolean {
  return env.OPENSOUL_NIX_MODE === "1";
}

export const isNixMode = resolveIsNixMode();

const LEGACY_STATE_DIRNAMES = [".openclaw", ".clawdbot", ".moltbot", ".moldbot"] as const;
const NEW_STATE_DIRNAME = ".opensoul";
const CONFIG_FILENAME = "opensoul.json";
const LEGACY_CONFIG_FILENAMES = ["openclaw.json", "clawdbot.json", "moltbot.json", "moldbot.json"] as const;

function legacyStateDirs(homedir: () => string = os.homedir): string[] {
  return LEGACY_STATE_DIRNAMES.map((dir) => path.join(homedir(), dir));
}

function newStateDir(homedir: () => string = os.homedir): string {
  return path.join(homedir(), NEW_STATE_DIRNAME);
}

export function resolveLegacyStateDir(homedir: () => string = os.homedir): string {
  return legacyStateDirs(homedir)[0] ?? newStateDir(homedir);
}

export function resolveLegacyStateDirs(homedir: () => string = os.homedir): string[] {
  return legacyStateDirs(homedir);
}

export function resolveNewStateDir(homedir: () => string = os.homedir): string {
  return newStateDir(homedir);
}

/**
 * State directory for mutable data (sessions, logs, caches).
 * Can be overridden via OPENSOUL_STATE_DIR.
 * Default: ~/.opensoul
 */
export function resolveStateDir(
  env: NodeJS.ProcessEnv = process.env,
  homedir: () => string = os.homedir,
): string {
  const override = env.OPENSOUL_STATE_DIR?.trim() || env.OPENCLAW_STATE_DIR?.trim();
  if (override) {
    return resolveUserPath(override);
  }
  const newDir = newStateDir(homedir);
  const legacyDirs = legacyStateDirs(homedir);
  const hasNew = fs.existsSync(newDir);
  if (hasNew) {
    return newDir;
  }
  const existingLegacy = legacyDirs.find((dir) => {
    try {
      return fs.existsSync(dir);
    } catch {
      return false;
    }
  });
  if (existingLegacy) {
    return existingLegacy;
  }
  return newDir;
}

function resolveUserPath(input: string): string {
  const trimmed = input.trim();
  if (!trimmed) {
    return trimmed;
  }
  if (trimmed.startsWith("~")) {
    const expanded = trimmed.replace(/^~(?=$|[\\/])/, os.homedir());
    return path.resolve(expanded);
  }
  return path.resolve(trimmed);
}

export const STATE_DIR = resolveStateDir();

/**
 * Config file path (JSON5).
 * Can be overridden via OPENSOUL_CONFIG_PATH.
 * Default: ~/.opensoul/opensoul.json (or $OPENSOUL_STATE_DIR/opensoul.json)
 */
export function resolveCanonicalConfigPath(
  env: NodeJS.ProcessEnv = process.env,
  stateDir: string = resolveStateDir(env, os.homedir),
): string {
  const override = env.OPENSOUL_CONFIG_PATH?.trim() || env.OPENCLAW_CONFIG_PATH?.trim();
  if (override) {
    return resolveUserPath(override);
  }
  return path.join(stateDir, CONFIG_FILENAME);
}

/**
 * Resolve the active config path by preferring existing config candidates
 * before falling back to the canonical path.
 */
export function resolveConfigPathCandidate(
  env: NodeJS.ProcessEnv = process.env,
  homedir: () => string = os.homedir,
): string {
  const candidates = resolveDefaultConfigCandidates(env, homedir);
  const existing = candidates.find((candidate) => {
    try {
      return fs.existsSync(candidate);
    } catch {
      return false;
    }
  });
  if (existing) {
    return existing;
  }
  return resolveCanonicalConfigPath(env, resolveStateDir(env, homedir));
}

/**
 * Active config path (prefers existing config files).
 */
export function resolveConfigPath(
  env: NodeJS.ProcessEnv = process.env,
  stateDir: string = resolveStateDir(env, os.homedir),
  homedir: () => string = os.homedir,
): string {
  const override = env.OPENSOUL_CONFIG_PATH?.trim();
  if (override) {
    return resolveUserPath(override);
  }
  const stateOverride = env.OPENSOUL_STATE_DIR?.trim();
  const candidates = [
    path.join(stateDir, CONFIG_FILENAME),
    ...LEGACY_CONFIG_FILENAMES.map((name) => path.join(stateDir, name)),
  ];
  const existing = candidates.find((candidate) => {
    try {
      return fs.existsSync(candidate);
    } catch {
      return false;
    }
  });
  if (existing) {
    return existing;
  }
  if (stateOverride) {
    return path.join(stateDir, CONFIG_FILENAME);
  }
  const defaultStateDir = resolveStateDir(env, homedir);
  if (path.resolve(stateDir) === path.resolve(defaultStateDir)) {
    return resolveConfigPathCandidate(env, homedir);
  }
  return path.join(stateDir, CONFIG_FILENAME);
}

export const CONFIG_PATH = resolveConfigPathCandidate();

/**
 * Resolve default config path candidates across default locations.
 * Order: explicit config path → state-dir-derived paths → new default.
 */
export function resolveDefaultConfigCandidates(
  env: NodeJS.ProcessEnv = process.env,
  homedir: () => string = os.homedir,
): string[] {
  const explicit = env.OPENSOUL_CONFIG_PATH?.trim() || env.OPENCLAW_CONFIG_PATH?.trim();
  if (explicit) {
    return [resolveUserPath(explicit)];
  }

  const candidates: string[] = [];
  const opensoulStateDir = env.OPENSOUL_STATE_DIR?.trim() || env.OPENCLAW_STATE_DIR?.trim();
  if (opensoulStateDir) {
    const resolved = resolveUserPath(opensoulStateDir);
    candidates.push(path.join(resolved, CONFIG_FILENAME));
    candidates.push(...LEGACY_CONFIG_FILENAMES.map((name) => path.join(resolved, name)));
  }

  const defaultDirs = [newStateDir(homedir), ...legacyStateDirs(homedir)];
  for (const dir of defaultDirs) {
    candidates.push(path.join(dir, CONFIG_FILENAME));
    candidates.push(...LEGACY_CONFIG_FILENAMES.map((name) => path.join(dir, name)));
  }
  return candidates;
}

export const DEFAULT_GATEWAY_PORT = 18789;

/**
 * Gateway lock directory (ephemeral).
 * Default: os.tmpdir()/opensoul-<uid> (uid suffix when available).
 */
export function resolveGatewayLockDir(tmpdir: () => string = os.tmpdir): string {
  const base = tmpdir();
  const uid = typeof process.getuid === "function" ? process.getuid() : undefined;
  const suffix = uid != null ? `opensoul-${uid}` : "opensoul";
  return path.join(base, suffix);
}

const OAUTH_FILENAME = "oauth.json";

/**
 * OAuth credentials storage directory.
 *
 * Precedence:
 * - `OPENSOUL_OAUTH_DIR` (explicit override)
 * - `$*_STATE_DIR/credentials` (canonical server/default)
 */
export function resolveOAuthDir(
  env: NodeJS.ProcessEnv = process.env,
  stateDir: string = resolveStateDir(env, os.homedir),
): string {
  const override = env.OPENSOUL_OAUTH_DIR?.trim();
  if (override) {
    return resolveUserPath(override);
  }
  return path.join(stateDir, "credentials");
}

export function resolveOAuthPath(
  env: NodeJS.ProcessEnv = process.env,
  stateDir: string = resolveStateDir(env, os.homedir),
): string {
  return path.join(resolveOAuthDir(env, stateDir), OAUTH_FILENAME);
}

export function resolveGatewayPort(
  cfg?: OpenSoulConfig,
  env: NodeJS.ProcessEnv = process.env,
): number {
  const envRaw = env.OPENSOUL_GATEWAY_PORT?.trim() || env.OPENCLAW_GATEWAY_PORT?.trim();
  if (envRaw) {
    const parsed = Number.parseInt(envRaw, 10);
    if (Number.isFinite(parsed) && parsed > 0) {
      return parsed;
    }
  }
  const configPort = cfg?.gateway?.port;
  if (typeof configPort === "number" && Number.isFinite(configPort)) {
    if (configPort > 0) {
      return configPort;
    }
  }
  return DEFAULT_GATEWAY_PORT;
}
