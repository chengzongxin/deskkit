# DeskKit Windows 自包含发布 (win-x64)
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
$Proj = Join-Path $Root "apps\windows\src\DeskKit.App\DeskKit.App.csproj"
$Publish = Join-Path $Root "apps\windows\src\DeskKit.App\bin\Release\net8.0-windows\win-x64\publish"
$VersionFile = Join-Path $Root "release\version.json"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    $candidate = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (-not (Test-Path $candidate)) {
        Write-Error "找不到 dotnet。请安装 .NET 8 SDK，或将 dotnet 加入 PATH。"
    }
    $dotnet = $candidate
} else {
    $dotnet = $dotnet.Source
}

if ($KillRunning) {
    $procs = Get-Process -Name "DeskKit.App" -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "正在结束 $($procs.Count) 个 DeskKit.App 进程..."
        $procs | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
} else {
    $locked = Get-Process -Name "DeskKit.App" -ErrorAction SilentlyContinue
    if ($locked) {
        Write-Warning "检测到 DeskKit 仍在运行，publish 可能因文件被锁失败。可加 -KillRunning 先退出进程。"
    }
}

Write-Host "dotnet publish (Release, win-x64, self-contained)..."
& $dotnet publish $Proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=false `
    /p:DebugType=None `
    /p:DebugSymbols=false

if (-not (Test-Path (Join-Path $Publish "DeskKit.App.exe"))) {
    Write-Error "未找到输出: $Publish\DeskKit.App.exe"
}

Write-Host "完成: $Publish"

if ($SyncLocal) {
    $dest = Join-Path $env:LOCALAPPDATA "DeskKit\app"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Write-Host "同步到 $dest ..."
    robocopy $Publish $dest /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        Write-Error "robocopy 失败 (exit $LASTEXITCODE)"
    }
    Write-Host "已更新: $(Join-Path $dest 'DeskKit.App.exe')"
}

if ($Zip) {
    $dist = Join-Path $Root "release\dist"
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $version = "0.9.0"
    if (Test-Path $VersionFile) {
        try {
            $meta = Get-Content $VersionFile -Raw | ConvertFrom-Json
            if ($meta.version) { $version = $meta.version }
        } catch {
            Write-Warning "无法解析 version.json，使用默认版本号 $version"
        }
    }
    $zip = Join-Path $dist "DeskKit-$version-win-x64-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Write-Host "打包 $zip ..."
    Compress-Archive -Path (Join-Path $Publish "*") -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "zip 大小: $([math]::Round((Get-Item $zip).Length / 1MB, 2)) MB"
}