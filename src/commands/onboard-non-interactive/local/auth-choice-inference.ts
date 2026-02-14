import type { OnboardOptions } from "../../onboard-types.js";

/**
 * A single inferred auth match from CLI flags.
 */
export interface AuthChoiceMatch {
  /** Human-readable label for the matched provider flag. */
  label: string;
  /** The auth choice key that would be applied. */
  choice: string;
}

/**
 * Result of inferring auth choice from non-interactive flags.
 */
export interface InferredAuthChoice {
  /** The single best-guess auth choice, or `undefined` when ambiguous. */
  choice: string | undefined;
  /** All provider flags that matched. */
  matches: Array<AuthChoiceMatch>;
}

/** Map of OnboardOptions flag names to their corresponding auth choice strings. */
const FLAG_TO_AUTH_CHOICE: Array<{
  flag: keyof OnboardOptions;
  choice: string;
  label: string;
}> = [
  { flag: "xaiApiKey", choice: "xai-api-key", label: "--xai-api-key" },
  { flag: "openaiApiKey", choice: "openai-api-key", label: "--openai-api-key" },
  {
    flag: "cloudflareAiGatewayApiKey",
    choice: "cloudflare-ai-gateway-api-key",
    label: "--cloudflare-ai-gateway-api-key",
  },
  { flag: "aiGatewayApiKey", choice: "ai-gateway-api-key", label: "--ai-gateway-api-key" },
  { flag: "openrouterApiKey", choice: "openrouter-api-key", label: "--openrouter-api-key" },
  { flag: "moonshotApiKey", choice: "moonshot-api-key", label: "--moonshot-api-key" },
  { flag: "kimiCodeApiKey", choice: "kimi-code-api-key", label: "--kimi-code-api-key" },
  { flag: "geminiApiKey", choice: "gemini-api-key", label: "--gemini-api-key" },
  { flag: "zaiApiKey", choice: "zai-api-key", label: "--zai-api-key" },
  { flag: "xiaomiApiKey", choice: "xiaomi-api-key", label: "--xiaomi-api-key" },
  { flag: "syntheticApiKey", choice: "synthetic-api-key", label: "--synthetic-api-key" },
  { flag: "veniceApiKey", choice: "venice-api-key", label: "--venice-api-key" },
  { flag: "opencodeZenApiKey", choice: "opencode-zen", label: "--opencode-zen-api-key" },
  { flag: "qianfanApiKey", choice: "qianfan-api-key", label: "--qianfan-api-key" },
  { flag: "minimaxApiKey", choice: "minimax-api", label: "--minimax-api-key" },
  { flag: "token", choice: "token", label: "--token" },
];

/**
 * Inspects the non-interactive CLI flags and returns the inferred auth
 * provider choice. When more than one provider key is supplied the
 * caller should ask the user to disambiguate.
 */
export function inferAuthChoiceFromFlags(opts: OnboardOptions): InferredAuthChoice {
  const matches: Array<AuthChoiceMatch> = [];

  for (const entry of FLAG_TO_AUTH_CHOICE) {
    const value = opts[entry.flag];
    if (value !== undefined && value !== null && value !== "" && value !== false) {
      matches.push({ label: entry.label, choice: entry.choice });
    }
  }

  // When exactly one match exists, return it directly.
  if (matches.length === 1) {
    return { choice: matches[0].choice, matches };
  }

  // Zero or many matches → caller decides.
  return { choice: undefined, matches };
}
