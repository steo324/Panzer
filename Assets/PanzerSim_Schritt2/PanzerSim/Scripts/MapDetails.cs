using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Macht eine Karte detailreicher: setzt nach festen Regeln Kleinkram (Props) und Schilder (SignKit) entlang der Strassen,
    /// in den Orten, an Hoefen, Feld- und Waldraendern. Jede Karte liefert nur ihre Daten (Context); alles aus fester Zufallszahl.
    /// </summary>
    public static class MapDetails
    {
        public class Town { public string name, sub; public Vector2 c; public float r; public bool sign = true; }

        public class Context
        {
            public float half;
            public int seed = 1;
            public int signStyle = 0;             // 0 Deutschland, 1 Schweiz, 2 alt (Holz)
            public bool modern = true;            // Leitpfosten, Bushaltestellen, Verkehrszeichen, Laternen
            public bool powerLines = true;
            public List<Roads.Road> roads = new List<Roads.Road>();
            public List<Town> towns = new List<Town>();
            public List<(string name, Vector2 p)> destinations = new List<(string, Vector2)>();   // Ziele fuer Wegweiser (auch ausserhalb der Karte)
            public List<Vector3> houses = new List<Vector3>();   // x, z, Radius
            public List<Vector2> farms = new List<Vector2>();
            public System.Func<float, float, float> groundY;
            public System.Func<float, float, bool> free;         // hier darf etwas stehen (kein Wasser, kein Wald, keine Strasse, kein Haus)
            public System.Func<float, float, bool> meadow;       // Wiese/Weide (Heuballen, Steine, Buesche)
            public System.Func<float, float, bool> forest;       // Wald (Holzstapel und Hochsitze am Rand)
            public System.Func<float, float, bool> inTown;       // optional: im Ort (sonst Kreise der Orte)
        }

        public static void Decorate(Transform root, Context c)
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nodetails") >= 0) { Debug.Log("[Details] aus (-nodetails)"); return; }   // Leistungsvergleich
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rng = new System.Random(c.seed);
            var props = Props.Create(root);
            var signs = new SignKit(c.signStyle);
            var wires = new List<List<(Vector3, float)>>();
            int logged = 0;
            System.Func<float, float, bool> inTown = c.inTown ?? ((x, z) => { foreach (var t in c.towns) if ((new Vector2(x, z) - t.c).sqrMagnitude < t.r * t.r) return true; return false; });
            bool Inside(Vector2 p) => Mathf.Abs(p.x) < c.half - 8f && Mathf.Abs(p.y) < c.half - 8f;
            Vector3 G(Vector2 p) => new Vector3(p.x, c.groundY(p.x, p.y) - 0.05f, p.y);
            float Yaw(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

            var houseGrid = new Dictionary<long, List<Vector3>>();
            foreach (var h in c.houses) { long k = Key(h.x, h.y, 50f); if (!houseGrid.TryGetValue(k, out var l)) { l = new List<Vector3>(); houseGrid[k] = l; } l.Add(h); }
            bool NearHouseFast(Vector2 p, float pad)
            {
                int gx = Mathf.FloorToInt(p.x / 50f), gz = Mathf.FloorToInt(p.y / 50f);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                        if (houseGrid.TryGetValue(((long)(gx + dx) << 32) ^ (uint)(gz + dz), out var l))
                            foreach (var h in l) if ((p.x - h.x) * (p.x - h.x) + (p.y - h.y) * (p.y - h.y) < (h.z + pad) * (h.z + pad)) return true;
                return false;
            }

            // ---------- entlang der Strassen ----------
            var junctions = new List<(Vector2 p, Vector2 dirIn, int kindMinor, int kindMajor)>();
            var roadPts = new Dictionary<long, List<(int r, int i)>>();
            for (int ri = 0; ri < c.roads.Count; ri++)
                for (int i = 0; i < c.roads[ri].p.Count; i++) { var p = c.roads[ri].p[i]; long k = Key(p.x, p.y, 20f); if (!roadPts.TryGetValue(k, out var l)) { l = new List<(int, int)>(); roadPts[k] = l; } l.Add((ri, i)); }
            int Nearest(Vector2 p, int self, float maxD, out int idx)
            {
                idx = -1; int best = -1; float bd = maxD * maxD;
                int gx = Mathf.FloorToInt(p.x / 20f), gz = Mathf.FloorToInt(p.y / 20f);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                        if (roadPts.TryGetValue(((long)(gx + dx) << 32) ^ (uint)(gz + dz), out var l))
                            foreach (var (r, i) in l) { if (r == self) continue; float d = (c.roads[r].p[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = r; idx = i; } }
                return best;
            }

            for (int ri = 0; ri < c.roads.Count; ri++)
            {
                var rd = c.roads[ri]; var pts = rd.p;
                if (pts.Count < 3) continue;
                float hw = Roads.HalfWidth[rd.kind];
                bool paved = rd.kind == Roads.Haupt || rd.kind == Roads.Neben;
                float acc = 0f, nextLeit = 25f, nextLamp = 10f, nextPole = (float)rng.NextDouble() * 40f;
                bool power = c.powerLines && paved && (rd.kind == Roads.Haupt || rng.NextDouble() < 0.45) && pts.Count > 60;
                float poleSide = rng.NextDouble() < 0.5 ? -1f : 1f;
                List<(Vector3, float)> line = null;
                Vector3 lastPole = Vector3.zero;
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    acc += Vector2.Distance(pts[i - 1], pts[i]);
                    Vector2 p = pts[i], d = (pts[i + 1] - pts[i - 1]).normalized, s = new Vector2(-d.y, d.x);   // s = links
                    bool town = inTown(p.x, p.y);
                    // Leitpfosten (ausserorts, alle 50 m beidseitig)
                    if (c.modern && paved && !town && acc >= nextLeit)
                    {
                        nextLeit = acc + 50f;
                        foreach (float sg in new[] { -1f, 1f })
                        {
                            Vector2 q = p + s * sg * (hw + Roads.Shoulder[rd.kind] + 0.3f);
                            if (Inside(q)) props.Add(Props.T.Leitpfosten, G(q), Yaw(-d * sg) , 1f);
                        }
                    }
                    // Laternen im Ort (alle 32 m, wechselnde Seite)
                    if (c.modern && rd.kind != Roads.Feldweg && town && acc >= nextLamp)
                    {
                        nextLamp = acc + 32f;
                        float sg = ((int)(acc / 32f) & 1) == 0 ? 1f : -1f;
                        Vector2 q = p + s * sg * (hw + 1.2f);
                        if (Inside(q) && !NearHouseFast(q, 0.5f)) props.Add(Props.T.Laterne, G(q), Yaw(-s * sg));
                    }
                    // Strommasten (Holz, alle ~45 m) mit Leitung
                    if (power && acc >= nextPole)
                    {
                        nextPole = acc + 42f + (float)rng.NextDouble() * 8f;
                        Vector2 q = p + s * poleSide * (hw + 7f);
                        bool ok = Inside(q) && c.free(q.x, q.y) && !NearHouseFast(q, 3f);
                        if (ok)
                        {
                            Vector3 gp = G(q);
                            float yaw = Yaw(s);   // Traverse quer zur Strasse
                            props.Add(Props.T.Strommast, gp, yaw);
                            if (line == null || Vector3.Distance(lastPole, gp) > 75f) { line = new List<(Vector3, float)>(); wires.Add(line); }
                            line.Add((gp, yaw)); lastPole = gp;
                        }
                        else line = null;
                    }
                }
                // Einmuendung: Ende einer untergeordneten Strasse auf einer wichtigeren -> Vorfahrt achten / Stopp
                foreach (int end in new[] { 0, pts.Count - 1 })
                {
                    int other = Nearest(pts[end], ri, Roads.HalfWidth[Roads.Haupt] + 3f, out int oi);
                    if (other < 0) continue;
                    if (c.roads[other].kind > rd.kind) continue;   // nur auf gleichrangige/wichtigere
                    if (oi < 3 || oi > c.roads[other].p.Count - 4) continue;   // Weg geht dort nur weiter (geteilte OSM-Wege), keine Einmuendung
                    int inner = end == 0 ? Mathf.Min(4, pts.Count - 1) : Mathf.Max(pts.Count - 5, 0);
                    Vector2 dirIn = (pts[end] - pts[inner]).normalized;
                    junctions.Add((pts[end], dirIn, rd.kind, c.roads[other].kind));
                }
            }
            // Verkehrszeichen an Einmuendungen
            if (c.modern)
                foreach (var j in junctions)
                {
                    if (j.kindMinor == Roads.Feldweg && j.kindMajor == Roads.Feldweg) continue;
                    if (j.kindMinor == Roads.Feldweg && rng.NextDouble() < 0.7) continue;
                    if (inTown(j.p.x, j.p.y) && j.kindMajor != Roads.Haupt) continue;   // im Ort rechts vor links (keine Schilder)
                    Vector2 right = new Vector2(j.dirIn.y, -j.dirIn.x);
                    Vector2 q = j.p - j.dirIn * (Roads.HalfWidth[j.kindMajor] + 7f) + right * (Roads.HalfWidth[j.kindMinor] + 1.5f);
                    if (!Inside(q) || NearHouseFast(q, 0.5f)) continue;
                    signs.Traffic(G(q), -j.dirIn, rng.NextDouble() < 0.2 ? SignKit.Stopp : SignKit.VorfahrtAchten);
                    if (j.kindMajor <= Roads.Neben && rng.NextDouble() < 0.5)
                    {
                        // auf der Vorfahrtstrasse: Raute kurz vor der Kreuzung (nur ein Schild, Richtung zufaellig)
                        Vector2 along = new Vector2(-j.dirIn.y, j.dirIn.x) * (rng.NextDouble() < 0.5 ? 1f : -1f);
                        Vector2 q2 = j.p - along * 40f + new Vector2(along.y, -along.x) * (Roads.HalfWidth[j.kindMajor] + 1.6f);
                        if (Inside(q2) && !NearHouseFast(q2, 0.5f)) signs.Traffic(G(q2), -along, SignKit.Vorfahrtstrasse);
                    }
                    if (rng.NextDouble() < 0.25 && !inTown(j.p.x, j.p.y))
                    {
                        Vector2 q3 = j.p + right * 9f - j.dirIn * 4f;
                        if (Inside(q3) && c.free(q3.x, q3.y)) props.Add(Props.T.Wegkreuz, G(q3), Yaw(-j.dirIn));
                    }
                }

            // ---------- Ortseingaenge: Ortsschild, nach dem Ortsende Wegweiser und Tempo ----------
            // Gehoert ein Punkt zum Ort t? Kreis um den Ort, oder (mit inTown) bebaute Flaeche, deren naechster Ort t ist
            bool InT(Town t, Vector2 p)
            {
                if (c.inTown == null) return (p - t.c).sqrMagnitude < t.r * t.r;
                if ((p - t.c).sqrMagnitude > t.r * t.r || !c.inTown(p.x, p.y)) return false;
                foreach (var o in c.towns) if (o != t && o.sign && (p - o.c).sqrMagnitude / (o.r * o.r) < (p - t.c).sqrMagnitude / (t.r * t.r)) return false;   // relativ zur Ortsgroesse
                return true;
            }
            foreach (var t in c.towns)
            {
                if (!t.sign) continue;
                var done = new List<Vector2>();
                foreach (var rd in c.roads)
                {
                    if (rd.kind == Roads.Feldweg || rd.kind == Roads.Pflaster && c.signStyle != 2) continue;
                    if (rd.kind != Roads.Haupt && rd.p.Count < 75) continue;   // Ortsschilder nur an Durchgangsstrassen (nicht an jeder Wohnstrasse)
                    var pts = rd.p;
                    for (int i = 1; i < pts.Count - 1; i++)
                    {
                        bool a = InT(t, pts[i - 1]), b = InT(t, pts[i]);
                        if (a == b) continue;
                        if (c.inTown != null)
                        {
                            // Ortsrand aus der Bebauung: Zustand muss je 40 m davor und dahinter stabil sein
                            bool stable = true;
                            for (int k = 1; k <= 10 && stable; k++)
                            {
                                if (i - 1 - k >= 0 && InT(t, pts[i - 1 - k]) != a) stable = false;
                                if (i + k < pts.Count && InT(t, pts[i + k]) != b) stable = false;
                            }
                            if (!stable) continue;
                        }
                        Vector2 p = pts[i];
                        bool near = false; foreach (var q0 in done) if ((q0 - p).sqrMagnitude < 90f * 90f) near = true;
                        if (near || !Inside(p)) continue;
                        done.Add(p);
                        // Fahrtrichtung in den Ort hinein
                        Vector2 dIn = (b ? pts[i] - pts[i - 1] : pts[i - 1] - pts[i]).normalized;
                        Vector2 right = new Vector2(dIn.y, -dIn.x);
                        float hw = Roads.HalfWidth[rd.kind];
                        Vector2 q = p + right * (hw + 1.8f);
                        if (!NearHouseFast(q, 0.5f)) { signs.Place(G(q), -dIn, t.name, t.sub); if (logged++ < 12) Debug.Log($"[Details] Ortsschild {t.name} bei {q.x:0} {q.y:0}, Blick {Yaw(dIn):0}"); }
                        // auf der Gegenseite fuer die Ausfahrenden: Wegweiser zum naechsten Ort in Fahrtrichtung
                        Vector2 dOut = -dIn;
                        Vector2 qs = p - dIn * 25f + new Vector2(dOut.y, -dOut.x) * (hw + 2.2f);
                        string dest = Destination(c, t, p, dOut, out float km);
                        if (dest != null && !NearHouseFast(qs, 0.5f) && Inside(qs))
                        {
                            var plates = new List<(Vector2, string)> { (dOut, $"{dest} {km:0.#}".Replace('.', ',')) };
                            string dest2 = Destination(c, t, p, dOut, out float km2, dest);
                            if (dest2 != null) plates.Add((dOut, $"{dest2} {km2:0.#}".Replace('.', ',')));
                            signs.Signpost(G(qs), plates);
                        }
                        if (c.modern && rng.NextDouble() < 0.5)
                        {
                            Vector2 qt = p + dOut * 110f + new Vector2(dOut.y, -dOut.x) * (hw + 1.6f);
                            if (Inside(qt) && !inTown(qt.x, qt.y)) signs.Traffic(G(qt), -dOut, rd.kind == Roads.Haupt ? SignKit.Tempo100 : SignKit.Tempo70);
                        }
                        if (c.modern && rng.NextDouble() < 0.35)
                        {
                            Vector2 q30 = p + dIn * 70f + right * (hw + 1.6f);
                            if (Inside(q30) && !NearHouseFast(q30, 0.5f)) signs.Traffic(G(q30), -dIn, SignKit.Tempo30);
                        }
                    }
                }
                // im Ort: Bushaltestelle an der Hauptstrasse, Baenke, Briefkaesten
                if (c.modern)
                {
                    Vector2 best = Vector2.zero; float bd = 1e9f; Vector2 bdir = Vector2.up; int bkind = Roads.Neben;
                    foreach (var rd in c.roads)
                    {
                        if (rd.kind > Roads.Neben) continue;
                        for (int i = 2; i < rd.p.Count - 2; i += 2)
                        {
                            float dd = Vector2.Distance(rd.p[i], t.c);
                            if (dd > 25f && dd < bd && dd < t.r * 0.8f) { bd = dd; best = rd.p[i]; bdir = (rd.p[i + 1] - rd.p[i - 1]).normalized; bkind = rd.kind; }
                        }
                    }
                    if (bd < 1e8f)
                        foreach (float sg in new[] { -1f, 1f })
                        {
                            Vector2 s = new Vector2(-bdir.y, bdir.x) * sg;
                            Vector2 q = best + s * (Roads.HalfWidth[bkind] + 2.2f) + bdir * 15f * sg;
                            if (!NearHouseFast(q, 1.5f)) props.Add(Props.T.Haltestelle, G(q), Yaw(-s));
                        }
                }
            }
            // Baenke: im Ort am Strassenrand, ausserhalb an Feldwegen
            foreach (var rd in c.roads)
            {
                float hw = Roads.HalfWidth[rd.kind];
                for (int i = 3; i < rd.p.Count - 3; i += 7)
                {
                    Vector2 p = rd.p[i];
                    bool town = inTown(p.x, p.y);
                    double chance = town ? 0.03 : rd.kind == Roads.Feldweg ? 0.012 : 0.0;
                    if (rng.NextDouble() > chance) continue;
                    Vector2 d = (rd.p[i + 1] - rd.p[i - 1]).normalized, s = new Vector2(-d.y, d.x) * (rng.NextDouble() < 0.5 ? 1f : -1f);
                    Vector2 q = p + s * (hw + 1.8f);
                    if (Inside(q) && !NearHouseFast(q, 1f) && (town || c.free(q.x, q.y))) props.Add(Props.T.Bank, G(q), Yaw(-s));
                }
            }
            // Briefkaesten an Wohnhaeusern nahe der Strasse
            foreach (var h in c.houses)
            {
                if (h.z > 11f || rng.NextDouble() > 0.35) continue;
                int r = Nearest(new Vector2(h.x, h.y), -1, h.z + 22f, out int idx);
                if (r < 0 || c.roads[r].kind == Roads.Feldweg && !c.modern) continue;
                var rp = c.roads[r].p[idx];
                Vector2 toH = (new Vector2(h.x, h.y) - rp).normalized;
                Vector2 q = rp + toH * (Roads.HalfWidth[c.roads[r].kind] + 1.0f);
                if (!NearHouseFast(q, 0.2f) && Inside(q)) props.Add(Props.T.Briefkasten, G(q), Yaw(-toH));
            }

            // ---------- Hoefe: Zaun um eine Weide, Holzstapel, Heuballen ----------
            foreach (var f in c.farms)
            {
                // Weide 40 x 30 m neben dem Hof
                for (int tries = 0; tries < 6; tries++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                    Vector2 pc = f + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 55f;
                    float yaw = (float)rng.NextDouble() * 90f;
                    var q = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 X = q * Vector3.right * 20f, Z = q * Vector3.forward * 15f;
                    Vector2 x2 = new Vector2(X.x, X.z), z2 = new Vector2(Z.x, Z.z);
                    Vector2[] cs = { pc - x2 - z2, pc + x2 - z2, pc + x2 + z2, pc - x2 + z2 };
                    bool ok = true;
                    foreach (var cc in cs) if (!Inside(cc) || !c.free(cc.x, cc.y) || NearHouseFast(cc, 3f)) ok = false;
                    if (!c.free(pc.x, pc.y)) ok = false;
                    if (!ok) continue;
                    for (int e = 0; e < 4; e++)
                    {
                        Vector2 a0 = cs[e], a1 = cs[(e + 1) % 4];
                        float len = Vector2.Distance(a0, a1); Vector2 dir = (a1 - a0) / len;
                        int nSeg = Mathf.FloorToInt(len / 3f);
                        for (int k = 0; k < nSeg; k++)
                        {
                            if (e == 0 && k == nSeg / 2) continue;   // Tor (Luecke)
                            Vector2 sp = a0 + dir * (k * 3f);
                            if (c.free(sp.x, sp.y)) props.Add(Props.T.Zaunfeld, G(sp), Yaw(dir));
                        }
                    }
                    break;
                }
                for (int k = 0; k < 2; k++)
                {
                    Vector2 q = f + new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * 60f;
                    if (Inside(q) && c.free(q.x, q.y) && !NearHouseFast(q, 2f)) props.Add(Props.T.Holzstapel, G(q), (float)rng.NextDouble() * 180f);
                }
                if (c.meadow != null)
                {
                    Vector2 fc = f + new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * 220f;
                    for (int k = 0; k < 9; k++)
                    {
                        Vector2 q = fc + new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * 70f;
                        if (Inside(q) && c.meadow(q.x, q.y) && c.free(q.x, q.y)) props.Add(Props.T.Heuballen, G(q) + Vector3.down * 0.05f, (float)rng.NextDouble() * 180f);
                    }
                }
            }
            // Zaeune an Gaerten im Ort (kurze Stuecke an der Strasse)
            foreach (var h in c.houses)
            {
                if (h.z > 11f || rng.NextDouble() > 0.25) continue;
                int r = Nearest(new Vector2(h.x, h.y), -1, h.z + 25f, out int idx);
                if (r < 0 || idx < 2 || idx > c.roads[r].p.Count - 3) continue;
                var rp = c.roads[r].p[idx];
                Vector2 d = (c.roads[r].p[idx + 1] - c.roads[r].p[idx - 1]).normalized;
                Vector2 toH = (new Vector2(h.x, h.y) - rp); float side = Mathf.Sign(Vector2.Dot(toH, new Vector2(-d.y, d.x)));
                Vector2 basePt = rp + new Vector2(-d.y, d.x) * side * (Roads.HalfWidth[c.roads[r].kind] + 2.0f) - d * 6f;
                for (int k = 0; k < 4; k++)
                {
                    Vector2 sp = basePt + d * (k * 3f);
                    if (!NearHouseFast(sp, 0.3f) && Inside(sp)) props.Add(Props.T.Zaunfeld, G(sp), Yaw(d), new Vector3(1f, 0.75f, 1f));
                }
            }

            // ---------- Feld, Wald, Wiese: Steine, Buesche, Holz am Waldrand, Hochsitze ----------
            int area = Mathf.RoundToInt(c.half * c.half * 4f / 1e6f);   // km2
            for (int k = 0; k < area * 260; k++)
            {
                Vector2 p = new Vector2((float)(rng.NextDouble() * 2 - 1) * (c.half - 20f), (float)(rng.NextDouble() * 2 - 1) * (c.half - 20f));
                if (!c.free(p.x, p.y) || inTown(p.x, p.y)) continue;
                bool edge = c.forest != null && (c.forest(p.x + 12f, p.y) || c.forest(p.x - 12f, p.y) || c.forest(p.x, p.y + 12f) || c.forest(p.x, p.y - 12f));
                double roll = rng.NextDouble();
                if (edge)
                {
                    if (roll < 0.55) props.Add(Props.T.Busch, G(p) + Vector3.down * 0.1f, (float)rng.NextDouble() * 360f, 0.7f + (float)rng.NextDouble() * 0.8f);
                    else if (roll < 0.62) props.Add(Props.T.Holzstapel, G(p), (float)rng.NextDouble() * 180f);
                    else if (roll < 0.64)
                    {
                        // Hochsitz mit Blick aufs offene Feld
                        Vector2 away = c.forest(p.x + 12f, p.y) ? Vector2.left : c.forest(p.x - 12f, p.y) ? Vector2.right : c.forest(p.x, p.y + 12f) ? Vector2.down : Vector2.up;
                        props.Add(Props.T.Hochsitz, G(p), Yaw(away));
                    }
                    else if (roll < 0.75) props.Add(Props.T.Stein, G(p) + Vector3.down * 0.15f, (float)rng.NextDouble() * 360f, 0.5f + (float)rng.NextDouble() * 1.2f);
                }
                else if (c.meadow != null && c.meadow(p.x, p.y))
                {
                    if (roll < 0.10) props.Add(Props.T.Busch, G(p) + Vector3.down * 0.1f, (float)rng.NextDouble() * 360f, 0.6f + (float)rng.NextDouble() * 0.7f);
                    else if (roll < 0.16) props.Add(Props.T.Stein, G(p) + Vector3.down * 0.15f, (float)rng.NextDouble() * 360f, 0.4f + (float)rng.NextDouble() * 1.0f);
                }
            }
            // Buesche an Strassenraendern und Feldwegen
            foreach (var rd in c.roads)
            {
                float hw = Roads.HalfWidth[rd.kind];
                for (int i = 2; i < rd.p.Count - 2; i += 3)
                {
                    if (rng.NextDouble() > (rd.kind == Roads.Feldweg ? 0.06 : 0.035)) continue;
                    Vector2 p = rd.p[i];
                    if (inTown(p.x, p.y)) continue;
                    Vector2 d = (rd.p[i + 1] - rd.p[i - 1]).normalized, s = new Vector2(-d.y, d.x) * (rng.NextDouble() < 0.5 ? 1f : -1f);
                    Vector2 q = p + s * (hw + (rd.kind == Roads.Feldweg ? 2.5f : 6.5f) + (float)rng.NextDouble() * 3f);
                    if (Inside(q) && c.free(q.x, q.y)) props.Add(Props.T.Busch, G(q) + Vector3.down * 0.1f, (float)rng.NextDouble() * 360f, 0.6f + (float)rng.NextDouble() * 0.6f);
                }
            }

            props.Finish();
            Wires.Build(root, wires);
            signs.Build(root);
            Debug.Log($"[Details] {props.Count(Props.T.Strommast)} Masten in {wires.Count} Leitungen, {signs.Signs} Schilder, {sw.ElapsedMilliseconds} ms");
        }

        static long Key(float x, float z, float cell) => ((long)Mathf.FloorToInt(x / cell) << 32) ^ (uint)Mathf.FloorToInt(z / cell);

        /// <summary>Naechstes Ziel in Fahrtrichtung (ca. +-40 Grad), 0,5 bis 15 km entfernt.</summary>
        static string Destination(Context c, Town from, Vector2 p, Vector2 dir, out float km, string except = null)
        {
            float bestKm = 0f; string best = null; float bs = 1e9f;
            void Try(string name, Vector2 q)
            {
                if (name == from.name || name == except) return;
                Vector2 d = q - p; float dist = d.magnitude;
                if (dist < 500f || dist > 40000f) return;
                float cos = Vector2.Dot(d / dist, dir);
                if (cos < 0.75f) return;
                float score = dist / cos;
                if (score < bs) { bs = score; best = name; float k = dist * 1.25f / 1000f; bestKm = k < 3f ? Mathf.Max(0.5f, Mathf.Round(k * 2f) / 2f) : Mathf.Round(k); }
            }
            foreach (var t in c.towns) Try(t.name, t.c);
            foreach (var (name, q) in c.destinations) Try(name, q);
            km = bestKm;
            return best;
        }
    }
}
