<#
.SYNOPSIS
    Builds and publishes production-ready self-contained deployment packages for ZixCafe Pro.
#>

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\dist"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  ZIXCAFE PRO — PACKAGING & BUILD SCRIPT  " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

$ServerPublish = "$OutputDir\ZixCafe-Server"
$ClientPublish = "$OutputDir\ZixCafe-Client"

Write-Host "`n[1/3] Publishing ZixCafe Server App..." -ForegroundColor Yellow
dotnet publish "$PSScriptRoot\..\src\ZixCafe.Server.App\ZixCafe.Server.App.csproj" `
    -c $Configuration -r win-x64 --self-contained false -o $ServerPublish

Write-Host "`n[2/3] Publishing ZixCafe Client Agent..." -ForegroundColor Yellow
dotnet publish "$PSScriptRoot\..\src\ZixCafe.Client.Agent\ZixCafe.Client.Agent.csproj" `
    -c $Configuration -r win-x64 --self-contained false -o $ClientPublish

Write-Host "`n[3/3] Publishing ZixCafe Watchdog Service..." -ForegroundColor Yellow
dotnet publish "$PSScriptRoot\..\src\ZixCafe.Client.Service\ZixCafe.Client.Service.csproj" `
    -c $Configuration -r win-x64 --self-contained false -o "$ClientPublish\Service"

Write-Host "`nCopying operations and deployment documentation..." -ForegroundColor Yellow
Copy-Item "$PSScriptRoot\..\OPERATIONS.md" "$OutputDir\"
Copy-Item "$PSScriptRoot\..\docs\kiosk-policy.md" "$OutputDir\"

Write-Host "`n[SUCCESS] Build and packaging complete!" -ForegroundColor Green
Write-Host "Output packages located at: $OutputDir" -ForegroundColor Green
