# Rules for AI agents

Verify: `node test/check.mjs` (the same command CI runs on every pull request).

Enter Key Control: a Chrome MV3 extension plus small Windows (C#) and macOS (Objective-C) helpers that make Enter insert a
new line and Ctrl+Enter (Cmd+Enter on macOS) send in Claude and other AI chat apps.

## Commands

- `node test/check.mjs`: static checks, no dependencies. Parses every script, checks every file `manifest.json` points to
  exists, checks the host lists agree, checks the desktop helpers are complete, and on Windows compiles
  `windows/ClaudeEnterNewline.cs` with the installer's compiler options. Node 24 (CI).
- Key behaviour needs real key presses (synthetic events are ignored by design): serve the repo root with
  `python -m http.server 8765` and open `test/harness.html` and `test/popup-harness.html?url=https://claude.ai/`.

## Layout

- `extension/`: the extension. `content.js` handles the keys, `sites.js` is the list of supported sites, `popup.*` the popup.
- `windows/ClaudeEnterNewline.cs` and `windows/install.ps1`: the Windows helper for the Claude desktop app.
- `macos/ClaudeEnterNewline.m` and `macos/install.sh`: the macOS helper.
- `test/`: `check.mjs` and the 2 manual harness pages.

## Rules

- Adding or removing a site touches 3 places that must agree: `extension/sites.js`, `host_permissions` and
  `content_scripts.matches` in `extension/manifest.json`. `node test/check.mjs` fails when they differ.
- Do not run `windows/install.ps1`, `macos/install.sh` or the uninstall scripts: they install into the user's session.
- `LICENSE` is the Solarity AI Use License (free use, no redistribution; see README); keep the copyright holder as
  "Solarity AI LLC".

## Shipping

- 1 issue per piece of work, then a branch `feat/<issue>-<slug>` or `fix/<issue>-<slug>`.
- Commit subjects `feat: … (#<issue>)` or `fix: … (#<issue>)`; the pull request body says `Fixes #<issue>`.
- Squash merge once CI is green.
