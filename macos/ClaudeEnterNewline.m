// Claude Enter Newline for macOS: while the Claude desktop app is in front, Enter adds a new line and
// Cmd+Enter or Ctrl+Enter sends. The app has no setting for it (anthropics/claude-code#89673).
//
//   Enter            -> Shift+Enter (new line)
//   Cmd/Ctrl+Enter   -> Enter       (send)
//   Shift+Enter      -> unchanged   (new line)
//
// Every other app and key passes through untouched, and nothing is recorded. It needs the Accessibility permission
// (System Settings > Privacy & Security > Accessibility); without it, it asks once and starts within 15 s of the grant.
// Objective-C rather than Swift: the Command Line Tools' Swift can fail on a duplicate SwiftBridging module map.
//
// Build:  xcrun clang -O2 -fobjc-arc -framework AppKit -framework ApplicationServices ClaudeEnterNewline.m -o ClaudeEnterNewline
// Check:  ClaudeEnterNewline --check   (the permission and the app in front, then exits)
// Test:   ClaudeEnterNewline --selftest (the remap table, exit 1 on a mismatch)
#import <AppKit/AppKit.h>
#import <ApplicationServices/ApplicationServices.h>

static const int64_t kReturnKey = 36;
static const int64_t kKeypadEnter = 76;
static CFMachPortRef gTap = NULL;

// The flags to send for one Enter event, or -1 to leave it alone. Pure, so --selftest can check it.
static int64_t Remap(CGEventFlags flags) {
    const CGEventFlags mods = kCGEventFlagMaskCommand | kCGEventFlagMaskControl | kCGEventFlagMaskShift | kCGEventFlagMaskAlternate;
    CGEventFlags held = flags & mods;
    if (held == 0) return (int64_t)(flags | kCGEventFlagMaskShift);                       // Enter -> new line
    if (held == kCGEventFlagMaskCommand || held == kCGEventFlagMaskControl)               // Cmd/Ctrl+Enter -> send
        return (int64_t)(flags & ~(kCGEventFlagMaskCommand | kCGEventFlagMaskControl));
    return -1;                                                                            // Shift+Enter and the rest
}

static BOOL ClaudeInFront(void) {
    NSString *front = NSWorkspace.sharedWorkspace.frontmostApplication.bundleIdentifier;
    return [front isEqualToString:@"com.anthropic.claudefordesktop"];
}

static BOOL Trusted(BOOL prompt) {
    NSDictionary *options = @{(__bridge NSString *)kAXTrustedCheckOptionPrompt: @(prompt)};
    return AXIsProcessTrustedWithOptions((__bridge CFDictionaryRef)options);
}

static CGEventRef OnKey(CGEventTapProxy proxy, CGEventType type, CGEventRef event, void *info) {
    if (type == kCGEventTapDisabledByTimeout || type == kCGEventTapDisabledByUserInput) {
        if (gTap) CGEventTapEnable(gTap, true);
        return event;
    }
    int64_t code = CGEventGetIntegerValueField(event, kCGKeyboardEventKeycode);
    if (code != kReturnKey && code != kKeypadEnter) return event;
    int64_t flags = Remap(CGEventGetFlags(event));
    if (flags < 0 || !ClaudeInFront()) return event;
    CGEventSetFlags(event, (CGEventFlags)flags);
    return event;
}

static int SelfTest(void) {
    struct { CGEventFlags in; int64_t out; const char *name; } cases[] = {
        {0, (int64_t)kCGEventFlagMaskShift, "Enter"},
        {kCGEventFlagMaskCommand, 0, "Cmd+Enter"},
        {kCGEventFlagMaskControl, 0, "Ctrl+Enter"},
        {kCGEventFlagMaskShift, -1, "Shift+Enter"},
        {kCGEventFlagMaskAlternate, -1, "Option+Enter"},
        {kCGEventFlagMaskCommand | kCGEventFlagMaskShift, -1, "Cmd+Shift+Enter"},
        {kCGEventFlagMaskNumericPad, (int64_t)(kCGEventFlagMaskNumericPad | kCGEventFlagMaskShift), "keypad Enter"},
    };
    int failed = 0;
    for (size_t i = 0; i < sizeof cases / sizeof cases[0]; i++) {
        int64_t got = Remap(cases[i].in);
        BOOL ok = got == cases[i].out;
        printf("%s %s\n", ok ? "ok" : "FAIL", cases[i].name);
        if (!ok) failed++;
    }
    return failed ? 1 : 0;
}

int main(int argc, const char *argv[]) {
    @autoreleasepool {
        for (int i = 1; i < argc; i++) {
            if (strcmp(argv[i], "--selftest") == 0) return SelfTest();
            if (strcmp(argv[i], "--check") == 0) {
                NSString *front = NSWorkspace.sharedWorkspace.frontmostApplication.bundleIdentifier ?: @"none";
                // Run from a terminal, macOS checks the terminal's permission, not the LaunchAgent's, so the helper's
                // own state comes from the last line of its log.
                NSString *log = [NSHomeDirectory() stringByAppendingPathComponent:@".local/state/claude-enter-newline/helper.log"];
                NSString *text = [NSString stringWithContentsOfFile:log encoding:NSUTF8StringEncoding error:nil] ?: @"";
                NSString *last = [[[text stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet]
                    componentsSeparatedByString:@"\n"] lastObject];
                printf("helper: %s\n", [last hasPrefix:@"Claude Enter Newline is running"] ? "running (permission granted)"
                                       : [last hasPrefix:@"Waiting"] ? "waiting for the Accessibility permission" : "not started");
                printf("app in front: %s%s\n", front.UTF8String, ClaudeInFront() ? " (Claude: Enter is remapped)" : "");
                return 0;
            }
        }

        // macOS only tells a process about an Accessibility grant made after it started when it restarts, so a
        // helper without the permission asks once (a marker file remembers it), then exits; launchd (KeepAlive)
        // starts a fresh one 10 s later, which sees the grant and starts the tap with nobody restarting it by hand.
        if (!Trusted(NO)) {
            NSString *marker = [NSHomeDirectory() stringByAppendingPathComponent:@".local/state/claude-enter-newline/asked"];
            if (![NSFileManager.defaultManager fileExistsAtPath:marker]) {
                Trusted(YES);
                [NSFileManager.defaultManager createFileAtPath:marker contents:nil attributes:nil];
                fprintf(stderr, "Waiting for the Accessibility permission; it starts within 15 s of the grant\n");
            }
            [NSThread sleepForTimeInterval:5];
            return 0;
        }

        CGEventMask mask = CGEventMaskBit(kCGEventKeyDown) | CGEventMaskBit(kCGEventKeyUp);
        gTap = CGEventTapCreate(kCGSessionEventTap, kCGHeadInsertEventTap, kCGEventTapOptionDefault, mask, OnKey, NULL);
        if (!gTap) {
            fprintf(stderr, "Could not create the keyboard tap\n");
            return 1;
        }
        CFRunLoopSourceRef source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, gTap, 0);
        CFRunLoopAddSource(CFRunLoopGetCurrent(), source, kCFRunLoopCommonModes);
        CGEventTapEnable(gTap, true);
        printf("Claude Enter Newline is running\n");
        fflush(stdout);
        [[NSRunLoop currentRunLoop] run];
    }
    return 0;
}
