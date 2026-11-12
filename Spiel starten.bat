@echo off
rem Ballistik Tank Fronts - Starter
rem Holt beim ersten Mal das fertige Spiel von GitHub (Releases), entpackt es nach
rem %LOCALAPPDATA%\BallistikTankFronts, legt eine Verknuepfung auf den Desktop und startet es.
rem Danach aktualisiert sich das Spiel beim Start von selbst.
title Ballistik Tank Fronts
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$ErrorActionPreference='Stop';" ^
 "$dir = Join-Path $env:LOCALAPPDATA 'BallistikTankFronts';" ^
 "$exe = Join-Path $dir 'BallistikStudios.exe';" ^
 "if (-not (Test-Path $exe)) {" ^
 "  Write-Host 'Ballistik Tank Fronts wird heruntergeladen (ca. 150 MB) ...';" ^
 "  New-Item -ItemType Directory -Force $dir | Out-Null;" ^
 "  $zip = Join-Path $env:TEMP 'BallistikStudios.zip';" ^
 "  & curl.exe -L --fail -# -o $zip 'https://github.com/steo324/Panzer/releases/latest/download/BallistikStudios.zip';" ^
 "  if ($LASTEXITCODE -ne 0) { throw 'Download fehlgeschlagen (Internet?)' };" ^
 "  Write-Host 'Entpacken ...';" ^
 "  Expand-Archive -Path $zip -DestinationPath $dir -Force;" ^
 "  Remove-Item $zip -Force;" ^
 "  $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Ballistik Tank Fronts.lnk';" ^
 "  $s = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk); $s.TargetPath = $exe; $s.WorkingDirectory = $dir; $s.Save();" ^
 "  Write-Host 'Fertig - Verknuepfung auf dem Desktop angelegt.';" ^
 "}" ^
 "Start-Process -FilePath $exe -WorkingDirectory $dir"
if errorlevel 1 (
  echo.
  echo Das hat nicht geklappt. Alternativ: https://github.com/steo324/Panzer/releases/latest
  pause
)
