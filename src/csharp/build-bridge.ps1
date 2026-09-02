# build-bridge.ps1 - 构建 C# 宿主并部署到 scripts\bridge\
# 用法：powershell -ExecutionPolicy Bypass -File csharp\build-bridge.ps1
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot                             # E:\translator\csharp
$proj = Join-Path $root "TranslatorHost\TranslatorHost.csproj"
$out  = Join-Path $root "TranslatorHost\bin\Release\net48\win-x64"
$dest = Join-Path (Split-Path -Parent $root) "scripts\bridge"

dotnet build $proj -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败" }

# 部署前检测宿主是否在运行（在跑则文件被锁，部署会半途失败留下混版本产物）
$exe = Join-Path $dest "translator-ui.exe"
$deploy = $true
if ((Test-Path $exe) -and (Get-Process translator-ui -ErrorAction SilentlyContinue)) {
    Write-Warning "scripts\bridge\translator-ui.exe 正在运行（translator 宿主被占用），本次跳过部署。"
    Write-Warning "构建产物在 $out ；请退出 translator（托盘右键退出）后重跑本脚本完成部署。"
    $deploy = $false
}

if ($deploy) {
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $out "*") $dest -Force
    if (Test-Path (Join-Path $out "runtimes\win-x64\native")) {
        Copy-Item (Join-Path $out "runtimes\win-x64\native\*") $dest -Force
    }
    Write-Host "bridge 已部署 -> $dest"
    Get-ChildItem $dest | ForEach-Object { Write-Host ("  " + $_.Name) }
}
