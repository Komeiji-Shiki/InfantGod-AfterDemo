param([Parameter(Mandatory = $true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
$taskGameRoot = Get-AfterDemoGameRoot $GamePath
$taskStatePath = Join-Path $taskGameRoot '.InfantGodAfterDemo-install.json'
$taskExisting = $null
if (Test-Path -LiteralPath $taskStatePath) {
    $taskExisting = Get-Content -LiteralPath $taskStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($taskExisting.status -eq 'Uninstalled') { $taskExisting = $null }
}
$taskPlugin = 'BepInEx\plugins\InfantGodAfterDemo'
$taskNative = 'Aistalt_Data\StreamingAssets\Mods\InfantGodAfterDemo'
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "$taskPlugin\Graywill.InfantGodAfterDemo.dll"))) { throw '请从编译后的安装包运行安装脚本。' }
$taskCore = Join-Path $taskGameRoot 'BepInEx\core\BepInEx.dll'
if (Test-Path -LiteralPath $taskCore) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($taskCore).FileMajorPart -ne 5) { throw '此 Mod 需要 BepInEx 5，当前加载器主版本不同。' }
} else {
    # 共用加载器只在缺失时安装。已有注入器不能直接覆盖。
    foreach ($taskName in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) {
        if (Test-Path -LiteralPath (Join-Path $taskGameRoot $taskName)) { throw "已有未知加载器文件 $taskName，请先确认其来源。" }
    }
    New-Item -ItemType Directory -Path (Join-Path $taskGameRoot 'BepInEx') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BepInEx\core') -Destination (Join-Path $taskGameRoot 'BepInEx') -Recurse -Force
    foreach ($taskName in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $taskGameRoot -Force }
}
$taskBackup = Join-Path $taskGameRoot ('.InfantGodAfterDemo-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$taskRecords = [Collections.Generic.List[object]]::new()
$taskRecorded = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if ($taskExisting) {
    $taskBackup = [IO.Path]::GetFullPath([string]$taskExisting.backupRoot)
    if (-not $taskBackup.StartsWith($taskGameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '备份目录不属于指定游戏。' }
    foreach ($taskRecord in $taskExisting.files) { $taskRecords.Add($taskRecord); $taskRecorded.Add([string]$taskRecord.path) | Out-Null }
}
New-Item -ItemType Directory -Path $taskBackup -Force | Out-Null
$taskState = [ordered]@{ status='Installing'; version='1.0.1'; backupRoot=$taskBackup; files=$taskRecords }
foreach ($taskRelativeRoot in @($taskPlugin,$taskNative)) {
    $taskSourceRoot = Join-Path $PSScriptRoot $taskRelativeRoot
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskSourceRoot -File -Recurse) {
        $taskRelative = $taskRelativeRoot + '\' + $taskFile.FullName.Substring($taskSourceRoot.Length + 1)
        $taskTarget = Get-AfterDemoChildPath $taskGameRoot $taskRelative
        if (-not $taskRecorded.Contains($taskRelative)) {
            $taskExists = Test-Path -LiteralPath $taskTarget
            if ($taskExists) {
                $taskSaved = Get-AfterDemoChildPath $taskBackup $taskRelative
                New-Item -ItemType Directory -Path (Split-Path -Parent $taskSaved) -Force | Out-Null
                Copy-Item -LiteralPath $taskTarget -Destination $taskSaved -Force
            }
            $taskRecords.Add([ordered]@{ path=$taskRelative; existed=$taskExists })
            $taskRecorded.Add($taskRelative) | Out-Null
        }
        $taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force | Out-Null
        Copy-Item -LiteralPath $taskFile.FullName -Destination $taskTarget -Force
    }
}
$taskState.status = 'Installed'
$taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
Write-Output '第八天以后已安装。进入游戏后在 设置 → Mod 管理器 启用，再重新载入存档。'
Write-Output '在游戏开始菜单点击“续玩进度”查看事件。第八天开始自动接入补充直播日程。'
