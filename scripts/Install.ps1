param([string]$Source = (Join-Path $PSScriptRoot '../artifacts/publish'), [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$installDirectory = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/KeepingYouAwake'))
$allowedDirectory = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
if (-not $installDirectory.StartsWith($allowedDirectory + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid installation path.' }
foreach ($binary in @('KeepingYouAwake.exe', 'kya.exe', 'Uninstall.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourcePath $binary))) { throw "Missing published file: $binary" }
}
$running = @(Get-Process -Name 'KeepingYouAwake' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $installDirectory 'KeepingYouAwake.exe') })
if ($running.Count -gt 0) { throw 'Please exit the installed app before updating.' }
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $sourcePath -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $installDirectory -Force }
foreach ($binary in @('KeepingYouAwake.exe', 'kya.exe')) {
    if ((Get-FileHash -LiteralPath (Join-Path $sourcePath $binary)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installDirectory $binary)).Hash) { throw "Installation hash mismatch: $binary" }
}
$shortcutDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'KeepingYouAwake'
New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut((Join-Path $shortcutDirectory 'KeepingYouAwake.lnk'))
$shortcut.TargetPath = Join-Path $installDirectory 'KeepingYouAwake.exe'
$shortcut.Arguments = '--show'
$shortcut.WorkingDirectory = $installDirectory
$shortcut.Description = 'Coffee tray: keep awake and schedule shutdown'
$shortcut.IconLocation = "$installDirectory\KeepingYouAwake.exe,0"
$shortcut.Save()
$removeShortcut = $wsh.CreateShortcut((Join-Path $shortcutDirectory 'Uninstall.lnk'))
$removeShortcut.TargetPath = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$removeShortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$installDirectory\Uninstall.ps1`""
$removeShortcut.WorkingDirectory = $env:TEMP
$removeShortcut.Save()
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = @($userPath -split ';' | Where-Object { $_ })
if (-not ($pathEntries | Where-Object { $_.TrimEnd('\') -ieq $installDirectory.TrimEnd('\') })) {
    [Environment]::SetEnvironmentVariable('Path', (($pathEntries + $installDirectory) -join ';'), 'User')
}
if (-not ('KeepingYouAwake.EnvironmentBroadcast' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace KeepingYouAwake {
    public static class EnvironmentBroadcast {
        [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
    }
}
'@
}
$broadcastResult = [UIntPtr]::Zero
[KeepingYouAwake.EnvironmentBroadcast]::SendMessageTimeout([IntPtr]0xffff, 0x1a, [UIntPtr]::Zero, 'Environment', 2, 1000, [ref]$broadcastResult) | Out-Null
# Register in Windows Installed Apps; no elevation and no automatic startup.
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KeepingYouAwake'
New-Item -Path $uninstallKey -Force | Out-Null
$properties = @{ DisplayName = 'KeepingYouAwake'; DisplayVersion = '1.0.0'; Publisher = 'KeepingYouAwake'; InstallLocation = $installDirectory; DisplayIcon = "$installDirectory\KeepingYouAwake.exe,0"; UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$installDirectory\Uninstall.ps1`"" }
foreach ($name in $properties.Keys) { New-ItemProperty -Path $uninstallKey -Name $name -Value $properties[$name] -PropertyType String -Force | Out-Null }
New-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
[ordered]@{ Version = '1.0.0'; InstalledUtc = [DateTime]::UtcNow.ToString('o'); Source = $sourcePath } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installDirectory 'installation.json') -Encoding UTF8
if (-not $NoLaunch) { Start-Process -FilePath (Join-Path $installDirectory 'KeepingYouAwake.exe') -ArgumentList '--show' -WorkingDirectory $installDirectory -WindowStyle Hidden }
Write-Output "Installed: $installDirectory"
Write-Output "CLI: $installDirectory\kya.exe (kya in newly opened terminals)"
