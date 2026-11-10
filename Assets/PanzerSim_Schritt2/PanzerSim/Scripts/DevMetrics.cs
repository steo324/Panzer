using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Entwickler-Anzeige (F3): Bildrate, Frame-Zeiten mit Verlauf, Rendering-Zahlen, Speicher, Spiel- und Weizen-Werte.
    /// Startet sich selbst in jeder Szene.
    /// </summary>
    public class DevMetrics : MonoBehaviour
    {
        public static bool Visible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<DevMetrics>() != null) return;
            var go = new GameObject("Dev-Metriken");
            DontDestroyOnLoad(go);
            go.AddComponent<DevMetrics>();
        }

        const int History = 240;
        readonly float[] frameMs = new float[History];
        int head;
        float fps, avgMs, minFps, low1Fps, maxMs, refresh;
        float cpuMs, gpuMs;
        int tankCount;
        readonly FrameTiming[] timings = new FrameTiming[1];

        ProfilerRecorder mainThread, renderThread, batches, setPass, drawCalls, tris, verts, usedMem, gcMem, gcAlloc, gfxMem;
        Texture2D white;
        GUIStyle style, head2;
        readonly List<float> sorted = new List<float>(History);

        void OnEnable()
        {
            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            usedMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            gcMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gfxMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
        }

        void OnDisable()
        {
            mainThread.Dispose(); renderThread.Dispose(); batches.Dispose(); setPass.Dispose(); drawCalls.Dispose();
            tris.Dispose(); verts.Dispose(); usedMem.Dispose(); gcMem.Dispose(); gcAlloc.Dispose(); gfxMem.Dispose();
        }

        void Update()
        {
            if (Inp.Down(KeyCode.F3)) Visible = !Visible;

            float ms = Time.unscaledDeltaTime * 1000f;
            frameMs[head] = ms; head = (head + 1) % History;

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                cpuMs = (float)timings[0].cpuFrameTime;
                gpuMs = (float)timings[0].gpuFrameTime;
            }

            refresh -= Time.unscaledDeltaTime;
            if (refresh > 0f) return;
            refresh = 0.25f;
            tankCount = FindObjectsByType<TankController>(FindObjectsSortMode.None).Length;
            sorted.Clear();
            float sum = 0f; maxMs = 0f;
            foreach (var f in frameMs) if (f > 0f) { sorted.Add(f); sum += f; maxMs = Mathf.Max(maxMs, f); }
            if (sorted.Count == 0) return;
            sorted.Sort();
            avgMs = sum / sorted.Count;
            fps = 1000f / avgMs;
            minFps = 1000f / sorted[sorted.Count - 1];
            low1Fps = 1000f / sorted[Mathf.Min(sorted.Count - 1, Mathf.FloorToInt(sorted.Count * 0.99f))];
        }

        static double Avg(ProfilerRecorder r)
        {
            if (!r.Valid || r.Count == 0) return -1;
            double s = 0; int n = r.Count;
            for (int i = 0; i < n; i++) s += r.GetSample(i).Value;
            return s / n;
        }
        static string Ms(ProfilerRecorder r) { double v = Avg(r); return v < 0 ? "-" : (v * 1e-6).ToString("0.0") + " ms"; }
        static string Num(ProfilerRecorder r)
        {
            if (!r.Valid) return "-";
            long v = r.LastValue;
            return v >= 1000000 ? (v / 1e6).ToString("0.00") + " Mio" : v >= 10000 ? (v / 1e3).ToString("0.0") + " k" : v.ToString();
        }
        static string Mb(ProfilerRecorder r) => r.Valid ? (r.LastValue / 1048576.0).ToString("0") + " MB" : "-";
        static string K(int v) => v >= 1000000 ? (v / 1e6).ToString("0.00") + " Mio" : v >= 10000 ? (v / 1e3).ToString("0.0") + " k" : v.ToString();

        void OnGUI()
        {
            if (!Visible) return;
            if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                style.normal.textColor = new Color(0.9f, 0.95f, 0.9f);
                head2 = new GUIStyle(style) { fontSize = 15, fontStyle = FontStyle.Bold };
            }

            const float w = 430f;
            float k = GameSettings.UiFactor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float x = (Screen.width / k - w) / 2f, y = 8f;
            var txt = new System.Text.StringBuilder();
            string col = fps >= 60 ? "#7CFC7C" : fps >= 30 ? "#FFD84A" : "#FF6060";
            txt.AppendLine($"<b>Frame</b>   {avgMs:0.0} ms   max {maxMs:0.0} ms");
            txt.AppendLine($"CPU {(cpuMs > 0 ? cpuMs.ToString("0.0") + " ms" : "-")}   GPU {(gpuMs > 0 ? gpuMs.ToString("0.0") + " ms" : "-")}");
            txt.AppendLine($"Main-Thread {Ms(mainThread)}   Render-Thread {Ms(renderThread)}");
            txt.AppendLine($"<b>Rendering</b>   Batches {Num(batches)}   SetPass {Num(setPass)}");
            txt.AppendLine($"Draw Calls {Num(drawCalls)}   Dreiecke {Num(tris)}   Ecken {Num(verts)}");
            txt.AppendLine($"<b>Speicher</b>   gesamt {Mb(usedMem)}   Grafik {Mb(gfxMem)}");
            txt.AppendLine($"GC {Mb(gcMem)}   GC pro Frame {(gcAlloc.Valid ? (gcAlloc.LastValue / 1024.0).ToString("0.0") + " KB" : "-")}");
            txt.AppendLine($"<b>Weizen</b>   3D {K(WheatField.StatPlants)}   Bilder {K(WheatField.StatBillboards)}");
            txt.AppendLine($"Dreiecke ca. {K(WheatField.StatTris)}   bewegt {WheatField.StatActive}   Kacheln {WheatField.StatChunks}/{WheatField.StatTiles}");

            var cam = Camera.main;
            txt.AppendLine($"<b>Spiel</b>   Panzer {tankCount}   Zeit x{Time.timeScale:0.##}   Physik {1f / Time.fixedDeltaTime:0} Hz");
            if (cam != null)
            {
                Vector3 p = cam.transform.position;
                txt.AppendLine($"Kamera {p.x:0} / {p.y:0} / {p.z:0}   Sicht {cam.fieldOfView:0.#}°");
            }
            txt.AppendLine($"Aufloesung {Screen.width}x{Screen.height}   vSync {QualitySettings.vSyncCount}   {SystemInfo.graphicsDeviceName}");

            string body = txt.ToString().TrimEnd();
            float textH = style.CalcHeight(new GUIContent(body), w - 20f), graphH = 46f;
            float h = 30f + textH + graphH + 16f;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(x, y, w, h), white);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 10, y + 6, w - 20, 22),
                $"<color={col}>{fps:0} FPS</color>   <size=12>min {minFps:0}   1% {low1Fps:0}</size>   <size=11>(F3)</size>", head2);
            GUI.Label(new Rect(x + 10, y + 30, w - 20, textH), body, style);

            // Verlauf der Frame-Zeiten (gruen < 16,7 ms, gelb < 33 ms, rot darueber)
            float gx = x + 10, gy = y + h - graphH - 8, gw = w - 20, bw = gw / History;
            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(new Rect(gx, gy, gw, graphH), white);
            float scale = graphH / 50f;  // 50 ms = volle Hoehe
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(new Rect(gx, gy + graphH - 16.7f * scale, gw, 1), white);
            GUI.DrawTexture(new Rect(gx, gy + graphH - 33.3f * scale, gw, 1), white);
            for (int i = 0; i < History; i++)
            {
                float f = frameMs[(head + i) % History];
                if (f <= 0f) continue;
                float bh = Mathf.Min(graphH, f * scale);
                GUI.color = f < 16.7f ? new Color(0.45f, 0.95f, 0.45f) : f < 33.3f ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.35f, 0.35f);
                GUI.DrawTexture(new Rect(gx + i * bw, gy + graphH - bh, Mathf.Max(1f, bw), bh), white);
            }
            GUI.color = Color.white;
            GUI.matrix = Matrix4x4.identity;
        }
    }
}
