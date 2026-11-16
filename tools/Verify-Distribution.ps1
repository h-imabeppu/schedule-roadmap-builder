$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$notices = Get-Content 'THIRD_PARTY_NOTICES.md' -Raw -Encoding UTF8
$packages = @{}
foreach ($path in @('src\ScheduleRoadmapBuilder.Core\obj\project.assets.json', 'src\ScheduleRoadmapBuilder.App\obj\project.assets.json', 'tests\ScheduleRoadmapBuilder.Tests\obj\project.assets.json')) {
    $assets = Get-Content $path -Raw | ConvertFrom-Json
    foreach ($p in $assets.libraries.PSObject.Properties) {
        if ($p.Value.type -eq 'package') { $packages[$p.Name] = $true }
    }
}
foreach ($package in $packages.Keys) {
    $name, $version = $package.Split('/')
    if ($notices -notmatch ([regex]::Escape("[$name](https://www.nuget.org/packages/$name/$version) | $version |"))) {
        throw "Missing inventory entry: $package"
    }
    $folder = Join-Path 'licenses' "$name-$version"
    $texts = @(Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '(?i)licen[sc]e' -and $_.Extension -ne '.nuspec' })
    if ($texts.Count -eq 0) { throw "Missing license original: $package" }
    if (-not (Test-Path (Join-Path $folder 'source.txt'))) { throw "Missing license source: $package" }
}
$published = (Resolve-Path 'publish').Path
$files = @(Get-Item 'README.md', 'THIRD_PARTY_NOTICES.md', 'samples\sample.srb.json')
$files += @(Get-ChildItem 'licenses' -Recurse -File)
$root = (Get-Location).Path
foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length + 1)
    $copy = Join-Path $published $relative
    if (-not (Test-Path -LiteralPath $copy)) { throw "Missing distribution file: $relative" }
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) {
        throw "Distribution file mismatch: $relative"
    }
}
$exe = Join-Path $published 'ScheduleRoadmapBuilder.App.exe'
if (-not (Test-Path -LiteralPath $exe) -or (Get-Item -LiteralPath $exe).Length -eq 0) { throw 'Missing executable' }
$dlls = @(Get-ChildItem -LiteralPath $published -File -Filter *.dll)
if ($dlls.Count -ne 0) { throw 'Single-file distribution contains unexpected loose DLLs' }
Write-Output "Verified $($packages.Count) packages, $($files.Count) distribution files, and single-file exe."
