using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Kamera hinter dem Panzer oder im Visier, plus Anzeige (HUD).
    /// Maus = zielen, Shift = Visier, Mausrad = Visier-Entfernung, R = automatische Entfernung,
    /// X = Roentgenblick (Platten ausblenden), Esc = Maus freigeben.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TankCamera : MonoBehaviour
    {
        public TankController target;
        public float sensitivity = 2f;
        public float distance = 11f;

        float yaw, pitch = 5f;
        bool sightMode;
        bool autoRange = true;
        float manualRange = 500f;
        Camera cam;
        float baseFov;
        float aimDistance;
        string aimedName;
        TankController lastTarget;
        int zoomLevel;                                      // Visier-Zoomstufe (nur wenn das echte Visier umschaltbar war)
        int ZoomCount => target.spec.sightZoomFov != null && target.spec.sightZoomFov.Length > 0 ? target.spec.sightZoomFov.Length : 1;
        float SightFov => ZoomCount > 1 ? target.spec.sightZoomFov[Mathf.Clamp(zoomLevel, 0, ZoomCount - 1)] : target.spec.sightFov;
        float SightMag => ZoomCount > 1 ? target.spec.sightZoomMag[Mathf.Clamp(zoomLevel, 0, ZoomCount - 1)] : target.spec.sightMag;

        void Start()
        {
            cam = GetComponent<Camera>();
            cam.farClipPlane = 5000f;
            baseFov = cam.fieldOfView;
            if (target != null) yaw = target.transform.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (target != lastTarget) { lastTarget = target; yaw = target.transform.eulerAngles.y; pitch = 5f; sightMode = false; zoomLevel = 0; }

            if (Inp.Down(KeyCode.LeftShift))
            {
                sightMode = !sightMode;
                // Im Visier das eigene Modell ausblenden (sonst ragt die eigene Kanone ins Bild)
                if (target.visual != null) target.visual.SetVisible(!sightMode && TankController.ViewMode != 1);
            }
            if (Inp.Down(KeyCode.R)) autoRange = !autoRange;
            if (Inp.Down(KeyCode.X)) SetViewMode((TankController.ViewMode + 1) % 3);
            // Mausrad = Zoom (Visier) bzw. Kameraabstand (Aussenansicht)
            float sc = Inp.Scroll;
            if (sc != 0f)
            {
                if (sightMode) zoomLevel = Mathf.Clamp(zoomLevel + (sc > 0f ? 1 : -1), 0, ZoomCount - 1);
                else distance = Mathf.Clamp(distance - sc * 1.5f, 5f, 25f);
            }
            // Q / E = Visier-Entfernung einstellen
            if (Inp.Down(KeyCode.E)) { manualRange = Mathf.Clamp((autoRange ? Mathf.Round(aimDistance / 100f) * 100f : manualRange) + 100f, 100f, 3000f); autoRange = false; }
            if (Inp.Down(KeyCode.Q)) { manualRange = Mathf.Clamp((autoRange ? Mathf.Round(aimDistance / 100f) * 100f : manualRange) - 100f, 100f, 3000f); autoRange = false; }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = Inp.MouseDelta * sensitivity * GameSettings.MouseSens * (sightMode ? SightFov / 60f : 1f);
                if (GameSettings.InvertY) d.y = -d.y;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, -40f, 60f);
            }

            Quaternion aimRot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 aimDir = aimRot * Vector3.forward;

            bool inSight = sightMode && target.parts.turret.parent != null;
            if (inSight)
            {
                // Wie im echten Panzer: das Zielfernrohr ist fest mit der Kanone verbunden.
                // Die Strichplatte zeigt also, wohin das Rohr gerade zielt (auf die eingestellte Entfernung).
                var gun = target.parts.gunPivot;
                Vector3 sightDir = Quaternion.AngleAxis(target.SuperElevation, gun.right) * gun.forward;
                transform.position = target.SightPosition;
                transform.rotation = Quaternion.LookRotation(sightDir, target.parts.turret.up);
                cam.fieldOfView = SightFov;
            }
            else
            {
                Vector3 pivot = target.transform.position + Vector3.up * 3f;
                Vector3 camPos = pivot - aimDir * distance + Vector3.up * 1f;
                // Kamera stoesst an Boden/Hindernisse an, statt hindurchzugehen
                Vector3 toCam = camPos - pivot;
                if (target.RaycastIgnoringSelf(pivot, toCam.normalized, toCam.magnitude + 0.3f, out RaycastHit camHit))
                    camPos = camHit.point - toCam.normalized * 0.3f + Vector3.up * 0.2f;
                transform.position = camPos;
                cam.fieldOfView = GameSettings.Fov;
                transform.rotation = aimRot;
            }

            // Wohin zeigt das Fadenkreuz?
            if (target.RaycastIgnoringSelf(transform.position, aimDir, 4000f, out RaycastHit hit))
            {
                target.aimPoint = hit.point;
                aimDistance = Vector3.Distance(target.parts.muzzle.position, hit.point);
                var tank = hit.collider.GetComponentInParent<TankController>();
                aimedName = tank != null ? tank.spec.name + (tank.destroyed ? " (zerstoert)" : "") : null;
            }
            else
            {
                target.aimPoint = transform.position + aimDir * 2000f;
                aimDistance = 2000f;
                aimedName = null;
            }
            target.sightRange = autoRange ? aimDistance : manualRange;

            // Verwackeln bei Schuss/Explosion (nach dem Zielen, damit das Fadenkreuz-Ziel ruhig bleibt)
            Vector3 shake = CameraShake.Sample(Time.deltaTime);
            if (shake != Vector3.zero) transform.rotation *= Quaternion.Euler(shake * (sightMode ? 0.35f : 1f));
        }

        void SetViewMode(int mode)
        {
            TankController.ViewMode = mode;
            foreach (var tc in FindObjectsByType<TankController>(FindObjectsSortMode.None))
                tc.ApplyXray();
            string[] names = { "Ansicht: normal", "Ansicht: Innenteile (Roentgen)", "Ansicht: Panzerplatten" };
            RangeLog.Add(names[mode]);
        }

        // ---------------- Anzeige ----------------
        GUIStyle box, small, big;

        void OnGUI()
        {
            if (target == null) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true, richText = true };
                box.normal.textColor = Color.white;
                small = new GUIStyle(box) { fontSize = 11 };
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 32, fontStyle = FontStyle.Bold };
                big.normal.textColor = new Color(1f, 0.3f, 0.2f);
            }

            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            float k = GameSettings.UiFactor;
            var t0 = target;

            EnsureTextures();
            bool inSight = sightMode && t0.parts.turret.parent != null;
            if (inSight) DrawSight(cx, cy);
            else
            {
                // Kreis = wohin du zielst
                Draw(ring, cx, cy, 30f * k, new Color(1f, 1f, 1f, 0.9f));
                // Punkt = wohin die Kanone gerade zeigt (auf die eingestellte Entfernung)
                if (GunScreenPoint(out Vector2 g))
                {
                    Draw(dot, g.x, g.y, 9f * k, new Color(0f, 0f, 0f, 0.6f));
                    Draw(dot, g.x, g.y, 6f * k, new Color(1f, 0.6f, 0.1f, 1f));
                }
            }

            // ab hier: Anzeigen in "virtuellen" Pixeln, die mit der Aufloesung (und der UI-Groesse) mitwachsen
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float SW = Screen.width / k, SH = Screen.height / k;
            cx = SW / 2f; cy = SH / 2f;
            if (aimedName != null)
                GUI.Label(new Rect(cx + 20, cy + 15, 300, 25), $"{aimedName}  {aimDistance:0} m", box);

            // Panzer-Status
            var t = target;
            var a = t.Ammo;
            Ballistics.SimulateFlat(a, Mathf.Max(t.sightRange, 10f), out float vAt, out _, out _);
            string reload = t.reloadLeft > 0f
                ? $"Laedt ... {t.reloadLeft * t.ReloadMultiplier():0.0} s{(t.ReloadMultiplier() > 1f ? " (ohne Ladeschuetze!)" : "")}"
                : "<color=#7f7>GELADEN</color>";
            string s = $"<b>{t.spec.name}</b>   {t.SpeedKmh:0} km/h\n" +
                       $"Munition [{t.AmmoSlot}/{t.AmmoLoadout.Length}]: {a.name} ({a.type}, {a.gun})\n" +
                       (a.IsHE ? $"Sprenggranate: Druckwelle schlaegt bis {Penetration.FlatPenetration(a, vAt):0} mm durch\n"
                        : a.IsHEAT ? $"Hohlladung: {Penetration.FlatPenetration(a, vAt):0} mm auf jede Entfernung\n"
                        : $"Durchschlag auf {t.sightRange:0} m: {Penetration.FlatPenetration(a, vAt):0} mm\n") +
                       $"Visier: {t.sightRange:0} m {(autoRange ? "(automatisch)" : "(von Hand, Q/E)")}\n" +
                       $"{reload}\n\n<b>Besatzung</b>\n";
            foreach (ModuleType role in new[] { ModuleType.Commander, ModuleType.Gunner, ModuleType.Loader, ModuleType.Driver, ModuleType.RadioOperator })
            {
                t.seats.TryGetValue(role, out var m);
                string who = m == null ? "<color=#f66>leer</color>"
                           : m.Destroyed ? "<color=#f66>tot</color>"
                           : $"{m.label} {m.hp:0}%" + (m.type != role ? " (eingesprungen)" : "");
                s += $"  {TankModule.RoleName(role)}: {who}\n";
            }
            s += "\n<b>Technik</b>\n";
            foreach (var m in t.parts.modules)
            {
                if (m.IsCrew) continue;
                string st = m.Destroyed ? "<color=#f66>ZERSTOERT</color>" : m.hp < m.maxHp ? $"<color=#fd6>{m.hp / m.maxHp * 100f:0}%</color>" : "ok";
                s += $"  {m.label}: {st}\n";
            }
            if (t.burning) s += "\n<color=#f84><b>BRAND! [F] Feuerloescher</b></color>";
            GUI.Box(new Rect(10, 10, 400, box.CalcHeight(new GUIContent(s), 400) + 6), s, box);

            // Protokoll: nur die letzten 4 Treffer, kleine Schrift
            string log = "";
            for (int i = 0; i < RangeLog.Lines.Count && i < 4; i++) log += RangeLog.Lines[i] + "\n";
            if (log.Length > 0)
            {
                float w = Mathf.Min(620, SW - 20);
                float h = small.CalcHeight(new GUIContent(log), w);
                GUI.Box(new Rect(10, SH - h - 10, w, h), log.TrimEnd('\n'), small);
            }

            if (!inSight) GUI.Box(new Rect(SW - 330, 10, 320, 150),
                "W/S: fahren   A/D: lenken   Leertaste: bremsen\n" +
                "Maus: zielen   Linksklick: feuern\n" +
                "Shift: Visier an/aus\n" +
                "Mausrad: Zoom (Visier: nur wenn umschaltbar)   Q/E: Entfernung   R: auto\n" +
                "1-7: Munition wechseln\n" +
                "X: Ansicht (normal/Innenteile/Panzerung)   F: Feuer loeschen\n" +
                "N: neue Ziele   T: aufrichten   B: zur Rampe (Testgelaende)\n" +
                "Esc: Pause / Menue\nMarker: gruen = Durchschlag, rot = nein, gelb = Abpraller", small);

            if (t.destroyed)
                GUI.Label(new Rect(0, cy - 80, SW, 50), $"DU WURDEST ZERSTOERT: {t.destroyedReason}  (N druecken)", big);
            GUI.matrix = Matrix4x4.identity;
        }
        // ---------------- Zielmarken und Visier ----------------
        Texture2D white, ring, dot, tri, vignette;

        void EnsureTextures()
        {
            if (white != null) return;
            white = Texture2D.whiteTexture;
            ring = MakeCircle(64, 0.80f, 1.0f);
            dot = MakeCircle(32, 0f, 1.0f);
            tri = MakeTriangle(64);
            vignette = MakeVignette(512);
        }

        static Texture2D MakeCircle(int n, float inner, float outer)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    float aOut = Mathf.Clamp01((outer - r) * n * 0.5f);
                    float aIn = inner <= 0f ? 1f : Mathf.Clamp01((r - inner) * n * 0.5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, aOut * aIn));
                }
            t.Apply();
            return t;
        }

        /// <summary>Gefuelltes Dreieck, Spitze oben in der Mitte.</summary>
        static Texture2D MakeTriangle(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fy = 1f - (y + 0.5f) / n;              // 0 = oben (Spitze), 1 = unten
                    float half = fy * 0.5f;                     // halbe Breite an dieser Hoehe
                    float dx = Mathf.Abs((x + 0.5f) / n - 0.5f);
                    float a = Mathf.Clamp01((half - dx) * n);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static Texture2D MakeVignette(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - n / 2f, y - n / 2f).magnitude / (n / 2f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.90f, 1.0f, r));
                    a = Mathf.Max(a, Mathf.InverseLerp(0.6f, 1.0f, r) * 0.35f); // leichte Abdunklung zum Rand
                    t.SetPixel(x, y, new Color(0f, 0f, 0f, r >= 1f ? 1f : a));
                }
            t.Apply();
            return t;
        }

        void Draw(Texture2D tex, float x, float y, float size, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - size / 2f, y - size / 2f, size, size), tex);
            GUI.color = Color.white;
        }

        void FillRect(float x, float y, float w, float h, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), white);
            GUI.color = Color.white;
        }

        bool GunScreenPoint(out Vector2 p)
        {
            var gun = target.parts.gunPivot;
            Vector3 gunDir = Quaternion.AngleAxis(target.SuperElevation, gun.right) * gun.forward;
            Vector3 gunPoint = target.RaycastIgnoringSelf(target.parts.muzzle.position, gunDir, 4000f, out RaycastHit gh)
                ? gh.point : target.parts.muzzle.position + gunDir * 2000f;
            Vector3 sp = cam.WorldToScreenPoint(gunPoint);
            p = new Vector2(sp.x, Screen.height - sp.y);
            return sp.z > 0f;
        }

        GUIStyle sightText;

        void DrawSight(float cx, float cy)
        {
            if (sightText == null)
            {
                sightText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            }
            var spec = target.spec;
            float size = Mathf.Min(Screen.width, Screen.height) * 0.97f;
            float left = cx - size / 2f, top = cy - size / 2f;

            // Runde Optik mit schwarzem Rand
            GUI.DrawTexture(new Rect(left, top, size, size), vignette);
            FillRect(0, 0, left + 1, Screen.height, Color.black);
            FillRect(left + size - 1, 0, Screen.width - left - size + 1, Screen.height, Color.black);
            FillRect(0, 0, Screen.width, top + 1, Color.black);
            FillRect(0, top + size - 1, Screen.width, Screen.height - top - size + 1, Color.black);

            // Strich (1/6400 Vollkreis) bzw. sowjetische Tausendstel (1/6000)
            float pxPerDeg = Screen.height / cam.fieldOfView;
            float mil = pxPerDeg * (spec.sightType == SightType.Soviet ? 0.06f : 0.05625f);
            Color ink = new Color(0.04f, 0.04f, 0.04f, 0.95f);
            float lw = Mathf.Max(1.5f, Screen.height / 600f); // Strichstaerke

            switch (spec.sightType)
            {
                case SightType.German:
                    // TZF: grosses Dreieck (Spitze = Zielpunkt), daneben kleine Dreiecke im Abstand von 4 Strich
                    GUI.color = ink;
                    GUI.DrawTexture(new Rect(cx - 2f * mil, cy, 4f * mil, 3f * mil), tri);
                    for (int k = 1; k <= 3; k++)
                        for (int sgn = -1; sgn <= 1; sgn += 2)
                        {
                            float x = cx + sgn * (k * 4f) * mil;
                            GUI.DrawTexture(new Rect(x - 1f * mil, cy, 2f * mil, 1.5f * mil), tri);
                        }
                    GUI.color = Color.white;
                    break;

                case SightType.US:
                    // M70: Fadenkreuz mit Strichskala alle 5 Strich
                    float len = 45f * mil;
                    FillRect(cx - len, cy - lw / 2f, len * 2f, lw, ink);
                    FillRect(cx - lw / 2f, cy - len * 0.6f, lw, len * 1.6f, ink);
                    for (int k = -8; k <= 8; k++)
                    {
                        if (k == 0) continue;
                        float h = (k % 2 == 0 ? 2f : 1.2f) * mil;
                        FillRect(cx + k * 5f * mil - lw / 2f, cy - h, lw, h, ink);
                    }
                    for (int k = 1; k <= 8; k++)
                        FillRect(cx - (k % 2 == 0 ? 2f : 1.2f) * mil, cy + k * 5f * mil - lw / 2f, (k % 2 == 0 ? 4f : 2.4f) * mil, lw, ink);
                    break;

                case SightType.Soviet:
                    // TSh-16: Winkelmarke (Spitze = Zielpunkt), seitliche Marken alle 4 Tausendstel
                    float arm = 4f * mil;
                    var m0 = GUI.matrix;
                    GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
                    FillRect(cx, cy - lw / 2f, arm, lw, ink);
                    GUI.matrix = m0;
                    GUIUtility.RotateAroundPivot(135f, new Vector2(cx, cy));
                    FillRect(cx, cy - lw / 2f, arm, lw, ink);
                    GUI.matrix = m0;
                    for (int k = 1; k <= 4; k++)
                        for (int sgn = -1; sgn <= 1; sgn += 2)
                            FillRect(cx + sgn * k * 4f * mil - lw / 2f, cy - 1f * mil, lw, 2f * mil, ink);
                    FillRect(cx - 30f * mil, cy - lw / 2f, 12f * mil, lw, ink);
                    FillRect(cx + 18f * mil, cy - lw / 2f, 12f * mil, lw, ink);
                    break;
            }

            // Entfernungsskala oben (in 100 m), Zeiger in der Mitte = eingestellte Entfernung
            float range = target.sightRange;
            float y0 = top + size * 0.13f;
            float pxPer100 = size * 0.035f;
            sightText.normal.textColor = ink;
            sightText.fontSize = Mathf.RoundToInt(Screen.height / 55f);
            for (int r = 0; r <= 3000; r += 100)
            {
                float x = cx + (r - range) / 100f * pxPer100;
                if (Mathf.Abs(x - cx) > size * 0.28f) continue;
                bool major = r % 200 == 0;
                FillRect(x - lw / 2f, y0, lw, major ? size * 0.018f : size * 0.010f, ink);
                if (major) GUI.Label(new Rect(x - 20f, y0 - size * 0.035f, 40f, size * 0.03f), (r / 100).ToString(), sightText);
            }
            GUI.color = ink;
            GUI.DrawTexture(new Rect(cx - size * 0.008f, y0 + size * 0.022f, size * 0.016f, size * 0.014f), tri); // Zeiger
            GUI.color = Color.white;
            GUI.Label(new Rect(cx - 150f, y0 + size * 0.04f, 300f, size * 0.03f),
                $"{range:0} m {(autoRange ? "(auto)" : "")}", sightText);

            // Wohin du mit der Maus willst (der Turm dreht hinterher)
            Vector3 ap = cam.WorldToScreenPoint(target.aimPoint);
            if (ap.z > 0f) Draw(ring, ap.x, Screen.height - ap.y, size * 0.03f, new Color(1f, 0.55f, 0.1f, 0.85f));

            // Unten: Name des Visiers und Ladezustand
            string state = target.reloadLeft > 0f ? $"Laedt {target.reloadLeft * target.ReloadMultiplier():0.0} s" : "GELADEN";
            sightText.normal.textColor = target.reloadLeft > 0f ? new Color(0.6f, 0.1f, 0.05f) : new Color(0.05f, 0.35f, 0.05f);
            GUI.Label(new Rect(cx - 200f, top + size * 0.83f, 400f, size * 0.03f), state, sightText);
            sightText.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(cx - 200f, top + size * 0.87f, 400f, size * 0.03f), $"{spec.sightName}   {SightMag:0.#}x", sightText);
        }
    }
}
