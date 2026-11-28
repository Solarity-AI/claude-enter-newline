// Static checks for the extension, no dependencies: `node test/check.mjs` from the repo root.
// - every .js file in extension/ and test/ parses (node --check)
// - every file manifest.json points to exists
// - the hosts in sites.js, host_permissions and content_scripts.matches are the same set
// - the desktop helpers are complete; on Windows the helper compiles with the installer's csc options
import { execFileSync } from "node:child_process";
import { existsSync, mkdtempSync, readdirSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

const root = fileURLToPath(new URL("..", import.meta.url));
const ext = join(root, "extension");
const errors = [];

for (const dir of [ext, join(root, "test")]) {
  for (const file of readdirSync(dir).filter((f) => /\.(m?js)$/.test(f))) {
    try {
      execFileSync(process.execPath, ["--check", join(dir, file)], { stdio: "pipe" });
    } catch (err) {
      errors.push(`${file}: syntax error\n${err.stderr}`);
    }
  }
}

const manifest = JSON.parse(readFileSync(join(ext, "manifest.json"), "utf8"));
const referenced = [
  manifest.background.service_worker,
  manifest.action.default_popup,
  ...Object.values(manifest.action.default_icon),
  ...Object.values(manifest.icons),
  ...manifest.content_scripts.flatMap((cs) => cs.js)
];
for (const file of new Set(referenced)) {
  if (!existsSync(join(ext, file))) errors.push(`manifest.json references missing file ${file}`);
}

const sandbox = { self: {} };
vm.runInNewContext(readFileSync(join(ext, "sites.js"), "utf8"), sandbox);
const toPattern = (host) => `https://${host}/*`;
const fromSites = sandbox.self.EKC.SITES.flatMap((s) => s.hosts).map(toPattern).sort();
const lists = {
  host_permissions: [...manifest.host_permissions].sort(),
  "content_scripts[0].matches": [...manifest.content_scripts[0].matches].sort()
};
for (const [name, list] of Object.entries(lists)) {
  const missing = fromSites.filter((p) => !list.includes(p));
  const extra = list.filter((p) => !fromSites.includes(p));
  if (missing.length) errors.push(`${name} is missing ${missing.join(", ")} (listed in sites.js)`);
  if (extra.length) errors.push(`${name} has ${extra.join(", ")}, which sites.js doesn't list`);
}

// Files popup.html loads (scripts, stylesheet, icon) exist, so a rename can't leave a blank or broken popup.
const popupHtml = readFileSync(join(ext, "popup.html"), "utf8");
for (const [, ref] of popupHtml.matchAll(/(?:src|href)="([^"#?:]+)"/g)) {
  if (!existsSync(join(ext, ref))) errors.push(`popup.html references missing file ${ref}`);
}

// The README's "Supported sites" table lists exactly the sites and hosts in sites.js.
const readme = readFileSync(join(root, "README.md"), "utf8");
const tableStart = readme.indexOf("### Supported sites");
const tableEnd = readme.indexOf("\n###", tableStart + 1);
const tableText = tableStart < 0 ? "" : readme.slice(tableStart, tableEnd < 0 ? undefined : tableEnd);
const readmeRows = tableText.split("\n").filter((l) => l.startsWith("|") && !/^\|\s*(Site|-)/.test(l))
  .map((l) => l.split("|").slice(1, 3).map((c) => c.trim()));
const wantRows = sandbox.self.EKC.SITES.map((s) => `${s.name} = ${s.hosts.join(", ")}`).sort();
const haveRows = readmeRows.map(([name, hosts]) => `${name} = ${hosts}`).sort();
if (JSON.stringify(wantRows) !== JSON.stringify(haveRows)) {
  errors.push(`README.md "Supported sites" table differs from sites.js\n  sites.js: ${wantRows.join("; ")}\n  README:   ${haveRows.join("; ")}`);
}

// The desktop helpers: every file the READMEs and installers rely on is there.
const helperFiles = [
  "windows/ClaudeEnterNewline.cs", "windows/install.ps1", "windows/uninstall.ps1", "windows/README.md",
  "macos/ClaudeEnterNewline.m", "macos/install.sh", "macos/uninstall.sh", "macos/README.md"
];
for (const file of helperFiles) {
  if (!existsSync(join(root, file))) errors.push(`missing desktop helper file ${file}`);
}

// The Windows helper's safeguards against Windows silently removing its keyboard hook (see windows/README.md):
// no process lookup inside the hook callback, and a timer that installs the hook again.
const cs = readFileSync(join(root, "windows", "ClaudeEnterNewline.cs"), "utf8");
const hookBody = cs.slice(cs.indexOf("static IntPtr Hook("), cs.indexOf("static INPUT Key("));
if (/ProcessPath|OpenProcess|IsClaudeDesktop/.test(hookBody)) errors.push("windows: the hook callback looks up processes again; keep it to a flag read");
if (!/InstallHook\("timer"\)/.test(cs)) errors.push("windows: the periodic re-hook timer is gone");

// On Windows, compile the helper exactly as install.ps1 does.
const csc = join(process.env.WINDIR || "C:\\Windows", "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
let compiled = "skipped (not Windows)";
if (process.platform === "win32" && existsSync(csc)) {
  const out = join(mkdtempSync(join(tmpdir(), "cen-")), "ClaudeEnterNewline.exe");
  try {
    execFileSync(csc, ["/nologo", "/optimize+", "/target:winexe", `/out:${out}`, "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll",
      join(root, "windows", "ClaudeEnterNewline.cs")], { stdio: "pipe" });
    compiled = "compiled";
  } catch (err) {
    errors.push(`windows: csc failed\n${err.stdout}${err.stderr}`);
  }
}

if (errors.length) {
  console.error(errors.join("\n"));
  process.exit(1);
}
console.log(`OK: ${fromSites.length} hosts agree across sites.js and manifest.json; JS syntax and manifest files check out; desktop helpers present, Windows helper ${compiled}.`);
