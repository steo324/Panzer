using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Pak-Stellung (KI): 7,5-cm-Panzerabwehrkanone hinter Schutzschild, schiesst mit echter Ballistik auf Panzer des
    /// anderen Teams (Entfernung geschaetzt, wird mit jedem Schuss genauer). Ungepanzert: jeder Treffer oder eine
    /// Sprenggranate in der Naehe schaltet die Bedienung aus.
    /// </summary>
    public class AtGun : MonoBehaviour, IShellTarget, ISplashTarget
    {
        /// <summary>Test: Pak schiesst nicht (Benchmark).</summary>
        public static bool BenchHold;
        public int team = 1;
        public bool Dead { get; private set; }
        public Vector3 Position => transform.position + Vector3.up * 1f;
        Transform yawPivot, barrel, muzzle;
        TankController target;
        float searchT, reload = 4f, rangeErr, settle;
        int shots;
        GameMap map;
        static ShellData AP => AmmoLibrary.All[57];
        const float Range = 1600f;

        public static AtGun Spawn(GameMap m, Vector3 pos, float yaw, int team, Transform parent)
        {
            var go = new GameObject("Pak-Stellung");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var g = go.AddComponent<AtGun>();
            g.team = team; g.map = m;
            g.Build();
            SoftTargets.All.Add(g);
            return g;
        }

        void Build()
        {
            Color steel = new Color(0.30f, 0.33f, 0.26f), dark = new Color(0.12f, 0.12f, 0.11f), sand = new Color(0.50f, 0.44f, 0.32f);
            // Erdwall (Sandsaecke) vorn: echte Deckung (Collider ohne Ziel-Schnittstelle)
            for (int i = -2; i <= 2; i++)
            {
                var bag = Part(PrimitiveType.Cube, transform, new Vector3(i * 0.9f, 0.3f, 2.4f - Mathf.Abs(i) * 0.25f), new Vector3(0.95f, 0.6f, 0.6f), sand, false);
                bag.transform.localRotation = Quaternion.Euler(0f, i * 12f, 0f);
                bag.AddComponent<BoxCollider>();
                bag.transform.SetParent(transform.parent, true);   // Wall gehoert nicht zur Kanone: Treffer dort zaehlen nicht (echte Deckung)
            }
            yawPivot = new GameObject("Lafette").transform; yawPivot.SetParent(transform, false);
            Part(PrimitiveType.Cylinder, yawPivot, new Vector3(-0.85f, 0.4f, 0f), new Vector3(0.75f, 0.06f, 0.75f), dark, false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Part(PrimitiveType.Cylinder, yawPivot, new Vector3(0.85f, 0.4f, 0f), new Vector3(0.75f, 0.06f, 0.75f), dark, false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Part(PrimitiveType.Cube, yawPivot, new Vector3(0f, 0.9f, 0.55f), new Vector3(1.9f, 1.1f, 0.06f), steel, true);    // Schutzschild
            Part(PrimitiveType.Cube, yawPivot, new Vector3(-0.5f, 0.25f, -1.3f), new Vector3(0.14f, 0.14f, 2.4f), steel, false).transform.localRotation = Quaternion.Euler(0f, -14f, 0f);   // Holme
            Part(PrimitiveType.Cube, yawPivot, new Vector3(0.5f, 0.25f, -1.3f), new Vector3(0.14f, 0.14f, 2.4f), steel, false).transform.localRotation = Quaternion.Euler(0f, 14f, 0f);
            barrel = new GameObject("Rohr").transform; barrel.SetParent(yawPivot, false); barrel.localPosition = new Vector3(0f, 0.95f, 0.3f);
            Part(PrimitiveType.Cylinder, barrel, new Vector3(0f, 0f, 1.6f), new Vector3(0.11f, 1.6f, 0.11f), steel, true).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(PrimitiveType.Cube, barrel, new Vector3(0f, 0f, -0.2f), new Vector3(0.35f, 0.3f, 0.7f), steel, true);
            muzzle = new GameObject("Muendung").transform; muzzle.SetParent(barrel, false); muzzle.localPosition = new Vector3(0f, 0f, 3.4f);
            // Bedienung: drei Soldaten hinter dem Schild
            for (int i = 0; i < 3; i++) Soldier.Figure(yawPivot, new Vector3(-0.7f + i * 0.7f, 0f, -0.6f - (i % 2) * 0.5f), i == 1 ? 0.75f : 1f);
        }

        GameObject Part(PrimitiveType t, Transform parent, Vector3 lp, Vector3 scale, Color c, bool collider)
        {
            var g = GameObject.CreatePrimitive(t);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = lp; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(c);
            if (!collider) Destroy(g.GetComponent<Collider>());
            else g.AddComponent<AtGunPart>();
            return g;
        }

        public void Hit(ShellData d, Vector3 vel, Vector3 pos, bool byPlayer) => Kill(byPlayer, "Treffer");
        public void Splash(float fillerKg, float distance, bool byPlayer)
        {
            // Splitter: im Nahbereich sicher, weiter weg mit Glueck (Schild schuetzt etwas)
            float r = SoftTargets.Radius(fillerKg);
            if (distance < r * 0.45f || Random.value < 0.5f * (1f - distance / r)) Kill(byPlayer, "Sprenggranate");
        }

        void Kill(bool byPlayer, string how)
        {
            if (Dead) return;
            Dead = true;
            SoftTargets.All.Remove(this);
            GroundThreats.ReportKill("Pak-Stellung", byPlayer);
            if (!NetSync.Headless) { Effects.Explosion(Position, 0.4f, true); Effects.Burn(transform, Vector3.up * 0.6f, 25f, 0.4f); }
            if (barrel != null) barrel.localRotation = Quaternion.Euler(14f, 0f, 0f);   // Rohr sackt ab
            foreach (var s in GetComponentsInChildren<SoldierFigure>()) s.Fall();
        }

        void OnDestroy() => SoftTargets.All.Remove(this);

        void Update()
        {
            if (Dead || map == null || BenchHold) return;
            float dt = Time.deltaTime;
            searchT -= dt; reload -= dt;
            if (searchT <= 0f) { searchT = 1.5f; Find(); }
            if (target == null) return;
            Vector3 tp = target.transform.TransformPoint(0f, target.spec.clearance + target.spec.hullHeight * 0.6f, 0f);
            Vector3 to = tp - muzzle.position;
            float dist = to.magnitude;
            // Lafette dreht langsam (Bedienung schiebt die Holme), Rohr hebt sich um die Ueberhoehung
            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float yaw = Mathf.MoveTowardsAngle(yawPivot.eulerAngles.y, wantYaw, 9f * dt);
            yawPivot.rotation = Quaternion.Euler(0f, yaw, 0f);
            float est = dist * (1f + rangeErr);
            float elev = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg + MachineGun.Superelevation(AP, est);
            barrel.localRotation = Quaternion.Euler(-elev, 0f, 0f);
            bool onTarget = Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < 0.5f;
            settle = onTarget ? settle + dt : 0f;
            if (reload <= 0f && settle > 1.2f)
            {
                // Vorhalt grob (bewegtes Ziel), kleiner Richtfehler
                Ballistics.SimulateFlat(AP, Mathf.Max(dist, 10f), out _, out _, out float tof);
                Vector3 v = target.rb != null && !target.rb.isKinematic ? target.rb.linearVelocity : Vector3.zero;
                Vector3 aimDir = Quaternion.Euler(-elev + Random.Range(-0.08f, 0.08f), wantYaw + Random.Range(-0.1f, 0.1f), 0f) * Vector3.forward;
                Vector3 lead = v * tof * 0.8f; lead.y = 0f;
                aimDir = Quaternion.FromToRotation(new Vector3(to.x, 0f, to.z).normalized, new Vector3(to.x + lead.x, 0f, to.z + lead.z).normalized) * aimDir;
                var s = Shell.Fire(AP, muzzle.position + aimDir * 0.5f, aimDir, false);
                _ = s;
                if (!NetSync.Headless) { Effects.Muzzle(muzzle.position, aimDir, 75f, true); Sfx.Cannon(muzzle.position, 75f); }
                shots++; rangeErr *= Random.Range(0.2f, 0.5f);
                reload = Random.Range(6f, 8f); settle = 0f;
            }
        }

        void Find()
        {
            TankController best = null; float bd = Range;
            foreach (var t in Object.FindObjectsByType<TankController>(FindObjectsSortMode.None))
            {
                if (t.destroyed || t.team == team || t.team == TankController.TeamTarget) continue;
                float d = Vector3.Distance(t.transform.position, transform.position);
                if (d > bd || !GroundThreats.LineOfSight(muzzle.position, t)) continue;
                bd = d; best = t;
            }
            if (best != target) { target = best; rangeErr = Random.Range(-0.12f, 0.12f); shots = 0; }
        }
    }

    /// <summary>Kennzeichen fuer Collider der Kanone selbst (nicht der Sandsaecke).</summary>
    public class AtGunPart : MonoBehaviour { }

    /// <summary>Einfache Soldatenfigur (Koerper, Kopf mit Helm); faellt um, wenn sie getroffen wird.</summary>
    public class SoldierFigure : MonoBehaviour
    {
        bool fallen;
        public void Fall()
        {
            if (fallen) return;
            fallen = true;
            transform.localRotation *= Quaternion.Euler(Random.value < 0.5f ? 85f : -85f, Random.Range(-40f, 40f), 0f);
            transform.localPosition += Vector3.up * 0.15f;
        }
    }

    /// <summary>
    /// Infanterist mit Panzerfaust: liegt in Deckung, steht auf und schiesst, wenn ein feindlicher Panzer naeher als
    /// 90 m kommt (Hohlladung, 45 m/s, im Bogen). Zwei Panzerfaeuste. Stirbt durch MG, Sprenggranaten oder Ueberfahren.
    /// </summary>
    public class Soldier : MonoBehaviour, IShellTarget, ISplashTarget
    {
        public int team = 1;
        public bool hasFaust;
        public bool Dead { get; private set; }
        public Vector3 Position => transform.position + Vector3.up * 0.9f;
        int fausts = 2;
        float aimT, cool, searchT;
        TankController target;
        Transform body;
        static ShellData Faust => AmmoLibrary.All[67];
        public static int FaustShots;

        public static Soldier Spawn(Vector3 pos, float yaw, int team, bool faust, Transform parent)
        {
            var go = new GameObject(faust ? "Panzerfaust-Schuetze" : "Infanterist");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var s = go.AddComponent<Soldier>();
            s.team = team; s.hasFaust = faust;
            s.body = Figure(go.transform, Vector3.zero, 1f, faust);
            s.body.localRotation = Quaternion.Euler(-80f, 0f, 0f);   // liegt in Deckung
            s.body.localPosition = new Vector3(0f, 0.25f, 0f);
            var col = go.AddComponent<CapsuleCollider>(); col.center = new Vector3(0f, 0.9f, 0f); col.height = 1.8f; col.radius = 0.35f;
            SoftTargets.All.Add(s);
            return s;
        }

        /// <summary>Figur aus Grundkoerpern (ohne Collider). scale &lt; 1: kniend.</summary>
        public static Transform Figure(Transform parent, Vector3 lp, float scale, bool faust = false)
        {
            Color uniform = new Color(0.33f, 0.36f, 0.27f), helmet = new Color(0.26f, 0.29f, 0.22f), skin = new Color(0.78f, 0.62f, 0.5f);
            var root = new GameObject("Soldat").transform;
            root.SetParent(parent, false); root.localPosition = lp;
            root.gameObject.AddComponent<SoldierFigure>();
            GameObject P(PrimitiveType t, Vector3 p, Vector3 s, Color c)
            {
                var g = GameObject.CreatePrimitive(t);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(root, false); g.transform.localPosition = p; g.transform.localScale = s;
                g.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(c);
                return g;
            }
            float h = scale;
            P(PrimitiveType.Capsule, new Vector3(0f, 0.55f * h, 0f), new Vector3(0.22f, 0.45f * h, 0.22f), uniform);          // Beine
            P(PrimitiveType.Capsule, new Vector3(0f, 1.15f * h, 0f), new Vector3(0.42f, 0.35f * h, 0.3f), uniform);           // Oberkoerper
            P(PrimitiveType.Sphere, new Vector3(0f, 1.6f * h, 0f), new Vector3(0.22f, 0.24f, 0.22f), skin);                   // Kopf
            P(PrimitiveType.Sphere, new Vector3(0f, 1.68f * h, 0f), new Vector3(0.29f, 0.17f, 0.31f), helmet);                // Helm
            if (faust)
            {
                var tube = P(PrimitiveType.Cylinder, new Vector3(0.18f, 1.45f * h, 0.15f), new Vector3(0.05f, 0.45f, 0.05f), new Color(0.35f, 0.33f, 0.2f));
                tube.transform.localRotation = Quaternion.Euler(80f, 0f, 0f);
                P(PrimitiveType.Sphere, new Vector3(0.18f, 1.5f * h, 0.6f), new Vector3(0.15f, 0.15f, 0.22f), new Color(0.25f, 0.3f, 0.2f));   // Kopf der Panzerfaust
            }
            return root;
        }

        public void Hit(ShellData d, Vector3 vel, Vector3 pos, bool byPlayer) => Kill(byPlayer);
        public void Splash(float fillerKg, float distance, bool byPlayer)
        {
            float r = SoftTargets.Radius(fillerKg);
            bool lying = aimT <= 0f;   // liegend schwerer zu treffen
            if (distance < r * (lying ? 0.35f : 0.55f) || Random.value < (lying ? 0.35f : 0.6f) * (1f - distance / r)) Kill(byPlayer);
        }

        public void Kill(bool byPlayer)
        {
            if (Dead) return;
            Dead = true;
            SoftTargets.All.Remove(this);
            var c = GetComponent<Collider>(); if (c != null) c.enabled = false;
            if (body != null) { body.localRotation = Quaternion.Euler(Random.value < 0.5f ? 85f : -85f, Random.Range(0f, 360f), 0f); body.localPosition = new Vector3(0f, 0.2f, 0f); }
            GroundThreats.ReportKill(hasFaust ? "Panzerfaust-Schuetze" : "Infanterist", byPlayer);
            Destroy(gameObject, 45f);
        }

        void OnDestroy() => SoftTargets.All.Remove(this);

        void Update()
        {
            if (Dead) return;
            float dt = Time.deltaTime;
            searchT -= dt; cool -= dt;
            // ueberfahren?
            foreach (var t in GroundThreats.Tanks)
            {
                if (t == null) continue;
                Vector3 l = t.transform.InverseTransformPoint(transform.position);
                if (Mathf.Abs(l.x) < t.spec.hullWidth * 0.5f + 0.3f && Mathf.Abs(l.z) < t.spec.hullLength * 0.5f + 0.3f && Mathf.Abs(l.y) < 2.5f && t.SpeedKmh > 1f) { Kill(t.isPlayer); return; }
            }
            if (!hasFaust || fausts <= 0) return;
            if (searchT <= 0f)
            {
                searchT = 0.5f;
                target = null; float bd = 90f;
                foreach (var t in GroundThreats.Tanks)
                {
                    if (t == null || t.destroyed || t.team == team || t.team == TankController.TeamTarget) continue;
                    float d = Vector3.Distance(t.transform.position, transform.position);
                    if (d < bd && d > 12f && GroundThreats.LineOfSight(Position + Vector3.up * 0.6f, t)) { bd = d; target = t; }
                }
            }
            if (target == null) { aimT = 0f; if (body != null) { body.localRotation = Quaternion.Euler(-80f, 0f, 0f); body.localPosition = new Vector3(0f, 0.25f, 0f); } return; }
            // aufstehen (knien), zielen, schiessen
            Vector3 tp = target.transform.TransformPoint(0f, target.spec.clearance + target.spec.hullHeight * 0.5f, 0f);
            Vector3 to = tp - Position;
            transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
            if (body != null) { body.localRotation = Quaternion.identity; body.localPosition = Vector3.zero; }
            aimT += dt;
            if (aimT > 2.2f && cool <= 0f)
            {
                float d = new Vector2(to.x, to.z).magnitude, v = Faust.muzzleVelocity;
                float k = Mathf.Clamp(9.81f * d / (v * v), 0f, 0.99f);
                float ang = 0.5f * Mathf.Asin(k) * Mathf.Rad2Deg + Mathf.Atan2(to.y, d) * Mathf.Rad2Deg + Random.Range(-1.2f, 1.2f);
                Vector3 dir = Quaternion.Euler(-ang, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + Random.Range(-1.5f, 1.5f), 0f) * Vector3.forward;
                Vector3 from = transform.position + Vector3.up * 1.4f + dir * 0.8f;
                var s = Shell.Fire(Faust, from, dir, false);
                if (s != null) s.noTracer = true;
                if (!NetSync.Headless) { Effects.SmokePuff(transform.position + Vector3.up * 1.4f - dir * 1.5f, 1.5f, 3f, 6); Sfx.Cannon(from, 40f); }
                fausts--; FaustShots++; cool = 12f; aimT = 0f;
                RangeLog.AddLocal($"Panzerfaust abgefeuert ({d:0} m)!");
            }
        }
    }

    /// <summary>Minenfeld: versteckte Panzerminen, am Rand Warnschilder. Eine Mine unter der Kette reisst sie ab und beschaedigt die Wanne.</summary>
    public class Minefield : MonoBehaviour
    {
        readonly List<Vector3> mines = new List<Vector3>();
        public int Remaining => mines.Count;
        public Vector3 FirstMine => mines.Count > 0 ? mines[0] : transform.position;
        public static int Detonations;

        public static Minefield Spawn(GameMap map, Vector3 center, float yaw, Vector2 size, int count, Transform parent, bool signs = true)
        {
            var go = new GameObject("Minenfeld");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
            var mf = go.AddComponent<Minefield>();
            for (int i = 0; i < count; i++)
            {
                Vector3 l = new Vector3(Random.Range(-size.x, size.x) * 0.5f, 0f, Random.Range(-size.y, size.y) * 0.5f);
                Vector3 w = go.transform.TransformPoint(l);
                w.y = map.GroundY(w.x, w.z);
                mf.mines.Add(w);
                if (!NetSync.Headless)
                {
                    // kaum sichtbar: flacher Deckel, halb im Boden
                    var lid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(lid.GetComponent<Collider>());
                    lid.name = "Mine"; lid.transform.SetParent(go.transform, true);
                    lid.transform.position = w + Vector3.up * 0.02f; lid.transform.localScale = new Vector3(0.32f, 0.03f, 0.32f);
                    lid.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.36f, 0.34f, 0.25f));
                }
            }
            if (signs && !NetSync.Headless)
                for (int s = 0; s < 4; s++)
                {
                    Vector3 l = new Vector3((s % 2 == 0 ? -1 : 1) * (size.x * 0.5f + 4f), 0f, (s < 2 ? -1 : 1) * (size.y * 0.5f + 4f));
                    Vector3 w = go.transform.TransformPoint(l); w.y = map.GroundY(w.x, w.z);
                    Sign(go.transform, w, yaw);
                }
            return mf;
        }

        static void Sign(Transform parent, Vector3 pos, float yaw)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(post.GetComponent<Collider>());
            post.transform.SetParent(parent, true);
            post.transform.position = pos + Vector3.up * 0.6f; post.transform.localScale = new Vector3(0.08f, 1.2f, 0.08f);
            post.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.35f, 0.25f, 0.15f));
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(board.GetComponent<Collider>());
            board.transform.SetParent(parent, true);
            board.transform.position = pos + Vector3.up * 1.15f;
            board.transform.rotation = Quaternion.Euler(0f, yaw, 45f);   // Raute
            board.transform.localScale = new Vector3(0.45f, 0.45f, 0.03f);
            board.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.85f, 0.75f, 0.15f));
            var skull = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // schwarzer Punkt: "Achtung Minen"
            Destroy(skull.GetComponent<Collider>());
            skull.transform.SetParent(board.transform, false);
            skull.transform.localPosition = new Vector3(0f, 0f, -0.6f); skull.transform.localScale = new Vector3(0.35f, 0.35f, 0.4f);
            skull.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.05f, 0.05f, 0.05f));
        }

        void Update()
        {
            if (mines.Count == 0 || !NetSync.MayApplyDamage) return;
            foreach (var t in GroundThreats.Tanks)
            {
                if (t == null || t.destroyed) continue;
                Vector3 c = t.transform.position;
                float reach = t.spec.hullLength * 0.6f + 1f;
                for (int i = mines.Count - 1; i >= 0; i--)
                {
                    Vector3 m = mines[i];
                    if ((m - c).sqrMagnitude > reach * reach) continue;
                    Vector3 l = t.transform.InverseTransformPoint(m);
                    float half = t.spec.hullWidth * 0.5f;
                    // nur unter einer Kette (aussen, etwa eine Kettenbreite)
                    float tw = Mathf.Max(t.spec.trackShoeWidth, 0.45f);
                    if (Mathf.Abs(l.z) > t.spec.hullLength * 0.5f || Mathf.Abs(l.x) > half + 0.1f || Mathf.Abs(l.x) < half - tw - 0.1f) continue;
                    mines.RemoveAt(i);
                    Detonate(t, m);
                }
            }
        }

        void Detonate(TankController t, Vector3 m)
        {
            Detonations++;
            if (!NetSync.Headless) { Effects.Explosion(m, 6f, true); Effects.Crater(m, Vector3.up, 1.2f, null); }
            string msg = t.DamageTrack(m, 400f, m + Vector3.down * 0.5f, true);
            // Druckwelle unter der Wanne: kleine Splitter in den Fahrerraum (wie eine nahe Sprenggranate)
            Shell.Fire(AmmoLibrary.All[22], m + Vector3.down * 0.2f, Vector3.up, false, 30f);
            RangeLog.Add($"MINE unter {t.DisplayName}! {msg}");
            if (t.isPlayer) TankCamera.Notice("MINE!  Kette abgerissen  -  G: reparieren", true);
            SoftTargets.Splash(m, 6f, false);
        }
    }

    /// <summary>
    /// Bodenabwehr (Einzelspieler, Menue-Schalter): Pak-Stellungen, Panzerfaust-Trupps und Minenfelder auf der Karte,
    /// zwischen deinem Startplatz und der Gegenseite. Ausgeschaltete Pak/Infanterie kommen nicht wieder (N: neu).
    /// </summary>
    public static class GroundThreats
    {
        public static bool On { get; private set; }
        public static int Kills { get; private set; }
        public static readonly List<TankController> Tanks = new List<TankController>();
        static readonly List<AtGun> guns = new List<AtGun>();
        static readonly List<Soldier> soldiers = new List<Soldier>();
        static readonly List<Minefield> fields = new List<Minefield>();
        static Transform root;
        static Runner runner;
        static float scanT;
        public static IEnumerable<AtGun> Guns => guns;
        public static IEnumerable<Soldier> Soldiers => soldiers;
        public static IEnumerable<Minefield> Fields => fields;

        public static void Begin(GameMap map, Vector2 start, float yaw, int enemyTeam)
        {
            Stop();
            On = true; Kills = 0;
            root = new GameObject("Bodenabwehr").transform;
            if (runner == null) { var go = new GameObject("Bodenabwehr-Ablauf"); Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); }
            Vector2 fw = ModeKit.Dir(yaw), side = new Vector2(fw.y, -fw.x);
            float lim = map.Extent - 100f;
            Vector2 Clamp(Vector2 p) => new Vector2(Mathf.Clamp(p.x, -lim, lim), Mathf.Clamp(p.y, -lim, lim));
            // Pak: 3 Stellungen 700-1200 m voraus, Front zu dir
            for (int i = 0; i < 3; i++)
            {
                Vector2 c = Clamp(start + fw * Random.Range(700f, 1200f) + side * ((i - 1) * Random.Range(250f, 400f)));
                var p = ModeKit.FreeSpot(map, c, 80f);
                guns.Add(AtGun.Spawn(map, p - Vector3.up * 0.5f, ModeKit.Yaw(p, new Vector3(start.x, p.y, start.y)), enemyTeam, root));
            }
            // Infanterie: 4 Trupps (je 3 Mann, 1-2 mit Panzerfaust) 250-700 m voraus, gern an Waldrand/Hecken
            for (int i = 0; i < 4; i++)
            {
                Vector2 c = Clamp(start + fw * Random.Range(250f, 700f) + side * Random.Range(-450f, 450f));
                float ty = ModeKit.Yaw(new Vector3(c.x, 0f, c.y), new Vector3(start.x, 0f, start.y));
                for (int k = 0; k < 3; k++)
                {
                    Vector2 q = c + Random.insideUnitCircle * 9f;
                    var pos = new Vector3(q.x, map.GroundY(q.x, q.y), q.y);
                    soldiers.Add(Soldier.Spawn(pos, ty + Random.Range(-20f, 20f), enemyTeam, k < 2, root));
                }
            }
            // Minen: 3 Felder (60 x 30 m) quer zur Anfahrt, 300-800 m voraus
            for (int i = 0; i < 3; i++)
            {
                Vector2 c = Clamp(start + fw * Random.Range(300f, 800f) + side * ((i - 1) * Random.Range(200f, 350f)));
                fields.Add(Minefield.Spawn(map, new Vector3(c.x, map.GroundY(c.x, c.y), c.y), yaw + 90f, new Vector2(60f, 30f), 14, root));
            }
            RangeLog.AddLocal("BODENABWEHR: Pak-Stellungen, Panzerfaust-Trupps und Minenfelder (gelbe Rautenschilder) liegen vor dir.");
        }

        public static void Stop()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            guns.Clear(); soldiers.Clear(); fields.Clear(); Tanks.Clear();
            SoftTargets.All.RemoveAll(t => t is AtGun || t is Soldier);
            On = false;
        }

        public static void ReportKill(string what, bool byPlayer)
        {
            Kills++;
            RangeLog.AddLocal($"{what} ausgeschaltet{(byPlayer ? " (von dir)" : "")}");
            if (byPlayer) TankCamera.Notice($"{what.ToUpperInvariant()} AUSGESCHALTET", true);
        }

        /// <summary>Freie Sicht von einem Punkt auf einen Panzer (Gelaende, Haeuser, Waelder verdecken).</summary>
        public static bool LineOfSight(Vector3 from, TankController t)
        {
            Vector3 to = t.transform.TransformPoint(0f, t.spec.clearance + t.spec.hullHeight * 0.7f, 0f);
            if (Smoke.Hides(from, to)) return false;   // Nebel verdeckt
            Vector3 d = to - from;
            if (!Physics.Raycast(from, d.normalized, out RaycastHit hit, d.magnitude, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore)) return true;
            if (hit.distance > d.magnitude - 5f) return true;
            var tc = hit.collider.GetComponentInParent<TankController>();
            if (tc == t) return true;
            var plate = hit.collider.GetComponentInParent<ArmorPlate>();
            return plate != null && plate.Tank == t;
        }

        class Runner : MonoBehaviour
        {
            void Update()
            {
                if (!On) return;
                scanT -= Time.deltaTime;
                if (scanT <= 0f) { scanT = 0.3f; Tanks.Clear(); Tanks.AddRange(Object.FindObjectsByType<TankController>(FindObjectsSortMode.None)); }
            }
            void OnGUI()
            {
                if (!On || NetSync.Headless) return;
                var gm = GameManager.Instance;
                if (gm == null || !gm.InGame || SpawnSelect.IsOpen || TeamHud.MapOpen) return;
                int g = 0, s = 0; foreach (var x in guns) if (x != null && !x.Dead) g++; foreach (var x in soldiers) if (x != null && !x.Dead && x.hasFaust) s++;
                float k = GameSettings.UiFactor;
                var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                string txt = $"Bodenabwehr: Pak {g}   Panzerfaeuste {s}   ausgeschaltet {Kills}";
                st.normal.textColor = Color.black; GUI.Label(new Rect(Screen.width - 401f * k, 245f * k, 380f * k, 22f * k), txt, st);
                st.normal.textColor = new Color(1f, 0.85f, 0.45f); GUI.Label(new Rect(Screen.width - 402f * k, 244f * k, 380f * k, 22f * k), txt, st);
            }
        }

        public static string BenchText
        {
            get
            {
                int g = 0, s = 0, m = 0; foreach (var x in guns) if (x != null && !x.Dead) g++; foreach (var x in soldiers) if (x != null && !x.Dead) s++; foreach (var f in fields) if (f != null) m += f.Remaining;
                return $"Pak {g}/{guns.Count}, Soldaten {s}/{soldiers.Count}, Minen {m}, Detonationen {Minefield.Detonations}, Panzerfaust-Schuesse {Soldier.FaustShots}, ausgeschaltet {Kills}";
            }
        }
    }
}
