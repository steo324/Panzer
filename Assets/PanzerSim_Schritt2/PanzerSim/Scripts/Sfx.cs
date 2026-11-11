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

        /// <summary>Einzelschuss des MG (kurzer, trockener Knall).</summary>
        public static void MG(Vector3 pos) =>
            Play(Clip(ref mg, 0.16f, 0.0008f, 0.045f, 260f, 0.85f, 61), pos, 1f, 0.35f, 8f);
        static AudioClip mg;

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
            if (pool.Count >= 24) { pool[0].Stop(); return pool[0]; }
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
