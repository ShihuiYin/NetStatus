@echo off
setlocal
set "ROOT=%~dp0"

rem ---- 编译好之后自动部署到这里（想改路径就改这一行，或先设环境变量 NETSTATUS_DIR）----
if not defined NETSTATUS_DIR set "NETSTATUS_DIR=C:\Tools\NetStatus"
set "TARGET=%NETSTATUS_DIR%"

rem ---- 项目在网络路径上（\\wsl.localhost\...）时，csc.exe 没法在那里写临时文件，先镜像到本地再编译 ----
if "%ROOT:~0,2%"=="\\" goto :relocate
goto :build

:relocate
set "MIRROR=%TEMP%\netstatus-build"
echo [提示] 项目在网络路径上，先复制到 %MIRROR% 再编译 ...
if exist "%MIRROR%\" rd /s /q "%MIRROR%"
mkdir "%MIRROR%" >nul 2>nul
set "SRCROOT=%ROOT:~0,-1%"
robocopy "%SRCROOT%" "%MIRROR%" /E /XD dist /NFL /NDL /NJH /NJS /NP >nul
if not exist "%MIRROR%\build.bat" (
  echo [错误] 复制到本地临时目录失败，请把整个项目文件夹拷到本地磁盘再编译。
  pause
  exit /b 1
)
call "%MIRROR%\build.bat"
set "RC=%ERRORLEVEL%"
if exist "%MIRROR%\dist\NetStatus.exe" (
  if not exist "%ROOT%dist\" mkdir "%ROOT%dist" >nul 2>nul
  copy /y "%MIRROR%\dist\NetStatus.exe" "%ROOT%dist\NetStatus.exe" >nul 2>nul
)
exit /b %RC%

:build
set "SRC=%ROOT%src"
set "OUT=%ROOT%dist"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [错误] 找不到 Windows 自带的 C# 编译器 csc.exe
  pause
  exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"

echo 正在编译 NetStatus.exe ...（使用 Windows 自带编译器，无需安装任何东西）
"%CSC%" /nologo /noconfig /target:winexe /optimize+ /platform:anycpu /out:"%OUT%\NetStatus.exe" /win32manifest:"%SRC%\app.manifest" /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Management.dll "%SRC%\*.cs"

if errorlevel 1 (
  echo.
  echo [失败] 编译出错，请把上面的错误信息反馈给我。
  pause
  exit /b 1
)

echo.
echo [成功] 已生成： %OUT%\NetStatus.exe

rem ---------------- 自动部署到安装目录 ----------------
if not exist "%TARGET%\" (
  echo [跳过] 没找到安装目录 %TARGET%，只生成 dist 里的 exe，没有自动部署。
  echo        需要的话先建好这个目录再编译，或者手动复制过去。
  goto :done
)

set "KILLED="
copy /y "%OUT%\NetStatus.exe" "%TARGET%\NetStatus.exe" >nul 2>nul
if not errorlevel 1 goto :deployed

echo.
echo [提示] 覆盖 %TARGET%\NetStatus.exe 失败，通常是这个程序正在运行。
tasklist /fi "imagename eq NetStatus.exe" | find /i "NetStatus.exe" >nul
if errorlevel 1 goto :copyfail

set "ANSWER="
set /p "ANSWER=要自动退出正在运行的 NetStatus 再重试吗？[Y/n] "
if /i "%ANSWER%"=="n" goto :copyfail

echo 正在退出 NetStatus ...
taskkill /im NetStatus.exe >nul 2>nul
set "KILLED=1"
ping -n 3 127.0.0.1 >nul
copy /y "%OUT%\NetStatus.exe" "%TARGET%\NetStatus.exe" >nul 2>nul
if errorlevel 1 goto :copyfail

:deployed
echo [成功] 已更新 %TARGET%\NetStatus.exe
if defined KILLED (
  echo 正在重新启动 NetStatus ...
  start "" "%TARGET%\NetStatus.exe"
)
goto :done

:copyfail
echo [警告] 自动部署失败，请手动复制：
echo        %OUT%\NetStatus.exe  -^>  %TARGET%\

:done
echo.
echo 图标会出现在任务栏右下角（可能要点开"显示隐藏的图标"）。
pause
exit /b 0
