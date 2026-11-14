# Veroeffentlicht den aktuellen Build als GitHub-Release (Spiel + Linux-Server).
# Spieler aktualisieren sich beim naechsten Spielstart selbst, der Server innerhalb von 5 Minuten.
# Voraussetzung: einmal "gh auth login" (GitHub-Programm, siehe LIESMICH).
# Aufruf:  powershell -ExecutionPolicy Bypass -File Tools\publish.ps1 -Notes "Was ist neu" [-Name "Kalter Stahl"]
# Versionen sind Update-Nummern (1.0, 1.1 ...); das Bauen (BuildGame) vergibt die naechste Nummer selbst.
param([string]$Notes = "", [string]$Name = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$repo = "steo324/Panzer"
$ver = (Get-Content (Join-Path $root "Assets\Resources\version.txt") -Raw).Trim()
$zip = Join-Path $root "Builds\BallistikStudios.zip"
$srvDir = Join-Path $root "Builds\BallistikServer"
$tgz = Join-Path $root "Builds\BallistikServer.tar.gz"
if (-not (Test-Path $zip)) { throw "Spiel-ZIP fehlt: $zip (erst bauen)" }
if (-not (Test-Path (Join-Path $srvDir "BallistikServer.x86_64"))) { throw "Linux-Server fehlt: $srvDir (erst bauen)" }

# Server als tar.gz (enthaelt den Ordner BallistikServer/)
if (Test-Path $tgz) { Remove-Item $tgz -Force }
& "$env:SystemRoot\System32\tar.exe" -czf $tgz -C (Join-Path $root "Builds") "BallistikServer"
if ($LASTEXITCODE -ne 0) { throw "tar fehlgeschlagen" }

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }
$title = if ([string]::IsNullOrWhiteSpace($Name)) { "Update $ver" } else { "Update $ver - $Name" }
if ([string]::IsNullOrWhiteSpace($Notes)) { $Notes = $title }
& $gh release create "v$ver" $zip $tgz --repo $repo --title $title --notes $Notes --latest
if ($LASTEXITCODE -ne 0) { throw "gh release create fehlgeschlagen" }
Write-Host "VEROEFFENTLICHT: $title (v$ver)"
