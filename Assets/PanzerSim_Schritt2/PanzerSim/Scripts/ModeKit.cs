using UnityEngine;

namespace PanzerSim
{
    /// <summary>Gemeinsame Bausteine der Spielmodi (Duell, Verteidigung): Kreis am Boden, freie Plaetze, Gegnerwahl, Anzeigezeile.</summary>
    public static class ModeKit
    {
        /// <summary>Kreis am Boden (folgt dem Gelaende).</summary>
        public static LineRenderer Ring(Transform parent, GameMap map, Vector3 c, float radius, Color col, float width = 0.9f)
        {
            var go = new GameObject("Kreis");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            int n = Mathf.Clamp(Mathf.RoundToInt(radius * 0.6f), 48, 240);
            lr.positionCount = n; lr.loop = true; lr.useWorldSpace = true; lr.widthMultiplier = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float x = c.x + Mathf.Cos(a) * radius, z = c.z + Mathf.Sin(a) * radius;
                lr.SetPosition(i, new Vector3(x, map.GroundY(x, z) + 0.4f, z));
            }
            lr.sharedMaterial = GameMap.Mat(col);
            return lr;
        }

        /// <summary>Fahnenmast mit Tuch (ohne Collider).</summary>
        public static Renderer Flagpole(Transform parent, Vector3 pos, Color col)
        {
            var g = new GameObject("Fahne").transform;
            g.SetParent(parent, false); g.position = pos;
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(g, false);
            pole.transform.localPosition = new Vector3(0f, 6f, 0f); pole.transform.localScale = new Vector3(0.22f, 6f, 0.22f);
            pole.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.3f, 0.3f, 0.3f));
            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(cloth.GetComponent<Collider>());
            cloth.transform.SetParent(g, false);
            cloth.transform.localPosition = new Vector3(1.75f, 10.6f, 0f); cloth.transform.localScale = new Vector3(3.3f, 2.1f, 0.06f);
            var r = cloth.GetComponent<Renderer>(); r.sharedMaterial = GameMap.Mat(col);
            return r;
        }

        /// <summary>Freier, ebener Platz fuer einen Panzer nahe c (kein Haus/Baum/Panzer, innerhalb der Karte).</summary>
        public static Vector3 FreeSpot(GameMap map, Vector2 c, float search = 80f)
        {
            float lim = map.Extent - 80f;
            Vector2 best = c; float bestScore = float.MaxValue;
            for (int k = 0; k < 40; k++)
            {
                Vector2 q = k == 0 ? c : c + Random.insideUnitCircle * search * Mathf.Min(1f, 0.25f + k / 20f);
                q.x = Mathf.Clamp(q.x, -lim, lim); q.y = Mathf.Clamp(q.y, -lim, lim);
                if (!map.CanSpawn(q.x, q.y)) continue;
                float y = map.GroundY(q.x, q.y);
                float slope = Mathf.Abs(map.GroundY(q.x + 6f, q.y) - map.GroundY(q.x - 6f, q.y)) + Mathf.Abs(map.GroundY(q.x, q.y + 6f) - map.GroundY(q.x, q.y - 6f));
                bool blocked = Physics.CheckBox(new Vector3(q.x, y + 2.6f, q.y), new Vector3(2.6f, 1.6f, 4.4f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                float score = (q - c).magnitude + slope * 20f + (blocked ? 1000f : 0f);
                if (score < bestScore) { bestScore = score; best = q; }
                if (!blocked && slope < 1.5f) break;
            }
            return new Vector3(best.x, map.GroundY(best.x, best.y) + 0.5f, best.y);
        }

        public static Vector2 Dir(float yawDeg) => new Vector2(Mathf.Sin(yawDeg * Mathf.Deg2Rad), Mathf.Cos(yawDeg * Mathf.Deg2Rad));
        public static float Yaw(Vector3 from, Vector3 to) { Vector3 d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        // Gegner nach Staerke (wie im Wellen-Modus)
        public static readonly System.Func<TankSpec>[][] Tiers =
        {
            new System.Func<TankSpec>[] { () => TankLibrary.PanzerIII, () => TankLibrary.PanzerIV, () => TankLibrary.Sherman, () => TankLibrary.T34 },
            new System.Func<TankSpec>[] { () => TankLibrary.T34_85_44, () => TankLibrary.Panther, () => TankLibrary.KV1, () => TankLibrary.Firefly, () => TankLibrary.PanzerIV },
            new System.Func<TankSpec>[] { () => TankLibrary.Tiger, () => TankLibrary.IS1, () => TankLibrary.IS2, () => TankLibrary.T44, () => TankLibrary.Panther },
            new System.Func<TankSpec>[] { () => TankLibrary.TigerII, () => TankLibrary.IS3, () => TankLibrary.T54, () => TankLibrary.T10, () => TankLibrary.T44_100 },
        };
        public static readonly System.Func<TankSpec>[] Modern =
            { () => TankLibrary.T55A, () => TankLibrary.T10M, () => TankLibrary.Leopard2A6, () => TankLibrary.Challenger2 };

        public static TankSpec PickEnemy(int tier, bool modern, float modernChance)
        {
            if (modern && Random.value < modernChance) return Modern[Random.Range(0, Modern.Length)]();
            int t = Mathf.Clamp(tier - (Random.value < 0.35f ? 1 : 0), 0, Tiers.Length - 1);
            return Tiers[t][Random.Range(0, Tiers[t].Length)]();
        }

        /// <summary>KI-Panzer bauen (Einzelspieler).</summary>
        public static TankController SpawnAi(TankSpec spec, Vector3 pos, float yaw, int team, float skill, float extent)
        {
            var t = TankBuilder.Build(spec, pos, Quaternion.Euler(0f, yaw, 0f), false);
            t.team = team;
            t.playerName = $"{spec.name} (KI)";
            TankAI.Attach(t, skill, extent);
            return t;
        }

        /// <summary>Kampfflaeche (Duell) bzw. Stellung (Verteidigung) als Kreis auf Minikarte und grosser Karte.</summary>
        public static void DrawZones(System.Func<Vector3, Vector2> toMap, Rect r, float k, Texture2D dot)
        {
            if (DuelMode.On) Circle(toMap, r, k, dot, DuelMode.Center, DuelMode.Radius, new Color(1f, 0.55f, 0.15f));
            if (DefenseMode.On) Circle(toMap, r, k, dot, DefenseMode.Center, DefenseMode.Radius, new Color(0.3f, 0.95f, 0.35f));
        }

        static void Circle(System.Func<Vector3, Vector2> toMap, Rect r, float k, Texture2D dot, Vector3 c, float radius, Color col)
        {
            GUI.color = col;
            float s = 4f * k;
            Vector2 c0 = toMap(c), c1 = toMap(c + Vector3.right * radius);
            int n = Mathf.Clamp(Mathf.RoundToInt((c1 - c0).magnitude * 0.6f), 16, 120);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector2 q = toMap(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
                if (r.Contains(q)) GUI.DrawTexture(new Rect(q.x - s / 2f, q.y - s / 2f, s, s), dot);
            }
            if (r.Contains(c0)) GUI.DrawTexture(new Rect(c0.x - s, c0.y - s, s * 2f, s * 2f), dot);
            GUI.color = Color.white;
        }

        static GUIStyle banner;
        /// <summary>Zeile oben in der Mitte (wie im Wellen-Modus).</summary>
        public static void Banner(string rich, int line = 0)
        {
            float f = GameSettings.UiFactor;
            if (banner == null) banner = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, richText = true };
            banner.fontSize = Mathf.RoundToInt((line == 0 ? 22 : 17) * f);
            var r = new Rect(Screen.width / 2f - 450f * f, (8f + line * 34f) * f, 900f * f, 36f * f);
            string plain = System.Text.RegularExpressions.Regex.Replace(rich, "<.*?>", "");
            GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), plain, banner);
            GUI.color = line == 0 ? new Color(1f, 0.85f, 0.4f) : Color.white; GUI.Label(r, rich, banner);
            GUI.color = Color.white;
        }
    }
}
