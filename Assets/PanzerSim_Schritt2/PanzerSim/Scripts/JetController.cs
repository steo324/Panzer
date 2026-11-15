using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Testflug: leichter zweistrahliger Jet (Werte ungefaehr wie ein Trainer/Leichtkampfflugzeug: 5 t, 2 x 13 kN Schub, 17,5 m2 Fluegel).
    /// Steuerung: Shift/Strg Schub, W/S Nase hoch/runter, A/D rollen, Q/E Seitenruder, Leertaste Bombe, linke Maustaste 20-mm-Kanone,
    /// R nachladen (Test). Ein rotes X am Boden zeigt, wo die Bombe aufschlaegt (gleiche Flugbahnrechnung wie die Bombe selbst).
    /// Aussehen: eigener Platzhalter (Modelle/JetPlatzhalter); liegt auf diesem Rechner ein eigenes Modell unter
    /// %USERPROFILE%/AppData/LocalLow/.../LokaleModelle/Jet.glb, wird stattdessen das geladen (nur lokal, nie im Spiel-Download).
    /// </summary>
    public class JetController : MonoBehaviour
    {
        public static JetController Active;

        // ---- Flugzeug (ungefaehr) ----
        const float Mass = 5000f, WingArea = 17.5f, MaxThrust = 26400f;
        const float Cd0 = 0.034f, InducedK = 0.08f, ClAlpha = 0.085f, Cl0 = 0.15f, ClMax = 1.45f, StallDeg = 16f;
        const float PitchRate = 55f, RollRate = 160f, YawRate = 22f;   // Grad/s bei voller Ruderwirkung
        const int BombLoad = 4, GunLoad = 400;
        const float GunRpm = 900f, GunMuzzle = 1000f;

        Rigidbody rb;
        Transform visual, xMark;
        GameObject placeholder;
        float throttle = 0.75f, gunCooldown;
        int bombs = BombLoad, rounds = GunLoad, gunAlt, bombSide = 1;
        bool crashed;
        Vector3 impact; bool impactValid; float impactTime;
        float pitchIn, rollIn, yawIn;

        static ShellData Bomb => AmmoLibrary.All[58];
        static ShellData GunHE => AmmoLibrary.All[59];
        static ShellData GunAP => AmmoLibrary.All[60];

        public bool Crashed => crashed;
        public float SpeedKmh => rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
        public float AltitudeAgl => GameManager.Instance != null && GameManager.Instance.CurrentMap != null
            ? transform.position.y - GameManager.Instance.CurrentMap.GroundY(transform.position.x, transform.position.z) : transform.position.y;

        /// <summary>Jet in der Luft ueber dem Startpunkt der Karte erzeugen (600 m ueber Grund, 540 km/h).</summary>
        public static JetController Spawn(GameMap map)
        {
            Vector2 p = map.PlayerXZ;
            float yaw = map.PlayerYaw;
            var go = new GameObject("Jet (Testflug)");
            go.transform.SetPositionAndRotation(new Vector3(p.x, map.GroundY(p.x, p.y) + 600f, p.y), Quaternion.Euler(0f, yaw, 0f));
            var jc = go.AddComponent<JetController>();
            jc.Build();
            jc.rb.linearVelocity = go.transform.forward * 150f;
            Active = jc;
            return jc;
        }

        void Build()
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = Mass;
            rb.useGravity = true;
            rb.linearDamping = 0f; rb.angularDamping = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Kisten fuer Zusammenstoesse (Granaten ignorieren diese Ebene: die eigenen Geschosse treffen den Jet nie)
            Box(new Vector3(0f, 0f, 0f), new Vector3(1.6f, 1.7f, 12.8f));
            Box(new Vector3(0f, 0.6f, -0.7f), new Vector3(9.0f, 0.3f, 2.6f));
            Box(new Vector3(0f, 1.6f, -5.0f), new Vector3(0.3f, 2.4f, 2.4f));
            rb.inertiaTensor = new Vector3(30000f, 38000f, 12000f);
            rb.inertiaTensorRotation = Quaternion.identity;

            visual = new GameObject("Aussehen").transform;
            visual.SetParent(transform, false);
            var prefab = Resources.Load<GameObject>("Modelle/JetPlatzhalter");
            if (prefab != null)
            {
                placeholder = Instantiate(prefab, visual, false);
                Visuals.MatteTank(placeholder.GetComponentsInChildren<Renderer>());
            }
            TryLocalModel();

            // rotes X fuer den Bombeneinschlag
            xMark = new GameObject("Bomben-X").transform;
            var xm = GameMap.NewMat(new Color(1f, 0.12f, 0.05f));
            for (int i = 0; i < 2; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(bar.GetComponent<Collider>());
                bar.transform.SetParent(xMark, false);
                bar.transform.localScale = new Vector3(16f, 0.25f, 1.6f);
                bar.transform.localRotation = Quaternion.Euler(0f, i == 0 ? 45f : -45f, 0f);
                var r = bar.GetComponent<Renderer>(); r.sharedMaterial = xm; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            xMark.gameObject.SetActive(false);
        }

        void Box(Vector3 c, Vector3 size)
        {
            var g = new GameObject("Kollision");
            g.layer = ArmorPlate.PhysicsOnlyLayer;
            g.transform.SetParent(transform, false);
            g.transform.localPosition = c;
            var b = g.AddComponent<BoxCollider>(); b.size = size;
        }

        /// <summary>Pfad fuer ein eigenes Modell auf diesem Rechner (wird nie mit dem Spiel verteilt).</summary>
        public static string LocalModelPath => System.IO.Path.Combine(Application.persistentDataPath, "LokaleModelle", "Jet.glb");

        async void TryLocalModel()
        {
            string path = LocalModelPath;
            if (!System.IO.File.Exists(path)) return;
            try
            {
                var gltf = new GLTFast.GltfImport();
                bool ok = await gltf.Load(path);
                if (!ok || this == null || visual == null) return;
                var holder = new GameObject("Lokales Modell").transform;
                holder.SetParent(visual, false);
                ok = await gltf.InstantiateMainSceneAsync(holder);
                if (!ok || this == null) { if (holder != null) Destroy(holder.gameObject); return; }
                if (placeholder != null) placeholder.SetActive(false);
                Debug.Log("Testflug: eigenes Modell geladen: " + path);
            }
            catch (System.Exception e) { Debug.LogWarning("Testflug: eigenes Modell nicht ladbar: " + e.Message); }
        }

        void Update()
        {
            if (crashed || Active != this) return;
            if (Time.timeScale <= 0f) return;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            float dt = Time.deltaTime;
            if (Inp.Held(KeyCode.LeftShift)) throttle += 0.45f * dt;
            if (Inp.Held(KeyCode.LeftControl)) throttle -= 0.45f * dt;
            throttle = Mathf.Clamp01(throttle);
            pitchIn = (Inp.Held(KeyCode.W) ? 1f : 0f) - (Inp.Held(KeyCode.S) ? 1f : 0f);
            rollIn = (Inp.Held(KeyCode.D) ? 1f : 0f) - (Inp.Held(KeyCode.A) ? 1f : 0f);
            yawIn = (Inp.Held(KeyCode.E) ? 1f : 0f) - (Inp.Held(KeyCode.Q) ? 1f : 0f);
            if (BenchInputs) { pitchIn = BenchPitch; rollIn = 0f; yawIn = 0f; }

            if (Inp.Down(KeyCode.Space)) DropBomb();
            if (Inp.Down(KeyCode.R)) { bombs = BombLoad; rounds = GunLoad; RangeLog.AddLocal("Testflug: nachgeladen"); }
            gunCooldown -= dt;
            if ((locked && Inp.FireHeld || BenchGun) && rounds > 0)
                while (gunCooldown <= 0f && rounds > 0) { FireGun(); gunCooldown += 60f / GunRpm; }
            if (gunCooldown < 0f) gunCooldown = 0f;

            UpdateImpactPoint();
        }

        // ---- Testschalter (Benchmark) ----
        public static bool BenchInputs, BenchGun; public static float BenchPitch;
        float dbgAlpha, dbgBeta;
        /// <summary>Test: Fluglage fuers Protokoll.</summary>
        public string DebugText => $"Hoehe NN {transform.position.y:0}, Anstellw. {dbgAlpha:0.0}, Schiebew. {dbgBeta:0.0}, Neigung {Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), transform.forward, transform.right):0}, Rollen {Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.up, transform.forward).normalized, Vector3.ProjectOnPlane(Vector3.up, transform.forward).normalized, transform.forward):0}, Dreh {(transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg)}";

        void FixedUpdate()
        {
            if (crashed) return;
            Vector3 v = rb.linearVelocity;
            float speed = v.magnitude;
            float alt = transform.position.y;
            float rho = 1.225f * Mathf.Exp(-Mathf.Max(alt, 0f) / 8500f);
            float q = 0.5f * rho * speed * speed;

            Vector3 lv = transform.InverseTransformDirection(v);
            float alphaDeg = speed > 1f ? Mathf.Atan2(-lv.y, lv.z) * Mathf.Rad2Deg : 0f;   // Anstellwinkel: Nase ueber der Flugrichtung = positiv
            float betaDeg = speed > 1f ? Mathf.Atan2(lv.x, lv.z) * Mathf.Rad2Deg : 0f;     // Schiebewinkel
            dbgAlpha = alphaDeg; dbgBeta = betaDeg;

            // Auftrieb: linear bis zum Abriss, danach faellt er ab
            float a = Mathf.Abs(alphaDeg);
            float cl = Cl0 + ClAlpha * alphaDeg;
            if (a > StallDeg) cl = Mathf.Sign(alphaDeg) * Mathf.Max(0.35f, ClMax - (a - StallDeg) * 0.07f);
            cl = Mathf.Clamp(cl, -ClMax, ClMax);
            if (speed > 1f && lv.z > 0f)
            {
                Vector3 liftDir = Vector3.Cross(v, transform.right).normalized;
                rb.AddForce(liftDir * q * WingArea * cl);
                rb.AddForce(-v / speed * q * WingArea * (Cd0 + InducedK * cl * cl + (a > StallDeg ? 0.3f : 0f)));
                // Seitenkraft: Rumpf und Seitenleitwerk schieben die Flugrichtung zur Nase
                rb.AddForce(-transform.right * q * WingArea * 0.55f * Mathf.Sin(betaDeg * Mathf.Deg2Rad));
            }
            else if (speed > 1f) rb.AddForce(-v / speed * q * WingArea * 0.8f);   // rueckwaerts / quer: nur Widerstand
            rb.AddForce(transform.forward * throttle * MaxThrust * Mathf.Pow(rho / 1.225f, 0.7f));

            // Ruder: gewuenschte Drehraten (wirken erst mit Fahrt), dazu Stabilitaet (Nase folgt der Flugrichtung)
            float ce = Mathf.Clamp01(q / 5000f);
            Vector3 w = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;
            Vector3 want = new Vector3(
                -pitchIn * PitchRate * ce + 0.45f * (alphaDeg - 0.6f) * ce,   // +X = Nase runter; ohne Ruder trimmt er auf etwa Geradeausflug
                yawIn * YawRate * ce + 1.6f * betaDeg * ce,                 // Nase dreht in den Fahrtwind (Windfahne)
                -rollIn * RollRate * ce);
            Vector3 accel = (want - w) * 4f * Mathf.Max(ce, 0.15f);
            rb.AddRelativeTorque(accel * Mathf.Deg2Rad, ForceMode.Acceleration);
        }

        void LateUpdate()
        {
            if (Active != this) return;
            var cam = Camera.main;
            if (cam == null) return;
            // Verfolgerkamera hinter und ueber dem Jet; die Hochachse folgt dem Jet nur halb (Horizont bleibt lesbar)
            Vector3 up = Vector3.Slerp(Vector3.up, transform.up, 0.6f);
            Vector3 want = transform.position - transform.forward * 22f + up * 5.5f;
            float k = 1f - Mathf.Exp(-Time.deltaTime * (crashed ? 1.5f : 7f));
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, k);
            Vector3 look = transform.position + transform.forward * 25f;
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - cam.transform.position, up), k);
            cam.fieldOfView = 62f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 6000f);
        }

        // ================= Waffen =================
        Vector3 BombStart(int side) => transform.position - transform.up * 1.4f + transform.right * 2.0f * side;

        void DropBomb()
        {
            if (bombs <= 0) { RangeLog.AddLocal("Keine Bomben mehr - R: nachladen"); return; }
            bombs--;
            bombSide = -bombSide;
            Vector3 v = rb.linearVelocity;
            var s = Shell.Fire(Bomb, BombStart(bombSide), v.sqrMagnitude > 1f ? v.normalized : Vector3.down, false, Mathf.Max(v.magnitude, 1f));
            if (s != null) s.noTracer = true;
            if (impactValid) RangeLog.AddLocal($"Bombe ab - Einschlag in {impactTime:0.0} s, {Vector3.Distance(transform.position, impact):0} m");
            LastBombPrediction = impactValid ? impact : Vector3.zero;
        }
        public static Vector3 LastBombPrediction;
        /// <summary>Test: Bombe loesen wie mit der Leertaste.</summary>
        public void BenchDrop() => DropBomb();

        void FireGun()
        {
            rounds--;
            var d = gunAlt++ % 3 == 2 ? GunAP : GunHE;   // Gurt: 2 Spreng, 1 Panzer
            Vector3 origin = transform.position + transform.forward * 7f - transform.up * 0.5f;
            Vector3 dir = Quaternion.Euler(Random.Range(-0.15f, 0.15f), Random.Range(-0.15f, 0.15f), 0f) * transform.forward;
            float speed = GunMuzzle + Mathf.Max(0f, Vector3.Dot(rb.linearVelocity, transform.forward));
            Shell.Fire(d, origin, dir, false, speed);
            try { Effects.Muzzle(origin, transform.forward, 20f, false); Sfx.MG(origin, 20f); } catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>Wo eine jetzt geloeste Bombe aufschlaegt: gleiche Rechnung wie die Bombe im Flug (Schwerkraft + Luftwiderstand).</summary>
        void UpdateImpactPoint()
        {
            impactValid = false;
            var map = GameManager.Instance != null ? GameManager.Instance.CurrentMap : null;
            if (map != null && bombs > 0)
            {
                Vector3 p = BombStart(-bombSide), v = rb.linearVelocity;
                float dt = Time.fixedDeltaTime, t = 0f;
                for (int i = 0; i < 4000; i++)
                {
                    v += Ballistics.Acceleration(Bomb, v) * dt;
                    Vector3 n = p + v * dt;
                    t += dt;
                    float g = map.GroundY(n.x, n.z);
                    if (n.y <= g)
                    {
                        float f = Mathf.Clamp01((p.y - map.GroundY(p.x, p.z)) / Mathf.Max(p.y - n.y, 0.001f));
                        impact = Vector3.Lerp(p, n, f); impact.y = map.GroundY(impact.x, impact.z);
                        impactTime = t; impactValid = true;
                        break;
                    }
                    p = n;
                }
            }
            if (xMark == null) return;
            xMark.gameObject.SetActive(impactValid && !crashed);
            if (!impactValid) return;
            Vector3 normal = Vector3.up;
            if (Physics.Raycast(impact + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 120f, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore)) normal = hit.normal;
            xMark.position = impact + normal * 0.3f;
            xMark.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            float scale = Mathf.Clamp(Vector3.Distance(Camera.main != null ? Camera.main.transform.position : transform.position, impact) / 600f, 1f, 4f);
            xMark.localScale = Vector3.one * scale;   // aus grosser Hoehe noch erkennbar
        }

        // ================= Absturz =================
        void OnCollisionEnter(Collision c)
        {
            if (crashed) return;
            float hit = c.relativeVelocity.magnitude;
            bool upright = Vector3.Dot(transform.up, Vector3.up) > 0.7f;
            if (hit > 18f || !upright || SpeedKmh > 400f) Crash();
        }

        void Crash()
        {
            crashed = true;
            Vector3 p = transform.position;
            try { Effects.Explosion(p, 60f, true); } catch (System.Exception e) { Debug.LogException(e); }
            if (visual != null) visual.gameObject.SetActive(false);
            if (xMark != null) xMark.gameObject.SetActive(false);
            rb.isKinematic = true;
            RangeLog.AddLocal("Jet abgestuerzt - N: neu starten");
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            if (xMark != null) Destroy(xMark.gameObject);
        }

        // ================= Anzeige =================
        GUIStyle hud, hudBig, mark;
        void OnGUI()
        {
            if (Active != this || NetSync.Headless) return;
            if (hud == null)
            {
                hud = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
                hud.normal.textColor = new Color(0.6f, 1f, 0.6f);
                hudBig = new GUIStyle(hud) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                mark = new GUIStyle(hud) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            float sc = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            float W = Screen.width / sc;
            var cam = Camera.main;

            if (crashed)
            {
                GUI.Label(new Rect(W / 2f - 400, 420, 800, 60), "<color=#f66>ABGESTUERZT</color>   N: neu starten", hudBig);
                GUI.matrix = Matrix4x4.identity;
                return;
            }
            // Visier der Kanone (600 m voraus) und Bomben-X
            if (cam != null)
            {
                Vector3 s = cam.WorldToScreenPoint(transform.position + transform.forward * 600f);
                if (s.z > 0f) GUI.Label(new Rect(s.x / sc - 30, (Screen.height - s.y) / sc - 30, 60, 60), "<color=#9f9>+</color>", mark);
                if (impactValid)
                {
                    Vector3 x = cam.WorldToScreenPoint(impact);
                    if (x.z > 0f) GUI.Label(new Rect(x.x / sc - 30, (Screen.height - x.y) / sc - 30, 60, 60), "<color=#f43>X</color>", mark);
                }
            }
            float stall = rb.linearVelocity.magnitude;
            string warn = AltitudeAgl < 150f && rb.linearVelocity.y < -20f ? "\n<color=#f66><b>BODEN! HOCHZIEHEN</b></color>" : stall < 60f ? "\n<color=#fc6><b>ZU LANGSAM</b></color>" : "";
            GUI.Label(new Rect(24, 20, 520, 260),
                $"<b>Testflug (Jet)</b>\n" +
                $"Fahrt  {SpeedKmh:0} km/h\n" +
                $"Hoehe  {AltitudeAgl:0} m ueber Grund\n" +
                $"Schub  {throttle * 100f:0} %\n" +
                $"Bomben {bombs} x 5 t" + (impactValid ? $"   (Einschlag in {impactTime:0.0} s)" : "") + "\n" +
                $"Kanone {rounds} Schuss 20 mm" + warn, hud);
            GUI.Label(new Rect(W - 470, 20, 450, 220),
                "Shift / Strg: Schub mehr / weniger\nW / S: Nase hoch / runter\nA / D: rollen   Q / E: Seitenruder\n" +
                "Leertaste: Bombe   Linke Maustaste: Kanone\nR: nachladen   N: neu starten   Esc: Pause", hud);
            GUI.matrix = Matrix4x4.identity;
        }
    }
}
