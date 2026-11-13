using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Geraeusche: Kanonenknall, Einschlag auf Stahl, Einschlag im Boden, Explosion.
    /// Die Klaenge werden im Code erzeugt (Rauschen + tiefer Wumms), es braucht keine Audiodateien.
    /// Der Schall kommt mit Schallgeschwindigkeit an (343 m/s): weit weg sieht man den Blitz zuerst.
    /// </summary>
    public static class Sfx
    {
        const float SpeedOfSound = 343f;
        static AudioClip cannon, steel, ground, blast;
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static GameObject root;

        public static void Cannon(Vector3 pos, float caliberMm) =>
            Play(Clip(ref cannon, 2.2f, 0.004f, 0.55f, 45f, 0.9f, 11), pos, Mathf.Clamp(caliberMm / 88f, 0.6f, 1.2f), 1f, 40f);

        public static void SteelHit(Vector3 pos) =>
            Play(Clip(ref steel, 0.9f, 0.001f, 0.18f, 900f, 0.45f, 23), pos, 1f, 0.8f, 15f);

        public static void GroundHit(Vector3 pos) =>
            Play(Clip(ref ground, 0.8f, 0.002f, 0.2f, 70f, 0.7f, 37), pos, 1f, 0.6f, 12f);

        /// <summary>
        /// Einzelschuss des MG: Ueberschallknall, Muendungsknall, Verschlussklacken und kurzes Echo; drei Varianten im Wechsel.
        /// Schweres MG (ab 12 mm: DShK, KPVT) tiefer und lauter.
        /// </summary>
        public static void MG(Vector3 pos, float caliberMm = 7.62f)
        {
            bool heavy = caliberMm >= 12f;
            var set = heavy ? mgHeavy : mgLight;
            mgTurn = (mgTurn + 1 + Random.Range(0, 2)) % set.Length;
            if (set[mgTurn] == null) set[mgTurn] = MgClip(heavy, 101 + mgTurn * 17 + (heavy ? 7 : 0));
            Play(set[mgTurn], pos, 1f, heavy ? 0.85f : 0.6f, heavy ? 18f : 10f);
        }
        static readonly AudioClip[] mgLight = new AudioClip[3], mgHeavy = new AudioClip[3];
        static int mgTurn;

        static AudioClip MgClip(bool heavy, int seed)
        {
            const int rate = 44100;
            float length = heavy ? 0.42f : 0.32f;
            int n = Mathf.CeilToInt(length * rate);
            var d = new float[n];
            var rng = new System.Random(seed);
            float lpCrack = 0f, lpBlast = 0f, lpEcho = 0f, phase = 0f;
            float blastHz = (heavy ? 85f : 150f) * (0.95f + 0.1f * (float)rng.NextDouble());
            float clickAt = (heavy ? 0.03f : 0.02f) + 0.004f * (float)rng.NextDouble(), clickHz = 2600f + 900f * (float)rng.NextDouble();
            var blastBuf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpCrack += (white - lpCrack) * 0.5f;
                float crack = (white - lpCrack) * Mathf.Exp(-t / 0.0022f);                          // heller Ueberschallknall
                lpBlast += (white - lpBlast) * (heavy ? 0.08f : 0.14f);
                phase += 2f * Mathf.PI * blastHz * (1f + 1.5f * Mathf.Exp(-t * 60f)) / rate;
                float blast = (lpBlast * 2.2f + Mathf.Sin(phase) * 0.7f) * Mathf.Exp(-t / (heavy ? 0.04f : 0.026f));   // Muendungsknall
                blastBuf[i] = blast;
                float tc = t - clickAt;
                float click = tc > 0f ? (Mathf.Sin(tc * clickHz * 6.283f) + 0.6f * Mathf.Sin(tc * clickHz * 1.57f * 6.283f)) * Mathf.Exp(-tc / 0.005f) * 0.18f : 0f;   // Verschluss
                // Echo: verzoegerter, dumpfer Muendungsknall (Gelaende/Waldrand)
                int k = i - (int)(0.085f * rate);
                float e = k >= 0 ? blastBuf[k] : 0f;
                lpEcho += (e - lpEcho) * 0.06f;
                float echo = lpEcho * 0.9f * Mathf.Exp(-t / 0.18f);
                d[i] = crack * 0.9f + blast + click + echo;
            }
            float peak = 0.001f; foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < n; i++) d[i] = d[i] / peak * 0.95f;
            var clip = AudioClip.Create((heavy ? "MG schwer " : "MG ") + seed, n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        public static void Explosion(Vector3 pos, float fillerKg) =>
            Play(Clip(ref blast, 2.5f, 0.003f, 0.7f, 38f, 0.95f, 51), pos, Mathf.Clamp(0.7f + fillerKg * 4f, 0.7f, 1.3f), 1f, 25f + fillerKg * 200f);

        static void Play(AudioClip clip, Vector3 pos, float pitch, float volume, float minDist)
        {
            if (clip == null || NetSync.Headless) return;
            var cam = Camera.main;
            float delay = cam != null ? Vector3.Distance(cam.transform.position, pos) / SpeedOfSound : 0f;
            var src = Source();
            src.transform.position = pos;
            src.clip = clip;
            src.pitch = pitch * Random.Range(0.94f, 1.06f);
            src.volume = volume;
            src.minDistance = minDist;
            src.PlayDelayed(delay);
        }

        static AudioSource Source()
        {
            if (root == null)
            {
                root = new GameObject("Geraeusche");
                if (Application.isPlaying) Object.DontDestroyOnLoad(root);
                pool.Clear();
            }
            foreach (var s in pool) if (s != null && !s.isPlaying) return s;
            if (pool.Count >= 40) { pool[0].Stop(); return pool[0]; }   // MG mit 20 Schuss/s braucht einige Quellen gleichzeitig
            var go = new GameObject("Ton");
            go.transform.SetParent(root.transform, false);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.maxDistance = 4000f;
            a.dopplerLevel = 0f;
            pool.Add(a);
            return a;
        }

        /// <summary>
        /// Knall erzeugen: scharfer Anschlag (Rauschen), tiefer Wumms (abfallender Sinus) und Nachhall.
        /// attack/decay in s, thumpHz = Grundton, thumpMix = Anteil Wumms gegenueber Rauschen.
        /// </summary>
        static AudioClip Clip(ref AudioClip cache, float length, float attack, float decay, float thumpHz, float thumpMix, int seed)
        {
            if (cache != null) return cache;
            const int rate = 44100;
            int n = Mathf.CeilToInt(length * rate);
            var d = new float[n];
            var rng = new System.Random(seed);
            float lp = 0f, lp2 = 0f, phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);
                float tail = Mathf.Exp(-t / (decay * 3.5f)) * 0.25f;            // Nachhall / Grollen
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.25f;                                       // etwas dumpfer
                lp2 += (lp - lp2) * 0.04f;                                        // tiefes Grollen
                float f = thumpHz * (1f + 2.5f * Mathf.Exp(-t * 25f));           // Ton faellt schnell ab
                phase += 2f * Mathf.PI * f / rate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t / (decay * 0.6f));
                float crack = white * Mathf.Exp(-t / 0.012f);                     // Peitschenknall am Anfang
                float s = (lp * (1f - thumpMix) + thump * thumpMix) * env + lp2 * 3f * tail + crack * 0.6f;
                d[i] = Mathf.Clamp(s * 0.9f, -1f, 1f);
            }
            cache = AudioClip.Create("Knall" + seed, n, 1, rate, false);
            cache.SetData(d, 0);
            return cache;
        }
    }
}
