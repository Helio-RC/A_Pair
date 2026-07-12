#!/usr/bin/env pwsh
# SeatFlow 多平台发布 — TUI 交互 / CLI 兼容
param(
    [string]$Mode,
    [ValidateSet("Release","Debug")][string]$Configuration="Release",
    [string]$Suffix="",
    [string]$Version="",
    [switch]$Optimize, [switch]$Aot, [switch]$HashOnly, [switch]$Clean
)
$ErrorActionPreference="Stop"
Push-Location ..
$AppName,$Project="SeatFlow","SeatFlow.Presentation.Avalonia"
$P=@(@{N="win-x64";S=".exe";Sel=$false},@{N="linux-x64";S="";Sel=$false},@{N="osx-x64";S="";Sel=$false},@{N="osx-arm64";S="";Sel=$false})

function Step($t,$c="White"){Write-Host "  [$([datetime]::Now.ToString('HH:mm:ss'))] $t" -ForegroundColor $c}
function ShaTable{
    $f=@(gci publish -r -File|?{$_.Name -like "$AppName-*"}|sort Name)
    if(!$f){Write-Host "没有找到已发布文件" -F Yellow;return}
    Write-Host "`n| 文件 | SHA256 |"
    Write-Host "|------|--------|"
    foreach($x in $f){$h=(Get-FileHash -A SHA256 $x.FullName).Hash.ToLower();Write-Host "| ``$($x.Name)`` | $h |" -F Gray}
}
function Publish-One($SC,$Label,$Rids){
    $base="publish/$Label";mkdir -Force $base|Out-Null
    foreach($rid in $Rids){
        $sf=if($rid-like"win*"){".exe"}else{""};$tmp="$base/.tmp_$rid";$fn=if($Version){"$AppName-$Version-$Label-$rid"}else{"$AppName-$Label-$rid"};if($Suffix){$fn="$fn-$Suffix"};$fn="$fn$sf"
        $Host.UI.RawUI.WindowTitle="SeatFlow: $Label / $rid"
        Write-Host "`n══════════════════════════════════════════" -F Cyan
        Write-Host "  $Label / $rid" -F Cyan
        Write-Host "══════════════════════════════════════════" -F Cyan
        Step "开始编译..." Yellow
        $ta=if($Optimize-and$SC){@("-p:PublishTrimmed=true","-p:TrimMode=partial","-p:SuppressTrimAnalysisWarnings=true")}else{@()}
        if($Aot-and$SC){$ta+=@("-p:PublishAot=true")}
        dotnet publish $Project -c $Configuration -r $rid --self-contained $(if($SC){"true"}else{"false"}) -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true @ta -o $tmp
        if($LASTEXITCODE){Step "编译失败" Red;Pop-Location;exit 1}
        $exe="$AppName$sf"
        if(Test-Path "$tmp/$exe"){mv "$tmp/$exe" "$base/$fn" -Force;rm $tmp -r -Force -ea 0;$s=[math]::Round((gi "$base/$fn").Length/1MB,1);Step "完成 → $Label/$fn ($s MB)" Green}
        else{$fb=gci $tmp -File|?{$_.Name -like "$AppName*"}|select -First 1;if($fb){mv $fb.FullName "$base/$fn" -Force;rm $tmp -r -Force -ea 0;$s=[math]::Round((gi "$base/$fn").Length/1MB,1);Step "完成 → $Label/$fn ($s MB)" Green}else{Step "未找到可执行文件: $tmp" Red}}
    }
}

# CLI 模式
if($Mode-or$HashOnly){
    if($HashOnly){ShaTable;Pop-Location;exit 0}
    if($Clean-and(Test-Path publish)){gci publish -r|%{Write-Host "  $_" -F Gray};$c=Read-Host "确认删除以上文件? (y/N)";if($c-eq'y'){rm publish -r -Force;Write-Host "已清空" -F Yellow}else{Write-Host "已取消" -F Gray}}
    $m=if($Mode-eq"full"){"full"}elseif($Mode-eq"slim"){"slim"}else{"both"}
    $sw=[Diagnostics.Stopwatch]::StartNew()
    if($m-ne"slim"){Write-Host "`n--- 自包含 ---" -F Magenta;Publish-One $true "full" @("win-x64","linux-x64","osx-x64","osx-arm64")}
    if($m-ne"full"){Write-Host "`n--- 依赖运行时 ---" -F Magenta;Publish-One $false "slim" @("win-x64","linux-x64","osx-x64","osx-arm64")}
    $sw.Stop();Write-Host "`n完成 $([math]::Round($sw.Elapsed.TotalSeconds,1))s" -F Cyan;ShaTable;Pop-Location;exit 0
}

# TUI 模式
[Console]::Clear()
Write-Host "  SeatFlow 发布"
$types=@("自包含","依赖运行时","两者");$ti=2;$ts=$false;$aot=$false;$cl=$false;$cu=0;$suf=$Suffix;$ver=$Version;$ii=16

function Draw{
    [Console]::SetCursorPosition(0,1)
    $o=@()
    $o+="  SeatFlow 发布"
    $o+=""
    $o+="  平台（空格切换）："
    for($i=0;$i-lt4;$i++){
        $mk=if($P[$i].Sel){"[*]"}else{"[ ]"}
        $hi=if($cu-eq$i){">"}else{" "}
        $o+="     $hi$mk $($P[$i].N)"
    }
    $sa=if($cu-eq4){">"}else{" "}
    $o+="     $sa[A] 全选"
    $sn=if($cu-eq5){">"}else{" "}
    $o+="     $sn[N] 全不选"
    $o+=""
    $o+="  发布类型（Enter 选择）："
    for($i=0;$i-lt3;$i++){
        $mk=if($i-eq$ti){"*"}else{" "}
        $hi=if($cu-eq(6+$i)){">"}else{" "}
        $o+="     $hi$mk $($types[$i])"
    }
    $o+=""
    $tm=if($ts){"[*]"}else{"[ ]"}; $tc=if($cu-eq9){">"}else{" "}
    $o+="  优化选项："
    $o+="     $tc$tm 裁剪 (TrimMode=partial)"
    $am=if($aot){"[*]"}else{"[ ]"}; $ac=if($cu-eq10){">"}else{" "}
    $o+="     $ac$am AOT 编译 (需自包含)"
    $sx=if($cu-eq11){">"}else{" "}
    $sd=if($suf){"[$suf]"}else{"[未设置]"}
    $o+="     $sx$sd 文件名后缀（Enter 设置）"
    $vx=if($cu-eq12){">"}else{" "}
    $vd=if($ver){"[$ver]"}else{"[未设置]"}
    $o+="     $vx$vd 版本号（Enter 设置）"
    $o+=""
    $o+="  操作："
    $cb=if($cl){"[*]"}else{"[ ]"}; $ci=if($cu-eq13){">"}else{" "}
    $o+="     $ci$cb 编译前清空 publish/ 目录"
    $b1=if($cu-eq14){">"}else{" "}
    $o+="     $b1[ 开始编译 ]"
    $b2=if($cu-eq15){">"}else{" "}
    $o+="     $b2[ 仅计算哈希 ]"
    $o+=""
    foreach($l in $o){ [Console]::WriteLine($l) }
    [Console]::WriteLine("  ↑↓移动  Space切换  Enter确认  Esc退出")
}

[Console]::CursorVisible=$false;Draw
$run=$false
while(-not $run){
    $k=[Console]::ReadKey($true)
    switch($k.Key){
        UpArrow{$cu=($cu-1+$ii)%$ii;Draw}
        DownArrow{$cu=($cu+1)%$ii;Draw}
        Spacebar{if($cu-lt4){$P[$cu].Sel=!$P[$cu].Sel}elseif($cu-eq4){0..3|%{$P[$_].Sel=$true}}elseif($cu-eq5){0..3|%{$P[$_].Sel=$false}}elseif($cu-ge6-and$cu-le8){$ti=$cu-6}elseif($cu-eq9){$ts=!$ts}elseif($cu-eq10){$aot=!$aot}elseif($cu-eq13){$cl=!$cl};Draw}
        A{if($cu-eq4){0..3|%{$P[$_].Sel=$true};Draw}}
        N{if($cu-eq5){0..3|%{$P[$_].Sel=$false};Draw}}
        Escape{[Console]::CursorVisible=$true;Pop-Location;exit 0}
        Enter{
            if($cu-eq11){[Console]::CursorVisible=$true;Write-Host "";Write-Host "文件名后缀: " -NoNewline;$Suffix=[Console]::ReadLine().Trim();$suf=$Suffix;[Console]::CursorVisible=$false;Draw}
            elseif($cu-eq12){[Console]::CursorVisible=$true;Write-Host "";Write-Host "版本号 (如 1.1.0): " -NoNewline;$Version=[Console]::ReadLine().Trim();$ver=$Version;[Console]::CursorVisible=$false;Draw}
            elseif($cu-eq14){$run=$true}
            elseif($cu-eq15){[Console]::CursorVisible=$true;ShaTable;Pop-Location;exit 0}
            elseif($cu-ge6-and$cu-le8){$ti=$cu-6;Draw}
        }
    }
}
[Console]::CursorVisible=$true
if($cl-and(Test-Path publish)){gci publish -r|%{Write-Host "  $_" -F Gray};$c=Read-Host "确认删除以上文件? (y/N)";if($c-eq'y'){rm publish -r -Force;Write-Host "已清空" -F Yellow}else{Write-Host "已取消" -F Gray}}
$sp=@($P|? Sel)
if(!$sp){Write-Host "未选择任何平台" -F Red;Pop-Location;exit 1}
$doSc=($ti-eq0-or$ti-eq2);$doFd=($ti-eq1-or$ti-eq2)
$sw=[Diagnostics.Stopwatch]::StartNew()
if($doSc){Write-Host "`n--- 自包含 (Self-Contained) ---" -F Magenta;Publish-One $true "full" @($sp|% N)}
if($doFd){Write-Host "`n--- 依赖运行时 (Framework-Dependent) ---" -F Magenta;Publish-One $false "slim" @($sp|% N)}
$sw.Stop();Write-Host "`n完成 $([math]::Round($sw.Elapsed.TotalSeconds,1))s" -F Cyan;ShaTable;Pop-Location
