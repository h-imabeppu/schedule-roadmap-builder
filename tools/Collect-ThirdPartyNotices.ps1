$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$assets = Get-Content 'tests\ScheduleRoadmapBuilder.Tests\obj\project.assets.json' -Raw | ConvertFrom-Json
$cache = ($assets.packageFolders.PSObject.Properties | Select-Object -First 1).Name
$runtimeAssets = Get-Content 'src\ScheduleRoadmapBuilder.App\obj\project.assets.json' -Raw | ConvertFrom-Json
$runtime = @($runtimeAssets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Third-party notices')
$lines.Add('')
$lines.Add('確認日: 2026-11-16。公式NuGetから復元した実バージョンのnuspec・同梱原文、またはnuspecが指定する公式リポジトリのコミットから確認。')
$lines.Add('取得元は各行のNuGet配布元。原文とnuspecは licenses/ 以下に保存し、原文の取得元は各フォルダーの source.txt に記録する。')
$lines.Add('配布対象はMITの3パッケージ。著作権表示・ライセンス原文をexeと同じ配布フォルダーに同梱する。開発専用も追跡のため同梱するが、そのバイナリは配布しない。')
$lines.Add('Meiryo UI、.NET/Windows Desktop Runtimeは同梱しない。実行環境に既存のものを使用する。フォント、画像、アイコン、外部コードの追加コピーはない。')
$lines.Add('ライセンスの要確認事項がある場合、個人での検証と配布可否の判断を区別し、部門への配布前に別途確認する。')
$lines.Add('')
$lines.Add('| パッケージ | バージョン | ライセンス | 用途 / 配布バイナリ | 確認方法・必要な対応 | 原文 |')
$lines.Add('|---|---|---|---|---|---|')
foreach ($p in ($assets.libraries.PSObject.Properties | Sort-Object Name)) {
    if ($p.Value.type -ne 'package') { continue }
    $name, $version = $p.Name.Split('/')
    $folder = Join-Path $cache $p.Value.path
    $nuspec = Get-ChildItem -LiteralPath $folder -Filter *.nuspec | Select-Object -First 1
    [xml]$spec = Get-Content $nuspec.FullName
    $metadata = $spec.package.metadata
    $license = [string]$metadata.license.InnerText
    $destination = Join-Path 'licenses' "$name-$version"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath $nuspec.FullName -Destination (Join-Path $destination $nuspec.Name) -Force
    $sources = [System.Collections.Generic.List[string]]::new()
    $sources.Add("https://api.nuget.org/v3-flatcontainer/$($name.ToLowerInvariant())/$version/$($name.ToLowerInvariant()).$version.nupkg")
    $found = $false
    foreach ($file in (Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object { $_.Name -match '(?i)(licen[sc]e|notice|copying)' -and $_.Extension -ne '.dll' })) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name) -Force
        $sources.Add("Package: $($file.FullName.Substring($folder.Length))")
        if ($file.Name -match '(?i)licen[sc]e') { $found = $true }
    }
    if (-not $found) {
        $urls = @()
        if ($metadata.repository.url -and $metadata.repository.commit) {
            $repo = ([string]$metadata.repository.url).Replace('https://github.com/', '').TrimEnd('/')
            foreach ($candidate in @('LICENSE', 'LICENSE.txt', 'license.txt', 'LICENSE.md', 'License.txt')) {
                $urls += "https://raw.githubusercontent.com/$repo/$($metadata.repository.commit)/$candidate"
            }
        } elseif ($metadata.licenseUrl) {
            $urls += [string]$metadata.licenseUrl
        }
        foreach ($url in $urls) {
            try {
                $response = Invoke-WebRequest -UseBasicParsing -Uri $url
                $body = [string]$response.Content
                if ($body -notmatch '(?i)(permission is hereby granted|apache license|microsoft.*license)') {
                    Write-Warning "Not recognized as license text: $url"
                    continue
                }
                [IO.File]::WriteAllText((Join-Path (Join-Path (Get-Location) $destination) 'LICENSE.txt'), $body, [Text.UTF8Encoding]::new($false))
                $sources.Add($url)
                $found = $true
                if (-not $license -and $body -match '(?i)apache license' -and $body -match '2\.0') { $license = 'Apache-2.0' }
                break
            } catch [System.Net.WebException] {
                if ([int]$_.Exception.Response.StatusCode -eq 404) { Write-Output "Not found: $url"; continue }
                Write-Warning "$url : $($_.Exception.Message)"
            }
        }
    }
    $status = 'nuspec license + 同梱原文/公式コミット原文'
    if (-not $metadata.license.InnerText) { $status = 'nuspec licenseUrlの原文' }
    if (-not $found -or -not $license) { $license = '要確認'; $status += '（原文またはライセンス未確定。配布前に確認）' }
    $binary = 'テスト・開発専用 / 含まない'
    if ($runtime -contains $p.Name) { $binary = 'PPT実行 / 含む'; $status += '。原文・著作権表示を同梱' }
    $sources | Set-Content (Join-Path $destination 'source.txt') -Encoding UTF8
    $link = "https://www.nuget.org/packages/$name/$version"
    $lines.Add("| [$name]($link) | $version | $license | $binary | $status / 2026-11-16 | [原文](licenses/$name-$version/) |")
    Write-Output "$($p.Name): $license / license text=$found"
}
$lines | Set-Content 'THIRD_PARTY_NOTICES.md' -Encoding UTF8
