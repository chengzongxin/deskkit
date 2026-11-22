# DeskKit Windows 便携包
# 用法:
#   .\scripts\publish-win.ps1
#   .\scripts\publish-win.ps1 -SyncLocal          # 同步到 %LocalAppData%\DeskKit\app
#   .\scripts\publish-win.ps1 -Zip                  # 额外打 zip 到 release/dist
#   .\scripts\publish-win.ps1 -SyncLocal -Zip -KillRunning
param(
    [switch]$SyncLocal,
    [switch]$Zip,
    [switch]$KillRunning
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Src = Join-Path $Root "apps\windows"
$Build = Join-Path $Src "build"
$VersionFile = Join-Path $Root "release\version.json"

function Find-Gcc {
    $cmd = Get-Command gcc -ErrorAction SilentlyContinue
    if ($cmd) { return Split-Path $cmd.Source }
    $roots = @(
        (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"),
        "C:\mingw64\bin"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem $root -Filter gcc.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "mingw64\\bin\\gcc.exe$" } |
            Select-Object -First 1
        if ($hit) { return $hit.DirectoryName }
    }
    return $null
}

if ($KillRunning) {
    $procs = Get-Process -Name "deskkit" -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "正在结束 $($procs.Count) 个 deskkit 进程..."
        $procs | Stop-Process -Force
        Start-Sleep -Seconds 1
    }
} else {
    $locked = Get-Process -Name "deskkit" -ErrorAction SilentlyContinue
    if ($locked) {
        Write-Warning "检测到 deskkit 仍在运行，链接可能因文件被锁失败。可加 -KillRunning 先退出进程。"
    }
}

$gccBin = Find-Gcc
if (-not $gccBin) {
    Write-Error "找不到 gcc。请安装 WinLibs（MinGW），或把 mingw64\bin 加入 PATH。"
}
$env:PATH = "$gccBin;$env:PATH"

Write-Host "CMake 配置 $Src ..."
cmake -S $Src -B $Build -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
if ($LASTEXITCODE -ne 0) { Write-Error "CMake 配置失败" }

Write-Host "编译 deskkit ..."
cmake --build $Build --target deskkit
if ($LASTEXITCODE -ne 0) { Write-Error "编译失败" }

$exe = Join-Path $Build "deskkit.exe"
if (-not (Test-Path $exe)) {
    Write-Error "未找到输出: $exe"
}
Write-Host "完成: $exe"

$stage = Join-Path $Build "portable"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $exe $stage
Copy-Item (Join-Path $Build "assets") (Join-Path $stage "assets") -Recurse
# 解压后的目录里要有 contracts，否则程序找不到契约，直接退出。
Copy-Item (Join-Path $Root "contracts") (Join-Path $stage "contracts") -Recurse

if ($SyncLocal) {
    $dest = Join-Path $env:LOCALAPPDATA "DeskKit\app"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Write-Host "同步到 $dest ..."
    robocopy $stage $dest /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        Write-Error "robocopy 失败 (exit $LASTEXITCODE)"
    }
    $global:LASTEXITCODE = 0
    Write-Host "已更新: $(Join-Path $dest 'deskkit.exe')"
}

if ($Zip) {
    $dist = Join-Path $Root "release\dist"
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $version = "0.9.3-win"
    if (Test-Path $VersionFile) {
        try {
            $meta = Get-Content $VersionFile -Raw | ConvertFrom-Json
            if ($meta.version) { $version = [string]$meta.version }
        } catch {
            Write-Warning "无法解析 version.json，使用默认版本号 $version"
        }
    }
    $zipName = "DeskKit-$version-x64-portable.zip"
    $zipPath = Join-Path $dist $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Write-Host "打包 $zipPath ..."
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "zip 大小: $([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB"
}
