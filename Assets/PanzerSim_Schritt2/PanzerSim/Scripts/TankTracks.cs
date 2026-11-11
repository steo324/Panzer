using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanzerSim
{
    /// <summary>
    /// Kette aus einzelnen Gliedern, die um Antriebsrad, Laufrollen und Leitrad laeuft.
    /// Unten liegt sie unter den Laufrollen, oben haengt sie durch und liegt auf den Rollen auf.
    /// Der Durchhang reagiert auf Anfahren, Bremsen und Stoesse (lose Kette).
    /// Gezeichnet wird alles auf einmal per GPU-Instancing (schnell, keine Einzelobjekte).
    /// </summary>
    public class TankTracks
    {
        public struct WheelInfo
        {
            public Vector3 center;   // Mitte im Panzer (Wurzel-Koordinaten), inkl. Einfederung
            public float radius;     // in m
        }

        readonly TankController tank;
        readonly TankSpec spec;
        static Mesh cube;
        static Material mat;

        float travelL, travelR;
        float sag, sagVel, lastForward, lastVertical;
        public bool visible = true;

        readonly List<Vector2> path = new List<Vector2>();   // (z, y)
        readonly List<float> cum = new List<float>();
        readonly List<Matrix4x4> matrices = new List<Matrix4x4>();

        public TankTracks(TankController tc)
        {
            tank = tc;
            spec = tc.spec;
            sag = spec.trackSag;
            if (cube == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(tmp);
            }
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                mat = new Material(sh) { enableInstancing = true, color = new Color(0.16f, 0.15f, 0.14f) };
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.6f);
            }
        }

        readonly List<Vector2> supports = new List<Vector2>();   // (z, y) Auflagepunkte des oberen Trums

        /// <summary>Jedes Bild aufrufen. idler = Leitrad (hinten), sprocket = Antriebsrad (vorne),
        /// returnRollers = Stuetzrollen (optional): das obere Trum liegt auf ihnen auf und haengt nur dazwischen durch.</summary>
        public void Draw(float x, WheelInfo idler, WheelInfo sprocket, List<WheelInfo> road, bool left, List<WheelInfo> returnRollers = null)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float half = spec.trackThickness / 2f;

            // ---- Weg der Kettenmitte aufbauen (z, y) ----
            path.Clear();
            road.Sort((a, b) => a.center.z.CompareTo(b.center.z));

            // Unten: von hinten nach vorne unter den Laufrollen
            foreach (var w in road)
            {
                float y = w.center.y - w.radius - half;
                path.Add(new Vector2(w.center.z - w.radius * 0.35f, y));
                path.Add(new Vector2(w.center.z + w.radius * 0.35f, y));
            }
            // Vorne: um das Antriebsrad (von unten ueber vorne nach oben)
            float rs = sprocket.radius + half;
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, i / 10f) * Mathf.Deg2Rad;
                path.Add(new Vector2(sprocket.center.z + Mathf.Cos(a) * rs, sprocket.center.y + Mathf.Sin(a) * rs));
            }
            // Oben: von vorne nach hinten wie ein Seil ueber Stuetzen (Antriebsrad, Stuetzrollen, Leitrad).
            // Zwischen zwei Stuetzen haengt es durch (je laenger das Feld, desto mehr); auf Lauf- und Stuetzrollen liegt es auf.
            float ri = idler.radius + half;
            Vector2 topF = new Vector2(sprocket.center.z, sprocket.center.y + rs);
            Vector2 topR = new Vector2(idler.center.z, idler.center.y + ri);
            supports.Clear();
            supports.Add(topF);
            if (returnRollers != null)
            {
                returnRollers.Sort((a, b) => b.center.z.CompareTo(a.center.z));   // vorne zuerst
                foreach (var w in returnRollers)
                    if (w.center.z < topF.x && w.center.z > topR.x)
                        supports.Add(new Vector2(w.center.z, w.center.y + w.radius + half));
            }
            supports.Add(topR);
            float span = Mathf.Max(topF.x - topR.x, 0.01f);
            int topSteps = returnRollers != null && returnRollers.Count > 0 ? 48 : 28;
            int si = 0;
            for (int i = 1; i < topSteps; i++)
            {
                float t = i / (float)topSteps;
                float z = Mathf.Lerp(topF.x, topR.x, t);
                while (si < supports.Count - 2 && z < supports[si + 1].x) si++;
                Vector2 a = supports[si], b = supports[si + 1];
                float segLen = Mathf.Max(a.x - b.x, 0.001f);
                float lt = Mathf.Clamp01((a.x - z) / segLen);
                float segSag = sag * Mathf.Clamp01(segLen / span * 1.6f);   // kurze Felder zwischen Stuetzrollen haengen kaum durch
                float y = Mathf.Lerp(a.y, b.y, lt) - segSag * Mathf.Sin(lt * Mathf.PI);
                foreach (var w in road) y = Mathf.Max(y, OnTop(w, z, half));
                if (returnRollers != null) foreach (var w in returnRollers) y = Mathf.Max(y, OnTop(w, z, half));
                path.Add(new Vector2(z, y));
            }
            // Hinten: um das Leitrad (von oben ueber hinten nach unten)
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.Lerp(90f, 270f, i / 10f) * Mathf.Deg2Rad;
                path.Add(new Vector2(idler.center.z + Mathf.Cos(a) * ri, idler.center.y + Mathf.Sin(a) * ri));
            }
            path.Add(path[0]); // geschlossen

            cum.Clear();
            cum.Add(0f);
            for (int i = 1; i < path.Count; i++) cum.Add(cum[i - 1] + Vector2.Distance(path[i - 1], path[i]));
            float length = cum[cum.Count - 1];
            if (length < 0.5f) return;

            // ---- Bruchstellen dieser Kette: Lage auf dem Kettenweg (Bogenlaenge) ----
            bool sideLeft = x < 0f;
            breakS.Clear(); breakGap.Clear();
            foreach (var b in tank.trackBreaks)
            {
                if (b.leftSide != sideLeft) continue;
                breakS.Add(NearestArc(b.zy));
                breakGap.Add(b.gap);
            }
            bool broken = breakS.Count > 0;

            // ---- Bewegung: unten steht die Kette auf dem Boden, also laeuft sie relativ zum Panzer rueckwaerts ----
            // Eine gerissene Kette laeuft nicht mehr mit.
            float halfTrack = Mathf.Abs(x);
            float v = broken ? 0f : tank.ForwardSpeed + (left ? 1f : -1f) * tank.YawRate * halfTrack;
            if (left) travelL += v * dt; else travelR += v * dt;
            float travel = left ? travelL : travelR;

            int n = Mathf.Max(8, Mathf.RoundToInt(length / spec.trackPitch));
            float spacing = length / n;
            float offset = Mathf.Repeat(-travel, spacing);

            matrices.Clear();
            Matrix4x4 toWorld = tank.transform.localToWorldMatrix;
            Vector3 shoeScale = new Vector3(spec.trackShoeWidth, spec.trackThickness, spacing * 0.9f);
            Vector3 hornScale = new Vector3(0.07f, 0.09f, spacing * 0.55f);
            int seg = 0;
            for (int k = 0; k < n; k++)
            {
                float s = offset + k * spacing;
                if (broken && InGap(s, length)) continue;   // herausgerissene Glieder
                while (seg < cum.Count - 2 && cum[seg + 1] < s) seg++;
                float segLen = Mathf.Max(cum[seg + 1] - cum[seg], 0.0001f);
                float t = (s - cum[seg]) / segLen;
                Vector2 p = Vector2.Lerp(path[seg], path[seg + 1], t);
                Vector2 d = (path[seg + 1] - path[seg]) / segLen;

                Vector3 tangent = new Vector3(0f, d.y, d.x);
                Vector3 outward = new Vector3(0f, -d.x, d.y);  // zeigt vom Kettenring nach aussen
                Quaternion rot = Quaternion.LookRotation(tangent, outward);
                Vector3 pos = new Vector3(x, p.y, p.x);

                matrices.Add(toWorld * Matrix4x4.TRS(pos, rot, shoeScale));
                // Fuehrungszahn innen in der Mitte
                matrices.Add(toWorld * Matrix4x4.TRS(pos - outward * (spec.trackThickness / 2f + hornScale.y / 2f), rot, hornScale));
            }

            if (!visible) return;
            for (int i = 0; i < matrices.Count; i += 1023)
            {
                int count = Mathf.Min(1023, matrices.Count - i);
                Graphics.DrawMeshInstanced(cube, 0, mat, matrices.GetRange(i, count), null, ShadowCastingMode.On, true);
            }
        }

        readonly List<float> breakS = new List<float>(), breakGap = new List<float>();

        /// <summary>Bogenlaenge des Kettenweg-Punkts, der der Stelle zy (z, y im Panzer) am naechsten liegt.</summary>
        float NearestArc(Vector2 zy)
        {
            float best = float.MaxValue, bestS = 0f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector2 a = path[i], b = path[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(zy - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float d = (a + ab * t - zy).sqrMagnitude;
                if (d < best) { best = d; bestS = cum[i] + ab.magnitude * t; }
            }
            return bestS;
        }

        bool InGap(float s, float length)
        {
            for (int i = 0; i < breakS.Count; i++)
            {
                float d = Mathf.Abs(Mathf.Repeat(s - breakS[i] + length / 2f, length) - length / 2f);   // Abstand auf dem geschlossenen Ring
                if (d < breakGap[i] / 2f) return true;
            }
            return false;
        }

        /// <summary>
        /// Herausgerissene Kettenglieder als Physik-Teile: bei Sprengtreffern fliegen sie von blastFrom weg,
        /// sonst fallen sie einfach herunter. Granaten fliegen durch sie hindurch (eigene Ebene).
        /// </summary>
        public static void SpawnLinks(TankController tank, Vector3 at, int count, Vector3 blastFrom, bool explosive)
        {
            if (tank == null || count <= 0) return;
            var s = tank.spec;
            Material m = LinkMaterial();
            Vector3 along = tank.transform.forward;
            for (int i = 0; i < count && i < 14; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Kettenglied";
                go.layer = ArmorPlate.PhysicsOnlyLayer;
                go.GetComponent<Renderer>().sharedMaterial = m;
                go.transform.position = at + along * ((i - count / 2f) * s.trackPitch) + Random.insideUnitSphere * 0.05f;
                go.transform.rotation = tank.transform.rotation * Quaternion.Euler(Random.Range(-20f, 20f), 0f, Random.Range(-10f, 10f));
                go.transform.localScale = new Vector3(s.trackShoeWidth, s.trackThickness, s.trackPitch * 0.9f);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 15f;
                Vector3 vTank = tank.rb != null ? tank.rb.GetPointVelocity(at) : Vector3.zero;
                Vector3 push = explosive
                    ? ((go.transform.position - blastFrom).normalized + Vector3.up * 0.8f).normalized * Random.Range(6f, 14f)
                    : Vector3.down * 0.5f + Random.insideUnitSphere * 0.8f;
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = vTank + push;
#else
                rb.velocity = vTank + push;
#endif
                rb.angularVelocity = Random.insideUnitSphere * (explosive ? 15f : 3f);
                Object.Destroy(go, 30f);
            }
        }

        static Material LinkMaterial()
        {
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                mat = new Material(sh) { enableInstancing = true, color = new Color(0.16f, 0.15f, 0.14f) };
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.6f);
            }
            return mat;
        }

        /// <summary>Hoehe der Kettenmitte, wenn sie bei z oben auf dem Rad w aufliegt (sonst sehr tief).</summary>
        static float OnTop(WheelInfo w, float z, float half)
        {
            float dz = z - w.center.z;
            float r = w.radius + half;
            return Mathf.Abs(dz) < r ? w.center.y + Mathf.Sqrt(r * r - dz * dz) : float.MinValue;
        }

        /// <summary>Lose Kette: Durchhang schwingt bei Anfahren, Bremsen und Stoessen. Einmal pro Bild vor Draw aufrufen.</summary>
        public void UpdateSag()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float fwd = tank.ForwardSpeed, vert = tank.VerticalSpeed;
            float accF = (fwd - lastForward) / dt;
            float accV = (vert - lastVertical) / dt;
            lastForward = fwd;
            lastVertical = vert;

            // Antriebsrad vorne: beim Anfahren wird das obere Trum lockerer, beim Bremsen straffer
            float target = spec.trackSag + Mathf.Clamp(accF * 0.012f, -0.03f, 0.05f);
            float force = (target - sag) * 120f - sagVel * 7f + Mathf.Clamp(accV, -30f, 30f) * 0.15f;
            sagVel += force * dt;
            sag += sagVel * dt;
            sag = Mathf.Clamp(sag, 0f, spec.trackSag * 3f);
        }
    }
}
