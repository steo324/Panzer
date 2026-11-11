using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Panzerung aus der Form des 3D-Modells: Jedes Dreieck hat eine Zone (Bug, Seite, Dach ...)
    /// mit eigener Dicke. Der Treffer liefert das getroffene Dreieck, daraus Dicke und Winkel.
    /// </summary>
    public class ArmorMesh : ArmorPlate
    {
        public struct Zone
        {
            public string name;
            public float mm;
            public ArmorMaterial mat;
            public Zone(string name, float mm, ArmorMaterial mat) { this.name = name; this.mm = mm; this.mat = mat; }
        }

        public Zone[] zones;
        public byte[] triZone;     // Zone je Dreieck

        public override void GetArmor(RaycastHit hit, out float mm, out ArmorMaterial mat, out string lab)
        {
            int t = hit.triangleIndex;
            if (zones != null && triZone != null && t >= 0 && t < triZone.Length)
            {
                var z = zones[triZone[t]];
                mm = z.mm; mat = z.mat; lab = z.name;
                return;
            }
            base.GetArmor(hit, out mm, out mat, out lab);
        }
    }

    /// <summary>Baut die Mesh-Panzerung aus dem Modell und laesst sie den Gelenken folgen.</summary>
    public class ArmorMeshFollower : MonoBehaviour
    {
        class Part
        {
            public Transform bone;    // Gelenk im Modell, dem dieses Teil folgt
            public Transform obj;     // unser Kollisionsobjekt
            public MeshRenderer view; // farbige Ansicht (Dicke)
        }

        readonly List<Part> parts = new List<Part>();
        public TankController owner;
        static bool layersSetUp;
        static Material viewMat;

        public static void SetupLayers()
        {
            if (layersSetUp) return;
            layersSetUp = true;
            // Mesh-Panzerung stoesst mit nichts zusammen, sie ist nur fuer Treffer da
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(ArmorPlate.MeshArmorLayer, i, true);
        }

        public static ArmorMeshFollower Build(TankController tc, Transform model)
        {
            SetupLayers();
            // groesstes Mesh nehmen (Wanne+Turm), nicht Kleinteile wie Nebelwerfer
            SkinnedMeshRenderer smr = null;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (r.sharedMesh != null && (smr == null || r.sharedMesh.vertexCount > smr.sharedMesh.vertexCount)) smr = r;
            if (smr == null || smr.sharedMesh == null) return BuildFromMeshes(tc, model); // Modell ohne Gelenke (einzelne Teile)
            Mesh mesh = smr.sharedMesh;
            if (!mesh.isReadable) { Debug.LogWarning("Mesh-Panzerung: Modell-Mesh ist nicht lesbar (Read/Write)."); return null; }

            var s = tc.spec;
            Transform root = tc.transform;
            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;
            BoneWeight[] bw = mesh.boneWeights;
            Matrix4x4[] bind = mesh.bindposes;
            Transform[] bones = smr.bones;
            if (bw == null || bw.Length != verts.Length) { Debug.LogWarning("Mesh-Panzerung: keine Gelenk-Gewichte."); return null; }

            // Welches Gelenk gehoert zu welchem Teil? 0 = Wanne, 1 = Turm, 2 = Kanone, -1 = weglassen
            int boneCount = bones.Length;
            int[] boneToPart = new int[boneCount];
            int hullBone = -1, turretBone = -1, gunBone = -1;
            for (int b = 0; b < boneCount; b++)
            {
                string n = bones[b] != null ? bones[b].name : "";
                if (n == s.modelTurretBone || n.Contains("hatch") && IsChildOf(bones[b], s.modelTurretBone)) boneToPart[b] = 1;
                else if (n == s.modelGunBone) boneToPart[b] = 2;
                else if (n.StartsWith("wheeltrack") || n.StartsWith("antenna") || (!string.IsNullOrEmpty(s.wheelBoneLeft) && (n.StartsWith(s.wheelBoneLeft) || n.StartsWith(s.wheelBoneRight)))) boneToPart[b] = -1;
                else boneToPart[b] = 0;
                if (n == s.modelTurretBone) turretBone = b;
                if (n == s.modelGunBone) gunBone = b;
            }
            // Wannen-Gelenk: das oberste Gelenk (Wurzel des Skeletts)
            for (int b = 0; b < boneCount && hullBone < 0; b++)
                if (boneToPart[b] == 0 && (bones[b].parent == null || System.Array.IndexOf(bones, bones[b].parent) < 0)) hullBone = b;
            if (hullBone < 0) hullBone = 0;
            int[] mainBone = { hullBone, turretBone >= 0 ? turretBone : hullBone, gunBone >= 0 ? gunBone : hullBone };

            var go = new GameObject("Panzerung (Modellform) " + s.name);
            var follower = go.AddComponent<ArmorMeshFollower>();
            follower.owner = tc;

            // Lage von Turm-/Kanonengelenk im Panzer (fuer die Zonen)
            float hullTop = s.clearance + s.hullHeight;
            float gunZ = gunBone >= 0 ? root.InverseTransformPoint(bones[gunBone].position).z : s.hullLength / 2f;

            var zones = MakeZones(s);

            for (int part = 0; part < 3; part++)
            {
                int mb = mainBone[part];
                if (mb < 0 || bones[mb] == null) continue;
                Matrix4x4 toBone = bind[mb];
                Matrix4x4 boneToWorld = bones[mb].localToWorldMatrix;

                var newIndex = new Dictionary<int, int>();
                var pv = new List<Vector3>();
                var pn = new List<Vector3>();   // Normalen (nach aussen), fuer die Ansicht
                var tris = new List<int>();
                var tz = new List<byte>();

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] idx = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                        int p0 = boneToPart[Dominant(bw[a])];
                        if (p0 != part) continue;

                        // Lage im Panzer fuer die Zone
                        Vector3 wa = boneToWorld.MultiplyPoint3x4(toBone.MultiplyPoint3x4(verts[a]));
                        Vector3 wb = boneToWorld.MultiplyPoint3x4(toBone.MultiplyPoint3x4(verts[b]));
                        Vector3 wc = boneToWorld.MultiplyPoint3x4(toBone.MultiplyPoint3x4(verts[c]));
                        Vector3 cen = root.InverseTransformPoint((wa + wb + wc) / 3f);
                        Vector3 n = norms != null && norms.Length == verts.Length
                            ? (norms[a] + norms[b] + norms[c]) : Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                        n = root.InverseTransformDirection(boneToWorld.MultiplyVector(toBone.MultiplyVector(n))).normalized;

                        byte zone = Classify(part, cen, n, s, hullTop, gunZ);

                        tris.Add(Map(a, newIndex, pv, pn, verts, norms, toBone));
                        tris.Add(Map(b, newIndex, pv, pn, verts, norms, toBone));
                        tris.Add(Map(c, newIndex, pv, pn, verts, norms, toBone));
                        tz.Add(zone);
                    }
                }
                if (tris.Count == 0) continue;

                follower.AddPart(tc, zones, part, pv, pn, tris, tz, bones[mb]);
            }

            follower.Sync();
            return follower;
        }

        static ArmorMesh.Zone[] MakeZones(TankSpec s)
        {
            return new[]
            {
                new ArmorMesh.Zone("Wanne Bug oben", s.hullFront.mm, s.hullFront.mat),         // 0
                new ArmorMesh.Zone("Wanne Bug unten", Or(s.hullFrontLower, s.hullFront).mm, Or(s.hullFrontLower, s.hullFront).mat), // 1
                new ArmorMesh.Zone("Wanne Bug-Schraegplatte", Or(s.hullGlacis, s.hullRoof).mm, Or(s.hullGlacis, s.hullRoof).mat), // 2
                new ArmorMesh.Zone("Wanne Seite oben", s.hullSide.mm, s.hullSide.mat),          // 3
                new ArmorMesh.Zone("Wanne Seite unten", Or(s.hullLowerSide, s.hullSide).mm, Or(s.hullLowerSide, s.hullSide).mat), // 4
                new ArmorMesh.Zone("Wanne Heck", s.hullRear.mm, s.hullRear.mat),                // 5
                new ArmorMesh.Zone("Wanne Dach", s.hullRoof.mm, s.hullRoof.mat),                // 6
                new ArmorMesh.Zone("Wanne Boden", s.hullBottom.mm, s.hullBottom.mat),           // 7
                new ArmorMesh.Zone("Turm Front", s.turretFront.mm, s.turretFront.mat),          // 8
                new ArmorMesh.Zone("Turm Seite", s.turretSide.mm, s.turretSide.mat),            // 9
                new ArmorMesh.Zone("Turm Heck", s.turretRear.mm, s.turretRear.mat),             // 10
                new ArmorMesh.Zone("Turm Dach", s.turretRoof.mm, s.turretRoof.mat),             // 11
                new ArmorMesh.Zone("Blende", s.mantlet.mm, s.mantlet.mat),                      // 12
                new ArmorMesh.Zone("Kanonenrohr", 30f, ArmorMaterial.RHA),                      // 13
            };
        }

        void AddPart(TankController tc, ArmorMesh.Zone[] zones, int part, List<Vector3> pv, List<Vector3> pn, List<int> tris, List<byte> tz, Transform bone)
        {
            var colMesh = new Mesh { name = "Panzerung " + part };
            if (pv.Count > 65000) colMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            colMesh.SetVertices(pv);
            colMesh.SetTriangles(tris, 0);
            colMesh.RecalculateBounds();

            var obj = new GameObject(part == 0 ? "Wanne" : part == 1 ? "Turm" : "Kanone");
            obj.layer = ArmorPlate.MeshArmorLayer;
            obj.transform.SetParent(transform, false);
            var mc = obj.AddComponent<MeshCollider>();
            mc.sharedMesh = colMesh;
            var am = obj.AddComponent<ArmorMesh>();
            am.zones = zones;
            am.triZone = tz.ToArray();
            am.ownerTank = tc;
            am.label = obj.name;
            am.thicknessMm = 20f;

            // Ansicht: Dicke als Farbe (blau = duenn ... rot = dick)
            var view = new GameObject("Ansicht");
            view.layer = ArmorPlate.MeshArmorLayer;
            view.transform.SetParent(obj.transform, false);
            view.AddComponent<MeshFilter>().sharedMesh = ColorMesh(pv, pn, tris, tz, zones);
            var mr = view.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ViewMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.enabled = false;

            parts.Add(new Part { bone = bone, obj = obj.transform, view = mr });
        }

        /// <summary>
        /// Modell ohne Gelenke: einzelne Teile (Wanne, Turm, Kanone) als eigene Objekte.
        /// Jedes Mesh wird in den Raum seines Teils (Modell, Turm oder Kanone) umgerechnet.
        /// </summary>
        static ArmorMeshFollower BuildFromMeshes(TankController tc, Transform model)
        {
            var s = tc.spec;
            Transform root = tc.transform;
            Transform turretT = FindChild(model, s.modelTurretBone);
            Transform gunT = FindChild(model, s.modelGunBone);
            Transform[] space = { model, turretT != null ? turretT : model, gunT != null ? gunT : (turretT != null ? turretT : model) };

            var pv = new[] { new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            var pn = new[] { new List<Vector3>(), new List<Vector3>(), new List<Vector3>() };
            var tris = new[] { new List<int>(), new List<int>(), new List<int>() };
            var tz = new[] { new List<byte>(), new List<byte>(), new List<byte>() };
            float hullTop = s.clearance + s.hullHeight;
            float gunZ = gunT != null ? root.InverseTransformPoint(gunT.position).z : s.hullLength / 2f;
            Vector3 gunPos = gunT != null ? root.InverseTransformPoint(gunT.position) : new Vector3(0f, hullTop, gunZ);
            int used = 0;

            // 1. Durchgang: welche Meshes gehoeren zu welchem Teil, und wo ist die Mitte jedes Teils?
            var meshParts = new List<(MeshFilter mf, int part)>();
            var bounds = new Bounds[3]; var hasBounds = new bool[3];
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                string n = mf.name;
                if (!string.IsNullOrEmpty(s.wheelBoneLeft) && (n.StartsWith(s.wheelBoneLeft) || n.StartsWith(s.wheelBoneRight) || n.StartsWith("arm"))) continue; // Laufrollen sind keine Panzerung
                if (!string.IsNullOrEmpty(s.returnRollerLeft) && (n.StartsWith(s.returnRollerLeft) || n.StartsWith(s.returnRollerRight))) continue; // Stuetzrollen auch nicht
                if (!mesh.isReadable) { Debug.LogWarning("Mesh-Panzerung: " + n + " ist nicht lesbar."); continue; }
                int p = gunT != null && (mf.transform == gunT || mf.transform.IsChildOf(gunT)) ? 2
                      : turretT != null && (mf.transform == turretT || mf.transform.IsChildOf(turretT)) ? 1 : 0;
                meshParts.Add((mf, p));
                Matrix4x4 m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    if (!hasBounds[p]) { bounds[p] = new Bounds(corner, Vector3.zero); hasBounds[p] = true; } else bounds[p].Encapsulate(corner);
                }
            }

            // 2. Durchgang: Dreiecke uebernehmen
            foreach (var (mf, part) in meshParts)
            {
                var mesh = mf.sharedMesh;
                Matrix4x4 toPart = space[part].worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Vector3[] v = mesh.vertices, nr = mesh.normals;
                bool hasN = nr != null && nr.Length == v.Length;
                int baseIndex = pv[part].Count;
                for (int i = 0; i < v.Length; i++)
                {
                    pv[part].Add(toPart.MultiplyPoint3x4(v[i]));
                    pn[part].Add(hasN ? toPart.MultiplyVector(nr[i]).normalized : Vector3.zero);
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] idx = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                        Vector3 ra = toRoot.MultiplyPoint3x4(v[a]), rb = toRoot.MultiplyPoint3x4(v[b]), rc = toRoot.MultiplyPoint3x4(v[c]);
                        Vector3 cen = (ra + rb + rc) / 3f;
                        Vector3 fn = Vector3.Cross(rb - ra, rc - ra);   // Vorderseite (Unity: nur sie wird von Strahlen getroffen)
                        if (fn.sqrMagnitude < 1e-12f) continue;          // entartetes Dreieck
                        // Manche Modelle haben Flaechen, deren Vorderseite nach innen zeigt: Granaten flogen dort einfach hindurch.
                        // Darum jedes Dreieck nach aussen drehen - weg von der Mitte seines Teils (Rohr: weg von der Rohrachse).
                        Vector3 outward = part == 2 ? cen - gunPos : cen - bounds[part].center;
                        if (part == 2 && cen.z - gunZ >= 0.8f) outward.z = 0f;
                        // Nur drehen, wenn auch die Normalen des Modells nach innen zeigen - an konkaven Stellen (Nischen, Kanten)
                        // stimmt "weg von der Mitte" nicht, dort haben richtig gebaute Modelle passende Normalen.
                        bool normalsInward = !hasN || Vector3.Dot(toRoot.MultiplyVector(nr[a] + nr[b] + nr[c]), outward) < 0f;
                        if (s.meshArmorFixWinding && Vector3.Dot(fn, outward) < 0f && normalsInward) { int t = b; b = c; c = t; fn = -fn; }
                        // Ansicht: Normalen dieses Dreiecks ebenfalls nach aussen (sonst erscheint es dunkel)
                        Vector3 fnPart = toPart.MultiplyVector(mf.transform.worldToLocalMatrix.MultiplyVector(root.TransformDirection(fn))).normalized;
                        foreach (int q in new[] { a, b, c })
                            if (Vector3.Dot(pn[part][baseIndex + q], fnPart) < 0f) pn[part][baseIndex + q] = -pn[part][baseIndex + q];
                        tris[part].Add(baseIndex + a); tris[part].Add(baseIndex + b); tris[part].Add(baseIndex + c);
                        tz[part].Add(Classify(part, cen, fn.normalized, s, hullTop, gunZ));
                    }
                }
                used++;
            }
            if (used == 0) { Debug.LogWarning("Mesh-Panzerung: keine lesbaren Teile im Modell."); return null; }

            var go = new GameObject("Panzerung (Modellform) " + s.name);
            var follower = go.AddComponent<ArmorMeshFollower>();
            follower.owner = tc;
            var zones = MakeZones(s);
            for (int part = 0; part < 3; part++)
                if (tris[part].Count > 0) follower.AddPart(tc, zones, part, pv[part], pn[part], tris[part], tz[part], space[part]);
            follower.Sync();
            return follower;
        }

        static Transform FindChild(Transform t, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindChild(c, name); if (f != null) return f; }
            return null;
        }

        static Armor Or(Armor a, Armor fallback) => a.mm >= 0f ? a : fallback;

        static bool IsChildOf(Transform t, string parentName)
        {
            for (var p = t.parent; p != null; p = p.parent) if (p.name == parentName) return true;
            return false;
        }

        static int Dominant(BoneWeight w)
        {
            int b = w.boneIndex0; float m = w.weight0;
            if (w.weight1 > m) { m = w.weight1; b = w.boneIndex1; }
            if (w.weight2 > m) { m = w.weight2; b = w.boneIndex2; }
            if (w.weight3 > m) { b = w.boneIndex3; }
            return b;
        }

        static int Map(int i, Dictionary<int, int> map, List<Vector3> pv, List<Vector3> pn, Vector3[] verts, Vector3[] norms, Matrix4x4 toBone)
        {
            if (map.TryGetValue(i, out int j)) return j;
            j = pv.Count;
            pv.Add(toBone.MultiplyPoint3x4(verts[i]));  // Lage im Gelenk-Raum
            pn.Add(norms != null && i < norms.Length ? toBone.MultiplyVector(norms[i]).normalized : Vector3.zero);
            map[i] = j;
            return j;
        }

        /// <summary>Zone eines Dreiecks aus Lage (cen) und Ausrichtung (n) im Panzer: z = vorne, y = oben.</summary>
        static byte Classify(int part, Vector3 cen, Vector3 n, TankSpec s, float hullTop, float gunZ)
        {
            if (part == 2)
            {
                // Kanone: dicker Teil nahe am Drehpunkt = Blende, Rest = Rohr
                return (byte)(cen.z - gunZ < 0.8f ? 12 : 13);
            }
            if (part == 1)
            {
                if (n.y > 0.75f) return 11;
                if (n.z > 0.6f) return 8;
                if (n.z < -0.6f) return 10;
                return 9;
            }
            // Wanne
            float L = s.hullLength;
            if (n.y < -0.8f) return 7;
            if (n.y > 0.8f)
                return (byte)(cen.z > L / 2f - 1.6f && cen.y < hullTop - 0.1f ? 2 : 6); // stark schraege Bugplatte vorne / Dach
            if (n.z > 0.5f) return (byte)(cen.y > s.clearance + s.hullHeight * 0.55f ? 0 : 1);
            if (n.z < -0.5f) return 5;
            // Seite: weit aussen = Aufbau ueber den Ketten, weiter innen = untere Wanne hinter den Laufrollen
            return (byte)(Mathf.Abs(cen.x) > s.hullWidth / 2f - 0.35f ? 3 : 4);
        }

        static Material ViewMaterial()
        {
            if (viewMat != null) return viewMat;
            var sh = Shader.Find("Sprites/Default");
            viewMat = new Material(sh);
            viewMat.renderQueue = 3100; // nach dem Modell zeichnen
            return viewMat;
        }

        public static Color ThicknessColor(float mm)
        {
            // 0 mm blau -> 40 gruen -> 80 gelb -> 120+ rot
            float t = Mathf.Clamp01(mm / 120f);
            Color c = t < 0.33f ? Color.Lerp(new Color(0.2f, 0.4f, 1f), Color.green, t / 0.33f)
                    : t < 0.66f ? Color.Lerp(Color.green, Color.yellow, (t - 0.33f) / 0.33f)
                    : Color.Lerp(Color.yellow, Color.red, (t - 0.66f) / 0.34f);
            c.a = 0.8f;
            return c;
        }

        static Mesh ColorMesh(List<Vector3> pv, List<Vector3> pn, List<int> tris, List<byte> tz, ArmorMesh.Zone[] zones)
        {
            var v = new List<Vector3>(tris.Count);
            var col = new List<Color>(tris.Count);
            var idx = new List<int>(tris.Count);
            // Etwas nach aussen schieben, damit die Farbschicht nicht mit dem Modell flackert (Z-Fighting)
            const float lift = 0.012f;
            for (int i = 0; i < tris.Count; i++)
            {
                v.Add(pv[tris[i]] + pn[tris[i]] * lift);
                col.Add(ThicknessColor(zones[tz[i / 3]].mm));
                idx.Add(i);
            }
            var m = new Mesh { name = "Panzerung Ansicht" };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetColors(col);
            m.SetTriangles(idx, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Kollisionsobjekte auf die Gelenke des Modells setzen.</summary>
        public void Sync()
        {
            foreach (var p in parts)
            {
                if (p.bone == null || p.obj == null) continue;
                p.obj.SetPositionAndRotation(p.bone.position, p.bone.rotation);
                p.obj.localScale = p.bone.lossyScale;
            }
        }

        public void SetView(bool on)
        {
            foreach (var p in parts) if (p.view != null) p.view.enabled = on;
        }

        void LateUpdate() { Sync(); }
        void FixedUpdate() { Sync(); }
    }
}
