param([string]$Destination = (Join-Path $PSScriptRoot '..\tools'))
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest' -Headers @{'User-Agent'='TikTokScrapper-build/1.0'}
$tag = $release.tag_name
if ($tag -notmatch '^\d{4}\.\d{2}\.\d{2}(?:\.\d+)?$') { throw 'Unexpected engine release identifier' }
$root = "https://github.com/yt-dlp/yt-dlp/releases/download/$tag/"
$checksums = (Invoke-WebRequest -UseBasicParsing -Uri ($root + 'SHA2-256SUMS')).Content
if ($checksums -is [byte[]]) { $checksums = [Text.Encoding]::UTF8.GetString($checksums) }
$match = [regex]::Match($checksums, '(?m)^([a-fA-F0-9]{64})\s+\*?yt-dlp\.exe\s*$')
if (-not $match.Success) { throw 'Official checksum not found' }
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$temporary = Join-Path $Destination 'yt-dlp.exe.download'
Invoke-WebRequest -UseBasicParsing -Uri ($root + 'yt-dlp.exe') -OutFile $temporary
$actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
if ($actual -ne $match.Groups[1].Value) { throw 'Engine SHA-256 mismatch' }
Move-Item -LiteralPath $temporary -Destination (Join-Path $Destination 'yt-dlp.exe') -Force
[IO.File]::WriteAllText((Join-Path $Destination 'version.txt'), "$tag`r`nSHA256: $actual`r`nSource: $root`r`n")
Write-Output "Verified yt-dlp $tag ($actual)"
