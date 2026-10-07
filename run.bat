@echo off
setlocal
if not defined NETSTATUS_DIR set "NETSTATUS_DIR=C:\Tools\NetStatus"
set "INSTALLED=%NETSTATUS_DIR%\NetStatus.exe"
set "DIST=%~dp0dist\NetStatus.exe"

if not exist "%INSTALLED%" if not exist "%DIST%" (
  echo 还没有编译，先运行 build.bat ...
  call "%~dp0build.bat"
)

set "EXE=%INSTALLED%"
if not exist "%EXE%" set "EXE=%DIST%"
if not exist "%EXE%" (
  echo [错误] 找不到 NetStatus.exe，请先运行 build.bat
  pause
  exit /b 1
)

echo 启动： %EXE%
start "" "%EXE%"
exit /b 0
