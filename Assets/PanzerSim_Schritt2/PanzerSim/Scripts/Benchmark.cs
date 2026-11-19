using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Leistungstest: BallistikStudios.exe -bench &lt;Ordner&gt; [-preset 0..3] [-map n] [-tank n]
    /// Startet die Karte, misst 12 s lang die Bildrate (ohne VSync/Limit), speichert Bilder und schreibt
    /// "BENCH: ..." ins Log, dann beendet sich das Spiel.
    /// </summary>
    public class Benchmark : MonoBehaviour
    {
        public string outDir;
        public int preset = -1;

        IEnumerator Start()
        {
            if (preset >= 0) { GameSettings.ApplyPreset(preset); }
            GameSettings.VSync = false; GameSettings.FpsLimit = 0;
            GameSettings.Apply(false);
            yield return null;
            GameManager.Instance.BenchStart();
            yield return new WaitForSeconds(6f);   // Karte laden, Shader aufwaermen
            var cam = Camera.main.GetComponent<TankCamera>();
            var pl = GameManager.Instance.BenchPlayer;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchrestart") >= 0)
            {
                // wie ein Spieler: eine Runde spielen (schiessen), zurueck ins Menue, neue Runde
                if (pl != null) pl.BenchFire();
                yield return new WaitForSeconds(4f);
                GameManager.Instance.BenchRestart();
                yield return new WaitForSeconds(6f);
                pl = GameManager.Instance.BenchPlayer;
                Debug.Log("BENCH-RESTART: neue Runde gestartet");
            }
            float camOff = 0f, camPitch = -0.4f;
            var args = System.Environment.GetCommandLineArgs();
            int ci = System.Array.IndexOf(args, "-benchcam");   // -benchcam <Winkel> <Neigung>: Kamera seitlich (z. B. 90 12)
            if (ci >= 0 && ci + 2 < args.Length) { float.TryParse(args[ci + 1], out camOff); float.TryParse(args[ci + 2], out camPitch); }
            // -benchat <x> <z> <blick>: Panzer an diese Stelle setzen (z. B. um ein Gebaeude aus der Naehe anzusehen)
            int ai = System.Array.IndexOf(args, "-benchat");
            if (ai >= 0 && ai + 3 < args.Length && pl != null)
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                float.TryParse(args[ai + 1], System.Globalization.NumberStyles.Float, ic, out float bx);
                float.TryParse(args[ai + 2], System.Globalization.NumberStyles.Float, ic, out float bz);
                float.TryParse(args[ai + 3], System.Globalization.NumberStyles.Float, ic, out float byaw);
                var gm = GameManager.Instance.CurrentMap;
                pl.Teleport(new Vector3(bx, gm.GroundY(bx, bz) + 0.6f, bz), byaw);
                yield return new WaitForSeconds(3f);
            }
            // -benchjet (mit -jet): Testflug protokollieren, Bombe loesen, Bilder (X am Boden, Einschlag), Kanone
            if (System.Array.IndexOf(args, "-benchjet") >= 0)
            {
                var jc = JetController.Active;
                for (int s = 0; s < 46 && jc != null; s++)
                {
                    Debug.Log($"BENCH-JET t={s * 0.5f:0.0}s: {jc.SpeedKmh:0} km/h, {jc.AltitudeAgl:0} m, abgestuerzt {jc.Crashed}, {jc.DebugText}");
                    if (s == 6 && !string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "jet_x.png")); }
                    if (s == 8) jc.BenchDrop();
                    if (s == 36) JetController.BenchGun = true;
                    if (s == 40) { JetController.BenchGun = false; if (!string.IsNullOrEmpty(outDir)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "jet_kanone.png")); }
                    yield return new WaitForSeconds(0.5f);
                }
                if (Shell.LastBomb != Vector3.zero && Camera.main != null && !string.IsNullOrEmpty(outDir))
                {
                    if (jc != null) jc.enabled = false;
                    Camera.main.transform.position = Shell.LastBomb + new Vector3(0f, 120f, -260f);
                    Camera.main.transform.LookAt(Shell.LastBomb);
                    yield return new WaitForSeconds(0.3f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "jet_trichter.png"));
                    yield return new WaitForSeconds(0.5f);
                }
            }
            // -benchface: Wanne zum naechsten Ziel drehen (Jagdpanzer mit begrenztem Schwenkbereich, z. B. mit -autofire)
            if (System.Array.IndexOf(args, "-benchauto") >= 0 && pl != null)
            {
                // Maschinenkanone: 3 s Dauerfeuer, Bild mitten im Feuerstoss
                int before = pl.RoundsLeft;
                TankController.BenchHold = true;
                yield return new WaitForSeconds(1.5f);
                if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "dauerfeuer.png")); }
                yield return new WaitForSeconds(1.5f);
                TankController.BenchHold = false;
                Debug.Log("BENCH-AUTO: Schuss vorher " + before + ", nachher " + pl.RoundsLeft);
                yield return new WaitForSeconds(2f);
            }
            if (System.Array.IndexOf(args, "-benchgun2") >= 0 && pl != null && pl.Gun2 != null)
            {
                // zweite Kanone (37 mm Grant): umschalten, dreimal schiessen, Bild vom Turm
                for (int k = 0; k < 3; k++)
                {
                    yield return new WaitForSeconds(3.5f);
                    pl.BenchGun2();
                    Debug.Log($"BENCH-GUN2: Schuss {k + 1} mit {pl.Ammo.name}, Waffe {pl.WeaponName}, Ziel {Vector3.Distance(pl.transform.position, pl.aimPoint):0} m");
                    yield return new WaitForSeconds(0.08f);
                    if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"gun2_{k}.png")); }
                }
                yield return new WaitForSeconds(2f);
            }
            if (System.Array.IndexOf(args, "-benchface") >= 0 && pl != null)
            {
                TankController near = null; float bd2 = float.MaxValue;
                foreach (var ot in Object.FindObjectsByType<TankController>(FindObjectsSortMode.None))
                    if (ot != pl && !ot.destroyed) { float d2 = (ot.transform.position - pl.transform.position).sqrMagnitude; if (d2 < bd2) { bd2 = d2; near = ot; } }
                if (near != null)
                {
                    Vector3 dir = near.transform.position - pl.transform.position;
                    pl.Teleport(pl.transform.position + Vector3.up * 0.3f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 4f);   // 4 Grad daneben: Kanone muss seitlich schwenken
                    yield return new WaitForSeconds(3f);
                }
            }
            // -benchshot <x y z> <zx zy zz>: freie Kamera (Panzer-Koordinaten, Meter) auf einen Punkt am Panzer, z. B. fuer Nahaufnahmen der Kette
            int si = System.Array.IndexOf(args, "-benchshot");
            if (si >= 0 && si + 6 < args.Length && pl != null && cam != null)
            {
                var ic2 = System.Globalization.CultureInfo.InvariantCulture; var v = new float[6];
                for (int k = 0; k < 6; k++) float.TryParse(args[si + 1 + k], System.Globalization.NumberStyles.Float, ic2, out v[k]);
                cam.enabled = false;
                var ct = Camera.main.transform;
                ct.position = pl.transform.TransformPoint(new Vector3(v[0], v[1], v[2]));
                ct.LookAt(pl.transform.TransformPoint(new Vector3(v[3], v[4], v[5])));
            }
            int di = System.Array.IndexOf(args, "-benchdist");   // -benchdist <m>: Kamera naeher heran (Nahaufnahme)
            if (cam != null && di >= 0 && di + 1 < args.Length && float.TryParse(args[di + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float camDist)) cam.distance = camDist;
            if (cam != null && pl != null) cam.SetView(pl.transform.eulerAngles.y + camOff, camPitch);   // Standard: zum Horizont schauen

            // -benchhitcam: warten, bis die Trefferkamera aufgeht (mit -autofire), dann Bild
            if (System.Array.IndexOf(args, "-benchhitcam") >= 0)
            {
                for (int k = 0; k < 4; k++)
                {
                    while (HitCam.Active) yield return null;   // auf einen NEUEN Treffer warten
                    float w = 0f;
                    while (!HitCam.Active && w < 40f) { w += Time.unscaledDeltaTime; yield return null; }
                    yield return new WaitForSecondsRealtime(0.8f);
                    if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"trefferkamera_{k}.png")); }
                    Debug.Log($"BENCH-HITCAM: aktiv {HitCam.Active} nach {w:0.0} s");
                    while (HitCam.Active) yield return null;
                    if (System.Array.IndexOf(args, "-hitcamdelete") >= 0 && HitCam.Target != null && HitCam.Target != pl) { Object.Destroy(HitCam.Target.gameObject); yield return null; }   // wie neue Ziele mit N
                }
            }

            // -benchwaves: Wellen-Modus laufen lassen und alle 8 s protokollieren (mit -autofire schiesst der Spieler zurueck)
            if (System.Array.IndexOf(args, "-benchwaves") >= 0 && pl != null)
            {
                for (int s = 0; s < 12; s++)
                {
                    yield return new WaitForSeconds(8f);
                    string near = "";
                    foreach (var e in WaveMode.Enemies)
                        if (e != null) near += $" [{e.spec.name}: {Vector3.Distance(e.transform.position, pl.transform.position):0} m, {e.SpeedKmh:0} km/h, Laden {e.reloadLeft:0.0}, {(e.ai != null ? e.ai.DebugText : "")}]";
                    Debug.Log($"BENCH-WAVES t={(s + 1) * 8}s: Welle {WaveMode.Wave}, Gegner {WaveMode.EnemyCount}, Abschuesse {WaveMode.Kills}, Spieler zerstoert {pl.destroyed}{near}");
                    if (s == 3 && !string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "welle.png")); }
                }
                for (int i = RangeLog.Lines.Count - 1; i >= 0 && i > RangeLog.Lines.Count - 25; i--) Debug.Log("BENCH-WAVES Protokoll: " + RangeLog.Lines[i].Replace("\n", " | "));
            }

            // -benchconquest (mit -conquest): Eroberung laufen lassen, Flagge einnehmen, Karte, Rundenende
            if (System.Array.IndexOf(args, "-benchconquest") >= 0 && pl != null)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                string Shot(string n) => System.IO.Path.Combine(outDir ?? ".", n);
                for (int s = 0; s < 16; s++)
                {
                    pl = GameManager.Instance.BenchPlayer;
                    if (s == 1) ScreenCapture.CaptureScreenshot(Shot("eroberung_start.png"));
                    if (s == 2 && pl != null)
                    {
                        // zur naechsten Flagge fahren (hinstellen), Blick auf die Flagge
                        var f = Conquest.Flags[GameSettings.Team == 0 ? 0 : 2];
                        Vector3 p = f.pos + new Vector3(12f, 0f, -18f);
                        pl.Teleport(new Vector3(p.x, GameManager.Instance.CurrentMap.GroundY(p.x, p.z) + 0.6f, p.z), 0f);
                    }
                    if (s == 4 && pl != null && cam != null)
                    {
                        var f = Conquest.Flags[GameSettings.Team == 0 ? 0 : 2];
                        Vector3 d = f.pos + Vector3.up * 8f - Camera.main.transform.position;
                        cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 4f);
                    }
                    if (s == 5) ScreenCapture.CaptureScreenshot(Shot("eroberung_flagge.png"));
                    if (s == 8 && pl != null) TeamHud.ToggleMap(pl, Camera.main);
                    if (s == 9) { ScreenCapture.CaptureScreenshot(Shot("eroberung_karte.png")); }
                    if (s == 10) TeamHud.CloseMap();
                    if (s == 11 && cam != null && pl != null)
                    {
                        var f = Conquest.Flags[1];
                        Vector3 d = f.pos + Vector3.up * 8f - Camera.main.transform.position;
                        cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 2f);
                    }
                    if (s == 12) ScreenCapture.CaptureScreenshot(Shot("eroberung_mitte.png"));
                    if (s == 13) Conquest.BenchSetTickets(GameSettings.Team == 0 ? 60 : 0, GameSettings.Team == 0 ? 0 : 60);
                    Debug.Log($"BENCH-CONQUEST t={s * 6}s: {Conquest.BenchText}, Spieler zerstoert {(pl != null && pl.destroyed)}");
                    yield return new WaitForSeconds(6f);
                }
                ScreenCapture.CaptureScreenshot(Shot("eroberung_ende.png"));
                yield return new WaitForSeconds(0.5f);
                Debug.Log($"BENCH-CONQUEST Ende: {Conquest.BenchText}");
                for (int i = RangeLog.Lines.Count - 1; i >= 0 && i > RangeLog.Lines.Count - 40; i--) Debug.Log("BENCH-CONQUEST Protokoll: " + RangeLog.Lines[i].Replace("\n", " | "));
            }

            // -benchduel (mit -duel): Duell-Runden laufen lassen (mit -autofire schiesst der Spieler), Bilder
            if (System.Array.IndexOf(args, "-benchduel") >= 0)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                for (int s = 0; s < 20; s++)
                {
                    pl = GameManager.Instance.BenchPlayer;
                    if (s == 1 && cam != null && pl != null && DuelMode.Enemy != null) cam.SetView(ModeKit.Yaw(pl.transform.position, DuelMode.Enemy.transform.position), 3f);
                    if (s == 2) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "duell_start.png"));
                    if (s == 3 && pl != null) TeamHud.ToggleMap(pl, Camera.main);
                    if (s == 4) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "duell_karte.png")); }
                    if (s == 5) TeamHud.CloseMap();
                    if (s == 12) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "duell_spaeter.png"));
                    if (cam != null && pl != null && DuelMode.Enemy != null && s > 5) cam.SetView(ModeKit.Yaw(pl.transform.position, DuelMode.Enemy.transform.position), 3f);
                    Debug.Log($"BENCH-DUEL t={s * 5}s: {DuelMode.BenchText}");
                    yield return new WaitForSeconds(5f);
                }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "duell_ende.png"));
                yield return new WaitForSeconds(0.5f);
            }

            // -benchdefense (mit -defense): Verteidigung, erste Welle sofort, protokollieren, Bilder
            if (System.Array.IndexOf(args, "-benchdefense") >= 0)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                DefenseMode.BenchSkipPause();
                for (int s = 0; s < 18; s++)
                {
                    pl = GameManager.Instance.BenchPlayer;
                    if (s == 1 && cam != null && pl != null) cam.SetView(pl.transform.eulerAngles.y + 150f, 14f);
                    if (s == 2) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "verteidigung_stellung.png"));
                    if (s == 6 && pl != null) TeamHud.ToggleMap(pl, Camera.main);
                    if (s == 7) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "verteidigung_karte.png"));
                    if (s == 8) TeamHud.CloseMap();
                    if (s == 10 && cam != null && pl != null)
                    {
                        TankController near = null; float bd = 1e9f;
                        foreach (var e in DefenseMode.Enemies) if (e != null) { float d = Vector3.Distance(e.transform.position, pl.transform.position); if (d < bd) { bd = d; near = e; } }
                        if (near != null) cam.SetView(ModeKit.Yaw(pl.transform.position, near.transform.position), 2f);
                    }
                    if (s == 11) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "verteidigung_welle.png"));
                    if (DefenseMode.EnemyCount == 0) DefenseMode.BenchSkipPause();
                    Debug.Log($"BENCH-DEFENSE t={s * 6}s: {DefenseMode.BenchText}, Spieler zerstoert {(pl != null && pl.destroyed)}");
                    yield return new WaitForSeconds(6f);
                }
            }

            // -benchair (mit -air, am besten -tank 27 Gepard): Flugzeug kommt, Kamera auf den Vorhalt, Dauerfeuer, Abschuss und Absturz
            if (System.Array.IndexOf(args, "-benchair") >= 0 && pl != null && cam != null)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                string Shot(string n) => System.IO.Path.Combine(outDir ?? ".", n);
                bool shotApproach = false, shotFall = false, shotCrash = false;
                int killsBefore = AirRaid.Kills; float fallSeen = -1f;
                AirTarget watch = null;
                for (float w = 0f; w < 150f; w += Time.deltaTime)
                {
                    AirTarget near = null; float bd = 1e9f;
                    foreach (var a in AirTarget.All) if (a != null && !a.Down) { float d = Vector3.Distance(a.transform.position, pl.transform.position); if (d < bd) { bd = d; near = a; } }
                    if (near != null && !pl.destroyed)
                    {
                        Vector3 aim = pl.AutoGun ? AirRaid.LeadPoint(pl, near) : near.transform.position;
                        Vector3 d = aim - Camera.main.transform.position;
                        cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                        TankController.BenchHold = bd < 2200f;
                        if (bd < 2400f && !shotApproach)
                        {
                            shotApproach = true; ScreenCapture.CaptureScreenshot(Shot("flugzeug_anflug.png"));
                            yield return null;
                            // Nahaufnahme: freie Kamera neben dem Flugzeug
                            cam.enabled = false;
                            var ct = Camera.main.transform;
                            ct.position = near.transform.position - near.Velocity.normalized * 30f + near.transform.right * 18f + Vector3.up * 6f;
                            ct.LookAt(near.transform.position);
                            yield return null;
                            ScreenCapture.CaptureScreenshot(Shot("flugzeug_nah.png"));
                            yield return null; yield return null;
                            cam.enabled = true;
                        }
                        watch = near;
                    }
                    else TankController.BenchHold = false;
                    if (watch != null && watch.Down && fallSeen < 0f) fallSeen = w;
                    if (watch != null && fallSeen >= 0f)
                    {
                        // abgeschossenes Flugzeug weiter ansehen
                        Vector3 d = watch.transform.position - Camera.main.transform.position;
                        cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                        TankController.BenchHold = false;
                        if (!shotFall && w - fallSeen > 1.2f)
                        {
                            shotFall = true; ScreenCapture.CaptureScreenshot(Shot("flugzeug_absturz.png"));
                            yield return null;
                            cam.enabled = false;
                            var ct = Camera.main.transform;
                            ct.position = watch.transform.position - watch.Velocity.normalized * 45f + Vector3.Cross(Vector3.up, watch.Velocity.normalized) * 30f + Vector3.up * 10f;
                            ct.LookAt(watch.transform.position);
                            yield return null;
                            ScreenCapture.CaptureScreenshot(Shot("flugzeug_absturz_nah.png"));
                            yield return null; yield return null;
                            cam.enabled = true;
                        }
                    }
                    if (fallSeen >= 0f && watch == null && !shotCrash)
                    {
                        shotCrash = true;
                        yield return new WaitForSeconds(0.6f);
                        ScreenCapture.CaptureScreenshot(Shot("flugzeug_aufschlag.png"));
                        Debug.Log($"BENCH-AIR: Absturz nach {w - fallSeen:0.0} s");
                        break;
                    }
                    if (Mathf.FloorToInt(w / 3f) != Mathf.FloorToInt((w - Time.deltaTime) / 3f))
                        Debug.Log($"BENCH-AIR t={w:0}s: {AirRaid.BenchText}, Spieler {pl.spec.name} zerstoert {pl.destroyed}, Schuss {pl.RoundsLeft}");
                    yield return null;
                }
                TankController.BenchHold = false;
                Debug.Log($"BENCH-AIR Ende: Abschuesse {AirRaid.Kills - killsBefore}, {AirRaid.BenchText}");
                for (int i = RangeLog.Lines.Count - 1; i >= 0 && i > RangeLog.Lines.Count - 20; i--) Debug.Log("BENCH-AIR Protokoll: " + RangeLog.Lines[i].Replace("\n", " | "));
            }

            // -benchground (mit -ground): Pak, Infanterie, Minen ansehen und ausloesen
            if (System.Array.IndexOf(args, "-benchground") >= 0 && pl != null && cam != null)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                string Shot(string n) => System.IO.Path.Combine(outDir ?? ".", n);
                var gmap = GameManager.Instance.CurrentMap;
                var ct = Camera.main.transform;
                System.Collections.Generic.List<AtGun> guns = new System.Collections.Generic.List<AtGun>(GroundThreats.Guns);
                System.Collections.Generic.List<Soldier> sold = new System.Collections.Generic.List<Soldier>(GroundThreats.Soldiers);
                System.Collections.Generic.List<Minefield> mfs = new System.Collections.Generic.List<Minefield>(GroundThreats.Fields);
                Debug.Log("BENCH-GROUND Start: " + GroundThreats.BenchText);
                AtGun.BenchHold = true;
                // Nahaufnahmen mit freier Kamera
                cam.enabled = false;
                if (guns.Count > 0) { var g = guns[0].transform; ct.position = g.position + g.forward * 9f + g.right * 5f + Vector3.up * 2.2f; ct.LookAt(g.position + Vector3.up * 0.8f); yield return new WaitForSeconds(0.3f); ScreenCapture.CaptureScreenshot(Shot("pak.png")); yield return new WaitForSeconds(0.3f); }
                Soldier faust = sold.Find(s => s.hasFaust);
                if (faust != null) { var s = faust.transform; ct.position = s.position + s.forward * 6f + s.right * 3f + Vector3.up * 1.8f; ct.LookAt(s.position + Vector3.up * 0.4f); yield return new WaitForSeconds(0.3f); ScreenCapture.CaptureScreenshot(Shot("infanterie.png")); yield return new WaitForSeconds(0.3f); }
                cam.enabled = true;
                // Mine: Panzer so hinstellen, dass die linke Kette auf einer Mine steht
                if (mfs.Count > 0)
                {
                    Vector3 m = mfs[0].FirstMine;
                    float yaw = 30f;
                    Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    Vector3 p = m + right * (pl.spec.hullWidth * 0.5f - 0.3f) - (Quaternion.Euler(0f, yaw, 0f) * Vector3.forward) * 4f;
                    pl.Teleport(new Vector3(p.x, gmap.GroundY(p.x, p.z) + 0.6f, p.z), yaw);
                    cam.SetView(yaw + 140f, 16f);
                    TankController.BenchDrive = false;
                    for (float w = 0f; w < 4f; w += Time.deltaTime) { pl.AiControl(0.5f, 0f, false); yield return null; }
                    ScreenCapture.CaptureScreenshot(Shot("mine.png"));
                    yield return new WaitForSeconds(1f);
                    Debug.Log($"BENCH-GROUND Mine: Detonationen {Minefield.Detonations}, Kette links ab {pl.TrackBroken(true)}, rechts ab {pl.TrackBroken(false)}, zerstoert {pl.destroyed}");
                }
                // Panzerfaust: 55 m vor einen Trupp stellen
                if (faust != null && !pl.destroyed)
                {
                    pl.FullRepair();
                    var s = faust.transform;
                    Vector3 p = s.position + s.forward * 55f;
                    pl.Teleport(new Vector3(p.x, gmap.GroundY(p.x, p.z) + 0.6f, p.z), s.eulerAngles.y + 90f);
                    int before = Soldier.FaustShots;
                    for (float w = 0f; w < 9f; w += Time.deltaTime)
                    {
                        Vector3 d = s.position + Vector3.up - Camera.main.transform.position;
                        cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                        if (Soldier.FaustShots > before && w < 20f) { yield return new WaitForSeconds(0.4f); ScreenCapture.CaptureScreenshot(Shot("panzerfaust.png")); w = 20f; }
                        yield return null;
                    }
                    yield return new WaitForSeconds(2f);
                    Debug.Log($"BENCH-GROUND Panzerfaust: Schuesse {Soldier.FaustShots - before}, Spieler zerstoert {pl.destroyed}");
                    // Gegenwehr: 20-mm-Geschoss auf den Schuetzen, Sprenggranate auf die Pak
                    Shell.Fire(AmmoLibrary.All[60], s.position + Vector3.up * 0.9f + s.forward * 20f, -s.forward, false);
                    yield return new WaitForSeconds(0.5f);
                    Debug.Log($"BENCH-GROUND Schuss auf Schuetzen: tot {faust.Dead}");
                }
                if (guns.Count > 1)
                {
                    var g = guns[1].transform;
                    Shell.Fire(AmmoLibrary.All[11], g.position + Vector3.up * 30f + g.right * 3f, Vector3.down, false);   // 88-mm-Sprenggranate 3 m daneben
                    yield return new WaitForSeconds(1f);
                    Debug.Log($"BENCH-GROUND Sprenggranate neben Pak: ausgeschaltet {guns[1].Dead}");
                }
                AtGun.BenchHold = false;
                // Pak beschiesst den Spieler
                if (guns.Count > 0 && !guns[0].Dead)
                {
                    pl.FullRepair();
                    var g = guns[0].transform;
                    Vector3 p = g.position + g.forward * 700f;
                    pl.Teleport(new Vector3(p.x, gmap.GroundY(p.x, p.z) + 0.6f, p.z), g.eulerAngles.y + 180f);
                    int lines = RangeLog.Lines.Count;
                    for (float w = 0f; w < 30f; w += Time.deltaTime)
                    {
                        Vector3 d = g.position + Vector3.up - Camera.main.transform.position;
                        cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                        yield return null;
                    }
                    int pakHits = 0; for (int i = 0; i < RangeLog.Lines.Count - lines; i++) if (RangeLog.Lines[i].Contains("PzGr. 39")) pakHits++;
                    ScreenCapture.CaptureScreenshot(Shot("pak_feuer.png"));
                    Debug.Log($"BENCH-GROUND Pak: Treffer am Spieler {pakHits}, Spieler zerstoert {pl.destroyed}");
                }
                Debug.Log("BENCH-GROUND Ende: " + GroundThreats.BenchText);
                for (int i = Mathf.Min(RangeLog.Lines.Count - 1, 25); i >= 0; i--) Debug.Log("BENCH-GROUND Protokoll: " + RangeLog.Lines[i].Replace("\n", " | "));
            }

            // -benchsmoke: KI-Panzer 450 m voraus sieht uns, Nebelwerfer, KI verliert die Sicht; Bilder
            if (System.Array.IndexOf(args, "-benchsmoke") >= 0 && pl != null && cam != null)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                var gmap = GameManager.Instance.CurrentMap;
                Vector3 f = pl.transform.forward; f.y = 0f; f.Normalize();
                Vector3 ep = ModeKit.FreeSpot(gmap, new Vector2(pl.transform.position.x + f.x * 450f, pl.transform.position.z + f.z * 450f), 40f);
                var enemy = ModeKit.SpawnAi(TankLibrary.Sherman, ep, ModeKit.Yaw(ep, pl.transform.position), pl.team == 0 ? 1 : 0, 0.5f, gmap.Extent);
                enemy.ai.searchRange = 2000f;
                for (float w = 0f; w < 6f; w += Time.deltaTime) { cam.SetView(pl.transform.eulerAngles.y, 2f); yield return null; }
                Debug.Log($"BENCH-SMOKE vorher: KI {enemy.ai.DebugText}, Ziel {(enemy.ai.Target != null ? enemy.ai.Target.spec.name : "-")}");
                bool ok = Smoke.Discharge(pl);
                for (float w = 0f; w < 7f; w += Time.deltaTime) { cam.SetView(pl.transform.eulerAngles.y, 2f); yield return null; }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "nebel.png"));
                yield return new WaitForSeconds(0.5f);
                Debug.Log($"BENCH-SMOKE nachher (Nebelwerfer {ok}, Wolken {Smoke.CloudCount}, verdeckt {Smoke.Hides(enemy.transform.position + Vector3.up * 2.5f, pl.transform.position + Vector3.up * 1.5f)}): KI {enemy.ai.DebugText}, Ziel {(enemy.ai.Target != null ? enemy.ai.Target.spec.name : "-")}, Ladungen {Smoke.ChargesLeft(pl)}");
                // Seitenansicht der Nebelwand
                cam.SetView(pl.transform.eulerAngles.y + 70f, 10f);
                yield return new WaitForSeconds(1f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "nebel_seite.png"));
                yield return new WaitForSeconds(0.5f);
                Object.Destroy(enemy.gameObject);
                yield return null;   // erst danach ist er wirklich weg (sonst findet der naechste Test ihn noch)
            }

            // -benchartystrike: Kommandantensicht auf den naechsten Zielpanzer, L (Artillerie), Einschlag abwarten, Bilder
            if (System.Array.IndexOf(args, "-benchartystrike") >= 0 && pl != null && cam != null)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                TankController tgt = null; float bd = 1e9f;
                foreach (var o in FindObjectsByType<TankController>(FindObjectsSortMode.None))
                    if (o != null && o != pl && !o.destroyed && o.ai == null) { float d = Vector3.Distance(o.transform.position, pl.transform.position); if (d > 300f && d < bd) { bd = d; tgt = o; } }
                if (tgt != null)
                {
                    TankCamera.BenchCmd = true;
                    yield return new WaitForSeconds(0.5f);
                    for (float w = 0f; w < 1.5f; w += Time.deltaTime)
                    {
                        Vector3 d = tgt.transform.position + Vector3.up * 0.5f - Camera.main.transform.position;
                        cam.SetCommanderView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                        yield return null;
                    }
                    TankCamera.BenchArtyStrike = true;
                    yield return null; yield return null;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "artillerie_anforderung.png"));
                    Debug.Log($"BENCH-ARTYSTRIKE: Ziel {tgt.spec.name} {bd:0} m, angefordert {ArtyStrike.Pending}, Einschlag in {ArtyStrike.TimeToImpact:0.0} s, Punkt {ArtyStrike.Target} (Abstand zum Ziel {Vector3.Distance(ArtyStrike.Target, tgt.transform.position):0} m)");
                    float t0 = Time.time;
                    while (Time.time - t0 < ArtyStrike.Delay + 0.6f)
                    {
                        Vector3 d = tgt.transform.position + Vector3.up * 0.5f - Camera.main.transform.position;
                        cam.SetCommanderView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                        yield return null;
                    }
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", "artillerie_einschlag.png"));
                    yield return new WaitForSeconds(6f);
                    Debug.Log($"BENCH-ARTYSTRIKE: nach der Salve: Ziel zerstoert {tgt.destroyed}, Einschlaege {ArtyStrike.Impacts}, bereit in {ArtyStrike.ReadyIn:0} s, letzter Einschlag {Shell.LastHEImpact} ({Vector3.Distance(Shell.LastHEImpact, tgt.transform.position):0} m vom Ziel)");
                    TankCamera.BenchCmd = false;
                }
            }

            // -benchsoil: Bodenwirkung messen: je 8 s Vollgas auf Weg, Acker/Schlamm, Wiese und in einer Furt
            if (System.Array.IndexOf(args, "-benchsoil") >= 0 && pl != null && cam != null)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                var gmap = GameManager.Instance.CurrentMap;
                TankController.BenchDrive = false;
                int[] kinds = { GroundTracks.Weg, GroundTracks.Acker, GroundTracks.Wiese };
                string[] kn = { "Weg", "Acker", "Wiese" };
                for (int s = 0; s < 4; s++)
                {
                    Vector3 p = Vector3.zero; bool ok = false;
                    if (s < 3)
                    {
                        // Platz mit dieser Bodenart und 60 m Anlauf derselben Art geradeaus
                        for (int k = 0; k < 4000 && !ok; k++)
                        {
                            float x = Random.Range(-gmap.Extent * 0.7f, gmap.Extent * 0.7f), z = Random.Range(-gmap.Extent * 0.7f, gmap.Extent * 0.7f);
                            ok = gmap.SurfaceAt(x, z) == kinds[s] && gmap.SurfaceAt(x, z + 30f) == kinds[s] && gmap.SurfaceAt(x, z + 60f) == kinds[s] && gmap.CanSpawn(x, z)
                                 && Mathf.Abs(gmap.GroundY(x, z + 60f) - gmap.GroundY(x, z)) < 3f;
                            p = new Vector3(x, gmap.GroundY(x, z), z);
                        }
                    }
                    else ok = GroundEffect.FindFord(out p);
                    if (!ok) { Debug.Log($"BENCH-SOIL {(s < 3 ? kn[s] : "Furt")}: keine Stelle gefunden"); continue; }
                    pl.FullRepair();
                    pl.Teleport(p + Vector3.up * 0.6f, 0f);
                    yield return new WaitForSeconds(1f);
                    float vmax = 0f;
                    for (float w = 0f; w < 8f; w += Time.deltaTime) { pl.AiControl(1f, 0f, false); vmax = Mathf.Max(vmax, pl.SpeedKmh); cam.SetView(pl.transform.eulerAngles.y + 150f, 14f); yield return null; }
                    if (s == 3 || (s == 1 && Weather.Current == Weather.Rain)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir ?? ".", s == 3 ? "furt.png" : "schlamm.png"));
                    var g = pl.Ground;
                    Debug.Log($"BENCH-SOIL {(s < 3 ? kn[s] : "Furt")} (Wetter {Weather.Names[Weather.Current]}): Boden '{g.label}' Tempo x{g.speed:0.00} Haftung x{g.grip:0.00}, Hoechstgeschw. {vmax:0} km/h (Spezifikation {pl.spec.maxSpeedKmh:0}), Wasser {GroundEffect.WaterDepth(pl.transform.position):0.0} m");
                    pl.AiControl(0f, 0f, true);
                    yield return new WaitForSeconds(0.5f);
                }
            }

            // -benchdestroy: Baum umfahren, Haus zusammenschiessen (Bilder vorher/nachher)
            if (System.Array.IndexOf(args, "-benchdestroy") >= 0 && pl != null && cam != null)
            {
                System.IO.Directory.CreateDirectory(outDir ?? ".");
                string Shot(string n) => System.IO.Path.Combine(outDir ?? ".", n);
                var gmap = GameManager.Instance.CurrentMap;
                TankController.BenchDrive = false;
                TankController.BenchLogFell = true;
                // Baum: einzeln stehenden Baum am Rand suchen, 18 m davor hinstellen, drauf zu fahren
                if (Forest.NearestTree(pl.transform.position + pl.transform.forward * 150f, out Vector3 tree))
                {
                    Vector3 from = tree - pl.transform.forward * 18f;
                    float yaw = ModeKit.Yaw(from, tree);   // genau auf den Stamm
                    pl.Teleport(new Vector3(from.x, gmap.GroundY(from.x, from.z) + 0.6f, from.z), yaw);
                    yield return new WaitForSeconds(1.5f);
                    int before = Forest.Felled;
                    for (float w = 0f; w < 7f; w += Time.deltaTime)
                    {
                        pl.AiControl(1f, 0f, false);
                        cam.SetView(yaw + 120f, 12f);
                        if (Forest.Felled > before && w < 50f) { yield return new WaitForSeconds(1.2f); ScreenCapture.CaptureScreenshot(Shot("baum_umgefahren.png")); w = 50f; }
                        yield return null;
                    }
                    pl.AiControl(0f, 0f, true);
                    Debug.Log($"BENCH-DESTROY Baum: Gelaende am Startplatz {gmap.GroundY(from.x, from.z):0.0} m, am Baum {gmap.GroundY(tree.x, tree.z):0.0} m");
                    Debug.Log($"BENCH-DESTROY Baum bei {tree}: gefaellt {Forest.Felled - before}, Panzer {pl.SpeedKmh:0} km/h, Abstand jetzt {Vector3.Distance(pl.transform.position, tree):0} m");
                }
                // Haus: 120 m davor, Sprenggranaten (88 mm) auf die Wand, bis es einstuerzt
                bool found = Ruins.Nearest(pl.transform.position, out Vector3 hp, out Vector3 hs, out float hy);
                if (!found)
                {
                    // Karte mit Einzelteil-Haeusern (Weite Ebene): naechstes Objekt "Haus"
                    float bd = 1e9f;
                    foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                        if (tr.name == "Haus") { float dd = Vector3.Distance(tr.position, pl.transform.position); if (dd < bd) { bd = dd; hp = tr.position; hy = tr.eulerAngles.y; found = true; } }
                    hs = new Vector3(10f, 8f, 12f);
                }
                if (found)
                {
                    Vector3 dir = Quaternion.Euler(0f, hy, 0f) * Vector3.right;
                    Vector3 stand = hp + dir * (hs.x * 0.5f + 120f);
                    pl.Teleport(new Vector3(stand.x, gmap.GroundY(stand.x, stand.z) + 0.6f, stand.z), hy - 90f);
                    yield return new WaitForSeconds(2f);
                    Vector3 aim = hp + Vector3.up * hs.y * 0.35f;
                    Vector3 d0 = aim - Camera.main.transform.position;
                    cam.SetView(Mathf.Atan2(d0.x, d0.z) * Mathf.Rad2Deg, -Mathf.Asin(d0.normalized.y) * Mathf.Rad2Deg);
                    yield return new WaitForSeconds(0.5f);
                    ScreenCapture.CaptureScreenshot(Shot("haus_vorher.png"));
                    int c0 = Ruins.Collapsed, shots = 0;
                    for (; shots < 30 && Ruins.Collapsed == c0; shots++)
                    {
                        Vector3 o = aim + dir * 60f + Vector3.up * 1f;
                        Shell.Fire(AmmoLibrary.All[11], o, (aim + Random.insideUnitSphere * 1.5f - o).normalized, false);
                        yield return new WaitForSeconds(0.4f);
                    }
                    yield return new WaitForSeconds(2.5f);
                    cam.SetView(Mathf.Atan2(d0.x, d0.z) * Mathf.Rad2Deg, -Mathf.Asin(d0.normalized.y) * Mathf.Rad2Deg);
                    yield return new WaitForSeconds(0.3f);
                    ScreenCapture.CaptureScreenshot(Shot("haus_nachher.png"));
                    Debug.Log($"BENCH-DESTROY Haus {hs.x:0}x{hs.z:0}x{hs.y:0} m: eingestuerzt nach {shots} Treffern ({Ruins.BenchText})");
                }
                else Debug.Log("BENCH-DESTROY: keine Haeuser registriert (" + Ruins.BenchText + ")");
            }

            // -benchwobble: Zittern von Wanne und Rohr waehrend der Fahrt messen (Abweichung vom geglaetteten Neigungswinkel)
            if (System.Array.IndexOf(args, "-noroughness") >= 0) TankController.GroundRoughness = 0f;
            if (System.Array.IndexOf(args, "-benchwobble") >= 0 && pl != null)
            {
                yield return new WaitForSeconds(4f);   // anfahren
                float emaH = 0f, emaG = 0f, sumH = 0f, sumG = 0f, sumV = 0f; int cnt = 0; bool first = true;
                for (float w = 0f; w < 8f; w += Time.deltaTime)
                {
                    float hp = Mathf.Asin(Mathf.Clamp(pl.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float gp = Mathf.Asin(Mathf.Clamp(pl.parts.gunPivot.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                    if (first) { emaH = hp; emaG = gp; first = false; }
                    emaH = Mathf.Lerp(emaH, hp, 0.05f); emaG = Mathf.Lerp(emaG, gp, 0.05f);
                    sumH += Mathf.Abs(hp - emaH); sumG += Mathf.Abs(gp - emaG); sumV += pl.SpeedKmh; cnt++;
                    yield return null;
                }
                Debug.Log($"BENCH-WOBBLE: {pl.spec.name} (Stabilisator {pl.spec.stabilizer}, Rauheit {TankController.GroundRoughness}) bei {sumV / cnt:0} km/h: Wanne {sumH / cnt:0.000} Grad, Rohr {sumG / cnt:0.000} Grad");
            }

            // -benchbarrel: auf das Rohr des naechsten Zielpanzers schiessen, Motor beschaedigen, Reparatur pruefen
            if (System.Array.IndexOf(args, "-benchbarrel") >= 0 && pl != null)
            {
                TankController tg = null; float bd = 1e9f;
                foreach (var tk in Object.FindObjectsByType<TankController>(FindObjectsSortMode.None))
                    if (tk != pl && !tk.destroyed) { float dd = Vector3.Distance(tk.transform.position, pl.transform.position); if (dd < bd) { bd = dd; tg = tk; } }
                if (tg != null)
                {
                    var gp = tg.parts.gunPivot;
                    for (int s = 0; s < 3; s++)
                    {
                        Vector3 aimAt = gp.position + gp.forward * (1.2f + s * 0.6f);
                        Shell.Fire(AmmoLibrary.All[s == 2 ? 14 : 3], aimAt + gp.right * 40f, -gp.right, false);
                        yield return new WaitForSeconds(0.6f);
                    }
                    foreach (var m in tg.parts.modules) if (m.type == ModuleType.Engine) m.ApplyDamage(70f);
                    Debug.Log($"BENCH-BARREL: {tg.spec.name} Rohr {tg.ModuleCondition(ModuleType.Barrel) * 100f:0}%, Motor {tg.ModuleCondition(ModuleType.Engine) * 100f:0}%, Reparatur braucht {tg.RepairTimeNeeded():0} s");
                    tg.NetTrackRepaired();
                    Debug.Log($"BENCH-BARREL: nach Reparatur Rohr {tg.ModuleCondition(ModuleType.Barrel) * 100f:0}%, Motor {tg.ModuleCondition(ModuleType.Engine) * 100f:0}%, noch noetig {tg.NeedsRepair}");
                    for (int i = RangeLog.Lines.Count - 1; i >= 0; i--) Debug.Log("BENCH-BARREL Protokoll: " + RangeLog.Lines[i].Replace("\n", " | "));
                }
            }

            // -benchammo: alle Granaten des Panzers mit Durchschlag auf 0/500/1000/2000 m ins Protokoll, letzte Granate laden
            if (System.Array.IndexOf(args, "-benchammo") >= 0 && pl != null)
            {
                foreach (int ai2 in pl.AmmoLoadout)
                {
                    var a = AmmoLibrary.All[ai2];
                    string line = $"BENCH-AMMO: {a.name} ({a.type}, {a.muzzleVelocity:0} m/s):";
                    foreach (float d in new[] { 1f, 500f, 1000f, 2000f })
                    {
                        Ballistics.SimulateFlat(a, d, out float vAt, out _, out float tAt);
                        line += $"  {d:0} m: {Penetration.FlatPenetration(a, vAt):0} mm ({vAt:0} m/s, {tAt:0.00} s)";
                    }
                    // gegen 100 mm bei 60 Grad (T-54/T-55-Bug) aus 1000 m
                    Ballistics.SimulateFlat(a, 1000f, out float v1k, out _, out _);
                    var hr = Penetration.Evaluate(a, new Vector3(0f, 0f, v1k), Quaternion.Euler(-60f, 0f, 0f) * Vector3.back, 100f, ArmorMaterial.RHA);
                    Debug.Log(line + $"  | 100 mm/60 Grad aus 1 km: {hr.outcome} (wirksam {hr.effectiveArmorMm:0} mm)");
                    pl.ammoIndex = ai2;
                }
            }

            // Startpunkt-Auswahl (-benchspawn): Bild der Karte, dann Startpunkt 3 waehlen (Enter-Weg ueber Abbrechen)
            if (System.Array.IndexOf(args, "-benchspawn") >= 0)
            {
                GameManager.Instance.BenchSpawnSelect();
                yield return new WaitForSecondsRealtime(1.5f);
                if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "startpunkt.png")); }
                yield return new WaitForSecondsRealtime(0.5f);
                SpawnSelect.Cancel(); Time.timeScale = 1f;
                Debug.Log($"BENCH-SPAWN: Auswahl war offen, Panzer bei {pl.transform.position}");
            }

            // Team-Test (-benchteam): Ping auf den naechsten Zielpanzer, Bild (Namen, Minikarte, Ping), dann J (aussteigen), Bild
            if (System.Array.IndexOf(args, "-benchteam") >= 0 && pl != null && cam != null)
            {
                TankController tgt = null; float best = 1e9f;
                foreach (var o in FindObjectsByType<TankController>(FindObjectsSortMode.None))
                {
                    if (o == pl) continue;
                    float dd = Vector3.Distance(o.transform.position, pl.transform.position);
                    if (dd < best) { best = dd; tgt = o; }
                }
                if (tgt != null)
                {
                    for (float w = 0f; w < 2f; w += Time.unscaledDeltaTime)
                    {
                        Vector3 d = tgt.transform.position + Vector3.up * 1.5f - Camera.main.transform.position;
                        cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                        yield return null;
                    }
                    Pings.Send(pl, tgt.transform.position + Vector3.up * 1f);
                    yield return new WaitForSeconds(1f);
                    if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "team_ping.png")); }
                    yield return new WaitForSeconds(0.5f);
                    pl.Abandon();
                    for (float w = 0f; w < 5f; w += Time.unscaledDeltaTime)
                    {
                        cam.SetView(pl.transform.eulerAngles.y + 120f, 12f);
                        yield return null;
                    }
                    if (!string.IsNullOrEmpty(outDir)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "team_aus.png"));
                    yield return new WaitForSeconds(0.5f);
                    Debug.Log($"BENCH-TEAM: Ping-Ziel {tgt.spec.name} in {best:0} m, Pings {Pings.List.Count}, Spieler '{pl.DisplayName}' Team {pl.team}, zerstoert={pl.destroyed} ({pl.destroyedReason})");
                }
            }

            // Karte (-benchmap): Bild der grossen Karte (Taste M)
            if (System.Array.IndexOf(args, "-benchmap") >= 0 && pl != null)
            {
                TeamHud.ToggleMap(pl, Camera.main);
                yield return new WaitForSeconds(1.5f);
                if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "karte.png")); }
                yield return new WaitForSeconds(0.5f);
                TeamHud.CloseMap();
            }

            // Lenkraketen-Test (-benchtow): auf den Panzer, der am weitesten weg ist (bis 1 km), schiessen, Bilder im Flug
            if (System.Array.IndexOf(args, "-benchtow") >= 0 && pl != null && cam != null)
            {
                TankController tgt = null; float best = 0f;
                foreach (var other in FindObjectsByType<TankController>(FindObjectsSortMode.None))
                {
                    if (other == pl) continue;
                    float dist = Vector3.Distance(other.transform.position, pl.transform.position);
                    if (dist > best && dist < 1000f) { best = dist; tgt = other; }
                }
                if (tgt != null)
                {
                    Debug.Log($"BENCH-TOW: Ziel {tgt.spec.name} in {best:0} m");
                    for (float w = 0f; w < 4f; w += Time.unscaledDeltaTime)
                    {
                        Vector3 d = tgt.transform.position + Vector3.up * 1.2f - Camera.main.transform.position;
                        cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                        yield return null;
                    }
                    pl.BenchFire();
                    float t0 = Time.time;
                    for (int shot = 0; shot < 4; shot++)
                    {
                        float at = new[] { 0.35f, 1.0f, 1.8f, 2.6f }[shot];
                        while (Time.time - t0 < at)
                        {
                            Vector3 d = tgt.transform.position + Vector3.up * 1.2f - Camera.main.transform.position;
                            cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                            yield return null;
                        }
                        if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"tow_{shot}.png")); }
                        Debug.Log($"BENCH-TOW: t={Time.time - t0:0.00} s Rakete={(pl.activeMissile != null ? $"{pl.activeMissile.Distance:0} m, gelenkt {pl.activeMissile.Guided}" : "weg")}");
                    }
                    yield return new WaitForSeconds(4f);
                    for (int i = RangeLog.Lines.Count - 1; i >= 0; i--) Debug.Log("BENCH-TOW Protokoll: " + RangeLog.Lines[i].Replace("\n", " | "));
                    Debug.Log($"BENCH-TOW: Ziel zerstoert={tgt.destroyed}");
                }
            }

            // Artillerie-Test (-bencharty): Kartenmodus, Ziel = Panzer am weitesten weg, Steil- und Flachfeuer, Abweichung messen
            if (System.Array.IndexOf(args, "-bencharty") >= 0 && pl != null)
            {
                for (float w = 0f; w < 5f && !ArtilleryMode.Active; w += Time.unscaledDeltaTime) yield return null;
                TankController tgt = null; float best = 0f;
                foreach (var other in FindObjectsByType<TankController>(FindObjectsSortMode.None))
                {
                    if (other == pl) continue;
                    float dist = Vector3.Distance(other.transform.position, pl.transform.position);
                    if (dist > best && dist < 7000f) { best = dist; tgt = other; }
                }
                for (int round = 0; round < 2 && tgt != null; round++)
                {
                    bool steil = round == 0;
                    ArtilleryMode.BenchSetTarget(tgt.transform.position, steil);
                    Debug.Log($"BENCH-ARTY: Ziel {tgt.spec.name}, {(steil ? "Steilfeuer" : "Flachfeuer")}: {ArtilleryMode.BenchInfo}");
                    for (float w = 0f; w < 25f && !ArtilleryMode.BenchReady; w += Time.unscaledDeltaTime) yield return null;
                    float t0 = Time.time, tof = ArtilleryMode.BenchTof;
                    ArtilleryMode.BenchFire();
                    if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"arty_{round}_karte.png")); }
                    while (Shell.LastHEImpactTime < t0 && Time.time - t0 < tof + 8f) yield return null;
                    if (!string.IsNullOrEmpty(outDir)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"arty_{round}_einschlag.png"));
                    Vector3 miss = Shell.LastHEImpact - tgt.transform.position; miss.y = 0f;
                    Vector3 fromMe = Shell.LastHEImpact - pl.transform.position; fromMe.y = 0f;
                    Debug.Log($"BENCH-ARTY: Einschlag bei {Shell.LastHEImpact} ({fromMe.magnitude:0} m vom Geschuetz, Ziel {best:0} m), Rohr {pl.parts.gunPivot.forward}");
                    Debug.Log(Shell.LastHEImpactTime >= t0
                        ? $"BENCH-ARTY: Einschlag nach {Shell.LastHEImpactTime - t0:0.0} s (berechnet {tof:0.0} s), Abweichung {miss.magnitude:0.0} m"
                        : "BENCH-ARTY: kein Einschlag gemessen");
                    yield return new WaitForSeconds(2f);
                }
                for (int i = RangeLog.Lines.Count - 1; i >= 0; i--) Debug.Log("BENCH-ARTY Protokoll: " + RangeLog.Lines[i].Replace("\n", " | "));
            }

            // Erschuetterungs-Test (-benchstun): Granaten auf/neben fremde Panzer setzen, Besatzungszustand protokollieren
            if (System.Array.IndexOf(args, "-benchstun") >= 0 && pl != null)
            {
                // wie nach dem Verlassen einer Runde: ein Effekt-Partikelsystem ist weg (Effekte muessen sich neu aufbauen)
                pl.BenchFire();
                yield return new WaitForSeconds(0.5f);
                var fx = GameObject.Find("Splitter-Partikel");
                if (fx != null) Destroy(fx.transform.parent.gameObject);
                yield return null;
                pl.BenchFire();
                yield return null;
                Debug.Log($"BENCH-STUN: nach Effekt-Verlust Nachladen {pl.reloadLeft:0.0} s (soll {pl.spec.reloadS})");
                var others = new List<TankController>();
                foreach (var other in FindObjectsByType<TankController>(FindObjectsSortMode.None)) if (other != pl) others.Add(other);
                others.Sort((x, y) => Vector3.Distance(x.transform.position, pl.transform.position).CompareTo(Vector3.Distance(y.transform.position, pl.transform.position)));
                string[] names = { "155 mm HE aufs Turmdach", "155 mm HE 5 m daneben", "88 mm HE Turmseite", "88 mm HEAT Turmseite", "75 mm HE Turmseite", "155 mm HE Turmfront" };
                int[] ammo = { 17, 17, 11, 12, 9, 17 };
                for (int s = 0; s < names.Length; s++)
                {
                    var tg = others[s % others.Count];
                    Vector3 tp = tg.parts.turret != null ? tg.parts.turret.position : tg.transform.position + Vector3.up * 2f;
                    Vector3 side = tg.transform.right;
                    Vector3 from, dir;
                    if (s == 0) { from = tp + Vector3.up * 4f; dir = Vector3.down; }
                    else if (s == 1) { Vector3 g = tg.transform.position + side * 5f; from = g + Vector3.up * 4f; dir = Vector3.down; }
                    else if (s == 5) { from = tp + Vector3.up * 0.3f + tg.transform.forward * 25f; dir = -tg.transform.forward; }
                    else { from = tp + Vector3.up * 0.3f + side * 25f; dir = -side; }
                    Shell.Fire(AmmoLibrary.All[ammo[s]], from, dir, false, 300f);
                    yield return new WaitForSeconds(0.6f);
                    string crew = "";
                    foreach (var m in tg.parts.modules)
                        if (m.IsCrew) crew += $"{m.label}: {(m.Destroyed ? "tot" : $"{m.hp:0}%" + (m.Unconscious ? $" bewusstlos {m.stun:0} s" : ""))}; ";
                    Debug.Log($"BENCH-STUN: {names[s]} -> {tg.spec.name}: {crew}");
                    if (s < 2) yield return new WaitForSeconds(0.1f);
                }
                // Wrack ansehen: Rohr abgesackt, Rauch, ausgestiegene Besatzung
                var wreck = others[Mathf.Min(2, others.Count - 1)];
                // Besatzung bis auf den Fahrer ausschalten -> Panzer ausgeschaltet, ein Ueberlebender steigt aus
                foreach (var m in wreck.parts.modules) if (m.IsCrew && m.type != ModuleType.Driver) { m.stun = 0f; m.ApplyDamage(1000f); }
                foreach (var m in wreck.parts.modules) if (m.type == ModuleType.Driver) m.stun = 0f;
                // eigenen Panzer daneben stellen, damit man das Wrack aus der Naehe sieht
                Vector3 near = wreck.transform.position + wreck.transform.right * 16f + wreck.transform.forward * 8f + Vector3.up * 1f;
                pl.rb.position = near; pl.transform.position = near; pl.rb.linearVelocity = Vector3.zero;
                for (float w = 0f; w < 9f; w += Time.unscaledDeltaTime)
                {
                    Vector3 d = wreck.transform.position + Vector3.up * 1.5f - Camera.main.transform.position;
                    if (cam != null) cam.SetView(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
                    if (w > 3f && w - Time.unscaledDeltaTime <= 3f && !string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "wrack_3s.png")); }
                    yield return null;
                }
                if (!string.IsNullOrEmpty(outDir)) { System.IO.Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, "wrack.png")); }
                Debug.Log($"BENCH-STUN: Wrack {wreck.spec.name} Rohr {Mathf.DeltaAngle(0f, -wreck.parts.gunPivot.localEulerAngles.x):0.0} Grad (min {wreck.spec.minElevation}), Wreck={wreck.GetComponent<Wreck>() != null}");
                // Panzerung des Tigers (Modellform): 76 mm APCBC frontal (soll abprallen) und seitlich auf den Turm
                var tiger = others.Find(o => o.spec.name == "Tiger I");
                if (tiger != null)
                {
                    Vector3 tp = tiger.parts.turret.position;
                    Shell.Fire(AmmoLibrary.All[3], tiger.transform.position + Vector3.up * 1.3f + tiger.transform.forward * 30f, -tiger.transform.forward, false);
                    yield return new WaitForSeconds(0.5f);
                    Shell.Fire(AmmoLibrary.All[3], tp + Vector3.up * 0.3f + tiger.transform.right * 30f, -tiger.transform.right, false);
                    yield return new WaitForSeconds(0.5f);
                    Shell.Fire(AmmoLibrary.All[0], tiger.transform.position + Vector3.up * 1.0f - tiger.transform.right * 30f, tiger.transform.right, false);
                    yield return new WaitForSeconds(0.5f);
                }
                yield return new WaitForSeconds(1f);
                for (int i = RangeLog.Lines.Count - 1; i >= 0; i--) Debug.Log("BENCH-STUN Protokoll: " + RangeLog.Lines[i].Replace("\n", " | "));
            }

            var frames = new List<float>();
            float t = 0f;
            while (t < 12f)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime);
                t += Time.unscaledDeltaTime;
            }
            frames.Sort();
            float sum = 0f; foreach (var f in frames) sum += f;
            float avg = frames.Count / sum;
            int n1 = Mathf.Max(1, frames.Count / 100);
            float worst = 0f; for (int i = frames.Count - n1; i < frames.Count; i++) worst += frames[i];
            float low1 = n1 / worst;
            string name = preset >= 0 ? GameSettings.PresetNames[preset] : "aktuell";
            Debug.Log($"BENCH-MG: {MachineGun.ShotsFired} Schuss, {MachineGun.Hits} Einschlaege, Leuchtspur-Bilder gemeldet {Tracers.AddedTotal}, gezeichnet {Tracers.DrawnTotal}, MG-Leuchtspuren {MachineGun.TracerFrames}");
            Debug.Log($"BENCH: Stufe {name}  {Screen.width}x{Screen.height}  Schnitt {avg:0} FPS  1%-Tief {low1:0} FPS  ({SystemInfo.graphicsDeviceName})");

            if (!string.IsNullOrEmpty(outDir))
            {
                System.IO.Directory.CreateDirectory(outDir);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"bench_{preset}.png"));
                yield return new WaitForSeconds(0.5f);
                for (int i = 0; i < 3; i++)   // ein paar Bilder kurz nacheinander (Leuchtspuren sind nur kurz sichtbar)
                {
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"bench_{preset}_{i}.png"));
                    yield return new WaitForSeconds(0.35f);
                }
                // zweites Bild: zurueckschauen (Spuren, Wiese hinter dem Panzer)
                var p = GameManager.Instance.BenchPlayer;
                if (cam != null && p != null)
                {
                    TankController.BenchDrive = false;
                    cam.SetView(p.transform.eulerAngles.y + 180f, 18f);
                    yield return new WaitForSeconds(1.5f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"bench_{preset}_zurueck.png"));
                    yield return new WaitForSeconds(0.5f);
                }
            }
            Application.Quit();
        }
    }
}
