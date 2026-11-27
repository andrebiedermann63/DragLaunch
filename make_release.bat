@echo off
rem DragLaunch - fertig gebaute Version als ZIP fuer GitHub-Releases erstellen
rem Creates a ready-built DragLaunch ZIP for GitHub releases
rem Baut in einen eigenen, sauberen Ordner: keine Einstellungen, Backups, Icons oder Logs aus deinem Ordner App.
rem Builds into its own clean folder: no settings, backups, icons or logs from your App folder.

where dotnet >nul 2>&1
if errorlevel 1 goto nosdk

set "OUT=%~dp0Release\DragLaunch"
if exist "%~dp0Release" rmdir /s /q "%~dp0Release"

cd /d "%~dp0src"
dotnet build -c Release -o "%OUT%"
if errorlevel 1 (
  echo.
  echo Build fehlgeschlagen. / Build failed.
  pause
  exit /b 1
)

rem Nur was ein Benutzer braucht / only what a user needs
del /q "%OUT%\*.pdb" >nul 2>&1
copy /y "%~dp0LIESMICH-README.txt" "%OUT%\LIESMICH-README.txt" >nul
copy /y "%~dp0LICENSE" "%OUT%\LICENSE.txt" >nul
copy /y "%~dp0CHANGELOG-DE.md" "%OUT%\" >nul
copy /y "%~dp0CHANGELOG-EN.md" "%OUT%\" >nul

rem Versionsnummer aus dem Projekt lesen und ZIP erstellen / read version and create ZIP
powershell -NoProfile -Command ^
  "$v = ([xml](Get-Content '%~dp0src\DragLaunch.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1;" ^
  "$v = $v.Split('.')[0];" ^
  "$zip = '%~dp0Release\DragLaunch-' + $v + '-Windows.zip';" ^
  "Compress-Archive -Path '%OUT%' -DestinationPath $zip -Force;" ^
  "Write-Host ''; Write-Host ('Fertig / Done: ' + $zip)"
if errorlevel 1 (
  echo ZIP konnte nicht erstellt werden. / Could not create the ZIP.
  pause
  exit /b 1
)
echo.
echo Diese ZIP auf GitHub unter Releases anhaengen. / Attach this ZIP to a GitHub release.
explorer "%~dp0Release"
pause
exit /b 0

:nosdk
echo Das .NET 10 SDK fehlt. / The .NET 10 SDK is missing.
echo   https://dotnet.microsoft.com/download/dotnet/10.0
pause
exit /b 1
