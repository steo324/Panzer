using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Gemeinsame Werte fuer alle Pflanzen-Shader (Weizen, Gras, Baeume): Wind und die letzten Druckwellen.
    /// Die Shader biegen die Pflanzen selbst - die CPU merkt sich nur Ort, Zeit, Radius und Staerke.
    /// </summary>
    public static class Vegetation
    {
        const int Max = 8;
        static readonly Vector4[] blasts = new Vector4[Max];      // xyz Ort, w Startzeit
        static readonly Vector4[] blastParams = new Vector4[Max]; // x Radius, y Staerke
        static int next;
        static bool init;

        public static Vector2 WindDir = new Vector2(0.8f, 0.6f);
        public static float WindStrength = 0.07f;

        public static void EnsureInit()
        {
            if (init) return;
            init = true;
            Shader.SetGlobalVectorArray("_Blasts", blasts);
            Shader.SetGlobalVectorArray("_BlastParams", blastParams);
            SetWind(WindDir, WindStrength);
        }

        public static void SetWind(Vector2 dir, float strength)
        {
            WindDir = dir.normalized; WindStrength = strength;
            Shader.SetGlobalVector("_VegWind", new Vector4(WindDir.x, WindDir.y, strength, 0f));
        }

        public static void AddBlast(Vector3 pos, float radius, float strength)
        {
            EnsureInit();
            blasts[next] = new Vector4(pos.x, pos.y, pos.z, Time.timeSinceLevelLoad);
            blastParams[next] = new Vector4(radius, strength, 0f, 0f);
            next = (next + 1) % Max;
            Shader.SetGlobalVectorArray("_Blasts", blasts);
            Shader.SetGlobalVectorArray("_BlastParams", blastParams);
        }
    }
}
