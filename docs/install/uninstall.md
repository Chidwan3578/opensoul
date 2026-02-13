---
summary: "Uninstall OpenSoul completely (CLI, service, state, workspace)"
read_when:
  - You want to remove OpenSoul from a machine
  - The gateway service is still running after uninstall
title: "Uninstall"
---

# Uninstall

Two paths:

- **Easy path** if `opensoul` is still installed.
- **Manual service removal** if the CLI is gone but the service is still running.

## Easy path (CLI still installed)

Recommended: use the built-in uninstaller:

```bash
opensoul uninstall
```

Non-interactive (automation / npx):

```bash
opensoul uninstall --all --yes --non-interactive
npx -y opensoul uninstall --all --yes --non-interactive
```

Manual steps (same result):

1. Stop the gateway service:

```bash
opensoul gateway stop
```

2. Uninstall the gateway service (launchd/systemd/schtasks):

```bash
opensoul gateway uninstall
```

3. Delete state + config:

```bash
rm -rf "${OPENSOUL_STATE_DIR:-$HOME/.opensoul}"
```

If you set `OPENSOUL_CONFIG_PATH` to a custom location outside the state dir, delete that file too.

4. Delete your workspace (optional, removes agent files):

```bash
rm -rf ~/.opensoul/workspace
```

5. Remove the CLI install (pick the one you used):

```bash
npm rm -g opensoul
pnpm remove -g opensoul
bun remove -g opensoul
```

6. If you installed the macOS app:

```bash
rm -rf /Applications/OpenSoul.app
```

Notes:

- If you used profiles (`--profile` / `OPENSOUL_PROFILE`), repeat step 3 for each state dir (defaults are `~/.opensoul-<profile>`).
- In remote mode, the state dir lives on the **gateway host**, so run steps 1-4 there too.

## Manual service removal (CLI not installed)

Use this if the gateway service keeps running but `opensoul` is missing.

### macOS (launchd)

Default label is `ai.opensoul.gateway` (or `ai.opensoul.<profile>`; legacy `com.opensoul.*` may still exist):

```bash
launchctl bootout gui/$UID/ai.opensoul.gateway
rm -f ~/Library/LaunchAgents/ai.opensoul.gateway.plist
```

If you used a profile, replace the label and plist name with `ai.opensoul.<profile>`. Remove any legacy `com.opensoul.*` plists if present.

### Linux (systemd user unit)

Default unit name is `opensoul-gateway.service` (or `opensoul-gateway-<profile>.service`):

```bash
systemctl --user disable --now opensoul-gateway.service
rm -f ~/.config/systemd/user/opensoul-gateway.service
systemctl --user daemon-reload
```

### Windows (Scheduled Task)

Default task name is `OpenSoul Gateway` (or `OpenSoul Gateway (<profile>)`).
The task script lives under your state dir.

```powershell
schtasks /Delete /F /TN "OpenSoul Gateway"
Remove-Item -Force "$env:USERPROFILE\.opensoul\gateway.cmd"
```

If you used a profile, delete the matching task name and `~\.opensoul-<profile>\gateway.cmd`.

## Normal install vs source checkout

### Normal install (install.sh / npm / pnpm / bun)

If you used `https://opensoul.ai/install.sh` or `install.ps1`, the CLI was installed with `npm install -g opensoul@latest`.
Remove it with `npm rm -g opensoul` (or `pnpm remove -g` / `bun remove -g` if you installed that way).

### Source checkout (git clone)

If you run from a repo checkout (`git clone` + `opensoul ...` / `bun run opensoul ...`):

1. Uninstall the gateway service **before** deleting the repo (use the easy path above or manual service removal).
2. Delete the repo directory.
3. Remove state + workspace as shown above.
