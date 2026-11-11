# CLAUDE.md

Unity-Projekt "BallistikStudios" (Repo: https://github.com/steo324/Panzer.git).

## Git-Workflow (immer einhalten, ohne Nachfrage)

- **Immer auf `main` arbeiten – keine Branches** anlegen, keine Worktrees, keine Pull Requests.
- **Vor jeder neuen Task:** `git pull origin main` (bei Konflikten mergen), damit lokal der aktuelle Stand vorliegt.
- **Nach jeder abgeschlossenen Änderung:** direkt committen und `git push origin main` – ohne dass der User das extra sagen muss.
- **Nach Spiel-Änderungen zusätzlich veröffentlichen:** bauen (BuildGame.BuildWindows, stempelt Version), dann `powershell -ExecutionPolicy Bypass -File Tools\publish.ps1 -Notes "..."` → GitHub-Release; Spieler und Linux-Server aktualisieren sich daraus selbst. GitHub CLI: `C:\Program Files\GitHub CLI\gh.exe`.
- Git liegt unter `C:\Program Files\Git\cmd\git.exe` (falls `git` im PATH der Shell noch fehlt).
- Nicht versioniert (siehe `.gitignore`): `Library/`, `Temp/`, `Logs/`, `Builds/`, `UserSettings/`, IDE-Dateien.
