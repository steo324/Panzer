using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Ausgeschalteter Panzer, ohne dass er explodieren muss: Rohr sackt ab (keine Hydraulik, kein Richtschuetze),
    /// duenne Rauchsaeule bleibt, nach einem Brand wird er russig-dunkel, Ueberlebende oeffnen die Luken und steigen aus.
    /// Laeuft auf jedem Rechner fuer sich (nur Optik).
    /// </summary>
    public class Wreck : MonoBehaviour
    {
        TankController tc;
        float t, droopFrom, smokeAcc, scorch, scorchShown = -1f;
        bool droop;
        public bool burned;   // hat gebrannt (Treibstoff/Motor/Munition): wird dunkel, Rauch dichter
        readonly List<(Renderer r, int i, Color c)> colors = new List<(Renderer, int, Color)>();
        MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static Wreck Begin(TankController tc, bool burned)
        {
            var w = tc.GetComponent<Wreck>();
            if (w == null) { w = tc.gameObject.AddComponent<Wreck>(); w.tc = tc; }
            w.burned |= burned;
            return w;
        }

        void Start()
        {
            if (tc == null) tc = GetComponent<TankController>();
            droop = tc.parts.gunPivot != null && tc.parts.turret != null && tc.parts.turret.parent != null;
            if (droop) droopFrom = Mathf.DeltaAngle(0f, -tc.parts.gunPivot.localEulerAngles.x);
            if (NetSync.Headless) return;
            if (tc.visual != null && tc.visual.Renderers != null)
                foreach (var r in tc.visual.Renderers)
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].HasProperty(BaseColor)) colors.Add((r, i, mats[i].GetColor(BaseColor)));
                }
            StartCoroutine(BailOut());
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;

            // Rohr sackt langsam auf den tiefsten Winkel (Turm bleibt, wo er war)
            if (droop && tc.parts.turret != null && tc.parts.turret.parent != null && t < 6f)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.8f) / 3.5f));
                float e = Mathf.Lerp(droopFrom, tc.spec.minElevation, k);
                tc.parts.gunPivot.localRotation = Quaternion.Euler(-e, 0f, 0f);
            }
            if (NetSync.Headless) return;

            // Ausbrennen: Lack verkohlt langsam
            if (burned || tc.burning) scorch = Mathf.MoveTowards(scorch, 1f, dt / 50f);
            if (Mathf.Abs(scorch - scorchShown) > 0.02f) ApplyScorch();

            // Rauchsaeule, solange das Wrack steht (beim Brand macht das Feuer selbst den Rauch)
            if (tc.burning) return;
            smokeAcc += dt;
            float interval = burned ? 0.22f : 0.35f;
            if (smokeAcc < interval) return;
            smokeAcc = 0f;
            Vector3 p = transform.TransformPoint(new Vector3(0f, tc.spec.clearance + tc.spec.hullHeight + 0.2f, -tc.spec.hullLength * 0.25f));
            Vector3 wind = new Vector3(0.6f, 0f, 0.3f);
            Effects.SmokeOne(p + Random.insideUnitSphere * 0.3f, Vector3.up * Random.Range(1.2f, 2.2f) + wind + Random.insideUnitSphere * 0.2f,
                             Random.Range(0.9f, 1.4f) * (burned ? 1.5f : 1f), Random.Range(9f, 14f),
                             burned ? 0.08f : 0.22f, burned ? 0.6f : 0.35f);
        }

        void ApplyScorch()
        {
            scorchShown = scorch;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var (r, i, c) in colors)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb, i);
                Color soot = new Color(c.r * 0.16f + 0.02f, c.g * 0.15f + 0.02f, c.b * 0.14f + 0.02f, c.a);
                mpb.SetColor(BaseColor, Color.Lerp(c, soot, scorch));
                r.SetPropertyBlock(mpb, i);
            }
        }

        // ---------------- Besatzung steigt aus ----------------
        IEnumerator BailOut()
        {
            yield return new WaitForSeconds(Random.Range(1.5f, 3f));
            var turretCrew = new List<TankModule>();
            foreach (var m in tc.parts.modules)
                if (m != null && m.IsCrew && !m.Destroyed && !m.Unconscious) turretCrew.Add(m);
            int n = 0;
            foreach (var m in turretCrew)
            {
                if (m == null || m.Destroyed) continue;
                bool inTurret = tc.parts.turret != null && tc.parts.turret.parent != null && m.transform.IsChildOf(tc.parts.turret);
                Transform hatchParent = inTurret ? tc.parts.turret : transform;
                Vector3 exit = TopAbove(m.transform.position);
                OpenHatch(hatchParent, exit);
                StartCoroutine(Climb(exit, n % 2 == 0 ? 1f : -1f));
                n++;
                yield return new WaitForSeconds(Random.Range(0.8f, 1.6f));
            }
        }

        /// <summary>Oberkante des eigenen Panzers ueber einem Punkt (Lukendeckel).</summary>
        Vector3 TopAbove(Vector3 p)
        {
            Vector3 from = new Vector3(p.x, p.y + 6f, p.z);
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, 10f, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<TankController>() == tc && h.point.y > best) best = h.point.y;
            if (float.IsNegativeInfinity(best)) best = transform.position.y + tc.spec.clearance + tc.spec.hullHeight;
            return new Vector3(p.x, best, p.z);
        }

        float Ground(Vector3 p)
        {
            float best = p.y - 3f;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 20f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<TankController>() != null) continue;
                if (h.collider.attachedRigidbody != null) continue;
                return h.point.y;
            }
            return best;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(c);
            go.transform.SetParent(parent, false);
            return go;
        }

        void OpenHatch(Transform parent, Vector3 exit)
        {
            // aufgeklappter Lukendeckel: runde Platte steht senkrecht hinter der Luke
            var lid = Prim(PrimitiveType.Cylinder, parent, new Color(0.2f, 0.21f, 0.18f));
            lid.name = "Luke offen";
            lid.transform.position = exit - transform.forward * 0.32f + Vector3.up * 0.3f;
            lid.transform.rotation = Quaternion.FromToRotation(Vector3.up, transform.forward);   // Scheibe steht senkrecht
            lid.transform.localScale = new Vector3(0.6f, 0.025f, 0.6f);
        }

        IEnumerator Climb(Vector3 exit, float sideSign)
        {
            var man = new GameObject("Besatzung (ausgestiegen)");
            Destroy(man, 150f);
            var uniform = new Color(0.22f, 0.24f, 0.19f);
            var body = Prim(PrimitiveType.Capsule, man.transform, uniform);
            body.transform.localScale = new Vector3(0.42f, 0.55f, 0.32f);   // 1,1 m Rumpf+Beine
            body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            var head = Prim(PrimitiveType.Sphere, man.transform, new Color(0.1f, 0.1f, 0.09f));
            head.transform.localScale = Vector3.one * 0.26f;
            head.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            var face = Prim(PrimitiveType.Sphere, man.transform, new Color(0.75f, 0.58f, 0.46f));
            face.transform.localScale = new Vector3(0.18f, 0.16f, 0.1f);
            face.transform.localPosition = new Vector3(0f, 1.24f, 0.09f);

            // 1. aus der Luke hochziehen
            Vector3 side = transform.right * sideSign;
            man.transform.rotation = Quaternion.LookRotation(side);
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.9f)
            {
                man.transform.position = exit + Vector3.up * Mathf.Lerp(-1.2f, 0f, a);
                yield return null;
            }
            // 2. seitlich runterspringen
            Vector3 land = exit + side * (tc.spec.hullWidth * 0.5f + 1.3f);
            land.y = Ground(land);
            Vector3 start = exit;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.7f)
            {
                Vector3 p = Vector3.Lerp(start, land, a);
                p.y += Mathf.Sin(a * Mathf.PI) * 0.6f;
                man.transform.position = p;
                yield return null;
            }
            // 3. wegrennen (weg vom Wrack, leicht nach hinten), dann hinlegen und Deckung suchen
            Vector3 dir = (side - transform.forward * 0.6f + Random.insideUnitSphere * 0.3f);
            dir.y = 0f; dir.Normalize();
            man.transform.rotation = Quaternion.LookRotation(dir);
            float run = Random.Range(18f, 30f), done = 0f;
            Vector3 pos = land;
            while (done < run)
            {
                float step = 4.2f * Time.deltaTime;
                done += step; pos += dir * step;
                float bob = Mathf.Abs(Mathf.Sin(done * 2.6f)) * 0.08f;
                man.transform.position = new Vector3(pos.x, Ground(pos) + bob, pos.z);
                yield return null;
            }
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.5f)
            {
                man.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(Mathf.Lerp(0f, 82f, a), 0f, 0f);
                man.transform.position = new Vector3(pos.x, Ground(pos) + Mathf.Lerp(0f, 0.15f, a), pos.z);
                yield return null;
            }
        }
    }
}
