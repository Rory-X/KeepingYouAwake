param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnetPath = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $projectRoot
try {
    & (Join-Path $PSScriptRoot 'New-Icons.ps1')
    & $dotnetPath run --project 'tests/KeepingYouAwake.Tests' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    foreach ($project in @('App', 'Cli')) {
        & $dotnetPath build "src/KeepingYouAwake.$project" -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    if (-not $SkipPublish) {
        $publishDirectory = Join-Path $projectRoot 'artifacts/publish'
        foreach ($project in @('App', 'Cli')) {
            & $dotnetPath publish "src/KeepingYouAwake.$project" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory --nologo
            if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
        }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishDirectory -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $publishDirectory -Force
        Write-Output "Published: $publishDirectory"
    }
} finally { Pop-Location }
