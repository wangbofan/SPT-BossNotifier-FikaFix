#Requires -Version 5.1
<#
    编译 BossNotifier（Fika 修复版）并把产物复制到 dist\。

    用法：
        .\build.ps1                       # 默认 GameDir = E:\GAME
        .\build.ps1 -GameDir "D:\EFT"     # 指定游戏根目录
        .\build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [string]$GameDir = "E:\GAME",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$proj = Join-Path $root "src\BossNotifier\BossNotifier.csproj"
$dist = Join-Path $root "dist"

if (-not (Test-Path $proj)) { throw "找不到工程文件: $proj" }
if (-not (Test-Path (Join-Path $GameDir "EscapeFromTarkov_Data\Managed"))) {
    throw "GameDir 下没有 EscapeFromTarkov_Data\Managed，检查 -GameDir: $GameDir"
}

Write-Host "GameDir        : $GameDir"
Write-Host "Configuration  : $Configuration"

& dotnet build $proj -c $Configuration -p:GameDir=$GameDir --nologo
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

$built = Join-Path $root "src\BossNotifier\bin\$Configuration\BossNotifier.dll"
if (-not (Test-Path $built)) { throw "没找到产物: $built" }

New-Item -ItemType Directory -Path $dist -Force | Out-Null
Copy-Item $built (Join-Path $dist "BossNotifier.dll") -Force

$item = Get-Item (Join-Path $dist "BossNotifier.dll")
Write-Host ""
Write-Host ("OK -> {0} ({1} bytes)" -f $item.FullName, $item.Length)
