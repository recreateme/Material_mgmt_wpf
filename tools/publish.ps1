# 原料管理系统 WPF — 完整打包脚本
# 产物目录：dist\原料管理系统\
# 压缩包：  dist\原料管理系统-v4.1.0-win-x64.zip

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "src\MaterialMgmt.sln"))) {
    throw "未找到解决方案，请在仓库根目录执行（含 src\MaterialMgmt.sln）"
}

$Version = "4.1.0"
$Proj    = Join-Path $Root "src\MaterialMgmt.App\MaterialMgmt.App.csproj"
$Out     = Join-Path $Root "dist\原料管理系统"
$Zip     = Join-Path $Root "dist\原料管理系统-v$Version-win-x64.zip"
$DistDir = Join-Path $Root "dist"

Write-Host "==== 原料管理系统 v$Version 完整打包 ====" -ForegroundColor Cyan
Write-Host "输出目录: $Out"

# 0) 打包前备份已有运行库（优先保留 dist 内正在使用的库）
$BackupDir = Join-Path $DistDir "_db_backup_temp"
$PreserveDbNames = @("material_mgmt.db", "material_mgmt.db-wal", "material_mgmt.db-shm")
$preservedFrom = $null
if (Test-Path $BackupDir) { Remove-Item -Recurse -Force $BackupDir }
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$distData = Join-Path $Out "data"
$rootData = Join-Path $Root "data"
foreach ($candidate in @($distData, $rootData)) {
    $candidateDb = Join-Path $candidate "material_mgmt.db"
    if (Test-Path -LiteralPath $candidateDb) {
        foreach ($name in $PreserveDbNames) {
            $src = Join-Path $candidate $name
            if (Test-Path -LiteralPath $src) {
                Copy-Item -LiteralPath $src -Destination (Join-Path $BackupDir $name) -Force
            }
        }
        $preservedFrom = $candidate
        Write-Host "已备份现有数据库: $candidateDb"
        break
    }
}

# 同步备份运行配置（优先 dist，其次仓库根）
$preservedIni = $null
foreach ($candidateIni in @(
    (Join-Path $Out "config\settings.ini"),
    (Join-Path $Root "config\settings.ini")
)) {
    if (Test-Path -LiteralPath $candidateIni) {
        Copy-Item -LiteralPath $candidateIni -Destination (Join-Path $BackupDir "settings.ini") -Force
        $preservedIni = $candidateIni
        Write-Host "已备份现有配置: $candidateIni"
        break
    }
}

# 1) 清理并发布自包含
if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

Write-Host "`n[1/5] dotnet publish (win-x64 self-contained) ..."
dotnet publish $Proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o $Out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 2) 运行时目录
Write-Host "[2/5] 创建 data / config / logs ..."
foreach ($d in @("data", "config", "logs", "docs")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Out $d) | Out-Null
}

# 3) 配置与数据库（写回备份，不覆盖用户数据）
Write-Host "[3/5] 写入配置与数据库 ..."
$ini = Join-Path $Out "config\settings.ini"
$bakIni = Join-Path $BackupDir "settings.ini"
$exampleIni = Join-Path $Root "config\settings.example.ini"
if (Test-Path -LiteralPath $bakIni) {
    Copy-Item -LiteralPath $bakIni -Destination $ini -Force
    Write-Host "  已恢复现有 config\settings.ini（来源: $preservedIni）"
} elseif (Test-Path -LiteralPath $exampleIni) {
    Copy-Item -LiteralPath $exampleIni -Destination $ini -Force
    Write-Host "  已从 settings.example.ini 生成默认 config\settings.ini"
} else {
@"
[serial]
port = 
baud_rate = 9600
auto_connect = true

[scanner]
port = 
baud_rate = 9600
mode = Auto
enabled = true

[app]
window_width = 1280
window_height = 800
window_maximized = false
"@ | Set-Content -Path $ini -Encoding UTF8
}

$dstData = Join-Path $Out "data"
$restored = $false
foreach ($name in $PreserveDbNames) {
    $bak = Join-Path $BackupDir $name
    if (Test-Path -LiteralPath $bak) {
        Copy-Item -LiteralPath $bak -Destination (Join-Path $dstData $name) -Force
        $restored = $true
    }
}
if ($restored) {
    Write-Host "  已恢复现有数据库（来源: $preservedFrom）"
} else {
    Write-Host "  未找到可保留的数据库，首次运行将自动建库并写入种子账号"
}
Remove-Item -Recurse -Force $BackupDir -ErrorAction SilentlyContinue

# 4) 文档
Write-Host "[4/5] 复制文档 ..."
$docs = @(
    @{ Src = "DEPLOY.md"; Dst = "docs\DEPLOY.md" },
    @{ Src = "README.md"; Dst = "docs\README.md" },
    @{ Src = "docs\quickstart.md"; Dst = "docs\quickstart.md" },
    @{ Src = "docs\wpf-tutorial.md"; Dst = "docs\wpf-tutorial.md" },
    @{ Src = "docs\hardware-checklist.md"; Dst = "docs\hardware-checklist.md" },
    @{ Src = "docs\feature-checklist.md"; Dst = "docs\feature-checklist.md" },
    @{ Src = "docs\PROJECT.md"; Dst = "docs\PROJECT.md" },
    @{ Src = "docs\TECH.md"; Dst = "docs\TECH.md" },
    @{ Src = "config\settings.example.ini"; Dst = "docs\settings.example.ini" }
)
foreach ($d in $docs) {
    $from = Join-Path $Root $d.Src
    $to   = Join-Path $Out $d.Dst
    if (Test-Path -LiteralPath $from) {
        $toDir = Split-Path $to -Parent
        New-Item -ItemType Directory -Force -Path $toDir | Out-Null
        Copy-Item -LiteralPath $from -Destination $to -Force
    }
}

$readme = @"
原料管理系统 v$Version（WPF 桌面版）
================================

【运行】
  双击「原料管理系统.exe」即可（无需安装 .NET / Python）。
  请保持本文件夹完整，不要只拷贝单个 exe。

【目录】
  原料管理系统.exe     主程序
  data\                业务数据库（material_mgmt.db）
  config\settings.ini  串口与窗口配置
  logs\                运行日志 / 审计日志
  docs\                部署与联调说明

【默认账号】（仅空库首次启动写入）
  root / root666       开发人员
  admin / admin123     管理人员
  operator / operator123  实验人员

【硬件】
  电子天平：系统设置中选择 COM 口 → 保存并连接
  扫码枪：推荐 USB COM（9600, CR）；与电子秤使用不同 COM
  详见 docs\TECH.md 与 docs\hardware-checklist.md

【迁移旧数据】
  退出程序后，将旧版 data\material_mgmt.db 覆盖到本目录 data\ 下。
  详见 docs\DEPLOY.md

打包时间：$(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
"@
$readme | Set-Content -Path (Join-Path $Out "使用说明.txt") -Encoding UTF8

# 5) 压缩包
Write-Host "[5/5] 生成 zip ..."
if (Test-Path $Zip) { Remove-Item -Force $Zip }
Compress-Archive -Path $Out -DestinationPath $Zip -CompressionLevel Optimal

$exe = Join-Path $Out "原料管理系统.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "缺少主程序 exe" }

$exeInfo = Get-Item -LiteralPath $exe
$zipInfo = Get-Item -LiteralPath $Zip
$fileCount = (Get-ChildItem -LiteralPath $Out -Recurse -File).Count

Write-Host ""
Write-Host "==== 打包完成 ====" -ForegroundColor Green
Write-Host "文件夹 : $Out  ($fileCount 个文件)"
Write-Host "主程序 : $($exeInfo.FullName)  ($([math]::Round($exeInfo.Length/1MB, 2)) MB)"
Write-Host "压缩包 : $($zipInfo.FullName)  ($([math]::Round($zipInfo.Length/1MB, 2)) MB)"
Write-Host ""
Write-Host "分发时请拷贝整个「原料管理系统」文件夹，或直接分发 zip。"
