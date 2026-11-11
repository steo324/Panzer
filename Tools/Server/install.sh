#!/usr/bin/env bash
# Richtet den Panzer-Server auf Linux (z. B. CachyOS/Arch) ein: laedt die neueste Version, legt einen
# systemd-Dienst an (startet beim Hochfahren, aktualisiert sich selbst), oeffnet die Firewall-Ports.
# Aufruf:  bash install.sh "Name in der Serverliste" [Karte: 0 = Weite Ebene, 1 = Testgelaende]
set -euo pipefail
NAME="${1:-Ballistik-Server}"
MAP="${2:-0}"
DIR="$HOME/panzer-server"
RAW="https://raw.githubusercontent.com/steo324/Panzer/main/Tools/Server"

echo "== Panzer-Server wird eingerichtet in $DIR (Name: $NAME, Karte: $MAP)"
mkdir -p "$DIR"
curl -fsSL "$RAW/panzer-update.sh" -o "$DIR/panzer-update.sh"
chmod +x "$DIR/panzer-update.sh"
"$DIR/panzer-update.sh"

echo "== systemd-Dienst panzer-server"
sudo tee /etc/systemd/system/panzer-server.service > /dev/null <<EOF
[Unit]
Description=Ballistik Studios Panzer-Server
After=network-online.target
Wants=network-online.target

[Service]
User=$USER
WorkingDirectory=$DIR
# vor jedem Start: neueste Version holen (Fehler werden ignoriert, dann startet die vorhandene)
ExecStartPre=-$DIR/panzer-update.sh
ExecStart=$DIR/game/BallistikServer.x86_64 -batchmode -nographics -server -map $MAP -port 7777 -name "$NAME" -logFile $DIR/server.log
# der Server beendet sich selbst, wenn ein Update da ist -> systemd startet ihn neu (mit Update)
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

echo "== Firewall (UDP 7777 Spiel, 7778 Serverliste)"
if command -v ufw > /dev/null && sudo ufw status | grep -q "Status: active"; then
  sudo ufw allow 7777/udp; sudo ufw allow 7778/udp
elif command -v firewall-cmd > /dev/null && sudo firewall-cmd --state > /dev/null 2>&1; then
  sudo firewall-cmd --permanent --add-port=7777/udp; sudo firewall-cmd --permanent --add-port=7778/udp; sudo firewall-cmd --reload
else
  echo "   (keine aktive Firewall gefunden)"
fi

sudo systemctl daemon-reload
sudo systemctl enable --now panzer-server
echo "== Fertig. Status: sudo systemctl status panzer-server   Log: tail -f $DIR/server.log"
