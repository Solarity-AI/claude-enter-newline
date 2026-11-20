# Stops Claude Enter Newline and removes it, its scheduled task and the old sign-in shortcut.
# Run from the repository root:  powershell -ExecutionPolicy Bypass -File windows\uninstall.ps1
Unregister-ScheduledTask -TaskName 'Claude Enter Newline' -Confirm:$false -ErrorAction SilentlyContinue
Get-Process ClaudeEnterNewline -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
Remove-Item (Join-Path ([Environment]::GetFolderPath('Startup')) 'Claude Enter Newline.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:USERPROFILE '.local\state\claude-enter-newline') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA 'ClaudeEnterNewline') -Recurse -Force -ErrorAction SilentlyContinue
Write-Output 'Removed Claude Enter Newline.'
