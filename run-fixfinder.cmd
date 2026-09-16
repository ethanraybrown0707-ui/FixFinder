@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "DLL_PATH=%SCRIPT_DIR%FixFinder.Gui\bin\Debug\net8.0-windows\FixFinder.Gui.dll"

if not exist "%DLL_PATH%" (
    echo.
    echo Could not find "%DLL_PATH%".
    echo Build it first:  dotnet build FixFinder.Gui\FixFinder.Gui.csproj
    pause
    exit /b 1
)

rem Launched via `dotnet <dll>` rather than the raw .exe: this machine enforces Smart App
rem Control and an Application Control policy, which block a freshly-built, self-signed
rem binary under the user profile. dotnet.exe is already trusted.
dotnet "%DLL_PATH%"

endlocal
