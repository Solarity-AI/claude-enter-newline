#!/bin/sh
# Builds Claude Enter Newline for macOS and keeps it running as a LaunchAgent (at login, and again if it stops).
# Builds with the Command Line Tools' clang, so nothing is downloaded. Prints ✅/⚠/❌ lines.
# CLAUDE_ENTER_NEWLINE_NO_AGENT=1 builds only and loads no LaunchAgent (used by tests).
set -eu

green() { printf '\033[32m%s\033[0m\n\n' "$1"; }
yellow() { printf '\033[33m%s\033[0m\n\n' "$1"; }
red() { printf '\033[31m%s\033[0m\n\n' "$1" >&2; }

[ "$(uname -s)" = Darwin ] || { yellow "⚠ Claude Enter Newline for macOS: not a Mac, nothing to do"; exit 0; }

src="$(cd "$(dirname "$0")" && pwd)/ClaudeEnterNewline.m"
state="$HOME/.local/state/claude-enter-newline"
exe="$state/ClaudeEnterNewline"
label=ai.solarity.claude-enter-newline
plist="$HOME/Library/LaunchAgents/$label.plist"
mkdir -p "$state" "$HOME/Library/LaunchAgents"

if ! xcrun --find clang >/dev/null 2>&1; then
    red "❌ No clang: install the Command Line Tools (xcode-select --install), then run this again"
    exit 1
fi

# The signature names the helper by identifier, not by the hash of this build, so the Accessibility permission granted
# once keeps working after every rebuild (an ad-hoc signature's default requirement is the build's hash, and a rebuild
# makes macOS ask again). An unchanged source is not rebuilt at all.
requirement="designated => identifier \"$label\""
stamp="$state/source.sha256"
sum="$(shasum -a 256 "$src" | cut -d' ' -f1)"
if [ -x "$exe" ] && [ "$(cat "$stamp" 2>/dev/null)" = "$sum" ] && codesign -d -r- "$exe" 2>&1 | grep -qF "$requirement"; then
    green "✅ $exe is up to date"
else
    # Build next to the old one and swap, so a failed build leaves the running helper alone.
    xcrun clang -O2 -fobjc-arc -framework AppKit -framework ApplicationServices "$src" -o "$exe.new"
    "$exe.new" --selftest >/dev/null || { red "❌ The new build failed its self-test; the old one stays"; rm -f "$exe.new"; exit 1; }
    codesign --force --sign - --identifier "$label" -r="$requirement" "$exe.new" >/dev/null 2>&1 ||
        { red "❌ codesign failed; the old build stays"; rm -f "$exe.new"; exit 1; }
    mv -f "$exe.new" "$exe"
    printf '%s\n' "$sum" > "$stamp"
    green "✅ Built $exe"
fi

[ "${CLAUDE_ENTER_NEWLINE_NO_AGENT:-}" = 1 ] && exit 0

cat > "$plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>$label</string>
    <key>ProgramArguments</key><array><string>$exe</string></array>
    <key>RunAtLoad</key><true/>
    <key>KeepAlive</key><true/>
    <key>ThrottleInterval</key><integer>10</integer>
    <key>ProcessType</key><string>Interactive</string>
    <key>StandardErrorPath</key><string>$state/helper.log</string>
    <key>StandardOutPath</key><string>$state/helper.log</string>
</dict>
</plist>
PLIST

domain="gui/$(id -u)"
launchctl bootout "$domain/$label" >/dev/null 2>&1 || true
launchctl bootstrap "$domain" "$plist"
green "✅ Started; it runs at every login and restarts if it stops"

sleep 3
if "$exe" --check | grep -q "helper: running"; then
    green "✅ Accessibility permission granted: Enter adds a new line in the Claude app"
else
    yellow "⚠ Grant the Accessibility permission to ClaudeEnterNewline (macOS shows a prompt; or open
   x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility ). It starts within 15 s of the grant; rebuilds keep it."
fi
