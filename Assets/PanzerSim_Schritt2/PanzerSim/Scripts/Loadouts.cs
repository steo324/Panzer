using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Munitionsmischung je Panzer: wie viele Granaten jeder Sorte an Bord sind (zusammen hoechstens der echte Vorrat).
    /// Der Spieler stellt sie im Menue ein (gespeichert je Panzer), KI und Zielpanzer nehmen die Standard-Mischung.
    /// </summary>
    public static class Loadouts
    {
        /// <summary>Standard: Vorgabe aus dem Panzer, sonst erste Sorte die Haelfte, der Rest gleich verteilt.</summary>
        public static int[] Default(int[] ammo, int cap, int[] def)
        {
            int n = ammo != null ? ammo.Length : 0;
            var r = new int[n];
            if (n == 0) return r;
            if (def != null && def.Length == n) { for (int i = 0; i < n; i++) r[i] = def[i]; return r; }
            if (n == 1) { r[0] = cap; return r; }
            r[0] = cap / 2;
            int rest = cap - r[0];
            for (int i = 1; i < n; i++) r[i] = rest / (n - 1);
            r[0] += rest - (rest / (n - 1)) * (n - 1);   // Rundungsrest zur ersten Sorte
            return r;
        }

        public static int Capacity(TankSpec s) => s.ammoCapacity > 0 ? s.ammoCapacity : 60;
        static int Capacity2(Mount g) => g.gunCapacity > 0 ? g.gunCapacity : 100;

        public static Mount Gun2(TankSpec s)
        {
            if (s.mounts != null) foreach (var m in s.mounts) if (m.gunAmmo != null && m.gunAmmo.Length > 0) return m;
            return null;
        }

        public static int[] DefaultMain(TankSpec s) => Default(s.ammo ?? new[] { s.ammoIndex }, Capacity(s), s.defaultLoad);
        public static int[] DefaultGun2(TankSpec s) { var g = Gun2(s); return g == null ? null : Default(g.gunAmmo, Capacity2(g), null); }

        /// <summary>Gespeicherte Mischung des Spielers (oder Standard, falls keine/ungueltig).</summary>
        public static int[] Main(TankSpec s) => Read("ammo." + s.name, (s.ammo ?? new[] { s.ammoIndex }).Length, Capacity(s)) ?? DefaultMain(s);
        public static int[] Gun2Load(TankSpec s)
        {
            var g = Gun2(s); if (g == null) return null;
            return Read("ammo2." + s.name, g.gunAmmo.Length, Capacity2(g)) ?? DefaultGun2(s);
        }
        public static int Capacity2(TankSpec s) { var g = Gun2(s); return g == null ? 0 : Capacity2(g); }

        public static void Save(TankSpec s, int[] main, int[] gun2)
        {
            if (main != null) PlayerPrefs.SetString("ammo." + s.name, string.Join(",", main));
            if (gun2 != null) PlayerPrefs.SetString("ammo2." + s.name, string.Join(",", gun2));
            PlayerPrefs.Save();
        }

        static int[] Read(string key, int n, int cap)
        {
            string v = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(v)) return null;
            var parts = v.Split(',');
            if (parts.Length != n) return null;
            var r = new int[n]; int sum = 0;
            for (int i = 0; i < n; i++) { if (!int.TryParse(parts[i], out r[i]) || r[i] < 0) return null; sum += r[i]; }
            return sum <= cap ? r : null;
        }
    }
}
