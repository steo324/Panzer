using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Update-Nummer des Builds (z. B. "1.4"; bis November 2026 Datum + Uhrzeit). Das Build-Skript schreibt sie nach
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
        /// <summary>Fuer die Anzeige: "Update 1.4" (alte Datums-Versionen: "Version 2026.11.13.2025").</summary>
        public static string Display => Current.Split('.').Length == 2 ? "Update " + Current : "Version " + Current;

        /// <summary>Im Editor oder ohne Versionsdatei: keine Updates, keine strenge Versionspruefung.</summary>
        public static bool IsDev => Application.isEditor || Current == "dev";
    }
}
