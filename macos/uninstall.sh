#!/bin/sh
# Stops Claude Enter Newline for macOS and removes its LaunchAgent and build.
set -eu
label=ai.solarity.claude-enter-newline
launchctl bootout "gui/$(id -u)/$label" >/dev/null 2>&1 || true
rm -f "$HOME/Library/LaunchAgents/$label.plist" "$HOME/.local/state/claude-enter-newline/ClaudeEnterNewline"
printf '\033[32m%s\033[0m\n' "✅ Claude Enter Newline for macOS removed"
