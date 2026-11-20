# Claude Enter Newline for macOS

Workaround for the Claude desktop app on macOS, which has no setting to make Enter add a new line
([anthropics/claude-code#89673](https://github.com/anthropics/claude-code/issues/89673), open).
The Windows version is in [`../windows`](../windows/).

While the Claude desktop app is in front:

| You press | The app gets | Result |
|---|---|---|
| Enter | Shift+Enter | new line |
| Cmd+Enter or Ctrl+Enter | Enter | send |
| Shift+Enter | unchanged | new line |

Other apps, the `claude` CLI in a terminal and all other keys are untouched; the CLI and the Codex CLI take Enter = new
line from `~/.claude/keybindings.json` and `~/.codex/config.toml` instead. In a dialog where Enter should press a
button, use Cmd+Enter or click.

## Install / remove

From the repository root:

```bash
sh macos/install.sh
```

```bash
sh macos/uninstall.sh
```

It builds with the Command Line Tools' `clang` into `~/.local/state/claude-enter-newline/ClaudeEnterNewline`, so
nothing is downloaded, and the LaunchAgent `ai.solarity.claude-enter-newline` starts it at login and again if it stops.

**Permission:** macOS asks once for the Accessibility permission (System Settings › Privacy & Security ›
Accessibility). The helper starts within 15 s of the grant, with no restart by hand: until then it exits and launchd
starts a fresh copy, which sees the new permission. The build is signed ad hoc with a requirement that names the helper
by identifier (`ai.solarity.claude-enter-newline`) rather than by the build's hash, so later rebuilds keep the
permission, and an unchanged source is not rebuilt.

Check it: `~/.local/state/claude-enter-newline/ClaudeEnterNewline --check`

Remove it once Anthropic ships a built-in option.
