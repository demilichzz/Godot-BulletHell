param(
    [string]$GodotPath,
    [string]$EmitterPath
)

# 仅构建并启动独立编辑场景，不修改游戏默认入口。
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $GodotPath) {
    # 优先复用当前已安装并运行的Godot Mono程序。
    $GodotPath = Get-Process '*godot*' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like '*mono*' -and $_.Path -notlike '*console*' } |
        Select-Object -First 1 -ExpandProperty Path
}
if (-not $GodotPath) {
    $godotCommand = Get-Command 'godot', 'godot-mono', 'godot4' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($godotCommand) { $GodotPath = $godotCommand.Source }
}
if (-not $GodotPath -or -not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw '找不到Godot .NET程序。请先打开Godot项目，或用 -GodotPath 指定Godot Mono可执行文件。'
}
Push-Location -LiteralPath $projectRoot
try {
    & dotnet build
    if ($LASTEXITCODE -ne 0) { throw '项目编译失败，编辑器未启动。' }
    $editorArguments = @('--path', $projectRoot, 'res://EmitterEditor/EmitterEditor.tscn')
    if ($EmitterPath) { $editorArguments += @('--', ('--emitter=' + [IO.Path]::GetFullPath($EmitterPath))) }
    & $GodotPath @editorArguments
}
finally { Pop-Location }
