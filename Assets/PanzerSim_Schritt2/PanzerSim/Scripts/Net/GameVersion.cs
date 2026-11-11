using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Versionsnummer des Builds (Datum + Uhrzeit in UTC, z. B. "2026.11.11.1542"). Das Build-Skript schreibt sie nach
    /// Assets/Resources/version.txt. Server und Spieler muessen dieselbe Version haben; das Auto-Update vergleicht sie
    /// mit dem neuesten GitHub-Release.
    /// </summary>
    public static class GameVersion
    {
        static string current;
        public static string Current
        {
            get
            {
                if (current == null)
                {
                    var t = Resources.Load<TextAsset>("version");
                    current = t != null ? t.text.Trim() : "dev";
                }
                return current;
            }
        }
        /// <summary>Im Editor oder ohne Versionsdatei: keine Updates, keine strenge Versionspruefung.</summary>
        public static bool IsDev => Application.isEditor || Current == "dev";
    }
}
