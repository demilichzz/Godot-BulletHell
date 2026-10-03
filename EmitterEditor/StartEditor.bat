@echo off
setlocal
title BulletHell Emitter Editor

rem Use the installed Godot .NET executable on this computer.
set "EDITOR_GODOT=D:\Develop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%EDITOR_GODOT%" (
    echo Godot .NET executable was not found. Update EDITOR_GODOT in this file.
    pause
    exit /b 1
)

rem Resolve the project relative to this launcher, regardless of the current directory.
pushd "%~dp0.."
if errorlevel 1 (
    echo Cannot open the project directory.
    pause
    exit /b 1
)

echo Building BulletHell...
dotnet build
if errorlevel 1 goto failed

echo Opening the emitter editor...
start "" "%EDITOR_GODOT%" --path "%CD%" "res://EmitterEditor/EmitterEditor.tscn"
if errorlevel 1 goto failed
popd
exit /b 0

:failed
echo The editor could not be started. See the error above.
pause
popd
exit /b 1
