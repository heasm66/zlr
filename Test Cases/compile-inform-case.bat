@echo off
setlocal
if "%1"=="" goto usage
if not exist inform6.exe goto noinform
:foundinform
if not exist "%1.inf" goto nocode
if not exist Compiled\nul md Compiled
inform6 -wkD +include_path=InformLibrary "%1.inf" "Compiled\%1.zcode"
move /y gameinfo.dbg "Compiled\%1.dbg"
exit /b

:usage
echo Usage: compile-inform-case.bat {base}
echo Reads {base}.inf in the current directory.
echo Produces {base}.zcode and {base}.dbg in Compiled.
exit /b 1

:noinform
set informpath1=%ProgramFiles%\Inform 7\Compilers\inform6.exe
set informpath2=%ProgramFiles%\Inform 7\Compilers\inform-631.exe
set informpath3=%ProgramFiles(x86)%\Inform 7\Compilers\inform6.exe
set informpath4=%ProgramFiles(x86)%\Inform 7\Compilers\inform-631.exe
set informpath=%informpath1%
if not exist "%informpath%" set informpath=%informpath2%
if not exist "%informpath%" set informpath=%informpath3%
if not exist "%informpath%" set informpath=%informpath4%
if exist "%informpath%" goto copyinform
:copyinformfailed
echo Inform6.exe is missing. Please copy the compiler from a
echo recent Inform 7 build and call it inform6.exe.
echo I checked the following paths:
echo %CD%\inform6.exe
echo %informpath1%
echo %informpath2%
echo %informpath3%
echo %informpath4%
exit /b 2

:copyinform
echo Copying Inform 6 from %informpath%...
copy "%informpath%" inform6.exe
if not exist inform6.exe goto copyinformfailed
goto foundinform

:nocode
echo The source file "%1.inf" does not exist.
exit /b 3
