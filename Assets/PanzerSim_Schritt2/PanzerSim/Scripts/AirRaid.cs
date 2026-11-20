using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Ein KI-Flugzeug als Luftziel. Fliegt Angriffe auf Panzer des anderen Teams: anfliegen, im flachen Sturz mit der
    /// 20-mm-Kanone schiessen und eine 250-kg-Bombe werfen, abdrehen, steigen, von vorn. Granaten treffen es (Collider
    /// auf der normalen Ebene, siehe Shell.cs); Schaden laesst es rauchen, bei 0 stuerzt es brennend ab und zerschellt.
    /// Flug: vereinfacht (Richtung dreht mit begrenzter Rate, Querneigung aus der Kurve, Mindesthoehe ueber Grund).
    /// </summary>
    public class AirTarget : MonoBehaviour, IShellTarget
    {
        enum State { Approach, Attack, Egress, Falling }
        State state;

        public int team = 1;
        public float hp = 100f;
        public bool Down => state == State.Falling || dead;
        public Vector3 Velocity => fwd * speed;
        public string Name = "Jagdbomber (KI)";

        GameMap map;
        Vector3 fwd;
        float speed = 165f, roll, spin, stateT, gunT, burstT, smokeT, lastYaw;
        int bombs = 2, rounds = 300;
        bool dead;
        TankController target;
        Transform visual;

        const float TurnRate = 28f, MinAgl = 70f, CruiseAgl = 480f;
        static ShellData Bomb => AmmoLibrary.All[66];
        static ShellData GunHE => AmmoLibrary.All[59];
        static ShellData GunAP => AmmoLibrary.All[60];

        public static readonly List<AirTarget> All = new List<AirTarget>();

        public static AirTarget Spawn(GameMap map, Vector3 pos, Vector3 dir, int team)
        {
            var go = new GameObject("Flugzeug (KI)");
            go.transform.position = pos;
            var a = go.AddComponent<AirTarget>();
            a.map = map; a.team = team;
            dir.y = 0f; a.fwd = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            a.lastYaw = Mathf.Atan2(a.fwd.x, a.fwd.z) * Mathf.Rad2Deg;
            a.Build();
            return a;
        }

        void Build()
        {
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
            // Trefferkisten (normale Ebene: Granaten treffen sie) - etwas groesser als das Modell, wie die Silhouette
            Box(Vector3.zero, new Vector3(1.8f, 1.9f, 13f));
            Box(new Vector3(0f, 0.6f, -0.7f), new Vector3(9.5f, 0.4f, 2.8f));
            Box(new Vector3(0f, 1.6f, -5.0f), new Vector3(0.4f, 2.6f, 2.6f));
            visual = new GameObject("Aussehen").transform;
            visual.SetParent(transform, false);
            var prefab = Resources.Load<GameObject>("Modelle/JetPlatzhalter");
            if (prefab != null)
            {
                var m = Instantiate(prefab, visual, false);
                foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
                Visuals.MatteTank(m.GetComponentsInChildren<Renderer>());
            }
            All.Add(this);
        }

        void Box(Vector3 c, Vector3 size)
        {
            var g = new GameObject("Trefferzone");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = c;
            g.AddComponent<BoxCollider>().size = size;
        }

        void OnDestroy() => All.Remove(this);

        float Agl(Vector3 p) => p.y - map.GroundY(p.x, p.z);

        /// <summary>Granate trifft (aufgerufen von Shell, nur wo Schaden zaehlt).</summary>
        public void Hit(ShellData d, Vector3 vel, Vector3 pos, bool byPlayer)
        {
            if (Down) return;
            float dmg = d.HasFiller ? 22f + d.fillerKg * 420f : 8f + d.caliberMm * 0.9f * Mathf.Clamp(vel.magnitude / 900f, 0.4f, 1.4f);
            hp -= dmg;
            if (hp <= 0f)
            {
                state = State.Falling; stateT = 0f;
                spin = Random.Range(120f, 260f) * (Random.value < 0.5f ? -1f : 1f);
                AirRaid.ReportDown(this, byPlayer);
                if (!NetSync.Headless) Effects.Explosion(pos, 3f, false);
            }
        }

        void Update()
        {
            if (dead || map == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            stateT += dt;
            Vector3 pos = transform.position;

            if (state == State.Falling) { Fall(dt, pos); return; }

            if (target == null || target.destroyed || stateT > 70f) { target = AirRaid.PickTarget(this); if (state != State.Egress) { state = State.Approach; stateT = 0f; } }
            Vector3 goal;
            float turn = TurnRate;
            if (target == null) goal = pos + fwd * 1000f + Vector3.up * (CruiseAgl - Agl(pos));
            else
            {
                Vector3 tp = target.transform.position;
                Vector3 flat = tp - pos; flat.y = 0f;
                float hd = flat.magnitude;
                switch (state)
                {
                    case State.Approach:
                        goal = tp + Vector3.up * CruiseAgl;
                        if (hd < 2600f && Vector3.Angle(new Vector3(fwd.x, 0f, fwd.z), flat) < 35f) { state = State.Attack; stateT = 0f; burstT = 0f; }
                        break;
                    case State.Attack:
                    {
                        goal = tp + Vector3.up * 3f;
                        turn = TurnRate * 0.8f;
                        Vector3 to = tp - pos;
                        float ang = Vector3.Angle(fwd, to);
                        // Kanone: kurze Feuerstoesse zwischen 1300 und 400 m
                        burstT -= dt; gunT -= dt;
                        if (to.magnitude < 1300f && to.magnitude > 400f && ang < 6f && rounds > 0)
                        {
                            if (burstT < -1.6f) burstT = 1.1f;
                            if (burstT > 0f) while (gunT <= 0f && rounds > 0) { FireGun(to); gunT += 60f / 750f; }
                        }
                        if (gunT < 0f) gunT = 0f;
                        // Bombe: Wurfweite aus Hoehe und Fahrt
                        float agl = Agl(pos);
                        float fall = Mathf.Sqrt(2f * Mathf.Max(agl, 1f) / 9.81f);
                        if (bombs > 0 && agl > 35f && hd < speed * fall * 1.02f && hd > speed * fall * 0.6f && ang < 25f) DropBomb();
                        if (hd < 300f || Vector3.Dot(flat, fwd) < 0f || stateT > 35f) { state = State.Egress; stateT = 0f; }
                        break;
                    }
                    default:   // Egress: weiterfliegen und steigen, dann neuer Anflug
                        goal = pos + new Vector3(fwd.x, 0f, fwd.z).normalized * 2000f + Vector3.up * (CruiseAgl + 150f - Agl(pos));
                        if (stateT > 14f) { state = State.Approach; stateT = 0f; bombs = Mathf.Max(bombs, 1); }
                        break;
                }
            }
            // Kartenrand: zurueck zur Mitte
            float E = map.Extent;
            if (Mathf.Abs(pos.x) > E + 1500f || Mathf.Abs(pos.z) > E + 1500f) goal = new Vector3(0f, pos.y, 0f);

            Vector3 want = (goal - pos).normalized;
            // nie unter die Mindesthoehe (Gelaende voraus pruefen)
            Vector3 ahead = pos + fwd * speed * 3f;
            float floor = Mathf.Max(map.GroundY(ahead.x, ahead.z), map.GroundY(pos.x, pos.z)) + MinAgl;
            if (pos.y + want.y * speed * 3f < floor) want.y = Mathf.Max(want.y, Mathf.Clamp((floor - pos.y) / (speed * 3f), 0.05f, 0.6f));
            want.y = Mathf.Clamp(want.y, -0.45f, 0.5f);
            fwd = Vector3.RotateTowards(fwd, want.normalized, turn * Mathf.Deg2Rad * dt, 0f).normalized;
            Move(dt, pos);
            if (hp < 55f) Trail(dt, hp < 25f ? 0.25f : 0.55f);
        }

        void Move(float dt, Vector3 pos)
        {
            pos += fwd * speed * dt;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt; lastYaw = yaw;
            roll = Mathf.Lerp(roll, Mathf.Clamp(-yawRate * 2.6f, -75f, 75f), 1f - Mathf.Exp(-dt * 3f));
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, roll));
        }

        void Fall(float dt, Vector3 pos)
        {
            // brennend: Nase senkt sich, Flugzeug trudelt
            Vector3 down = (new Vector3(fwd.x, 0f, fwd.z).normalized + Vector3.down * 1.4f).normalized;
            fwd = Vector3.RotateTowards(fwd, down, 22f * Mathf.Deg2Rad * dt, 0f).normalized;
            speed = Mathf.Min(speed + 6f * dt, 230f);
            pos += fwd * speed * dt;
            roll += spin * dt;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, roll));
            Trail(dt, 0.15f, true);
            if (Agl(pos) < 2f || stateT > 40f) Crash(pos);
        }

        void Crash(Vector3 pos)
        {
            dead = true;
            pos.y = map.GroundY(pos.x, pos.z);
            if (!NetSync.Headless)
            {
                Effects.Explosion(pos, 60f, true);
                Effects.Crater(pos, Vector3.up, 5f, null);
                // Wrackteile: brennender Haufen
                var wreck = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(wreck.GetComponent<Collider>());
                wreck.name = "Flugzeugwrack";
                wreck.transform.position = pos + Vector3.up * 0.4f;
                wreck.transform.rotation = Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f));
                wreck.transform.localScale = new Vector3(2.5f, 0.8f, 6f);
                wreck.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.08f, 0.08f, 0.08f));
                Effects.Burn(wreck.transform, Vector3.up * 0.5f, 45f, 1.2f);
                Destroy(wreck, 90f);
            }
            AirRaid.ReportCrash(this, pos);
            Destroy(gameObject);
        }

        void Trail(float dt, float interval, bool fire = false)
        {
            if (NetSync.Headless) return;
            smokeT -= dt;
            if (smokeT > 0f) return;
            smokeT = interval * 0.25f;
            Vector3 p = transform.position - fwd * 6f;
            Effects.SmokeOne(p, -fwd * 8f + Vector3.up * 2f, fire ? 4.5f : 3f, fire ? 7f : 4f, fire ? 0.12f : 0.35f, 0.8f);
        }

        void FireGun(Vector3 to)
        {
            rounds--;
            Vector3 origin = transform.position + fwd * 8f - transform.up * 0.4f;
            Vector3 aim = Vector3.Slerp(fwd, to.normalized, 0.6f);
            aim = Quaternion.Euler(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), 0f) * aim;
            var s = Shell.Fire(rounds % 3 == 0 ? GunAP : GunHE, origin, aim, false, 1000f + speed);
            if (!NetSync.Headless) { try { Effects.Muzzle(origin, fwd, 20f, false); Sfx.MG(origin, 20f); } catch (System.Exception e) { Debug.LogException(e); } }
            _ = s;
        }

        void DropBomb()
        {
            bombs--;
            Vector3 p = transform.position - transform.up * 1.5f;
            var s = Shell.Fire(Bomb, p, fwd, false, speed);
            if (s != null) s.noTracer = true;
            AirRaid.BombsDropped++;
        }

        public string DebugText => $"{state} hp {hp:0} Hoehe {Agl(transform.position):0} m, Ziel {(target != null ? target.spec.name : "-")} {(target != null ? Vector3.Distance(target.transform.position, transform.position) : 0f):0} m";
    }

    /// <summary>
    /// Luftangriffe (Einzelspieler, Menue-Schalter): immer ein paar KI-Flugzeuge in der Luft, die Panzer deines Teams angreifen.
    /// Abgeschossene kommen nach 35 s neu. Fuer Flakpanzer (Gepard) zeigt eine Vorhaltemarke, wohin man zielen muss.
    /// </summary>
    public static class AirRaid
    {
        public static bool On { get; private set; }
        public static int Planes = 2;
        public static int Kills { get; private set; }
        public static int BombsDropped;
        static GameMap map;
        static System.Func<TankController> getPlayer;
        static readonly List<float> respawn = new List<float>();
        static Runner runner;

        public static void Begin(GameMap m, System.Func<TankController> player, int planes = 2)
        {
            Stop();
            map = m; getPlayer = player; Planes = planes;
            On = true; Kills = 0; BombsDropped = 0;
            respawn.Clear();
            for (int i = 0; i < Planes; i++) respawn.Add(4f + i * 9f);
            if (runner == null) { var go = new GameObject("Luftangriffe"); Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); }
            RangeLog.AddLocal("LUFTANGRIFFE: feindliche Jagdbomber greifen an. Flakpanzer: auf die Vorhaltemarke zielen!");
        }

        public static void Stop()
        {
            foreach (var a in AirTarget.All.ToArray()) if (a != null) Object.Destroy(a.gameObject);
            AirTarget.All.Clear();
            respawn.Clear();
            On = false;
        }

        static int EnemyTeam { get { var p = getPlayer?.Invoke(); return p != null && p.team == 1 ? 0 : 1; } }

        public static TankController PickTarget(AirTarget a)
        {
            TankController best = null; float bd = float.MaxValue;
            foreach (var t in Object.FindObjectsByType<TankController>(FindObjectsSortMode.None))
            {
                if (t.destroyed || t.team == a.team || t.team == TankController.TeamTarget) continue;
                float d = Vector3.Distance(t.transform.position, a.transform.position) * Random.Range(0.8f, 1.2f);
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        public static void ReportDown(AirTarget a, bool byPlayer)
        {
            Kills++;
            RangeLog.AddLocal($"*** {a.Name} ABGESCHOSSEN{(byPlayer ? " (von dir)" : "")} ***");
            TankCamera.Notice($"FLUGZEUG ABGESCHOSSEN  ({Kills})", byPlayer);
            if (byPlayer) PlayerStats.OnAirKill();
        }

        public static void ReportCrash(AirTarget a, Vector3 pos) { if (On) respawn.Add(35f); }

        static void Tick(float dt)
        {
            if (!On || map == null) return;
            var pl = getPlayer?.Invoke();
            for (int i = respawn.Count - 1; i >= 0; i--)
            {
                respawn[i] -= dt;
                if (respawn[i] > 0f) continue;
                respawn.RemoveAt(i);
                Vector3 c = pl != null ? pl.transform.position : Vector3.zero;
                float ang = Random.Range(0f, 360f);
                Vector3 p = c + new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad)) * Random.Range(3500f, 4500f);
                p.y = map.GroundY(Mathf.Clamp(p.x, -map.Extent, map.Extent), Mathf.Clamp(p.z, -map.Extent, map.Extent)) + 550f;
                p.y = Mathf.Max(p.y, c.y + 450f);
                AirTarget.Spawn(map, p, c - p, EnemyTeam);
                RangeLog.AddLocal("Feindliches Flugzeug im Anflug!");
            }
        }

        // ---------------- Anzeige: Markierung, Entfernung, Vorhalt fuer Flak ----------------
        static GUIStyle lab;
        static Texture2D ring;
        static void Draw()
        {
            var gm = GameManager.Instance;
            var cam = Camera.main;
            if (gm == null || !gm.InGame || cam == null || SpawnSelect.IsOpen || TeamHud.MapOpen || ArtilleryMode.Active) return;
            float k = GameSettings.UiFactor;
            if (lab == null) { lab = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold }; lab.normal.textColor = Color.white; }
            if (ring == null) ring = MakeRing(64);
            lab.fontSize = Mathf.RoundToInt(13 * k);
            var pl = gm.Player;
            bool flak = pl != null && !pl.destroyed && pl.AutoGun;
            foreach (var a in AirTarget.All)
            {
                if (a == null) continue;
                Vector3 p = a.transform.position;
                float d = Vector3.Distance(cam.transform.position, p);
                if (d > 6000f) continue;
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f) continue;
                float y = Screen.height - sp.y, s = 18f * k;
                Color col = a.Down ? new Color(0.6f, 0.6f, 0.6f) : new Color(1f, 0.35f, 0.3f);
                GUI.color = col;
                GUI.DrawTexture(new Rect(sp.x - s / 2f, y - s / 2f, s, s), ring);
                GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(sp.x - 99f * k, y + s / 2f + 1, 200f * k, 20f * k), $"{(a.Down ? "abgeschossen" : a.Name)}  {d / 1000f:0.0} km", lab);
                GUI.color = col; GUI.Label(new Rect(sp.x - 100f * k, y + s / 2f, 200f * k, 20f * k), $"{(a.Down ? "abgeschossen" : a.Name)}  {d / 1000f:0.0} km", lab);
                // Vorhaltemarke (Feuerleitrechner des Flakpanzers): wo das Flugzeug ist, wenn die Granate ankommt
                if (flak && !a.Down && d < 4000f)
                {
                    Vector3 lead = LeadPoint(pl, a);
                    Vector3 lp = cam.WorldToScreenPoint(lead);
                    if (lp.z > 0f)
                    {
                        float ly = Screen.height - lp.y, ls = 12f * k;
                        GUI.color = new Color(0.3f, 1f, 0.4f, 0.95f);
                        GUI.DrawTexture(new Rect(lp.x - ls / 2f, ly - ls / 2f, ls, ls), ring);
                        GUI.DrawTexture(new Rect(lp.x - 1.5f * k, ly - 1.5f * k, 3f * k, 3f * k), Texture2D.whiteTexture);
                    }
                }
            }
            GUI.color = Color.white;
            if (On)
            {
                int air = 0; foreach (var a in AirTarget.All) if (a != null && !a.Down) air++;
                GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.Label(new Rect(Screen.width - 401f * k, 222f * k, 380f * k, 22f * k), $"Flugzeuge in der Luft: {air}   Abgeschossen: {Kills}", lab);
                GUI.color = new Color(1f, 0.75f, 0.5f); GUI.Label(new Rect(Screen.width - 402f * k, 221f * k, 380f * k, 22f * k), $"Flugzeuge in der Luft: {air}   Abgeschossen: {Kills}", lab);
                GUI.color = Color.white;
            }
        }

        /// <summary>Vorhaltepunkt: Flugzeit der Granate iterativ, Flugzeug fliegt geradeaus weiter.</summary>
        public static Vector3 LeadPoint(TankController shooter, AirTarget a)
        {
            Vector3 from = shooter.parts.muzzle != null ? shooter.parts.muzzle.position : shooter.transform.position + Vector3.up * 3f;
            Vector3 p = a.transform.position;
            float t = 0f;
            for (int i = 0; i < 4; i++)
            {
                float d = Vector3.Distance(from, p);
                Ballistics.SimulateFlat(shooter.Ammo, Mathf.Max(d, 10f), out _, out _, out t);
                p = a.transform.position + a.Velocity * t;
            }
            return p + Vector3.up * (0.5f * 9.81f * t * t);   // Fall der Granate ausgleichen
        }

        static Texture2D MakeRing(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.Abs(d - c * 0.85f) / (n * 0.06f))));
                }
            t.Apply();
            return t;
        }

        /// <summary>Flugzeuge auf Minikarte und grosser Karte.</summary>
        public static void DrawOnMap(System.Func<Vector3, Vector2> toMap, Rect r, float k, Texture2D dot)
        {
            foreach (var a in AirTarget.All)
            {
                if (a == null || a.Down) continue;
                Vector2 q = toMap(a.transform.position);
                if (!r.Contains(q)) continue;
                float s = 9f * k;
                GUI.color = new Color(1f, 0.35f, 0.3f);
                GUI.DrawTexture(new Rect(q.x - s / 2f, q.y - s / 2f, s, s), dot);
                Vector3 v = a.Velocity.normalized;
                for (int i = 1; i <= 3; i++) { Vector2 w = q + new Vector2(v.x, -v.z) * i * 4f * k; GUI.DrawTexture(new Rect(w.x - 1.5f * k, w.y - 1.5f * k, 3f * k, 3f * k), dot); }
            }
            GUI.color = Color.white;
        }

        public static string BenchText
        {
            get
            {
                string s = $"Abschuesse {Kills}, Bomben {BombsDropped}, in der Luft {AirTarget.All.Count}";
                foreach (var a in AirTarget.All) if (a != null) s += $" [{a.DebugText}]";
                return s;
            }
        }

        class Runner : MonoBehaviour
        {
            void Update() => Tick(Time.deltaTime);
            void OnGUI() { if (!NetSync.Headless) Draw(); }
        }
    }
}
