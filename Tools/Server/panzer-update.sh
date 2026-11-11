#!/usr/bin/env bash
# Laedt das neueste Server-Release von GitHub, wenn es neuer ist als das installierte.
# Laeuft vor jedem Serverstart (systemd ExecStartPre). Ohne Internet startet einfach die vorhandene Version.
set -euo pipefail
REPO="steo324/Panzer"
DIR="${PANZER_DIR:-$HOME/panzer-server}"
mkdir -p "$DIR"

api=$(curl -fsSL --max-time 20 -H "Accept: application/vnd.github+json" -H "User-Agent: panzer-server" \
      "https://api.github.com/repos/$REPO/releases/latest") || { echo "Update: GitHub nicht erreichbar, vorhandene Version bleibt"; exit 0; }
tag=$(printf '%s' "$api" | grep -m1 '"tag_name"' | sed -E 's/.*"tag_name": *"v?([^"]+)".*/\1/')
url=$(printf '%s' "$api" | grep -o '"browser_download_url": *"[^"]*BallistikServer\.tar\.gz"' | head -n1 | sed -E 's/.*"(https[^"]+)"/\1/')
cur=$(cat "$DIR/VERSION" 2>/dev/null || echo "keine")

if [ -z "$tag" ] || [ -z "$url" ]; then echo "Update: kein Server-Release gefunden"; exit 0; fi
if [ "$tag" = "$cur" ] && [ -x "$DIR/game/BallistikServer.x86_64" ]; then echo "Update: Version $cur ist aktuell"; exit 0; fi

echo "Update: $cur -> $tag"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
curl -fsSL --max-time 900 "$url" -o "$tmp/server.tgz"
tar -xzf "$tmp/server.tgz" -C "$tmp"
rm -rf "$DIR/game.old"
if [ -d "$DIR/game" ]; then mv "$DIR/game" "$DIR/game.old"; fi
mv "$tmp/BallistikServer" "$DIR/game"
chmod +x "$DIR/game/BallistikServer.x86_64"
echo "$tag" > "$DIR/VERSION"
rm -rf "$DIR/game.old"
echo "Update: fertig, Version $tag"
