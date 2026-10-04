@echo off
rem Builds the native mod, then publishes the app as a self-contained single exe in dist\F1M24VoiceManager.
cd /d "%~dp0"
call src\native\F1VoiceMapper\build.cmd || exit /b 1
cd /d "%~dp0"
dotnet publish src\F1M24VoiceManager.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist\F1M24VoiceManager
