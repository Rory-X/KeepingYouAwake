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
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $publishDirectory -Force
        $version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
        $releaseDirectory = Join-Path $projectRoot 'artifacts/release'
        New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
        $archivePath = Join-Path $releaseDirectory "KeepingYouAwake-$version-win-x64.zip"
        $releaseFiles = @('KeepingYouAwake.exe', 'kya.exe', 'README.md', 'Install.ps1', 'Uninstall.ps1') | ForEach-Object { Join-Path $publishDirectory $_ }
        Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archivePath -Force
        $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([System.IO.Path]::GetFileName($archivePath))" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding ASCII
        Write-Output "Published: $publishDirectory"
        Write-Output "Release: $archivePath"
    }
} finally { Pop-Location }
