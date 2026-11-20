// Claude Enter Newline (Windows)
// While the Claude desktop app is the active window:
//   Enter       -> new line (sent to the app as Shift+Enter)
//   Ctrl+Enter  -> send     (sent to the app as a plain Enter)
// Every other app, and every other key, is left alone.
// A tray icon shows that it is running; right-click it to pause or exit.
//
// Staying alive: Windows silently removes a low-level keyboard hook whose callback runs longer than
// LowLevelHooksTimeout (about 1 s), and never tells the program. The process then keeps running, so a watchdog that
// only checks the process sees nothing wrong, while Enter sends again. So:
//   1. The hook callback does no lookups: which app is in front is worked out outside it (a foreground-change event
//      plus a 250 ms check) and the callback only reads a flag.
//   2. The hook is installed again every 30 s, and after resume from sleep and unlock, so a removed hook comes back
//      within 30 s. The new hook goes in before the old one comes out, so no key press is missed.
//   3. status.txt next to the exe records the last re-hook; --check reports it.
//
// Build: powershell -ExecutionPolicy Bypass -File install.ps1

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Claude Enter Newline")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]

static class Program
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const ushort VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    const ushort VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const uint LLKHF_INJECTED = 0x10;
    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
    const uint INPUT_KEYBOARD = 1;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0x0000;
    const int REHOOK_MS = 30000, FOREGROUND_POLL_MS = 250;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc fn, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

    // Kept in fields so the GC never collects the delegates Windows calls.
    static readonly LowLevelKeyboardProc HookProc = Hook;
    static readonly WinEventProc ForegroundProc = OnForeground;

    static IntPtr hookHandle, winEventHandle;
    static volatile bool enabled = true;
    static volatile bool claudeInFront;
    static bool swallowNextEnterUp;
    static IntPtr lastForeground = IntPtr.Zero;
    static NotifyIcon tray;
    static long rehooks, remaps;
    static DateTime startedAt;
    static string lastRehookReason = "start";

    static string StateDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
    static string StatusFile { get { return Path.Combine(StateDir, "status.txt"); } }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--check") return Check();

        bool created;
        using (var mutex = new Mutex(true, @"Local\ClaudeEnterNewline", out created))
        {
            if (!created) return 0; // already running

            startedAt = DateTime.Now;
            if (!InstallHook("start"))
            {
                MessageBox.Show("Could not install the keyboard hook (error " + Marshal.GetLastWin32Error() + ").",
                    "Claude Enter Newline", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            winEventHandle = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                ForegroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            RefreshForeground(true);

            // Backup for a missed foreground event; looks the process up only when the front window changes.
            var poll = new System.Windows.Forms.Timer { Interval = FOREGROUND_POLL_MS };
            poll.Tick += (s, e) => RefreshForeground(false);
            poll.Start();

            var rehook = new System.Windows.Forms.Timer { Interval = REHOOK_MS };
            rehook.Tick += (s, e) => InstallHook("timer");
            rehook.Start();

            SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == PowerModes.Resume) { InstallHook("resume"); RefreshForeground(true); } };
            SystemEvents.SessionSwitch += (s, e) =>
            {
                if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect
                    || e.Reason == SessionSwitchReason.RemoteConnect) { InstallHook("unlock"); RefreshForeground(true); }
            };

            var toggle = new ToolStripMenuItem("Enabled (Enter = new line)") { Checked = true, CheckOnClick = true };
            toggle.CheckedChanged += (s, e) => SetEnabled(toggle.Checked);
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (s, e) => Application.Exit();
            var menu = new ContextMenuStrip();
            menu.Items.Add(toggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            tray = new NotifyIcon { Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (s, e) => toggle.Checked = !toggle.Checked;
            SetEnabled(true);

            Application.Run();

            poll.Stop();
            rehook.Stop();
            tray.Visible = false;
            tray.Dispose();
            if (winEventHandle != IntPtr.Zero) UnhookWinEvent(winEventHandle);
            if (hookHandle != IntPtr.Zero) UnhookWindowsHookEx(hookHandle);
        }
        return 0;
    }

    // Puts in a fresh hook, then takes the old one out, so there is never a moment without one.
    // Windows may already have removed the old one; unhooking it then just fails, which is fine.
    static bool InstallHook(string reason)
    {
        IntPtr fresh = SetWindowsHookEx(WH_KEYBOARD_LL, HookProc, GetModuleHandle(null), 0);
        if (fresh == IntPtr.Zero) { WriteStatus("hook failed (" + reason + "), error " + Marshal.GetLastWin32Error()); return false; }
        IntPtr old = hookHandle;
        hookHandle = fresh;
        if (old != IntPtr.Zero) UnhookWindowsHookEx(old);
        rehooks++;
        lastRehookReason = reason;
        WriteStatus("ok");
        return true;
    }

    static void WriteStatus(string state)
    {
        try
        {
            File.WriteAllText(StatusFile,
                "state=" + state + "\r\n" +
                "pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + "\r\n" +
                "started=" + startedAt.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                "last_rehook=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                "last_rehook_reason=" + lastRehookReason + "\r\n" +
                "rehooks=" + rehooks + "\r\n" +
                "enter_remaps=" + Interlocked.Read(ref remaps) + "\r\n" +
                "enabled=" + enabled + "\r\n");
        }
        catch { /* status is a convenience; never stop the helper over it */ }
    }

    static void SetEnabled(bool on)
    {
        enabled = on;
        tray.Text = on ? "Claude: Enter = new line, Ctrl+Enter = send" : "Claude Enter Newline: paused";
    }

    static void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        RefreshForeground(true);
    }

    static void RefreshForeground(bool force)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (!force && hwnd == lastForeground) return;
        lastForeground = hwnd;
        if (hwnd == IntPtr.Zero) { claudeInFront = false; return; }
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        claudeInFront = IsClaudeDesktopPath(ProcessPath(pid));
    }

    static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    // Runs for every key press in Windows: keep it to flag reads and SendInput, nothing that can block.
    static IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            int msg = wParam.ToInt32();
            bool injected = (k.flags & LLKHF_INJECTED) != 0;

            if (k.vkCode == VK_RETURN && !injected)
            {
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                if (isUp && swallowNextEnterUp)
                {
                    swallowNextEnterUp = false;
                    return (IntPtr)1;
                }

                if (isDown && enabled && claudeInFront)
                {
                    bool shift = Down(VK_SHIFT), ctrl = Down(VK_CONTROL), alt = Down(VK_MENU);
                    bool win = Down(VK_LWIN) || Down(VK_RWIN);

                    if (!shift && !ctrl && !alt && !win)
                    {
                        // Enter -> Shift+Enter (new line)
                        Send(Key(VK_SHIFT, false), Key(VK_RETURN, false), Key(VK_RETURN, true), Key(VK_SHIFT, true));
                        swallowNextEnterUp = true;
                        Interlocked.Increment(ref remaps);
                        return (IntPtr)1;
                    }
                    if (ctrl && !shift && !alt && !win)
                    {
                        // Ctrl+Enter -> Enter (send). Lift Ctrl for the Enter, then put it back as the user holds it.
                        bool l = Down(VK_LCONTROL), r = Down(VK_RCONTROL);
                        var seq = new System.Collections.Generic.List<INPUT>();
                        if (l) seq.Add(Key(VK_LCONTROL, true));
                        if (r) seq.Add(Key(VK_RCONTROL, true));
                        seq.Add(Key(VK_RETURN, false));
                        seq.Add(Key(VK_RETURN, true));
                        if (l) seq.Add(Key(VK_LCONTROL, false));
                        if (r) seq.Add(Key(VK_RCONTROL, false));
                        Send(seq.ToArray());
                        swallowNextEnterUp = true;
                        Interlocked.Increment(ref remaps);
                        return (IntPtr)1;
                    }
                }
            }
        }
        return CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }

    static INPUT Key(ushort vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (vk == VK_RCONTROL) flags |= KEYEVENTF_EXTENDEDKEY;
        var i = new INPUT { type = INPUT_KEYBOARD };
        i.u.ki = new KEYBDINPUT { wVk = vk, dwFlags = flags };
        return i;
    }

    static void Send(params INPUT[] inputs)
    {
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    static string ProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    // The desktop app (Microsoft Store/MSIX, whose package folder is Claude_* or AnthropicPBC.Claude_*, or the classic
    // installer), never the claude.exe CLI the app bundles under Claude-Data\...\claude-code.
    static bool IsClaudeDesktopPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!Path.GetFileName(path).Equals("Claude.exe", StringComparison.OrdinalIgnoreCase)) return false;
        return path.IndexOf(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"\WindowsApps\AnthropicPBC.Claude_", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // --check: which running Claude processes count as the desktop app, and whether the running helper re-hooked lately.
    // Exit 0 = all well, 2 = desktop app not found, 3 = helper not re-hooking (stale status).
    static int Check()
    {
        int found = 0;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("claude"))
        {
            string path = ProcessPath((uint)p.Id);
            bool match = IsClaudeDesktopPath(path);
            if (match) found++;
            Console.WriteLine((match ? "DESKTOP  " : "ignored  ") + p.Id + "  " + (path ?? "(no access)"));
        }
        uint fgPid;
        GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
        Console.WriteLine("foreground: " + (ProcessPath(fgPid) ?? "(unknown)"));
        Console.WriteLine(found > 0 ? "OK: desktop app detected" : "NOT FOUND: desktop app not running or not recognised");

        int code = found > 0 ? 0 : 2;
        try
        {
            string text = File.ReadAllText(StatusFile);
            Console.WriteLine("--- status.txt");
            Console.Write(text);
            foreach (var line in text.Split('\n'))
            {
                if (!line.StartsWith("last_rehook=")) continue;
                DateTime at;
                if (DateTime.TryParse(line.Substring("last_rehook=".Length).Trim(), out at))
                {
                    double age = (DateTime.Now - at).TotalSeconds;
                    bool fresh = age < REHOOK_MS / 1000 * 3;
                    Console.WriteLine(fresh ? "OK: hook renewed " + (int)age + " s ago"
                                            : "STALE: hook last renewed " + (int)age + " s ago; the helper is not running or is stuck");
                    if (!fresh && code == 0) code = 3;
                }
            }
        }
        catch (Exception) { Console.WriteLine("NO STATUS: the helper has not written status.txt (not running, or an older build)"); if (code == 0) code = 3; }
        return code;
    }
}
