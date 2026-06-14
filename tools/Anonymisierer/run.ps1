# Pull -> Build -> Run
$ErrorActionPreference = "Stop"
$root = git -C $PSScriptRoot rev-parse --show-toplevel

Write-Host "==> git pull..." -ForegroundColor Cyan
git -C $root pull
if (-not $?) { Write-Host "git pull fehlgeschlagen." -ForegroundColor Red; exit 1 }

Write-Host "==> dotnet build..." -ForegroundColor Cyan
dotnet build "$PSScriptRoot\Anonymisierer.csproj" -c Debug --nologo
if (-not $?) { Write-Host "Build fehlgeschlagen." -ForegroundColor Red; exit 1 }

Write-Host "==> Starte Anonymisierer..." -ForegroundColor Green
& "$PSScriptRoot\bin\Debug\net10.0-windows10.0.17763.0\Anonymisierer.exe"
