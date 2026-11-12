using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Alle sichtbaren Effekte: Muendungsfeuer, Rauch, Staub, Erdbrocken, Funken, Feuerbaelle, Lichtblitze.
    /// Pro Effektart gibt es nur EIN Partikelsystem in Weltkoordinaten; Effekte "spucken" Partikel hinein (Emit).
    /// Texturen werden im Code erzeugt, es braucht keine Dateien ausser dem Partikel-Shader.
    /// Druckwellen werden an Vegetation (Weizen, Gras, Baeume) und Kamera weitergegeben.
    /// </summary>
    public static class Effects
    {
        static ParticleSystem smoke, dust, dirt, fire, sparks, flash, embers, spall;
        static Material craterMat;
        static readonly Queue<GameObject> craters = new Queue<GameObject>();
        static readonly List<Light> lights = new List<Light>();
        static readonly List<float> lightEnd = new List<float>(), lightPeak = new List<float>(), lightLen = new List<float>();
        static GameObject root;
        static Runner runner;

        // ================= Oeffentliche Effekte =================

        /// <summary>Schuss: Muendungsblitz, Feuerzunge, Rauchwolke, Staub am Boden, Druckwelle.</summary>
        public static void Muzzle(Vector3 pos, Vector3 dir, float caliberMm, bool muzzleBrake)
        {
            if (!Ensure()) return;
            float s = caliberMm / 88f;
            dir.Normalize();
            Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;

            // Blitz und Feuerzunge
            EmitOne(flash, pos + dir * 0.4f * s, Vector3.zero, 3.2f * s, 0.07f, new Color(1f, 0.85f, 0.55f, 1f));
            for (int i = 0; i < 14; i++)
            {
                float t = Random.value;
                Vector3 v = dir * Mathf.Lerp(25f, 60f, t) + Random.insideUnitSphere * 6f;
                EmitOne(fire, pos + dir * t * 1.2f * s, v * s, Mathf.Lerp(1.4f, 0.5f, t) * s, Random.Range(0.06f, 0.14f), new Color(1f, 0.7f, 0.3f, 1f));
            }
            if (muzzleBrake)  // Muendungsbremse: Gas schiesst zur Seite
                for (int k = -1; k <= 1; k += 2)
                    for (int i = 0; i < 6; i++)
                        EmitOne(fire, pos, (side * k * 30f + dir * 8f + Random.insideUnitSphere * 5f) * s, 0.7f * s, 0.08f, new Color(1f, 0.6f, 0.25f, 1f));

            // Rauch: Hauptwolke nach vorne, Kringel um die Muendung
            for (int i = 0; i < 26; i++)
            {
                Vector3 v = dir * Random.Range(4f, 22f) + Random.insideUnitSphere * 3f;
                if (muzzleBrake && i % 3 == 0) v = side * (i % 2 == 0 ? 1 : -1) * Random.Range(6f, 12f) + Random.insideUnitSphere * 2f;
                EmitOne(smoke, pos + Random.insideUnitSphere * 0.3f, v, Random.Range(1.2f, 2.4f) * s, Random.Range(2.5f, 5f), Gray(0.78f, 0.55f));
            }
            AddLight(pos + dir * 1f, new Color(1f, 0.75f, 0.45f), 9f * s, 25f, 0.08f);

            // Staub am Boden unter/vor der Muendung (je naeher am Boden, desto mehr)
            if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 5f, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<ArmorPlate>() == null)
            {
                float near = 1f - hit.distance / 5f;
                Vector3 g = hit.point + new Vector3(dir.x, 0f, dir.z).normalized * 2.5f;
                int n = Mathf.RoundToInt(40 * near * s);
                for (int i = 0; i < n; i++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    radial = (radial + new Vector3(dir.x, 0f, dir.z) * 1.2f).normalized;
                    EmitOne(dust, g + radial * Random.Range(0.5f, 2f), radial * Random.Range(6f, 16f) + Vector3.up * Random.Range(0.3f, 1.5f),
                        Random.Range(1.5f, 3f), Random.Range(2f, 4f), Dust(0.5f * near + 0.15f));
                }
            }
            Blast(pos + dir * 2f, 14f * s, 0.8f);
            CameraShake.Add(pos, 0.5f * s, 30f);
            Sfx.Cannon(pos, caliberMm);
        }

        /// <summary>Splitter (Spall): kleine schwarze Teilchen, die von origin in Richtung dir bis zur Entfernung dist fliegen.</summary>
        public static void Spall(Vector3 origin, Vector3 dir, float dist)
        {
            if (!Ensure()) return;
            float speed = Random.Range(25f, 70f);                    // langsamer als echt, damit man sie sieht
            float life = Mathf.Clamp(dist / speed, 0.03f, 0.6f);     // verschwinden dort, wo der Splitter stecken bleibt
            float g = Random.Range(0.02f, 0.05f);
            EmitOne(spall, origin, dir.normalized * speed, g, life, new Color(0.03f, 0.03f, 0.03f, 1f));
        }

        /// <summary>Kleine Grube im Boden: dunkle Mulde mit aufgeworfenem Rand (bleibt eine Weile liegen).</summary>
        public static void Crater(Vector3 pos, Vector3 normal, float radius, Transform parent = null)
        {
            if (!Ensure()) return;
            const int seg = 18;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // Ringe: Mitte, Mulde, Rand (erhoeht), Auslauf (verblasst)
            float[] rr = { 0f, 0.55f, 1f, 1.6f };
            float[] hh = { 0.01f, 0.015f, 0.12f, 0.01f };
            Color[] cc =
            {
                new Color(0.08f, 0.06f, 0.04f, 0.95f), new Color(0.14f, 0.11f, 0.07f, 0.92f),
                new Color(0.30f, 0.24f, 0.16f, 0.85f), new Color(0.35f, 0.29f, 0.20f, 0f)
            };
            float[] jitter = new float[seg];
            for (int i = 0; i < seg; i++) jitter[i] = Random.Range(0.85f, 1.15f);
            verts.Add(new Vector3(0f, hh[0], 0f)); cols.Add(cc[0]); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int r = 1; r < rr.Length; r++)
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    float rad = rr[r] * radius * jitter[i];
                    verts.Add(new Vector3(Mathf.Cos(a) * rad, hh[r] * radius, Mathf.Sin(a) * rad));
                    cols.Add(cc[r]); uvs.Add(new Vector2(0.5f, 0.5f));
                }
            for (int i = 0; i < seg; i++) { tris.Add(0); tris.Add(1 + (i + 1) % seg); tris.Add(1 + i); }
            for (int r = 1; r < rr.Length - 1; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = 1 + (r - 1) * seg + i, a1 = 1 + (r - 1) * seg + (i + 1) % seg;
                    int b0 = a0 + seg, b1 = a1 + seg;
                    tris.Add(a0); tris.Add(a1); tris.Add(b0);
                    tris.Add(a1); tris.Add(b1); tris.Add(b0);
                }
            var mesh = new Mesh { name = "Grube" };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Grube");
            go.transform.position = pos + normal * 0.02f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            // am Gelaende haengen, damit die Grube mit der Karte verschwindet
            go.transform.SetParent(parent != null ? parent : root.transform, true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = craterMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            craters.Enqueue(go);
            while (craters.Count > 200) { var old = craters.Dequeue(); if (old != null) Object.Destroy(old); }
        }

        /// <summary>Einschlag ohne Sprengladung: Erde/Staub oder Funken auf Panzerstahl.</summary>
        public static void Impact(Vector3 pos, Vector3 normal, Vector3 shellDir, float caliberMm, bool armor, bool penetrated)
        {
            if (!Ensure()) return;
            float s = Mathf.Clamp(caliberMm / 88f, 0.3f, 1.5f);
            if (armor)
            {
                Vector3 refl = Vector3.Reflect(shellDir.normalized, normal);
                int n = penetrated ? 30 : 45;
                for (int i = 0; i < n; i++)
                {
                    Vector3 v = (Vector3.Lerp(normal, refl, 0.6f) + Random.insideUnitSphere * 0.9f).normalized * Random.Range(8f, 35f);
                    EmitOne(sparks, pos, v, Random.Range(0.04f, 0.09f), Random.Range(0.2f, 0.7f), new Color(1f, Random.Range(0.6f, 0.9f), 0.35f, 1f));
                }
                EmitOne(flash, pos + normal * 0.2f, Vector3.zero, 1.6f * s, 0.05f, new Color(1f, 0.9f, 0.7f, 1f));
                for (int i = 0; i < 6; i++)
                    EmitOne(smoke, pos + normal * 0.3f, normal * Random.Range(0.5f, 2f) + Random.insideUnitSphere, Random.Range(0.6f, 1.1f) * s, Random.Range(1.5f, 3f), Gray(0.35f, 0.45f));
                AddLight(pos + normal * 0.5f, new Color(1f, 0.8f, 0.5f), 4f * s, 12f, 0.06f);
                Sfx.SteelHit(pos);
            }
            else
            {
                GroundBurst(pos, normal, 0.6f * s, false);
                Sfx.GroundHit(pos);
            }
        }

        /// <summary>Rauchwolke(n) an einer Stelle (Raketenmotor, Rueckstrahl).</summary>
        public static void SmokePuff(Vector3 pos, float size, float life, int count)
        {
            if (!Ensure()) return;
            for (int i = 0; i < count; i++)
                EmitOne(smoke, pos + Random.insideUnitSphere * size * 0.3f, Random.insideUnitSphere * 0.6f + Vector3.up * 0.3f,
                        size * Random.Range(0.8f, 1.2f), life * Random.Range(0.8f, 1.2f), Gray(0.72f, 0.82f));
        }

        /// <summary>MG-Kugel: auf Stahl ein paar Funken (prallt ab), im Boden ein kleiner Staubspritzer. Bewusst sparsam (15 Schuss/s).</summary>
        public static void BulletHit(Vector3 pos, Vector3 normal, Vector3 dir, bool armor)
        {
            if (!Ensure()) return;
            if (armor)
            {
                Vector3 refl = Vector3.Reflect(dir.normalized, normal);
                for (int i = 0; i < 6; i++)
                {
                    Vector3 v = (Vector3.Lerp(normal, refl, 0.7f) + Random.insideUnitSphere * 0.7f).normalized * Random.Range(6f, 20f);
                    EmitOne(sparks, pos, v, Random.Range(0.02f, 0.05f), Random.Range(0.1f, 0.35f), new Color(1f, Random.Range(0.6f, 0.9f), 0.35f, 1f));
                }
                EmitOne(flash, pos + normal * 0.05f, Vector3.zero, 0.25f, 0.03f, new Color(1f, 0.9f, 0.7f, 1f));
            }
            else
            {
                for (int i = 0; i < 3; i++)
                    EmitOne(smoke, pos + normal * 0.1f, (normal * Random.Range(1f, 3f) + Random.insideUnitSphere * 0.6f), Random.Range(0.25f, 0.45f), Random.Range(0.6f, 1.2f), new Color(0.42f, 0.36f, 0.27f, 0.8f));
            }
        }

        /// <summary>Sprengladung: Feuerball, Rauchpilz, Erde (am Boden), Funken, Licht, Druckwelle.</summary>
        public static void Explosion(Vector3 pos, float fillerKg, bool onGround)
        {
            if (!Ensure()) return;
            float s = Mathf.Clamp(Mathf.Pow(Mathf.Max(fillerKg, 0.01f) * 1000f, 1f / 3f) / 6f, 0.4f, 6f);   // 200 g -> ~1
            EmitOne(flash, pos, Vector3.zero, 5f * s, 0.09f, new Color(1f, 0.85f, 0.6f, 1f));
            for (int i = 0; i < 30; i++)
            {
                Vector3 v = Random.onUnitSphere * Random.Range(3f, 12f) * s;
                if (onGround && v.y < 0f) v.y = -v.y * 0.5f;
                EmitOne(fire, pos + Random.insideUnitSphere * 0.4f * s, v, Random.Range(1.2f, 2.6f) * s, Random.Range(0.25f, 0.6f), new Color(1f, 0.6f, 0.25f, 1f));
            }
            for (int i = 0; i < 40; i++)
            {
                Vector3 v = Random.onUnitSphere * Random.Range(10f, 45f) * Mathf.Sqrt(s);
                if (onGround && v.y < 0f) v.y = -v.y;
                EmitOne(sparks, pos, v, Random.Range(0.05f, 0.1f), Random.Range(0.3f, 1.2f), new Color(1f, 0.7f, 0.3f, 1f));
            }
            for (int i = 0; i < 24; i++)
            {
                Vector3 v = Random.onUnitSphere * Random.Range(1f, 5f) * s + Vector3.up * Random.Range(1f, 4f) * s;
                EmitOne(smoke, pos + Random.insideUnitSphere * s, v, Random.Range(1.5f, 3.2f) * s, Random.Range(4f, 9f), Gray(0.22f, 0.7f));
            }
            if (onGround) GroundBurst(pos, Vector3.up, s, true);
            AddLight(pos + Vector3.up * 0.5f, new Color(1f, 0.6f, 0.3f), 12f * s, 20f * s + 10f, 0.25f);
            Blast(pos, 10f + 12f * s, 1.2f + 0.3f * s);
            CameraShake.Add(pos, 0.8f * s, 60f + 40f * s);
            Sfx.Explosion(pos, fillerKg);
        }

        /// <summary>Munitionsbrand: riesiger Feuerball, Rauchsaeule; danach brennt das Wrack weiter.</summary>
        public static void AmmoDetonation(Vector3 pos, Transform wreck)
        {
            if (!Ensure()) return;
            Explosion(pos, 25f, false);
            for (int i = 0; i < 60; i++)
            {
                Vector3 v = Random.onUnitSphere * Random.Range(4f, 14f) + Vector3.up * Random.Range(6f, 18f);
                EmitOne(fire, pos + Random.insideUnitSphere, v, Random.Range(2.5f, 5f), Random.Range(0.6f, 1.4f), new Color(1f, 0.55f, 0.2f, 1f));
            }
            for (int i = 0; i < 40; i++)
                EmitOne(smoke, pos + Random.insideUnitSphere * 2f, Vector3.up * Random.Range(6f, 16f) + Random.insideUnitSphere * 3f,
                        Random.Range(3f, 6f), Random.Range(8f, 14f), Gray(0.12f, 0.85f));
            if (wreck != null) Burn(wreck, new Vector3(0f, 1.5f, 0f), 90f, 1f);
        }

        /// <summary>Laesst ein Objekt brennen und qualmen (Wrack, Motorbrand).</summary>
        public static BurnEmitter Burn(Transform t, Vector3 localPos, float seconds, float strength)
        {
            if (!Ensure() || t == null) return null;
            var b = t.gameObject.AddComponent<BurnEmitter>();
            b.localPos = localPos; b.timeLeft = seconds; b.strength = strength;
            return b;
        }

        /// <summary>Druckwelle: Weizen/Gras/Baeume biegen sich weg, Kamera wackelt (siehe Vegetation).</summary>
        public static void Blast(Vector3 pos, float radius, float strength) => Vegetation.AddBlast(pos, radius, strength);

        // ================= Bausteine =================

        static void GroundBurst(Vector3 pos, Vector3 normal, float s, bool big)
        {
            int chunks = Mathf.RoundToInt((big ? 50 : 22) * Mathf.Sqrt(s));
            for (int i = 0; i < chunks; i++)
            {
                Vector3 v = (normal * Random.Range(0.8f, 1.6f) + Random.insideUnitSphere * 0.7f).normalized * Random.Range(5f, 18f) * Mathf.Sqrt(s);
                EmitOne(dirt, pos, v, Random.Range(0.06f, 0.22f) * Mathf.Sqrt(s), Random.Range(1f, 2.2f), new Color(0.22f, 0.17f, 0.11f, 1f));
            }
            int clouds = Mathf.RoundToInt((big ? 22 : 12) * Mathf.Sqrt(s));
            for (int i = 0; i < clouds; i++)
            {
                Vector3 v = (normal * Random.Range(0.5f, 2f) + Random.insideUnitSphere).normalized * Random.Range(1.5f, 6f) * s;
                EmitOne(dust, pos + Random.insideUnitSphere * 0.3f, v, Random.Range(1f, 2.2f) * s, Random.Range(2.5f, 6f), Dust(0.75f));
            }
        }

        static Color Gray(float v, float a) => new Color(v, v, v * 0.98f, a);
        static Color Dust(float a) => new Color(0.55f, 0.47f, 0.36f, a);

        static void EmitOne(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startSize = size, startLifetime = life, startColor = c,
                rotation = Random.value * 360f, applyShapeToPosition = false
            };
            ps.Emit(e, 1);
        }

        static void AddLight(Vector3 pos, Color c, float intensity, float range, float duration)
        {
            Light l = null;
            int idx = -1;
            for (int i = 0; i < lights.Count; i++) if (!lights[i].enabled) { l = lights[i]; idx = i; break; }
            if (l == null)
            {
                if (lights.Count >= 12) { idx = 0; l = lights[0]; }
                else
                {
                    var go = new GameObject("Blitzlicht");
                    go.transform.SetParent(root.transform, false);
                    l = go.AddComponent<Light>();
                    l.type = LightType.Point; l.shadows = LightShadows.None;
                    lights.Add(l); lightEnd.Add(0f); lightPeak.Add(0f); lightLen.Add(0f);
                    idx = lights.Count - 1;
                }
            }
            l.transform.position = pos; l.color = c; l.range = range; l.intensity = intensity; l.enabled = true;
            lightEnd[idx] = Time.time + duration; lightPeak[idx] = intensity; lightLen[idx] = duration;
        }

        // ================= Einrichtung =================

        static bool Ensure()
        {
            if (NetSync.Headless) return false;   // Server ohne Grafik: keine Effekte
            if (root != null) return true;
            var sh = Shader.Find("PanzerSim/Partikel");
            if (sh == null) sh = Resources.Load<Shader>("Shaders/Partikel");
            if (sh == null) { Debug.LogWarning("Effekte: Partikel-Shader fehlt."); return false; }
            Vegetation.EnsureInit();
            root = new GameObject("Effekte");
            if (Application.isPlaying) Object.DontDestroyOnLoad(root);   // im Editor-Test (Munitions-Test) nicht erlaubt
            runner = root.AddComponent<Runner>();

            var puff = PuffTexture(64, 7);
            var soft = PuffTexture(64, 0);
            var chunk = ChunkTexture(32);
            var glow = GlowTexture(64);
            var star = StarTexture(128);
            Material alpha(Texture t, float lit) { var m = new Material(sh); m.SetTexture("_BaseMap", t); m.SetFloat("_SrcBlend", 5); m.SetFloat("_DstBlend", 10); m.SetFloat("_Lit", lit); return m; }
            Material add(Texture t) { var m = new Material(sh); m.SetTexture("_BaseMap", t); m.SetFloat("_SrcBlend", 1); m.SetFloat("_DstBlend", 1); m.SetFloat("_Lit", 0); m.renderQueue = 3010; return m; }

            smoke = Make("Rauch", alpha(puff, 0.8f), 4000, -0.035f, 1.2f, Curve(0f, 0.6f, 1f, 3.2f), FadeInOut(0.08f, 0.5f), 0f);
            dust = Make("Staub", alpha(soft, 0.8f), 3000, 0.02f, 1.6f, Curve(0f, 0.5f, 1f, 2.6f), FadeInOut(0.05f, 0.4f), 0f);
            dirt = Make("Erdbrocken", alpha(chunk, 0.6f), 3000, 1f, 0.15f, Curve(0f, 1f, 1f, 1f), FadeInOut(0.01f, 0.85f), 0f);
            fire = Make("Feuer", add(glow), 3000, -0.15f, 2.5f, Curve(0f, 0.7f, 1f, 1.6f), FireColor(), 0f);
            sparks = Make("Funken", add(glow), 3000, 1f, 0.3f, Curve(0f, 1f, 1f, 0.4f), FadeInOut(0.01f, 0.5f), 0f);
            sparks.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Stretch;
            sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.035f;
            sparks.GetComponent<ParticleSystemRenderer>().lengthScale = 1f;
            flash = Make("Blitz", add(star), 200, 0f, 0f, Curve(0f, 0.6f, 1f, 1.2f), FadeInOut(0.01f, 0.4f), 0f);
            embers = Make("Glut", add(glow), 2000, -0.12f, 0.6f, Curve(0f, 1f, 1f, 0.3f), FadeInOut(0.1f, 0.5f), 0.8f);
            spall = Make("Splitter", alpha(chunk, 0.3f), 3000, 0.3f, 0f, Curve(0f, 1f, 1f, 1f), FadeInOut(0.01f, 0.95f), 0f);
            craterMat = alpha(Texture2D.whiteTexture, 0.85f);
            craterMat.renderQueue = 2990;   // vor Rauch/Staub zeichnen
            craters.Clear();
            return true;
        }

        static ParticleSystem Make(string name, Material mat, int max, float gravity, float drag,
                                   AnimationCurve size, Gradient color, float noise)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startSpeed = 0f; main.duration = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var lv = ps.limitVelocityOverLifetime; lv.enabled = drag > 0f; lv.drag = drag; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = true;
            var so = ps.sizeOverLifetime; so.enabled = true; so.size = new ParticleSystem.MinMaxCurve(1f, size);
            var co = ps.colorOverLifetime; co.enabled = true; co.color = color;
            var ro = ps.rotationOverLifetime; ro.enabled = true; ro.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            if (noise > 0f) { var n = ps.noise; n.enabled = true; n.strength = noise; n.frequency = 0.6f; }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 5f;
            ps.Play();
            return ps;
        }

        static AnimationCurve Curve(float t0, float v0, float t1, float v1)
        {
            var c = new AnimationCurve(new Keyframe(t0, v0), new Keyframe(t1, v1));
            return c;
        }

        static Gradient FadeInOut(float inT, float outStart)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inT), new GradientAlphaKey(1f, outStart), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static Gradient FireColor()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.25f),
                              new GradientColorKey(new Color(0.7f, 0.2f, 0.05f), 0.65f), new GradientColorKey(new Color(0.2f, 0.05f, 0.02f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // ---- Texturen ----
        static Texture2D PuffTexture(int n, int seed)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                    if (seed > 0)
                    {
                        float nz = Mathf.PerlinNoise(x * 0.12f + seed * 3.1f, y * 0.12f + seed) * 0.6f + Mathf.PerlinNoise(x * 0.3f + seed, y * 0.3f) * 0.4f;
                        a *= Mathf.Clamp01(nz * 1.6f - 0.15f);
                        float shade = 0.82f + 0.18f * Mathf.Clamp01(v * 0.5f + 0.5f);   // oben heller
                        px[y * n + x] = new Color(shade, shade, shade, a);
                    }
                    else px[y * n + x] = new Color(1f, 1f, 1f, a * 0.9f);
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Texture2D ChunkTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float ang = Mathf.Atan2(v, u);
                    float rr = 0.75f + 0.2f * Mathf.Sin(ang * 3f + 1f) + 0.1f * Mathf.Sin(ang * 7f);
                    float r = Mathf.Sqrt(u * u + v * v) / rr;
                    float a = Mathf.Clamp01((1f - r) * 6f);
                    float sh = 0.7f + 0.3f * Mathf.Clamp01(v);
                    t.SetPixel(x, y, new Color(sh, sh, sh, a));
                }
            t.Apply(true);
            return t;
        }

        static Texture2D GlowTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01(1f - r);
                    a = Mathf.Pow(a, 1.6f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply(true);
            return t;
        }

        static Texture2D StarTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f)), 8f) * 0.6f + Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2f + 0.4f)), 16f) * 0.4f;
                    float core = Mathf.Clamp01(1f - r * 2.2f);
                    float a = Mathf.Clamp01(core + rays * Mathf.Clamp01(1f - r) * 0.8f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply(true);
            return t;
        }

        // ---- Lichtblitze ausblenden ----
        class Runner : MonoBehaviour
        {
            void Update()
            {
                float now = Time.time;
                for (int i = 0; i < lights.Count; i++)
                {
                    var l = lights[i];
                    if (!l.enabled) continue;
                    float left = lightEnd[i] - now;
                    if (left <= 0f) { l.enabled = false; continue; }
                    float f = left / Mathf.Max(lightLen[i], 0.001f);
                    l.intensity = lightPeak[i] * f * f;
                }
            }
        }

        /// <summary>Flammen und Rauch aus einem brennenden Panzer.</summary>
        public class BurnEmitter : MonoBehaviour
        {
            public Vector3 localPos; public float timeLeft, strength = 1f;
            float acc, flicker;
            Light glow;

            void Start()
            {
                var go = new GameObject("Brandlicht");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = localPos + Vector3.up * 0.8f;
                glow = go.AddComponent<Light>();
                glow.type = LightType.Point; glow.color = new Color(1f, 0.5f, 0.2f); glow.range = 14f; glow.shadows = LightShadows.None;
            }

            void Update()
            {
                if (fire == null) return;
                float dt = Time.deltaTime;
                timeLeft -= dt;
                float k = Mathf.Clamp01(timeLeft / 10f) * strength;     // brennt am Ende aus
                if (timeLeft <= -20f) { if (glow != null) Destroy(glow.gameObject); Destroy(this); return; }
                Vector3 p = transform.TransformPoint(localPos);
                acc += dt;
                while (acc > 0.05f)
                {
                    acc -= 0.05f;
                    if (k > 0.02f)
                    {
                        for (int i = 0; i < 2; i++)
                            EmitOne(fire, p + Random.insideUnitSphere * 0.6f, Vector3.up * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 0.6f,
                                    Random.Range(0.8f, 1.6f) * k, Random.Range(0.4f, 0.9f), new Color(1f, 0.55f, 0.2f, 1f));
                        if (Random.value < 0.4f)
                            EmitOne(embers, p, Vector3.up * Random.Range(2f, 5f) + Random.insideUnitSphere, 0.06f, Random.Range(1f, 2.5f), new Color(1f, 0.6f, 0.2f, 1f));
                    }
                    // Rauch auch noch nach dem Feuer (duenner)
                    float sm = Mathf.Max(k, 0.25f * Mathf.Clamp01((timeLeft + 20f) / 20f));
                    EmitOne(smoke, p + Vector3.up * 0.5f + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(2f, 4f) + Random.insideUnitSphere * 0.5f,
                            Random.Range(1.2f, 2.2f) * Mathf.Max(sm, 0.5f), Random.Range(6f, 10f), Gray(0.1f, 0.6f * sm + 0.15f));
                }
                if (glow != null)
                {
                    flicker = Mathf.Lerp(flicker, Random.Range(0.6f, 1.2f), 10f * dt);
                    glow.intensity = 6f * k * flicker;
                }
            }
        }
    }

    /// <summary>Kamera wackelt bei Schuessen und Explosionen in der Naehe.</summary>
    public static class CameraShake
    {
        static float amount;
        public static void Add(Vector3 source, float strength, float range)
        {
            var cam = Camera.main;
            if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, source);
            float f = Mathf.Clamp01(1f - d / range);
            amount = Mathf.Min(1.5f, amount + strength * f * f);
        }

        /// <summary>Wird von der Kamera jeden Frame abgefragt: Verwacklung als Drehung (Grad).</summary>
        public static Vector3 Sample(float dt)
        {
            amount = Mathf.MoveTowards(amount, 0f, dt * 2.5f);
            if (amount <= 0f) return Vector3.zero;
            float t = Time.time * 32f;
            return new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * amount * 2.4f;
        }
    }
}
