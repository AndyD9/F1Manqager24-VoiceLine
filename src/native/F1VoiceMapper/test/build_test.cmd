@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
cd /d "%~dp0"
cl /nologo /std:c++17 /EHsc /W4 hook_test.cpp /Fe:out\hook_test.exe /Fo:out\
