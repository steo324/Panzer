using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Gradientenrauschen (Perlin-Art) in reinem C#: gleich auf jedem Rechner und thread-sicher
    /// (Mathf.PerlinNoise darf nicht aus mehreren Threads aufgerufen werden). Werte etwa 0..1.
    /// </summary>
    public sealed class Noise2D
    {
        readonly int[] perm = new int[512];

        public Noise2D(int seed)
        {
            var r = new System.Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x; case 5: return -x; case 6: return y; default: return -y;
            }
        }

        /// <summary>Einfaches Rauschen, ca. 0..1.</summary>
        public float Get(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255; yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = perm[perm[xi] + yi], ab = perm[perm[xi] + yi + 1], ba = perm[perm[xi + 1] + yi], bb = perm[perm[xi + 1] + yi + 1];
            float x1 = Mathf.Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
            float x2 = Mathf.Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
            return Mathf.Lerp(x1, x2, v) * 0.5f + 0.5f;
        }

        /// <summary>Mehrere Oktaven (feinere Strukturen), ca. 0..1.</summary>
        public float Fbm(float x, float y, int octaves, float lacunarity = 2.03f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Get(x, y) * amp; norm += amp;
                x = x * lacunarity + 17.3f; y = y * lacunarity + 9.1f; amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>Grat-Rauschen: scharfe Kaemme (1 auf dem Kamm), fuer Huegelruecken.</summary>
        public float Ridge(float x, float y, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Get(x, y) * 2f - 1f);
                sum += n * n * amp; norm += amp;
                x = x * 2.01f + 31.7f; y = y * 2.01f + 3.3f; amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
