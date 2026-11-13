using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Turm-MG (koaxial neben der Kanone, z. B. MG 34 im Tiger): rechte Maustaste halten.
    /// ~900 Schuss/min, jede dritte Patrone ist eine Leuchtspur (deutsch gelb, sowjetisch gruen, amerikanisch rot).
    /// Die Geschosse fliegen mit Schwerkraft und Luftwiderstand; gegen Panzerplatten richten sie nichts aus
    /// (Funken), im Boden wirbeln sie Staub auf. Im Mehrspieler sieht jeder das Feuer der anderen (reine Optik).
    /// </summary>
    public static class MachineGun
    {
        const int TracerEvery = 3;
        const float Spread = 0.0015f;            // Streuung (rad)
        const float TracerBurn = 1.4f;           // Leuchtsatz brennt ~1 km

        /// <summary>7,92 mm sS-Patrone (12,8 g, 755 m/s) - auch fuer die MG-Skala im Zielfernrohr.</summary>
        public static readonly ShellData Round = new ShellData("7,92 mm sS", "MG 34", ShellType.AP, 7.92f, 0.0128f, 755f, 0f) { dragCoefficient = 0.30f };

        /// <summary>Koaxial-MGs (TankSpec.mgType): Patrone und Kadenz (Schuss/s).</summary>
        public static readonly ShellData[] Rounds =
        {
            Round,                                                                                                                  // 0: MG 34, 900/min
            new ShellData("7,62 mm L",   "DT",     ShellType.AP, 7.62f,  0.0096f, 840f,  0f) { dragCoefficient = 0.30f },           // 1: DT/DTM, 600/min
            new ShellData("12,7 mm B-32", "DShKM", ShellType.AP, 12.7f,  0.0485f, 840f,  0f) { dragCoefficient = 0.28f },           // 2: DShK, 600/min
            new ShellData("14,5 mm B-32", "KPVT",  ShellType.AP, 14.5f,  0.064f,  1000f, 0f) { dragCoefficient = 0.28f },           // 3: KPVT, 600/min
            new ShellData(".30-06 M2",   "M1919A4", ShellType.AP, 7.62f, 0.0107f, 853f,  0f) { dragCoefficient = 0.30f },           // 4: Browning, 500/min
            new ShellData("7,62 mm LPS", "SGMT",   ShellType.AP, 7.62f,  0.0096f, 855f,  0f) { dragCoefficient = 0.30f },           // 5: SGMT, 650/min
            new ShellData("7,62 mm DM41", "MG3",   ShellType.AP, 7.62f,  0.0095f, 820f,  0f) { dragCoefficient = 0.30f },           // 6: MG3, 1200/min
            new ShellData("7,62 mm L44A1", "L94A1", ShellType.AP, 7.62f, 0.0095f, 838f,  0f) { dragCoefficient = 0.30f },           // 7: L94A1 Kettenkanone, 550/min
        };
        static readonly float[] RatesS = { 15f, 10f, 10f, 10f, 8.3f, 10.8f, 20f, 9.2f };
        public static ShellData RoundFor(TankSpec s) => Rounds[Mathf.Clamp(s.mgType, 0, Rounds.Length - 1)];
        public static float RateFor(TankSpec s) => RatesS[Mathf.Clamp(s.mgType, 0, RatesS.Length - 1)];

        class Bullet { public Vector3 pos, vel, start; public float life; public bool tracer; public Color color; public TankController owner; public ShellData round; }
        static readonly List<Bullet> bullets = new List<Bullet>();
        static readonly Dictionary<TankController, (float acc, int count)> guns = new Dictionary<TankController, (float, int)>();

        /// <summary>Jedes Bild fuer jeden Panzer aufrufen: firing = MG feuert gerade.</summary>
        public static void Tick(TankController tc, bool firing, float dt)
        {
            if (tc == null || NetSync.Headless) return;
            guns.TryGetValue(tc, out var g);
            if (!firing) { g.acc = 0f; guns[tc] = g; return; }
            g.acc += dt * RateFor(tc.spec);
            while (g.acc >= 1f)
            {
                g.acc -= 1f; g.count++;
                FireOne(tc, g.count % TracerEvery == 0);
            }
            guns[tc] = g;
        }

        public static int ShotsFired, Hits, TracerFrames;   // fuer Tests/Anzeige

        static void FireOne(TankController tc, bool tracer)
        {
            var p = tc.parts;
            if (p.gunPivot == null || p.turret == null || p.turret.parent == null) return;
            ShotsFired++;
            // koaxial: rechts neben der Kanone, ueberhoeht fuer die eingestellte Visier-Entfernung (MG-Skala)
            // Rohr ist fuer die Kanonengranate ueberhoeht; das MG braucht (meist) mehr: Visierlinie + MG-Ueberhoehung
            // (AngleAxis um "rechts" mit positivem Winkel senkt die Richtung - darum Kanone minus MG)
            var round = RoundFor(tc.spec);
            Vector3 dir = Quaternion.AngleAxis(tc.SuperElevation - Superelevation(round, tc.sightRange), p.gunPivot.right) * p.gunPivot.forward;
            dir = (dir + Random.insideUnitSphere * Spread).normalized;
            Vector3 pos = p.gunPivot.position + p.gunPivot.right * 0.32f + p.gunPivot.forward * 1.1f;
            bullets.Add(new Bullet { pos = pos, start = pos, vel = dir * round.muzzleVelocity + (tc.rb != null ? tc.rb.linearVelocity : Vector3.zero), tracer = tracer, color = Tracers.MgColor(tc.spec.sightType), owner = tc, round = round });
            Sfx.MG(pos, round.caliberMm);
            if (tc.isPlayer) CameraShake.Add(pos, 0.04f, 5f);
            EnsureRunner();
        }

        /// <summary>Ueberhoehung (Grad) fuer die MG-Patrone auf diese Entfernung (auf 1500 m begrenzt).</summary>
        public static float Superelevation(ShellData round, float range)
        {
            if (!tables.TryGetValue(round, out var table)) { table = new float[31]; for (int k = 0; k < table.Length; k++) table[k] = -1f; tables[round] = table; }
            range = Mathf.Clamp(range, 10f, 1500f);
            int i = Mathf.Clamp(Mathf.RoundToInt(range / 50f), 0, table.Length - 1);
            if (table[i] < 0f) { Ballistics.SimulateFlat(round, i * 50f + 1f, out _, out float drop, out _); table[i] = Mathf.Atan2(drop, i * 50f + 1f) * Mathf.Rad2Deg; }
            return table[i];
        }
        static readonly Dictionary<ShellData, float[]> tables = new Dictionary<ShellData, float[]>();

        // ---------------- Geschosse bewegen ----------------
        static Runner runner;
        static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("MG-Geschosse");
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }

        class Runner : MonoBehaviour
        {
            void Update()
            {
                float dt = Time.deltaTime;
                if (dt <= 0f) return;
                for (int i = bullets.Count - 1; i >= 0; i--)
                {
                    var b = bullets[i];
                    b.life += dt;
                    Vector3 next = b.pos + b.vel * dt;
                    b.vel += Ballistics.Acceleration(b.round ?? Round, b.vel) * dt;
                    Vector3 step = next - b.pos;
                    bool hitSomething = false;
                    if (Physics.Raycast(b.pos, step.normalized, out RaycastHit hit, step.magnitude, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore))
                    {
                        var tank = hit.collider.GetComponentInParent<TankController>();
                        if (tank != b.owner || b.life > 0.05f)
                        {
                            Impact(hit, tank != null, b.vel);
                            hitSomething = true; Hits++;
                        }
                    }
                    if (hitSomething || b.life > 4f || b.pos.y < -200f) { bullets.RemoveAt(i); continue; }
                    b.pos = next;
                    if (b.tracer && b.life < TracerBurn)
                    {
                        float len = Mathf.Min(b.vel.magnitude * 0.03f, Vector3.Distance(b.start, b.pos));
                        if (len > 0.1f) { Tracers.Streak(b.pos, b.vel, len, 0.025f, b.color); TracerFrames++; }
                    }
                }
            }

            static void Impact(RaycastHit hit, bool armor, Vector3 vel)
            {
                // Panzer: Funken (prallt ab), Boden: kleine Staubwolke
                Effects.BulletHit(hit.point, hit.normal, vel, armor);
            }
        }
    }
}
