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
            float camOff = 0f, camPitch = -0.4f;
            var args = System.Environment.GetCommandLineArgs();
            int ci = System.Array.IndexOf(args, "-benchcam");   // -benchcam <Winkel> <Neigung>: Kamera seitlich (z. B. 90 12)
            if (ci >= 0 && ci + 2 < args.Length) { float.TryParse(args[ci + 1], out camOff); float.TryParse(args[ci + 2], out camPitch); }
            if (cam != null && pl != null) cam.SetView(pl.transform.eulerAngles.y + camOff, camPitch);   // Standard: zum Horizont schauen

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
                    if (dist > best && dist < 1600f) { best = dist; tgt = other; }
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
