using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>Ziel ohne Panzerung (Flugzeug, Pak, Soldat): Granaten und MG-Geschosse melden Treffer direkt.</summary>
    public interface IShellTarget
    {
        void Hit(ShellData d, Vector3 vel, Vector3 pos, bool byPlayer);
    }

    /// <summary>Weiche Ziele, die von Sprengwirkung in der Naehe getroffen werden (Splitter, Druckwelle).</summary>
    public interface ISplashTarget
    {
        Vector3 Position { get; }
        /// <summary>Sprengladung fillerKg explodiert in distance Metern.</summary>
        void Splash(float fillerKg, float distance, bool byPlayer);
    }

    public static class SoftTargets
    {
        public static readonly List<ISplashTarget> All = new List<ISplashTarget>();

        /// <summary>Wirkradius (Splitter) einer Sprengladung: 75 mm (0,7 kg) ca. 10 m, 155 mm ca. 25 m, 250-kg-Bombe ca. 60 m.</summary>
        public static float Radius(float fillerKg) => Mathf.Clamp(Mathf.Pow(Mathf.Max(fillerKg, 0.001f), 1f / 3f) * 11f, 1.5f, 70f);

        public static void Splash(Vector3 pos, float fillerKg, bool byPlayer)
        {
            if (!NetSync.MayApplyDamage || fillerKg <= 0f) return;
            float r = Radius(fillerKg);
            for (int i = All.Count - 1; i >= 0; i--)
            {
                if (i >= All.Count) continue;
                var t = All[i];
                if (t == null) continue;
                float d = Vector3.Distance(t.Position, pos);
                if (d < r) t.Splash(fillerKg, d, byPlayer);
            }
        }
    }
}
