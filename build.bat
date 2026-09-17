@echo off
setlocal
cd /d "%~dp0"
echo ============================================
echo  SedentaryReminder - build single-file EXE
echo  (win-x64, self-contained, no deps needed)
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found. Please install .NET 8 SDK first:
    echo         https://dotnet.microsoft.com/zh-cn/download
    echo         Run "dotnet --version" to verify, it should show 8.x.x
    pause
    exit /b 1
)

for /f "usebackq delims=" %%V in (`dotnet msbuild src\SedentaryReminder.csproj -nologo -getProperty:SafeFileVersion`) do set "APP_VERSION=%%V"
if not defined APP_VERSION (
    echo [ERROR] Could not read the application version from the project file.
    pause
    exit /b 1
)
set "OUTPUT_EXE=TickNote-v%APP_VERSION%.exe"

echo [1/2] Publishing...

rem If the app is running from dist\, the EXE is locked and publish will fail.
rem Use findstr (not find) so this works even when a GNU toolchain shadows find.exe in PATH.
tasklist 2>nul | findstr /I /C:"TickNote.exe" /C:"TickNote-v" >nul
if not errorlevel 1 (
    echo [WARN] A TickNote EXE is currently running and may be locked.
    echo        Please right-click the tray icon and choose "Exit" first,
    echo        then press any key to continue building...
    pause
)

dotnet publish src\SedentaryReminder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist
if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed. If the error says the EXE is locked / in use,
    echo         please exit SedentaryReminder from the system tray first.
    pause
    exit /b 1
)

echo.
echo [2/2] Done: dist\%OUTPUT_EXE%
echo Copy this single EXE to any Windows 10/11 x64 PC and run it directly.
pause
