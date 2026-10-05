function Get-AfterDemoGameRoot([string]$GamePath) {
    $taskResult = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $taskResult 'Aistalt.exe'))) { throw '请指定 Aistalt.exe 所在的 Build 目录。' }
    if (Get-Process -Name Aistalt -ErrorAction SilentlyContinue) { throw '请先关闭幼神，再安装或卸载。' }
    return $taskResult
}
function Get-AfterDemoChildPath([string]$Root, [string]$Relative) {
    $taskResult = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $taskResult.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '文件路径超出了指定目录。' }
    return $taskResult
}
