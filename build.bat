@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ============================================
echo  久坐提醒 - 打包单文件 EXE (win-x64 自包含)
echo  目标机零依赖，双击即用
echo ============================================
echo.

dotnet publish src\SedentaryReminder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist

if errorlevel 1 (
    echo.
    echo [失败] 打包出错。请确认已安装 .NET 8 SDK，执行 dotnet --version 应显示 8.x.x
    pause
    exit /b 1
)

echo.
echo [成功] 单文件 EXE 已输出到： dist\SedentaryReminder.exe
echo 拷贝该 EXE 到任何 Windows 10/11 x64 电脑即可直接运行。
pause
