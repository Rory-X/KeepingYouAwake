param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$expectedDirectory = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/KeepingYouAwake'))
$actualDirectory = (Resolve-Path -LiteralPath $PSScriptRoot).Path.TrimEnd('\')
if ($actualDirectory -ine $expectedDirectory.TrimEnd('\')) { throw 'Run this uninstaller from the installed app directory.' }
if (-not $Quiet) {
    $answer = [System.Windows.MessageBox]::Show('卸载 KeepingYouAwake 并取消待执行计划？设置和日志将保留。', '卸载 KeepingYouAwake', 'YesNo', 'Question')
    if ($answer -ne 'Yes') { exit 0 }
}
Get-Process -Name 'KeepingYouAwake' -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq (Join-Path $expectedDirectory 'KeepingYouAwake.exe') } | Stop-Process -Force
$statePath = Join-Path $env:LOCALAPPDATA 'KeepingYouAwake/data/state.json'
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $state.plan = $null
    $state.preferences.startWithWindows = $false
    $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding UTF8
}
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'KeepingYouAwake' -ErrorAction SilentlyContinue
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$remaining = @($userPath -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ine $expectedDirectory.TrimEnd('\') })
[Environment]::SetEnvironmentVariable('Path', ($remaining -join ';'), 'User')
$shortcutDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'KeepingYouAwake'
if (Test-Path -LiteralPath $shortcutDirectory) {
    $resolvedShortcuts = (Resolve-Path -LiteralPath $shortcutDirectory).Path
    $allowedShortcuts = [System.IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('Programs')) 'KeepingYouAwake'))
    if ($resolvedShortcuts -ine $allowedShortcuts) { throw 'Invalid shortcut path.' }
    Remove-Item -LiteralPath $resolvedShortcuts -Recurse -Force
}
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KeepingYouAwake' -Recurse -ErrorAction SilentlyContinue
# Verify the resolved absolute target immediately before recursive removal.
$resolvedInstall = (Resolve-Path -LiteralPath $expectedDirectory).Path
if ($resolvedInstall.TrimEnd('\') -ine $expectedDirectory.TrimEnd('\')) { throw 'Invalid uninstall target.' }
Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
if (-not $Quiet) { [System.Windows.MessageBox]::Show('KeepingYouAwake 已卸载。重新安装时不会恢复关机计划。', '卸载完成') | Out-Null }
