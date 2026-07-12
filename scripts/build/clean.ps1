#!/usr/bin/env pwsh
<#
.SYNOPSIS
  清理解决方案下所有 bin/ 和 obj/ — 请在 scripts/build/ 目录下执行
.EXAMPLE
  cd scripts\build
  .\clean.ps1              # 列出 + 确认
  .\clean.ps1 -Force       # 直接删除
  .\clean.ps1 -DryRun      # 仅预览
#>

param([switch]$Force, [switch]$DryRun)
$ErrorActionPreference = "Stop"
Set-Location ../..

Write-Host "=== SeatFlow 清理 bin/ & obj/ ===" -ForegroundColor Cyan

$dirs = @(Get-ChildItem -Directory -Recurse -Filter bin) + @(Get-ChildItem -Directory -Recurse -Filter obj)
$dirs = @($dirs | Sort-Object FullName -Unique)

if ($dirs.Count -eq 0) { Write-Host "没有可清理的目录" -ForegroundColor Green; exit 0 }

$total = 0
foreach ($d in $dirs) {
    $size = (Get-ChildItem $d.FullName -Recurse -File -ea 0 | Measure-Object Length -Sum).Sum
    $total += $size
    Write-Host "  .$($d.FullName.Replace($PWD, ''))  $([math]::Round($size/1MB, 1)) MB" -ForegroundColor Gray
}

$totalMB = [math]::Round($total / 1MB, 1)
Write-Host "`n共 $($dirs.Count) 个目录，$totalMB MB" -ForegroundColor Yellow

if ($DryRun) { Write-Host "DryRun — 未删除" -ForegroundColor Cyan; exit 0 }
if (-not $Force -and (Read-Host "确认删除? (y/N)") -notmatch '^[yY]') { Write-Host "已取消" -ForegroundColor Gray; exit 0 }

foreach ($d in $dirs) { Remove-Item $d.FullName -Recurse -Force -ea 0 }
Write-Host "已清理 $totalMB MB" -ForegroundColor Green
