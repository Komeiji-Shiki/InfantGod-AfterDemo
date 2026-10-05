param([Parameter(Mandatory = $true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Installer-Common.ps1')
$taskGameRoot = Get-AfterDemoGameRoot $GamePath
$taskStatePath = Join-Path $taskGameRoot '.InfantGodAfterDemo-install.json'
if (-not (Test-Path -LiteralPath $taskStatePath)) { throw '没有本 Mod 的安装记录，未改动游戏文件。' }
$taskState = Get-Content -LiteralPath $taskStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($taskState.status -eq 'Uninstalled') { Write-Output '本 Mod 已卸载。'; exit 0 }
$taskBackup = [IO.Path]::GetFullPath([string]$taskState.backupRoot)
if (-not $taskBackup.StartsWith($taskGameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '备份目录不属于指定游戏。' }
foreach ($taskRecord in $taskState.files) {
    $taskTarget = Get-AfterDemoChildPath $taskGameRoot ([string]$taskRecord.path)
    if ($taskRecord.existed) {
        $taskSaved = Get-AfterDemoChildPath $taskBackup ([string]$taskRecord.path)
        if (-not (Test-Path -LiteralPath $taskSaved)) { throw "缺少原文件备份：$taskSaved" }
        Copy-Item -LiteralPath $taskSaved -Destination $taskTarget -Force
    } elseif (Test-Path -LiteralPath $taskTarget) { Remove-Item -LiteralPath $taskTarget -Force }
}
$taskState.status = 'Uninstalled'
$taskState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskStatePath -Encoding utf8
Write-Output '第八天以后已卸载，本 Mod 覆盖前的文件已恢复。'
Write-Output '共用 BepInEx 加载器、其他 Mod、存档和回退备份均保留。'
