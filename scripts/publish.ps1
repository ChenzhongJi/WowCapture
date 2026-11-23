# 把 WowCapture 发布到"工作区之外"的普通目录并刷新桌面快捷方式。
# 为什么必须发布到别处：从 D:\Repo\WowCapture 里直接运行 exe 时，进程会被环境限制
# （写不进 %LOCALAPPDATA%、托盘图标无法注册）。同一份 exe 拷到普通目录即恢复正常。
#
# 用法：双击 publish.cmd（会绕过默认执行策略），或
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish.ps1
param(
    [string]$Output = (Join-Path $env:LOCALAPPDATA 'Apps\WowCapture')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\WowCapture.App\WowCapture.App.csproj'

New-Item -ItemType Directory -Path $Output -Force | Out-Null
dotnet publish $project -c Release --self-contained false -o $Output

$exe = Join-Path $Output 'WowCapture.exe'
$lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'WowCapture.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $Output
$shortcut.Description = 'WowCapture 截图工具'
$shortcut.Save()

Write-Host ''
Write-Host "已发布  : $Output" -ForegroundColor Green
Write-Host "快捷方式: $lnk"
Write-Host "配置目录: $(Join-Path $env:LOCALAPPDATA 'WowCapture')"