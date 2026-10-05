param([Parameter(Mandatory = $true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$taskManaged = Join-Path $GamePath 'Aistalt_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $taskManaged 'InfantGod.dll'))) { throw 'GamePath 应指向 Aistalt.exe 所在目录。' }
& python (Join-Path $PSScriptRoot 'tools\build_catalog.py') --game $GamePath
if ($LASTEXITCODE -ne 0) { throw '事件目录提取失败。' }
& python (Join-Path $PSScriptRoot 'src\bootstrap.py')
if ($LASTEXITCODE -ne 0) { throw '构建依赖准备失败。' }
$taskOldCliHome = $env:DOTNET_CLI_HOME
$taskOldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'src\deps\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
try {
    & dotnet build (Join-Path $PSScriptRoot 'src\InfantGodAfterDemo.csproj') -c Release "-p:GameManagedPath=$taskManaged" --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw '插件编译失败，未生成交付包。' }
} finally {
    $env:DOTNET_CLI_HOME = $taskOldCliHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $taskOldTelemetry
}
$taskOutput = Join-Path $PSScriptRoot 'dist\InfantGod-AfterDemo-v1.0.0'
$taskPlugin = Join-Path $taskOutput 'BepInEx\plugins\InfantGodAfterDemo'
$taskNative = Join-Path $taskOutput 'Aistalt_Data\StreamingAssets\Mods'
New-Item -ItemType Directory -Path $taskPlugin,$taskNative -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\deps\bepinex\BepInEx\core') -Destination (Join-Path $taskOutput 'BepInEx') -Recurse -Force
foreach ($taskName in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('src\deps\bepinex\' + $taskName)) -Destination $taskOutput -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\bin\Release\netstandard2.1\Graywill.InfantGodAfterDemo.dll') -Destination $taskPlugin -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'data\events.json') -Destination $taskPlugin -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native-mod\InfantGodAfterDemo') -Destination $taskNative -Recurse -Force
foreach ($taskName in @('Install-Mod.ps1','Uninstall-Mod.ps1','Installer-Common.ps1','README.md','ATTRIBUTIONS.txt','THIRD-PARTY-NOTICES.md','LICENSES')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination $taskOutput -Recurse -Force
}
$taskZip = Join-Path $PSScriptRoot 'dist\InfantGod-AfterDemo-v1.0.0.zip'
Compress-Archive -Path $taskOutput -DestinationPath $taskZip -Force
Write-Output "安装包：$taskZip"
