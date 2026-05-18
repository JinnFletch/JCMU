@echo off
setlocal enabledelayedexpansion
echo =================================================
echo   Building and Installing Local JCMU
echo =================================================

echo.
echo [1/3] Publishing JCMU.ConsoleBed (win-x64 Single File)...
dotnet publish JCMU.ConsoleBed\JCMU.ConsoleBed.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if %ERRORLEVEL% NEQ 0 goto :DOTNET_FAIL

echo.
echo [2/3] Compiling Inno Setup Installer...

:: --- REGISTRY DISCOVERY ---
:: Look for the Inno Setup 6 install path in the 64-bit and 32-bit registry keys
set "ISCC_PATH="

for %%K in (
    "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
    "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
    "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
) do (
    for /f "tokens=2*" %%A in ('reg query %%K /v Inno Setup: App Path 2^>nul') do (
        set "ISCC_PATH=%%B\ISCC.exe"
    )
)

:: Fallback to common manual paths if Registry fails
if not defined ISCC_PATH (
    if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles%\Inno Setup 6\ISCC.exe"
    if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
)

if not defined ISCC_PATH (
    echo [ERROR] Could not find ISCC.exe.
    echo I checked the Registry and common folders, but Inno Setup 6 isn't where it usually is.
    goto :FAIL
)

echo Found compiler at: "%ISCC_PATH%"
"%ISCC_PATH%" JCMU_Installer.iss
:: --------------------------

if %ERRORLEVEL% NEQ 0 goto :ISS_FAIL

echo.
echo [3/3] Running Installer...
if exist "%~dp0Output\JCMU_Installer.exe" (
    start "" "%~dp0Output\JCMU_Installer.exe"
) else (
    echo [ERROR] Compilation finished, but Output\JCMU_Installer.exe was not found.
    goto :FAIL
)

echo.
echo SUCCESS!
pause
exit /b 0

:DOTNET_FAIL
echo.
echo [ERROR] DotNet Publish failed!
pause
exit /b 1

:ISS_FAIL
echo.
echo [ERROR] Inno Setup compilation failed!
pause
exit /b 1

:FAIL
pause
exit /b 1