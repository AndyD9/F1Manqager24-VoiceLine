@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
cd /d "%~dp0"
rem /Brepro: byte-identical output across builds, so the app only reports a mod update when the code changes.
if not exist build mkdir build
cl /nologo /std:c++17 /O2 /MT /EHsc /W4 /LD /Brepro F1VoiceMapper.cpp /Fe:mod\F1VoiceMapper\F1VoiceMapper.dll /Fo:build\ /link /NOLOGO /Brepro kernel32.lib
