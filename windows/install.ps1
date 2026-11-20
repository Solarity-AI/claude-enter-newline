# Builds Claude Enter Newline with Windows' built-in C# compiler (nothing is downloaded),
# installs it to ~\.local\state\claude-enter-newline, and starts it now, at every sign-in and again within 5 minutes
# whenever it is not running (a scheduled task; the helper's mutex makes an extra start a no-op).
# Run from the repository root:  powershell -ExecutionPolicy Bypass -File windows\install.ps1
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

$src = Join-Path $PSScriptRoot 'ClaudeEnterNewline.cs'
# Not under AppData: run from inside the Claude desktop app (an MSIX package), writes to AppData land in the
# package's private copy (AppData\Local\Packages\AnthropicPBC.Claude_...\LocalCache), so Task Scheduler and the
# sign-in shortcut found no exe there (0x80070002) and the helper never came back.
$dir = Join-Path $env:USERPROFILE '.local\state\claude-enter-newline'
$exe = Join-Path $dir 'ClaudeEnterNewline.exe'
New-Item -ItemType Directory -Force $dir | Out-Null

# Stop a running copy so the exe can be replaced, then remove the old AppData install (and the app's private copy
# of it, when run from inside the app).
Get-Process ClaudeEnterNewline -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
Remove-Item (Join-Path $env:LOCALAPPDATA 'ClaudeEnterNewline') -Recurse -Force -ErrorAction SilentlyContinue

& $csc /nologo /optimize+ /target:winexe "/out:$exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# The old Startup shortcut started it only at sign-in, so a helper stopped by a reinstall from a background sync never
# came back until the next sign-in. A per-user scheduled task starts it in the signed-in desktop session at sign-in and
# every 5 minutes; IgnoreNew and the mutex keep it to one copy.
Remove-Item (Join-Path ([Environment]::GetFolderPath('Startup')) 'Claude Enter Newline.lnk') -ErrorAction SilentlyContinue
$task = 'Claude Enter Newline'
$user = "$env:USERDOMAIN\$env:USERNAME"
$logon = New-ScheduledTaskTrigger -AtLogOn -User $user
$every = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5)
$logon.Repetition = $every.Repetition
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $dir
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
  -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $task -Action $action -Trigger @($logon, $every) -Principal $principal -Settings $settings `
  -Description 'Claude desktop: Enter = new line, Ctrl+Enter = send. Restarts the helper if it is not running.' -Force | Out-Null

Start-ScheduledTask -TaskName $task
Start-Sleep -Seconds 2
# Right after the old copy is stopped, Task Scheduler may still count it as running and ignore the start (IgnoreNew);
# start the exe itself then (its mutex keeps it to one copy).
if (-not (Get-Process ClaudeEnterNewline -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $exe -WorkingDirectory $dir
    Start-Sleep -Seconds 2
}
if (-not (Get-Process ClaudeEnterNewline -ErrorAction SilentlyContinue)) { throw "Installed, but the helper is not running" }
if (-not (Test-Path (Join-Path $dir 'status.txt'))) { throw "Running, but the keyboard hook did not report in (no status.txt)" }
Write-Output "Installed: $exe"
Write-Output "Task:      $task (at sign-in and every 5 minutes)"
