param([string]$PublishDirectory = (Join-Path $PSScriptRoot '../artifacts/publish'))
$ErrorActionPreference = 'Stop'
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$appPath = Join-Path $publishPath 'KeepingYouAwake.exe'
$cliPath = Join-Path $publishPath 'kya.exe'
$instance = 'smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$dataDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../artifacts/tests/$instance"))
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$hostProcess = $null
$assertions = 0
function Assert-That([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAILED: $Description" }
    $script:assertions++
    Write-Output "PASS $Description"
}
function Call-Cli([string[]]$CommandArguments) {
    $output = & $cliPath @CommandArguments --instance $instance --json
    $exitCode = $LASTEXITCODE
    $response = ($output -join "`n") | ConvertFrom-Json
    $response | Add-Member -NotePropertyName ExitCode -NotePropertyValue $exitCode
    return $response
}
function Start-Host {
    $script:hostProcess = Start-Process -FilePath $appPath -ArgumentList "--simulate --headless --instance $instance --data-dir `"$dataDirectory`"" -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        $response = Call-Cli @('status')
        if ($response.success) {
            if (-not $response.state.simulation) { throw 'Refusing to test a real-power instance.' }
            return
        }
        if ($script:hostProcess.HasExited) { throw 'Simulation app exited unexpectedly.' }
        Start-Sleep -Milliseconds 100
    }
    throw 'Simulation app did not become ready.'
}
function Stop-Host {
    if ($script:hostProcess -and -not $script:hostProcess.HasExited) {
        Stop-Process -Id $script:hostProcess.Id -Force
        $script:hostProcess.WaitForExit(5000) | Out-Null
    }
}
try {
    Start-Host
    $state = Call-Cli @('status')
    Assert-That ($state.ExitCode -eq 0 -and -not $state.state.awake -and $state.state.simulation) 'isolated simulation starts idle'
    $response = Call-Cli @('awake', 'on', '--for', '2s', '--screen-off')
    Assert-That ($response.success -and $response.state.manualAwake -and -not $response.state.preferences.keepScreenOn) 'CLI toggles manual wake without keeping screen on'
    Start-Sleep -Milliseconds 2300
    $response = Call-Cli @('status')
    Assert-That (-not $response.state.awake) 'dispatcher expires real-time wake duration'
    $response = Call-Cli @('shutdown', '--after', '2m')
    $planId = $response.state.shutdown.id
    Assert-That ($response.success -and $response.state.scheduleAwake) 'CLI schedule acquires automatic wake lease'
    $response = Call-Cli @('shutdown', '--after', '3m')
    Assert-That (-not $response.success -and $response.code -eq 'conflict' -and $response.ExitCode -eq 5 -and $response.state.shutdown.id -eq $planId) 'conflicting schedule is rejected without overwrite'
    $response = Call-Cli @('shutdown', '--after', '3m', '--replace')
    Assert-That ($response.success -and $response.state.shutdown.id -ne $planId) 'explicit replacement succeeds'
    $response = Call-Cli @('cancel')
    Assert-That ($response.success -and -not $response.state.awake) 'cancellation releases schedule wake lease'
    $response = Call-Cli @('shutdown')
    Assert-That ($response.success -and $response.state.shutdown.phase -eq 'countdown' -and $response.state.countdownSeconds -ge 59) 'immediate request creates cancellable 60-second countdown'
    $response = Call-Cli @('cancel')
    Assert-That ($response.success -and $null -eq $response.state.shutdown) 'countdown cancels through CLI'
    $response = Call-Cli @('awake', 'on', '--screen-on')
    $response = Call-Cli @('shutdown', '--after', '10m')
    $planId = $response.state.shutdown.id
    Stop-Host
    Start-Host
    $response = Call-Cli @('status')
    Assert-That ($response.state.shutdown.id -eq $planId -and $response.state.awake -and -not $response.state.manualAwake -and $response.state.preferences.keepScreenOn) 'restart restores future plan and preferences but not manual wake'
    $null = Call-Cli @('cancel')
    Stop-Host
    $stateFile = Join-Path $dataDirectory 'state.json'
    $saved = Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $saved.plan = @{ id = [Guid]::NewGuid().ToString(); dueUtc = [DateTimeOffset]::UtcNow.AddMinutes(-5).ToString('o'); source = 'cli'; phase = 'pending'; countdownEndsUtc = $null }
    [System.IO.File]::WriteAllText($stateFile, ($saved | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    Start-Host
    $response = Call-Cli @('status')
    Assert-That ($null -eq $response.state.shutdown -and $response.state.lastEvent.kind -eq 'missed') 'expired persisted plan is cancelled on launch'
    Stop-Host
    [System.IO.File]::WriteAllText($stateFile, '{ broken json', [System.Text.UTF8Encoding]::new($false))
    Start-Host
    $response = Call-Cli @('status')
    Assert-That ($response.success -and -not $response.state.awake -and @(Get-ChildItem -LiteralPath $dataDirectory -Filter 'state.json.invalid-*').Count -eq 1) 'corrupt state is preserved and defaults restored safely'
    $response = Call-Cli @('shutdown', '--force')
    Assert-That ($response.ExitCode -eq 2 -and $response.code -eq 'invalid') 'force shutdown is not accepted'
    $response = Call-Cli @('shutdown')
    Write-Output 'Waiting for the real 60-second timer in simulation; no actual shutdown will be performed.'
    $deadline = [DateTime]::UtcNow.AddSeconds(75)
    do {
        Start-Sleep -Seconds 2
        $response = Call-Cli @('status')
    } while ($response.state.shutdown -and [DateTime]::UtcNow -lt $deadline)
    Assert-That ($response.state.simulation -and $null -eq $response.state.shutdown -and $response.state.lastEvent.kind -eq 'shutdown-submitted') 'real dispatcher reaches simulated shutdown after grace period'
    $events = Get-Content -LiteralPath (Join-Path $dataDirectory 'events.jsonl') -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json }
    Assert-That (@($events | Where-Object { $_.kind -eq 'shutdown-submitted' }).Count -eq 1) 'shutdown is submitted exactly once'
    [ordered]@{ Passed = $assertions; Simulation = $true; DataDirectory = $dataDirectory } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataDirectory 'result.json') -Encoding UTF8
    Write-Output "Smoke test passed: $assertions checks; real power operations: 0."
} finally { Stop-Host }
