using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Freie Kamera zum Schiessen auf dem Testgelaende.
    /// Rechte Maustaste halten = umsehen, WASD = bewegen, Q/E = runter/hoch, Shift = schnell
    /// Linksklick = feuern, 1-7 = Munition, R = automatische Visierhoehe an/aus, C = Treffer loeschen
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TestRangePlayer : MonoBehaviour
    {
        public float moveSpeed = 15f;
        public float lookSpeed = 2f;
        public bool autoElevation = true;

        int ammoIndex;
        float yaw, pitch;
        Camera cam;

        ShellData Ammo => AmmoLibrary.All[ammoIndex];

        void Start()
        {
            Shell.ShowMarkers = true;   // Testgelaende: bunte Trefferkugeln zur Auswertung
            cam = GetComponent<Camera>();
            cam.farClipPlane = 5000f;
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x;
        }

        void Update()
        {
            // Umsehen
            if (Inp.LookHeld)
            {
                Vector2 d = Inp.MouseDelta;
                yaw += d.x * lookSpeed;
                pitch = Mathf.Clamp(pitch - d.y * lookSpeed, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            // Bewegen
            Vector3 move = Vector3.zero;
            if (Inp.Held(KeyCode.W)) move += transform.forward;
            if (Inp.Held(KeyCode.S)) move -= transform.forward;
            if (Inp.Held(KeyCode.D)) move += transform.right;
            if (Inp.Held(KeyCode.A)) move -= transform.right;
            if (Inp.Held(KeyCode.E)) move += Vector3.up;
            if (Inp.Held(KeyCode.Q)) move -= Vector3.up;
            float speed = moveSpeed * (Inp.Held(KeyCode.LeftShift) ? 6f : 1f);
            transform.position += move * speed * Time.deltaTime;

            // Munition waehlen
            for (int i = 0; i < AmmoLibrary.All.Length && i < 9; i++)
                if (Inp.Down(KeyCode.Alpha1 + i)) ammoIndex = i;

            if (Inp.Down(KeyCode.R)) autoElevation = !autoElevation;
            if (Inp.Down(KeyCode.C)) ClearMarkers();

            if (Inp.FireDown) Fire();
        }

        void Fire()
        {
            Vector3 origin = transform.position + transform.forward * 0.5f;
            Vector3 dir = transform.forward;

            if (autoElevation && Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, 4000f))
            {
                // Wie ein Richtschuetze: Rohr so weit anheben, dass die Granate trotz Fall das Ziel trifft
                Ballistics.SimulateFlat(Ammo, hit.distance, out _, out float drop, out _);
                float extra = Mathf.Atan2(drop, hit.distance) * Mathf.Rad2Deg;
                dir = Quaternion.AngleAxis(-extra, transform.right) * transform.forward;
            }
            Shell.Fire(Ammo, origin, dir);
        }

        void ClearMarkers()
        {
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == "Treffer") Destroy(go);
            RangeLog.Lines.Clear();
        }

        // ---------------- Anzeige ----------------
        GUIStyle box;

        void OnGUI()
        {
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true };
                box.normal.textColor = Color.white;
            }

            // Fadenkreuz
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            GUI.Box(new Rect(cx - 1, cy - 10, 2, 20), GUIContent.none);
            GUI.Box(new Rect(cx - 10, cy - 1, 20, 2), GUIContent.none);

            // Munition
            var a = Ammo;
            string txt = $"<b>[{ammoIndex + 1}] {a.name}</b>  ({a.type})\n{a.gun}\n" +
                         $"{a.caliberMm:0.#} mm, {a.massKg:0.00} kg, {a.muzzleVelocity:0} m/s, Sprengstoff {a.fillerKg * 1000f:0} g\n\n" +
                         "Durchschlag senkrecht (RHA):\n";
            foreach (float d in new[] { 10f, 100f, 500f, 1000f, 1500f, 2000f })
            {
                Ballistics.SimulateFlat(a, d, out float v, out float drop, out float t);
                txt += $"  {d,5:0} m: {Penetration.FlatPenetration(a, v),4:0} mm   ({v:0} m/s, Fall {drop:0.0} m, {t:0.00} s)\n";
            }
            txt += $"\nAuto-Visierhoehe [R]: {(autoElevation ? "AN" : "AUS")}";
            box.richText = true;
            GUI.Box(new Rect(10, 10, 430, 270), txt, box);

            // Anvisierte Platte
            if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, 4000f))
            {
                string info = $"Entfernung: {hit.distance:0} m";
                var p = hit.collider.GetComponentInParent<ArmorPlate>();
                if (p != null)
                {
                    float ang = Vector3.Angle(-transform.forward, hit.normal);
                    info += $"\n{p.label}\n{p.thicknessMm:0} mm {ArmorPlate.MaterialName(p.material)}\nAuftreffwinkel ca. {ang:0}°";
                }
                GUI.Box(new Rect(cx + 20, cy + 20, 260, 90), info, box);
            }

            // Protokoll
            string log = "<b>Treffer</b>  (gruen = Durchschlag, rot = nicht, gelb = Abpraller, lila = zerbrochen, orange = Explosion)\n";
            foreach (var l in RangeLog.Lines) log += l + "\n";
            GUI.Box(new Rect(10, Screen.height - 330, 640, 320), log, box);

            GUI.Label(new Rect(Screen.width - 380, 10, 370, 120),
                "Rechte Maus halten: umsehen\nWASD / Q E: bewegen, Shift: schnell\nLinksklick: feuern\n1-7: Munition   R: Auto-Visier   C: Treffer loeschen", box);
        }
    }
}
