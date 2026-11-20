# Claude Enter Newline for Windows

The Claude desktop app on Windows has no setting to make Enter add a new line
([anthropics/claude-code#89673](https://github.com/anthropics/claude-code/issues/89673), open).
The macOS version is in [`../macos`](../macos/).

While the Claude desktop app is the active window:

| You press | The app gets | Result |
|---|---|---|
| Enter | Shift+Enter | new line |
| Ctrl+Enter | Enter | send |
| Shift+Enter | unchanged | new line |

Other apps, the `claude` CLI in a terminal and all other keys are untouched.
It applies to the whole app (Code and Chat tabs). In a dialog where Enter should press a button, use Ctrl+Enter or click.

## Install / remove

From the repository root, in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File windows\install.ps1
```

```powershell
powershell -ExecutionPolicy Bypass -File windows\uninstall.ps1
```

`install.ps1` builds the helper with Windows' own C# compiler (`csc.exe` from .NET Framework 4), so nothing is
downloaded, into `%USERPROFILE%\.local\state\claude-enter-newline`, and registers the scheduled task
"Claude Enter Newline", which starts it at sign-in and again within 5 minutes if it stops.
A tray icon shows it is running: double-click it to pause/resume; right-click → Exit stops it until the task restarts it.

## Check it

```powershell
& "$HOME\.local\state\claude-enter-newline\ClaudeEnterNewline.exe" --check | Out-String
```

It lists the Claude processes it recognises as the desktop app and reads `status.txt`, which the running helper
rewrites every 30 s. `OK: hook renewed N s ago` means the keyboard hook is live; `STALE` or `NO STATUS` means the
helper is not running (or is a build from before 2.0).

## Why it keeps working

Windows silently removes a low-level keyboard hook whose callback takes longer than about 1 s
(`LowLevelHooksTimeout`), for example while the PC is busy or waking from sleep, and never tells the program.
Version 1 then kept running with no hook, so its watchdog saw nothing wrong while Enter sent messages again.
Version 2:

1. does no lookups inside the hook: which app is in front comes from a foreground-change event (plus a 250 ms check),
   and the hook only reads a flag, so it cannot hit the time limit;
2. installs the hook again every 30 s and after resume from sleep or unlock, new hook first, so a removed hook comes
   back within 30 s and no key press is missed;
3. writes `status.txt`, so `--check` shows whether it is alive.

## Notes

- It only acts on Enter while `Claude.exe` from the desktop app's install folder is in front, and records no keys.
  Corporate security software may still flag an unsigned program that hooks the keyboard.
- Remove it once Anthropic ships a built-in option.
