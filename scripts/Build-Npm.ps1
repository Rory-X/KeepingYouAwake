param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$npmPath = (Get-Command npm.cmd -ErrorAction Stop).Source
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$publishDirectory = Join-Path $projectRoot 'artifacts/publish'
foreach ($binary in @('KeepingYouAwake.exe', 'kya.exe')) {
    $binaryPath = Join-Path $publishDirectory $binary
    if (-not (Test-Path -LiteralPath $binaryPath)) { throw "Missing binary: $binary. Run Build.ps1 first." }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($binaryPath).ProductVersion.Split('+')[0] -ne $version) { throw "Binary version mismatch: $binary" }
}
$outputDirectory = Join-Path $projectRoot 'artifacts/npm'
$packageDirectory = Join-Path $outputDirectory ('staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'native') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'npm/bin'), (Join-Path $projectRoot 'npm/scripts') -Destination $packageDirectory -Recurse
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'npm/package.json') -Raw | ConvertFrom-Json
$manifest.version = $version
[IO.File]::WriteAllText((Join-Path $packageDirectory 'package.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageDirectory
foreach ($binary in @('KeepingYouAwake.exe', 'kya.exe')) { Copy-Item -LiteralPath (Join-Path $publishDirectory $binary) -Destination (Join-Path $packageDirectory 'native') }
Push-Location $packageDirectory
try {
    & $npmPath pack --pack-destination $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'npm pack failed.' }
} finally { Pop-Location }
$tarballPath = Join-Path $outputDirectory "keeping-you-awake-$version.tgz"
$hash = (Get-FileHash -LiteralPath $tarballPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($tarballPath))" | Set-Content -LiteralPath "$tarballPath.sha256" -Encoding ASCII
Write-Output "npm package: $tarballPath"
