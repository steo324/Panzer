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
        public const float RateS = 15f;          // Schuss pro Sekunde
        const int TracerEvery = 3;
        const float Spread = 0.0015f;            // Streuung (rad)
        const float TracerBurn = 1.4f;           // Leuchtsatz brennt ~1 km

        /// <summary>7,92 mm sS-Patrone (12,8 g, 755 m/s) - auch fuer die MG-Skala im Zielfernrohr.</summary>
        public static readonly ShellData Round = new ShellData("7,92 mm sS", "MG 34", ShellType.AP, 7.92f, 0.0128f, 755f, 0f) { dragCoefficient = 0.30f };

        class Bullet { public Vector3 pos, vel, start; public float life; public bool tracer; public Color color; public TankController owner; }
        static readonly List<Bullet> bullets = new List<Bullet>();
        static readonly Dictionary<TankController, (float acc, int count)> guns = new Dictionary<TankController, (float, int)>();

        /// <summary>Jedes Bild fuer jeden Panzer aufrufen: firing = MG feuert gerade.</summary>
        public static void Tick(TankController tc, bool firing, float dt)
        {
            if (tc == null || NetSync.Headless) return;
            guns.TryGetValue(tc, out var g);
            if (!firing) { g.acc = 0f; guns[tc] = g; return; }
            g.acc += dt * RateS;
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
            Vector3 dir = Quaternion.AngleAxis(tc.SuperElevation - Superelevation(tc.sightRange), p.gunPivot.right) * p.gunPivot.forward;
            dir = (dir + Random.insideUnitSphere * Spread).normalized;
            Vector3 pos = p.gunPivot.position + p.gunPivot.right * 0.32f + p.gunPivot.forward * 1.1f;
            bullets.Add(new Bullet { pos = pos, start = pos, vel = dir * Round.muzzleVelocity + (tc.rb != null ? tc.rb.linearVelocity : Vector3.zero), tracer = tracer, color = Tracers.MgColor(tc.spec.sightType), owner = tc });
            Sfx.MG(pos);
            if (tc.isPlayer) CameraShake.Add(pos, 0.04f, 5f);
            EnsureRunner();
        }

        /// <summary>Ueberhoehung (Grad) fuer die MG-Patrone auf diese Entfernung (auf 1500 m begrenzt).</summary>
        public static float Superelevation(float range)
        {
            range = Mathf.Clamp(range, 10f, 1500f);
            int i = Mathf.Clamp(Mathf.RoundToInt(range / 50f), 0, table.Length - 1);
            if (table[i] < 0f) { Ballistics.SimulateFlat(Round, i * 50f + 1f, out _, out float drop, out _); table[i] = Mathf.Atan2(drop, i * 50f + 1f) * Mathf.Rad2Deg; }
            return table[i];
        }
        static readonly float[] table = Init();
        static float[] Init() { var a = new float[31]; for (int i = 0; i < a.Length; i++) a[i] = -1f; return a; }

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
                    b.vel += Ballistics.Acceleration(Round, b.vel) * dt;
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
