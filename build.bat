@echo off
rem DragLaunch - Build und Update / build and update
rem Baut DragLaunch in den Unterordner "App". Laeuft DragLaunch, wird es beendet und danach neu gestartet.
rem Builds DragLaunch into the "App" subfolder. A running DragLaunch is closed and restarted afterwards.
rem Einstellungen, Backups und Icons im Ordner App bleiben erhalten. / Settings, backups and icons in App are kept.

where dotnet >nul 2>&1
if errorlevel 1 goto nosdk
dotnet --list-sdks 2>nul | findstr /B "10." >nul
if errorlevel 1 goto nosdk

set RESTART=0
tasklist /FI "IMAGENAME eq DragLaunch.exe" 2>nul | find /I "DragLaunch.exe" >nul
if not errorlevel 1 (
  echo DragLaunch wird fuer das Update beendet... / Closing DragLaunch for the update...
  taskkill /IM DragLaunch.exe /F >nul 2>&1
  timeout /t 1 /nobreak >nul
  set RESTART=1
)
cd /d "%~dp0src"
dotnet build -c Release -o "%~dp0App"
if errorlevel 1 (
  echo.
  echo Build fehlgeschlagen - siehe Meldungen oben. / Build failed - see the messages above.
  pause
  exit /b 1
)
echo.
echo Fertig / Done: %~dp0App\DragLaunch.exe
if "%RESTART%"=="1" (
  echo DragLaunch wird neu gestartet... / Restarting DragLaunch...
  start "" "%~dp0App\DragLaunch.exe"
)
pause
exit /b 0

:nosdk
echo.
echo Das .NET 10 SDK fehlt. Bitte installieren und build.bat erneut starten:
echo The .NET 10 SDK is missing. Please install it and run build.bat again:
echo   https://dotnet.microsoft.com/download/dotnet/10.0
echo   (oder / or: winget install Microsoft.DotNet.SDK.10)
echo.
pause
exit /b 1
