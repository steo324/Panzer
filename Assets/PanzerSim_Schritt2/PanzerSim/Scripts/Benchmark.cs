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
            Debug.Log($"BENCH: Stufe {name}  {Screen.width}x{Screen.height}  Schnitt {avg:0} FPS  1%-Tief {low1:0} FPS  ({SystemInfo.graphicsDeviceName})");

            if (!string.IsNullOrEmpty(outDir))
            {
                System.IO.Directory.CreateDirectory(outDir);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outDir, $"bench_{preset}.png"));
                yield return new WaitForSeconds(0.5f);
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
