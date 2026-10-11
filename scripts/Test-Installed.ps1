param([string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs/KeepingYouAwake'))
$ErrorActionPreference = 'Stop'
$cliPath = Join-Path $InstallDirectory 'kya.exe'
$appPath = Join-Path $InstallDirectory 'KeepingYouAwake.exe'
function Invoke-BoundedCli([string]$Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $cliPath
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(25000)) { $process.Kill(); throw 'CLI did not return within 25 seconds.' }
        if (-not $stdout.Wait(5000) -or -not $stderr.Wait(5000)) { throw 'CLI left its output pipe open in the background process.' }
        if ($process.ExitCode -ne 0) { throw "CLI error $($process.ExitCode): $($stderr.Result) $($stdout.Result)" }
        return $stdout.Result | ConvertFrom-Json
    } finally { $process.Dispose() }
}
$existing = @(Get-Process -Name 'KeepingYouAwake' -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $appPath })
if ($existing.Count -gt 0) {
    $state = Invoke-BoundedCli 'status --json'
    if ($state.state.awake -or $state.state.shutdown) { throw 'Refusing to interrupt an active power-management session.' }
    $existing | Stop-Process -Force
}
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$state = Invoke-BoundedCli 'status --json'
if (-not $state.success -or $state.state.simulation -or $state.state.awake -or $state.state.shutdown) { throw 'Unexpected installed initial state.' }
Write-Output "PASS cold CLI auto-start returns and closes output handles ($($watch.ElapsedMilliseconds) ms)"
$state = Invoke-BoundedCli 'awake on --for 2s --screen-off --json'
if (-not $state.state.manualAwake -or $state.state.simulation) { throw 'Native awake API failed.' }
Write-Output 'PASS native idle-sleep prevention request succeeds'
Start-Sleep -Seconds 3
$state = Invoke-BoundedCli 'status --json'
if ($state.state.awake -or $state.state.shutdown) { throw 'Temporary native wake request was not released.' }
Write-Output 'PASS timed native wake request releases automatically'
$state = Invoke-BoundedCli 'show --json'
if (-not $state.success) { throw 'Installed panel could not open.' }
Write-Output 'PASS installed control panel opens'
