@echo off
setlocal
if "%1"=="" goto usage
where /q zilf.exe
if errorlevel 1 goto nozilf
where /q zapf.exe
if errorlevel 1 goto nozilf
if not exist "%1.zil" goto nocode
if not exist Compiled\nul md Compiled
zilf -q -d "%1.zil" "Compiled\%1.zap"
zapf -q "Compiled\%1.zap" "Compiled\%1.zcode"
del "Compiled\%1.zap" "Compiled\%1_*.zap"
exit /b

:usage
echo Usage: compile-zil-case.bat {base}
echo Reads {base}.zil in the current directory.
echo Produces {base}.zcode and {base}.dbg in Compiled.
exit /b 1

:nozilf
echo Could not locate zilf.exe and/or zapf.exe.
echo Please add the ZILF bin directory to PATH.
exit /b 2

:nocode
echo The source file "%1.zil" does not exist.
exit /b 3
