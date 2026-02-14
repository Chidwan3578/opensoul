import type { OpenSoulConfig } from "../../../config/config.js";
import type { RuntimeEnv } from "../../../runtime.js";
import type { OnboardOptions } from "../../onboard-types.js";
import { resolveOpenSoulAgentDir } from "../../../agents/agent-paths.js";
import { upsertAuthProfile } from "../../../agents/auth-profiles.js";
import {
  applyAuthProfileConfig,
  applyCloudflareAiGatewayConfig,
  applyKimiCodeConfig,
  applyMoonshotConfig,
  applyOpenrouterConfig,
  applyQianfanConfig,
  applySyntheticConfig,
  applyVeniceConfig,
  applyVercelAiGatewayConfig,
  applyXaiConfig,
  applyXiaomiConfig,
  applyZaiConfig,
} from "../../onboard-auth.config-core.js";
import {
  setAnthropicApiKey,
  setCloudflareAiGatewayConfig,
  setGeminiApiKey,
  setKimiCodingApiKey,
  setMinimaxApiKey,
  setMoonshotApiKey,
  setOpencodeZenApiKey,
  setOpenrouterApiKey,
  setQianfanApiKey,
  setSyntheticApiKey,
  setVeniceApiKey,
  setVercelAiGatewayApiKey,
  setXaiApiKey,
  setXiaomiApiKey,
  setZaiApiKey,
} from "../../onboard-auth.credentials.js";
import { applyOpenAIConfig } from "../../openai-model-default.js";

/**
 * Parameters for applying a non-interactive auth choice.
 */
export interface ApplyNonInteractiveAuthChoiceParams {
  nextConfig: OpenSoulConfig;
  authChoice: string;
  opts: OnboardOptions;
  runtime: RuntimeEnv;
  baseConfig: OpenSoulConfig;
}

/**
 * Applies the selected auth choice to the config in a non-interactive
 * onboarding flow. Returns the updated config, or `undefined` if the
 * choice is invalid and the process should abort.
 */
export async function applyNonInteractiveAuthChoice(
  params: ApplyNonInteractiveAuthChoiceParams,
): Promise<OpenSoulConfig | undefined> {
  const { nextConfig, authChoice, opts, runtime } = params;

  if (authChoice === "skip") {
    return nextConfig;
  }

  // Token-based auth (generic).
  if (authChoice === "token") {
    if (!opts.token) {
      runtime.error("--token is required when --auth-choice=token");
      runtime.exit(1);
      return undefined;
    }
    const profileId = opts.tokenProfileId ?? `${opts.tokenProvider ?? "anthropic"}:default`;
    const tokenProvider = opts.tokenProvider ?? "anthropic";
    upsertAuthProfile({
      profileId,
      credential: {
        type: "token",
        provider: tokenProvider,
        token: opts.token,
      },
      agentDir: resolveOpenSoulAgentDir(),
    });
    return applyAuthProfileConfig(nextConfig, {
      profileId,
      provider: tokenProvider,
      mode: "token",
    });
  }

  // xAI API key.
  if (authChoice === "xai-api-key" && opts.xaiApiKey) {
    setXaiApiKey(opts.xaiApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "xai:default",
      provider: "xai",
      mode: "api_key",
    });
    cfg = applyXaiConfig(cfg);
    return cfg;
  }

  // OpenAI API key.
  if (authChoice === "openai-api-key" && opts.openaiApiKey) {
    process.env.OPENAI_API_KEY = opts.openaiApiKey;
    const cfg = applyOpenAIConfig(nextConfig);
    return cfg;
  }

  // Vercel AI Gateway API key.
  if (authChoice === "ai-gateway-api-key" && opts.aiGatewayApiKey) {
    await setVercelAiGatewayApiKey(opts.aiGatewayApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "vercel-ai-gateway:default",
      provider: "vercel-ai-gateway",
      mode: "api_key",
    });
    cfg = applyVercelAiGatewayConfig(cfg);
    return cfg;
  }

  // Cloudflare AI Gateway API key.
  if (authChoice === "cloudflare-ai-gateway-api-key" && opts.cloudflareAiGatewayApiKey) {
    await setCloudflareAiGatewayConfig(
      opts.cloudflareAiGatewayAccountId ?? "",
      opts.cloudflareAiGatewayGatewayId ?? "",
      opts.cloudflareAiGatewayApiKey,
    );
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "cloudflare-ai-gateway:default",
      provider: "cloudflare-ai-gateway",
      mode: "api_key",
    });
    cfg = applyCloudflareAiGatewayConfig(cfg, {
      accountId: opts.cloudflareAiGatewayAccountId,
      gatewayId: opts.cloudflareAiGatewayGatewayId,
    });
    return cfg;
  }

  // OpenRouter API key.
  if (authChoice === "openrouter-api-key" && opts.openrouterApiKey) {
    await setOpenrouterApiKey(opts.openrouterApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "openrouter:default",
      provider: "openrouter",
      mode: "api_key",
    });
    cfg = applyOpenrouterConfig(cfg);
    return cfg;
  }

  // Moonshot API key.
  if (authChoice === "moonshot-api-key" && opts.moonshotApiKey) {
    await setMoonshotApiKey(opts.moonshotApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "moonshot:default",
      provider: "moonshot",
      mode: "api_key",
    });
    cfg = applyMoonshotConfig(cfg);
    return cfg;
  }

  // Kimi Coding API key.
  if (authChoice === "kimi-code-api-key" && opts.kimiCodeApiKey) {
    await setKimiCodingApiKey(opts.kimiCodeApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "kimi-coding:default",
      provider: "kimi-coding",
      mode: "api_key",
    });
    cfg = applyKimiCodeConfig(cfg);
    return cfg;
  }

  // Gemini API key.
  if (authChoice === "gemini-api-key" && opts.geminiApiKey) {
    await setGeminiApiKey(opts.geminiApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "google:default",
      provider: "google",
      mode: "api_key",
    });
    return cfg;
  }

  // ZAI API key.
  if (authChoice === "zai-api-key" && opts.zaiApiKey) {
    await setZaiApiKey(opts.zaiApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "zai:default",
      provider: "zai",
      mode: "api_key",
    });
    cfg = applyZaiConfig(cfg);
    return cfg;
  }

  // Xiaomi API key.
  if (authChoice === "xiaomi-api-key" && opts.xiaomiApiKey) {
    await setXiaomiApiKey(opts.xiaomiApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "xiaomi:default",
      provider: "xiaomi",
      mode: "api_key",
    });
    cfg = applyXiaomiConfig(cfg);
    return cfg;
  }

  // Synthetic API key.
  if (authChoice === "synthetic-api-key" && opts.syntheticApiKey) {
    await setSyntheticApiKey(opts.syntheticApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "synthetic:default",
      provider: "synthetic",
      mode: "api_key",
    });
    cfg = applySyntheticConfig(cfg);
    return cfg;
  }

  // Venice API key.
  if (authChoice === "venice-api-key" && opts.veniceApiKey) {
    await setVeniceApiKey(opts.veniceApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "venice:default",
      provider: "venice",
      mode: "api_key",
    });
    cfg = applyVeniceConfig(cfg);
    return cfg;
  }

  // Opencode Zen API key.
  if (authChoice === "opencode-zen" && opts.opencodeZenApiKey) {
    await setOpencodeZenApiKey(opts.opencodeZenApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "opencode:default",
      provider: "opencode",
      mode: "api_key",
    });
    return cfg;
  }

  // Qianfan API key.
  if (authChoice === "qianfan-api-key" && opts.qianfanApiKey) {
    setQianfanApiKey(opts.qianfanApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "qianfan:default",
      provider: "qianfan",
      mode: "api_key",
    });
    cfg = applyQianfanConfig(cfg);
    return cfg;
  }

  // Minimax API key.
  if (
    (authChoice === "minimax-api" || authChoice === "minimax-api-lightning") &&
    opts.minimaxApiKey
  ) {
    await setMinimaxApiKey(opts.minimaxApiKey);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "minimax:default",
      provider: "minimax",
      mode: "api_key",
    });
    return cfg;
  }

  // Anthropic API key (via generic apiKey choice).
  if (authChoice === "apiKey" && opts.token) {
    await setAnthropicApiKey(opts.token);
    let cfg = applyAuthProfileConfig(nextConfig, {
      profileId: "anthropic:default",
      provider: "anthropic",
      mode: "api_key",
    });
    return cfg;
  }

  runtime.error(`Unsupported auth choice for non-interactive mode: ${authChoice}`);
  runtime.exit(1);
  return undefined;
}
