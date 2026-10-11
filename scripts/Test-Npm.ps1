param([string]$PackagePath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if (-not $PackagePath) { $PackagePath = Join-Path $projectRoot "artifacts/npm/keeping-you-awake-$version.tgz" }
$tarballPath = (Resolve-Path -LiteralPath $PackagePath).Path
$npmPath = (Get-Command npm.cmd -ErrorAction Stop).Source
$npxPath = (Get-Command npx.cmd -ErrorAction Stop).Source
$testDirectory = Join-Path $projectRoot ('artifacts/tests/npm test ' + [Guid]::NewGuid().ToString('N'))
$instance = 'npm-' + [Guid]::NewGuid().ToString('N')
$hostProcess = $null
$assertions = 0
function Assert-That([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw "FAILED: $Description" }
    $script:assertions++
    Write-Output "PASS $Description"
}
function Call-Cli([string[]]$CommandArguments) {
    $output = & $script:cliPath @CommandArguments --instance $instance --json
    $exitCode = $LASTEXITCODE
    $response = ($output -join "`n") | ConvertFrom-Json
    $response | Add-Member -NotePropertyName ExitCode -NotePropertyValue $exitCode
    return $response
}
try {
    & $npmPath install --global --prefix $testDirectory $tarballPath --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'npm installation failed.' }
    $script:cliPath = Join-Path $testDirectory 'kya.cmd'
    $nativeDirectory = Join-Path $testDirectory 'node_modules/keeping-you-awake/native'
    $response = Call-Cli @('--version')
    Assert-That ($response.ExitCode -eq 0 -and $response.version -eq $version) 'installed command reports package version from a path containing spaces'
    $help = & $script:cliPath --help
    Assert-That ($LASTEXITCODE -eq 0 -and ($help -join "`n") -match 'KeepingYouAwake CLI') 'help output passes through npm command'
    $response = Call-Cli @('shutdown', '--force')
    Assert-That ($response.ExitCode -eq 2 -and $response.code -eq 'invalid') 'invalid arguments preserve native exit code and JSON'
    $dataDirectory = Join-Path $testDirectory 'simulation'
    $hostProcess = Start-Process -FilePath (Join-Path $nativeDirectory 'KeepingYouAwake.exe') -ArgumentList "--simulate --headless --instance $instance --data-dir `"$dataDirectory`"" -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        $response = Call-Cli @('status')
        if ($response.success) { break }
        if ($hostProcess.HasExited) { throw 'Simulation host exited.' }
        Start-Sleep -Milliseconds 100
    }
    Assert-That ($response.ExitCode -eq 0 -and $response.state.simulation -and -not $response.state.awake) 'npm command connects to bundled isolated simulation host'
    $response = Call-Cli @('awake', 'on', '--for', '2s')
    Assert-That ($response.success -and $response.state.manualAwake) 'arguments reach native CLI and enable simulated wake'
    Start-Sleep -Milliseconds 2300
    $response = Call-Cli @('status')
    Assert-That (-not $response.state.awake) 'simulated wake expires'
    $at = [DateTime]::Now.AddMinutes(10).ToString('yyyy-MM-dd HH:mm:ss')
    $response = Call-Cli @('shutdown', '--at', $at)
    Assert-That ($response.success -and $response.state.shutdown.phase -eq 'pending') 'date argument containing spaces remains one argument'
    $response = Call-Cli @('cancel')
    Assert-That ($response.success -and $null -eq $response.state.shutdown) 'npm command cancels simulated plan'
    $npxOutput = & $npxPath --yes --package $tarballPath kya --version --json
    Assert-That ($LASTEXITCODE -eq 0 -and ($npxOutput | ConvertFrom-Json).version -eq $version) 'npx executes the local tarball'
    Write-Output "npm test passed: $assertions checks; real power operations: 0."
} finally {
    if ($hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
        $hostProcess.WaitForExit(5000) | Out-Null
    }
}
