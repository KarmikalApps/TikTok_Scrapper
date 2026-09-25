param([string]$Output = (Join-Path $PSScriptRoot 'dist'), [switch]$SkipEngine)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'scripts\New-Icon.ps1')
if (-not $SkipEngine) { & (Join-Path $PSScriptRoot 'scripts\Get-Engine.ps1') }
dotnet restore (Join-Path $PSScriptRoot 'Tests\TikTokScrapper.Tests.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed' }
dotnet run --project (Join-Path $PSScriptRoot 'Tests\TikTokScrapper.Tests.csproj') --configuration Release --no-restore -- --engine (Join-Path $PSScriptRoot 'tools\yt-dlp.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
dotnet restore (Join-Path $PSScriptRoot 'Tests\Ui\TikTokScrapper.UiTests.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'UI test restore failed' }
dotnet run --project (Join-Path $PSScriptRoot 'Tests\Ui\TikTokScrapper.UiTests.csproj') --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'UI tests failed' }
$restoreConfigArgument = '-p:RestoreConfigFile=' + (Join-Path $PSScriptRoot 'NuGet.Config')
dotnet publish (Join-Path $PSScriptRoot 'TikTokScrapper.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true $restoreConfigArgument -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
[IO.Directory]::CreateDirectory((Join-Path $Output 'tools')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $Output 'scrapped')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $Output 'assets')) | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\yt-dlp.exe'), (Join-Path $PSScriptRoot 'tools\version.txt') -Destination (Join-Path $Output 'tools') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets\logo.png') -Destination (Join-Path $Output 'assets\logo.png') -Force
$readme = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'README.md')).Replace('src="Assets/logo.png"', 'src="assets/logo.png"')
[IO.File]::WriteAllText((Join-Path $Output 'README.md'), $readme, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $Output -Force
Write-Output "Portable app ready: $Output\TikTok Scrapper.exe"
