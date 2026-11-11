using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Ein Anbauteil am Panzer (Kotfluegel, Werkzeugkasten, Aussentank, Auspuff ...). Kein Panzerschutz:
    /// Granaten reissen es ab (es fliegt mit Physik weg) und fliegen weiter; Sprengtreffer in der Naehe sprengen es weg.
    /// </summary>
    public class DestructiblePart : MonoBehaviour
    {
        public TankController owner;
        public int index = -1;   // feste Nummer am Panzer (gleich auf allen Rechnern, fuer den Mehrspieler)
        public bool broken { get; private set; }

        /// <summary>Teil abreissen. dir = Stossrichtung, speed = Wegfluggeschwindigkeit (m/s).</summary>
        public void Break(Vector3 point, Vector3 dir, float speed)
        {
            if (broken) return;
            if (!NetSync.MayApplyDamage) return;   // Geister-Granate / Client ohne Server-Auftrag
            if (NetSync.ShouldBroadcast(owner)) owner.net.SendPartBreak(index, point, dir, speed);
            broken = true;
            foreach (var c in GetComponentsInChildren<Collider>()) { c.enabled = false; Destroy(c); }   // Treffer-Box sofort weg (auch fuer Strahlen im selben Bild)
            Vector3 vTank = owner != null && owner.rb != null ? owner.rb.GetPointVelocity(point) : Vector3.zero;
            transform.SetParent(null, true);
            gameObject.layer = ArmorPlate.PhysicsOnlyLayer;                     // Granaten fliegen durch Truemmer hindurch
            var mf = GetComponent<MeshFilter>();
            var box = gameObject.AddComponent<BoxCollider>();                    // passt sich an die Mesh-Groesse an
            if (mf != null && mf.sharedMesh != null) { box.center = mf.sharedMesh.bounds.center; box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.03f); }
            var rb = gameObject.AddComponent<Rigidbody>();
            Vector3 s = Vector3.Scale(box.size, transform.lossyScale);
            rb.mass = Mathf.Clamp(s.x * s.y * s.z * 400f, 4f, 250f);
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = vTank + (dir.normalized + Vector3.up * 0.4f).normalized * speed;
#else
            rb.velocity = vTank + (dir.normalized + Vector3.up * 0.4f).normalized * speed;
#endif
            rb.angularVelocity = Random.insideUnitSphere * Mathf.Clamp(speed, 2f, 12f);
            Effects.Impact(point, -dir.normalized, dir, 50f, true, false);
            Destroy(gameObject, 60f);
        }

        /// <summary>Alle Anbauteile im Umkreis einer Explosion wegsprengen (je naeher, desto schneller).</summary>
        public static void BlowOff(Vector3 pos, float fillerKg)
        {
            float radius = 0.6f + 1.4f * Mathf.Pow(Mathf.Max(fillerKg, 0.01f), 1f / 3f);
            var hit = new HashSet<DestructiblePart>();
            foreach (var c in Physics.OverlapSphere(pos, radius, 1 << ArmorPlate.MeshArmorLayer, QueryTriggerInteraction.Ignore))
            {
                var p = c.GetComponentInParent<DestructiblePart>();
                if (p == null || p.broken || !hit.Add(p)) continue;
                Vector3 cp = c.ClosestPoint(pos);
                float k = 1f - Mathf.Clamp01(Vector3.Distance(pos, cp) / radius);
                if (k < 0.15f) continue;
                p.Break(cp, cp - pos + Vector3.up * 0.2f, 4f + 12f * k);
            }
        }
    }

    /// <summary>
    /// Loest beim Laden Anbauteile aus dem Wannen-Mesh eines Modells heraus (nur Modelle aus einzelnen Meshes, kein Skelett):
    /// zusammenhaengende Teile ausserhalb des Wannenkastens, nicht zu gross und nicht im Laufwerk-Bereich.
    /// Luken, Kugelblenden usw. liegen buendig in der Wanne und bleiben Panzerung. Ergebnis wird je Modell zwischengespeichert.
    /// </summary>
    public static class DestructibleBuilder
    {
        class PartDef { public Mesh mesh; public Bounds localBounds; }
        class Result { public Mesh remaining; public List<PartDef> parts = new List<PartDef>(); }
        static readonly Dictionary<Mesh, Result> cache = new Dictionary<Mesh, Result>();

        static readonly string[] Sources = { "hull", "armFest" };

        // ---------------- Skelett-Modelle (z. B. Tiger) ----------------
        class SkinnedPart { public Mesh mesh; public int bone; }
        class SkinnedResult { public Mesh remaining; public List<SkinnedPart> parts = new List<SkinnedPart>(); }
        static readonly Dictionary<Mesh, SkinnedResult> skinnedCache = new Dictionary<Mesh, SkinnedResult>();

        /// <summary>
        /// Anbauteile aus Skelett-Meshes loesen: Teile werden zu festen Meshes am Knochen, der sie bewegt (Wanne oder Turm),
        /// und aus dem Skelett-Mesh entfernt. Kleine eigene Meshes (z. B. Nebelwerfer) werden ganz zum Anbauteil.
        /// </summary>
        static int BuildSkinned(TankController tc, Transform model)
        {
            int made = 0;
            var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            SkinnedMeshRenderer main = null;
            foreach (var r in smrs) if (r.sharedMesh != null && (main == null || r.sharedMesh.vertexCount > main.sharedMesh.vertexCount)) main = r;
            foreach (var smr in smrs)
            {
                var src = smr.sharedMesh;
                if (src == null || !src.isReadable || smr.bones == null || smr.bones.Length == 0) continue;
                bool whole = smr != main && src.vertexCount < 3000;
                if (!skinnedCache.TryGetValue(src, out var res))
                {
                    res = SplitSkinned(tc, smr, whole);
                    skinnedCache[src] = res;
                }
                if (res == null || res.parts.Count == 0) continue;
                var mats = smr.sharedMaterials;
                if (res.remaining != null) smr.sharedMesh = res.remaining; else smr.enabled = false;
                for (int i = 0; i < res.parts.Count; i++)
                {
                    var pd = res.parts[i];
                    Transform bone = smr.bones[pd.bone];
                    var go = new GameObject("anbau_" + smr.name + "_" + i);
                    go.transform.SetParent(bone, false);
                    go.AddComponent<MeshFilter>().sharedMesh = pd.mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                    var hitBox = new GameObject("Treffer-Box");
                    hitBox.layer = ArmorPlate.MeshArmorLayer;
                    hitBox.transform.SetParent(go.transform, false);
                    var bc = hitBox.AddComponent<BoxCollider>();
                    bc.center = pd.mesh.bounds.center;
                    bc.size = Vector3.Max(pd.mesh.bounds.size, Vector3.one * 0.04f);
                    var dp = go.AddComponent<DestructiblePart>(); dp.owner = tc; dp.index = tc.destructibles.Count; tc.destructibles.Add(dp);
                    made++;
                }
            }
            return made;
        }

        static SkinnedResult SplitSkinned(TankController tc, SkinnedMeshRenderer smr, bool whole)
        {
            var s = tc.spec;
            Mesh src = smr.sharedMesh;
            Transform root = tc.transform;
            Vector3[] v = src.vertices;
            BoneWeight[] bw = src.boneWeights;
            Matrix4x4[] bind = src.bindposes;
            Transform[] bones = smr.bones;
            if (bw == null || bw.Length != v.Length) return null;
            int subCount = src.subMeshCount;
            var tris = new int[subCount][];
            for (int sm = 0; sm < subCount; sm++) tris[sm] = src.GetTriangles(sm);

            // Lage im Panzer in der Ruhestellung (gebackenes Mesh, mit Skalierung)
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            Vector3[] bv = baked.vertices;
            Matrix4x4 toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            var rp = new Vector3[bv.Length];
            for (int i = 0; i < bv.Length; i++) rp[i] = toRoot.MultiplyPoint3x4(bv[i]);
            Object.DestroyImmediate(baked);

            int Dom(int i)
            {
                var w = bw[i]; int b = w.boneIndex0; float m = w.weight0;
                if (w.weight1 > m) { m = w.weight1; b = w.boneIndex1; }
                if (w.weight2 > m) { m = w.weight2; b = w.boneIndex2; }
                if (w.weight3 > m) b = w.boneIndex3;
                return b;
            }

            // Zusammenhaengende Teile (verschweisst nach Lage)
            var weld = new int[v.Length];
            var map = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 1000f), Mathf.RoundToInt(v[i].y * 1000f), Mathf.RoundToInt(v[i].z * 1000f));
                if (!map.TryGetValue(key, out int w)) { w = map.Count; map[key] = w; }
                weld[i] = w;
            }
            var parent = new int[map.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int sm = 0; sm < subCount; sm++)
                for (int i = 0; i + 2 < tris[sm].Length; i += 3)
                {
                    int a = Find(weld[tris[sm][i]]), b = Find(weld[tris[sm][i + 1]]), c = Find(weld[tris[sm][i + 2]]);
                    if (a != b) parent[a] = b;
                    b = Find(b); if (Find(c) != b) parent[Find(c)] = b;
                }

            // Je Teil: Grenzen, Dreiecke, Knochen-Stimmen, Dreiecke ausserhalb des Wannenkastens
            float hx = s.hullWidth / 2f - 0.05f, hz = s.hullLength / 2f - 0.15f, top = s.clearance + s.hullHeight + 0.12f, gearTop = s.clearance + 0.3f;
            var info = new Dictionary<int, (Bounds b, int n, int outside, Dictionary<int, int> votes)>();
            for (int sm = 0; sm < subCount; sm++)
                for (int i = 0; i + 2 < tris[sm].Length; i += 3)
                {
                    int ia = tris[sm][i], ib = tris[sm][i + 1], ic = tris[sm][i + 2];
                    int c = Find(weld[ia]);
                    Vector3 p = (rp[ia] + rp[ib] + rp[ic]) / 3f;
                    bool outside = Mathf.Abs(p.x) > hx || Mathf.Abs(p.z) > hz || p.y > top;
                    if (!info.TryGetValue(c, out var e)) e = (new Bounds(rp[ia], Vector3.zero), 0, 0, new Dictionary<int, int>());
                    e.b.Encapsulate(rp[ia]); e.b.Encapsulate(rp[ib]); e.b.Encapsulate(rp[ic]);
                    e.n++; if (outside) e.outside++;
                    int d = Dom(ia); e.votes[d] = e.votes.TryGetValue(d, out int k) ? k + 1 : 1;
                    info[c] = e;
                }
            int BoneOf((Bounds b, int n, int outside, Dictionary<int, int> votes) e) { int best = -1, bn = -1; foreach (var kv in e.votes) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; } return best; }
            string BoneName(int b) => b >= 0 && b < bones.Length && bones[b] != null ? bones[b].name : "";

            // Hauptteile: groesstes Teil am Wannen-Knochen und am Turm-Knochen
            int hullMain = -1, turretMain = -1, hn = -1, tn = -1;
            foreach (var kv in info)
            {
                string bn = BoneName(BoneOf(kv.Value));
                if (bn == s.modelTurretBone) { if (kv.Value.n > tn) { tn = kv.Value.n; turretMain = kv.Key; } }
                else if (kv.Value.n > hn) { hn = kv.Value.n; hullMain = kv.Key; }
            }
            Bounds turretBox = turretMain >= 0 ? info[turretMain].b : new Bounds();

            // Anbauteile auswaehlen und nach Lage gruppieren (je Gruppe ein Knochen)
            var group = new Dictionary<int, int>();          // Teil -> Gruppe
            var groupBone = new List<int>();
            var cellToGroup = new Dictionary<(Vector3Int, int), int>();
            foreach (var kv in info)
            {
                var e = kv.Value;
                int bone = BoneOf(e);
                string bn = BoneName(bone);
                bool take;
                if (whole) take = true;
                else if (kv.Key == hullMain || kv.Key == turretMain) take = false;
                else if (bn.Contains("wheeltrack") || bn.StartsWith("antenna") || bn.Contains("hatch") || bn == s.modelGunBone
                         || (!string.IsNullOrEmpty(s.wheelBoneLeft) && (bn.StartsWith(s.wheelBoneLeft) || bn.StartsWith(s.wheelBoneRight)))) take = false;
                else
                {
                    bool small = e.b.size.magnitude < 1.4f;
                    bool applique = Mathf.Min(e.b.size.x, e.b.size.z) < 0.08f && e.b.size.y > 0.3f && Mathf.Max(e.b.size.x, e.b.size.z) > 0.4f;
                    if (bn == s.modelTurretBone)
                    {
                        // am Turm nur, was seitlich/hinten ueber den Turmkoerper hinausragt (Staukasten, Kettenglieder) - nicht Kuppel/Luken
                        Vector3 c = e.b.center;
                        bool beyond = c.x < turretBox.min.x + 0.05f || c.x > turretBox.max.x - 0.05f || c.z < turretBox.min.z + 0.05f || c.z > turretBox.max.z - 0.05f;
                        take = small && beyond && !applique;
                    }
                    else take = small && e.outside >= e.n * 0.6f && !applique && e.b.center.y > gearTop;
                }
                if (!take) continue;
                Vector3 q = e.b.center;
                var cell = (new Vector3Int(Mathf.FloorToInt(q.x / 0.8f), Mathf.FloorToInt(q.y / 0.8f), Mathf.FloorToInt(q.z / 0.8f)), bone);
                if (!cellToGroup.TryGetValue(cell, out int g)) { g = groupBone.Count; groupBone.Add(bone); cellToGroup[cell] = g; }
                group[kv.Key] = g;
            }
            if (groupBone.Count == 0) return null;

            int groups = groupBone.Count;
            var restTris = new List<int>[subCount];
            var partTris = new List<int>[groups][];
            for (int sm = 0; sm < subCount; sm++) restTris[sm] = new List<int>();
            for (int g = 0; g < groups; g++) { partTris[g] = new List<int>[subCount]; for (int sm = 0; sm < subCount; sm++) partTris[g][sm] = new List<int>(); }
            for (int sm = 0; sm < subCount; sm++)
                for (int i = 0; i + 2 < tris[sm].Length; i += 3)
                {
                    var list = group.TryGetValue(Find(weld[tris[sm][i]]), out int g) ? partTris[g][sm] : restTris[sm];
                    list.Add(tris[sm][i]); list.Add(tris[sm][i + 1]); list.Add(tris[sm][i + 2]);
                }

            var res = new SkinnedResult();
            if (!whole)
            {
                // Skelett-Mesh ohne die Anbauteile (Punkte, Gewichte und Bindposen bleiben gleich)
                var rem = Object.Instantiate(src);
                rem.name = src.name + " (ohne Anbauteile)";
                for (int sm = 0; sm < subCount; sm++) rem.SetTriangles(restTris[sm], sm);
                res.remaining = rem;
            }
            for (int g = 0; g < groups; g++)
            {
                var m = Extract(src, partTris[g], "Anbauteil " + g);
                if (m == null) continue;
                // in den Raum des Knochens umrechnen (Bindpose: Mesh-Raum -> Knochen-Raum)
                Matrix4x4 bp = bind[groupBone[g]];
                var pv = m.vertices; var pn = m.normals; var pt = m.tangents;
                for (int i = 0; i < pv.Length; i++) pv[i] = bp.MultiplyPoint3x4(pv[i]);
                if (pn != null && pn.Length == pv.Length) for (int i = 0; i < pn.Length; i++) pn[i] = bp.MultiplyVector(pn[i]).normalized;
                if (pt != null && pt.Length == pv.Length) for (int i = 0; i < pt.Length; i++) { Vector3 t = bp.MultiplyVector(pt[i]).normalized; pt[i] = new Vector4(t.x, t.y, t.z, pt[i].w); }
                m.vertices = pv; if (pn != null && pn.Length == pv.Length) m.normals = pn; if (pt != null && pt.Length == pv.Length) m.tangents = pt;
                m.RecalculateBounds();
                res.parts.Add(new SkinnedPart { mesh = m, bone = groupBone[g] });
            }
            return res;
        }

        public static int Build(TankController tc, Transform model)
        {
            int made = BuildSkinned(tc, model);
            var s = tc.spec;
            Transform root = tc.transform;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                if (System.Array.IndexOf(Sources, mf.name) < 0) continue;
                var src = mf.sharedMesh;
                if (src == null || !src.isReadable) continue;
                if (!cache.TryGetValue(src, out var res))
                {
                    res = Split(src, root.worldToLocalMatrix * mf.transform.localToWorldMatrix, s, mf.name == "armFest");
                    cache[src] = res;
                }
                if (res == null || res.parts.Count == 0) continue;
                mf.sharedMesh = res.remaining;
                var mr = mf.GetComponent<MeshRenderer>();
                for (int i = 0; i < res.parts.Count; i++)
                {
                    var pd = res.parts[i];
                    var go = new GameObject("anbau_" + mf.name + "_" + i);
                    go.transform.SetParent(mf.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = pd.mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    if (mr != null) r.sharedMaterials = mr.sharedMaterials;
                    // Treffer-Box auf der Panzerungs-Ebene (stoesst mit nichts zusammen, Granaten treffen sie)
                    var hitBox = new GameObject("Treffer-Box");
                    hitBox.layer = ArmorPlate.MeshArmorLayer;
                    hitBox.transform.SetParent(go.transform, false);
                    var bc = hitBox.AddComponent<BoxCollider>();
                    bc.center = pd.localBounds.center;
                    bc.size = Vector3.Max(pd.localBounds.size, Vector3.one * 0.04f);
                    var part = go.AddComponent<DestructiblePart>(); part.owner = tc; part.index = tc.destructibles.Count; tc.destructibles.Add(part);
                    made++;
                }
            }
            return made;
        }

        /// <summary>Mesh in zusammenhaengende Teile zerlegen und Anbauteile herausloesen.</summary>
        /// <param name="attachmentsOnly">Mesh enthaelt nur Anbauten (armFest): keine Hauptwanne, Laufwerkshalter an der Seite bleiben fest.</param>
        static Result Split(Mesh src, Matrix4x4 toRoot, TankSpec s, bool attachmentsOnly)
        {
            Vector3[] v = src.vertices;
            int subCount = src.subMeshCount;
            var tris = new int[subCount][];
            for (int sm = 0; sm < subCount; sm++) tris[sm] = src.GetTriangles(sm);

            // Punkte an gleicher Stelle verschweissen (glTF trennt Punkte an UV-Naehten), dann Union-Find ueber Dreiecke
            var weld = new int[v.Length];
            var map = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 1000f), Mathf.RoundToInt(v[i].y * 1000f), Mathf.RoundToInt(v[i].z * 1000f));
                if (!map.TryGetValue(key, out int w)) { w = map.Count; map[key] = w; }
                weld[i] = w;
            }
            var parent = new int[map.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            for (int sm = 0; sm < subCount; sm++)
                for (int i = 0; i + 2 < tris[sm].Length; i += 3)
                { Union(weld[tris[sm][i]], weld[tris[sm][i + 1]]); Union(weld[tris[sm][i]], weld[tris[sm][i + 2]]); }

            // Teile: Grenzen (Panzer-Koordinaten) und Dreieckszahl
            var comps = new Dictionary<int, (Bounds b, int n)>();
            for (int sm = 0; sm < subCount; sm++)
                for (int i = 0; i + 2 < tris[sm].Length; i += 3)
                {
                    int c = Find(weld[tris[sm][i]]);
                    Vector3 p = toRoot.MultiplyPoint3x4(v[tris[sm][i]]);
                    if (comps.TryGetValue(c, out var e)) { e.b.Encapsulate(p); e.n++; comps[c] = e; }
                    else comps[c] = (new Bounds(p, Vector3.zero), 1);
                }
            int mainComp = -1, mainN = -1;
            if (!attachmentsOnly)   // Wannen-Mesh: das groesste Teil ist die Wanne selbst
                foreach (var kv in comps) if (kv.Value.n > mainN) { mainN = kv.Value.n; mainComp = kv.Key; }

            float hx = s.hullWidth / 2f - 0.05f, hz = s.hullLength / 2f - 0.15f, top = s.clearance + s.hullHeight + 0.12f;
            float gearTop = attachmentsOnly ? s.clearance + s.hullHeight * 0.55f : s.clearance + 0.3f;
            Vector3 Cen(int sm, int i) => toRoot.MultiplyPoint3x4((v[tris[sm][i]] + v[tris[sm][i + 1]] + v[tris[sm][i + 2]]) / 3f);
            bool Outside(Vector3 p) => Mathf.Abs(p.x) > hx || Mathf.Abs(p.z) > hz || p.y > top;
            bool Gear(Vector3 p) => p.y < gearTop && Mathf.Abs(p.z) <= hz;   // seitlich auf Laufwerkshoehe (vorne/hinten darf tiefer)

            // Welche Teile sind Anbauteile?
            //  - Anbau-Mesh (armFest, beim Riggen nur Nicht-Panzerteile): alles ausser dem Laufwerksbereich
            //  - Wannen-Mesh: nur KLEINE Teile (unter 1,4 m), die ueberwiegend ausserhalb des Wannenkastens liegen
            //    (Kaesten, Tanks, Scheinwerfer, Streben). Grosse Wandteile und senkrechte Zusatzplatten bleiben Panzerung.
            var candidate = new HashSet<int>();
            if (!attachmentsOnly)
            {
                var outsideCount = new Dictionary<int, int>();
                for (int sm = 0; sm < subCount; sm++)
                    for (int i = 0; i + 2 < tris[sm].Length; i += 3)
                        if (Outside(Cen(sm, i))) { int c = Find(weld[tris[sm][i]]); outsideCount[c] = outsideCount.TryGetValue(c, out int k) ? k + 1 : 1; }
                foreach (var kv in comps)
                {
                    if (kv.Key == mainComp) continue;
                    Bounds b = kv.Value.b;
                    bool small = b.size.magnitude < 1.4f;
                    bool mostlyOutside = outsideCount.TryGetValue(kv.Key, out int o) && o >= kv.Value.n * 0.6f;
                    bool applique = Mathf.Min(b.size.x, b.size.z) < 0.08f && b.size.y > 0.3f && Mathf.Max(b.size.x, b.size.z) > 0.4f;
                    if (small && mostlyOutside && !applique && b.center.y > gearTop) candidate.Add(kv.Key);
                }
            }

            // Dreiecke in Stuecke von 0,8 m einteilen: kleine Teile bleiben ganz, ein langer Kotfluegel bricht stueckweise ab
            var cellToGroup = new Dictionary<Vector3Int, int>();
            int groups = 0;
            var triGroup = new List<int>[subCount];
            for (int sm = 0; sm < subCount; sm++)
            {
                triGroup[sm] = new List<int>(tris[sm].Length / 3);
                for (int i = 0; i + 2 < tris[sm].Length; i += 3)
                {
                    int g = -1;
                    int comp = Find(weld[tris[sm][i]]);
                    Vector3 p = Cen(sm, i);
                    bool take = attachmentsOnly ? !Gear(p) : candidate.Contains(comp);
                    if (take)
                    {
                        // kleine Teile: Zelle nach Teilmitte (bleibt ganz); grosse Anbauten: Zelle nach Dreieck (Stuecke)
                        Vector3 q = !attachmentsOnly ? comps[comp].b.center : p;
                        var cell = new Vector3Int(Mathf.FloorToInt(q.x / 0.8f), Mathf.FloorToInt(q.y / 0.8f), Mathf.FloorToInt(q.z / 0.8f));
                        if (!cellToGroup.TryGetValue(cell, out g)) { g = groups++; cellToGroup[cell] = g; }
                    }
                    triGroup[sm].Add(g);
                }
            }
            if (groups == 0) return null;

            // Dreiecke verteilen: Rest-Mesh (alles ausser Anbauteilen) und je Gruppe ein Mesh
            var restTris = new List<int>[subCount];
            var partTris = new List<int>[groups][];
            for (int sm = 0; sm < subCount; sm++) restTris[sm] = new List<int>();
            for (int g = 0; g < groups; g++) { partTris[g] = new List<int>[subCount]; for (int sm = 0; sm < subCount; sm++) partTris[g][sm] = new List<int>(); }
            for (int sm = 0; sm < subCount; sm++)
                for (int i = 0, t = 0; i + 2 < tris[sm].Length; i += 3, t++)
                {
                    int g = triGroup[sm][t];
                    var list = g >= 0 ? partTris[g][sm] : restTris[sm];
                    list.Add(tris[sm][i]); list.Add(tris[sm][i + 1]); list.Add(tris[sm][i + 2]);
                }
            var res = new Result { remaining = Extract(src, restTris, src.name + " (ohne Anbauteile)") };
            for (int g = 0; g < groups; g++)
            {
                var m = Extract(src, partTris[g], "Anbauteil " + g);
                if (m != null) res.parts.Add(new PartDef { mesh = m, localBounds = m.bounds });
            }
            return res;
        }

        /// <summary>Neues, kompaktes Mesh aus einer Dreiecksauswahl (je Untermesh), mit Normalen/UVs/Tangenten.</summary>
        static Mesh Extract(Mesh src, List<int>[] tris, string name)
        {
            int total = 0; foreach (var t in tris) total += t.Count;
            if (total == 0) return null;
            Vector3[] v = src.vertices, n = src.normals; Vector4[] tg = src.tangents; Vector2[] uv = src.uv, uv2 = src.uv2; Color[] col = src.colors;
            var remap = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nu = new List<Vector2>(); var nu2 = new List<Vector2>(); var nc = new List<Color>();
            var outTris = new List<int>[tris.Length];
            for (int sm = 0; sm < tris.Length; sm++)
            {
                outTris[sm] = new List<int>(tris[sm].Count);
                foreach (int i in tris[sm])
                {
                    if (!remap.TryGetValue(i, out int j))
                    {
                        j = nv.Count; remap[i] = j;
                        nv.Add(v[i]);
                        if (n != null && n.Length == v.Length) nn.Add(n[i]);
                        if (tg != null && tg.Length == v.Length) nt.Add(tg[i]);
                        if (uv != null && uv.Length == v.Length) nu.Add(uv[i]);
                        if (uv2 != null && uv2.Length == v.Length) nu2.Add(uv2[i]);
                        if (col != null && col.Length == v.Length) nc.Add(col[i]);
                    }
                    outTris[sm].Add(j);
                }
            }
            var m = new Mesh { name = name };
            if (nv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(nv);
            if (nn.Count == nv.Count) m.SetNormals(nn);
            if (nt.Count == nv.Count) m.SetTangents(nt);
            if (nu.Count == nv.Count) m.SetUVs(0, nu);
            if (nu2.Count == nv.Count) m.SetUVs(1, nu2);
            if (nc.Count == nv.Count) m.SetColors(nc);
            m.subMeshCount = tris.Length;
            for (int sm = 0; sm < tris.Length; sm++) m.SetTriangles(outTris[sm], sm);
            m.RecalculateBounds();
            return m;
        }
    }
}
