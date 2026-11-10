using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Haengt ein 3D-Modell (z. B. von Sketchfab) als Optik an einen Panzer.
    /// Die Panzerplatten bleiben unsichtbar darunter und machen die Physik.
    /// Turm- und Kanonen-Gelenk des Modells folgen unserem Turm und unserer Kanone.
    /// </summary>
    public class TankVisual : MonoBehaviour
    {
        TankController tank;
        Transform model, turretBone, gunBone;
        Quaternion turretOffset = Quaternion.identity, gunOffset = Quaternion.identity;
        Vector3 turretBoneLocalPos, gunRestLocal;
        bool gunRestSet;
        Renderer[] renderers;

        class Wheel
        {
            public Transform bone, arm;      // arm: Schwingarm/Feder, federt mit, dreht sich nicht
            public Quaternion armRestRot;
            public bool left, suspended;
            public Vector3 restLocal;        // Lage im Panzer (Wurzel-Koordinaten)
            public Quaternion restRot;       // Drehung relativ zum Panzer
            public float lift;               // aktuelle Einfederung
        }
        readonly System.Collections.Generic.List<Wheel> wheels = new System.Collections.Generic.List<Wheel>();
        Material trackL, trackR;
        TankTracks tracks;
        ArmorMeshFollower meshArmor;
        readonly System.Collections.Generic.List<TankTracks.WheelInfo> roadL = new System.Collections.Generic.List<TankTracks.WheelInfo>();
        readonly System.Collections.Generic.List<TankTracks.WheelInfo> roadR = new System.Collections.Generic.List<TankTracks.WheelInfo>();
        float spinL, spinR, scrollL, scrollR, scale = 1f;
        static readonly string[] TexProps = { "baseColorTexture", "_BaseMap", "_MainTex" };

        public static TankVisual Attach(TankController tc)
        {
            var s = tc.spec;
            if (string.IsNullOrEmpty(s.modelResource)) return null;
            var prefab = Resources.Load<GameObject>(s.modelResource);
            if (prefab == null)
            {
                Debug.LogWarning($"Modell 'Assets/Resources/{s.modelResource}' nicht gefunden (glTFast installiert?). Panzer wird als Kisten gezeigt.");
                return null;
            }
            var v = tc.gameObject.AddComponent<TankVisual>();
            v.Setup(tc, prefab);
            tc.visual = v;
            return v;
        }

        void Setup(TankController tc, GameObject prefab)
        {
            tank = tc;
            var s = tc.spec;
            var go = Instantiate(prefab, tc.transform);
            go.name = "Modell";
            model = go.transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(0f, s.modelYaw, 0f);
            model.localScale = Vector3.one;

            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true; // Turm dreht per Gelenk -> nicht wegschneiden

            // Groesse: Breite des Modells = Breite des Panzers (mit Ketten)
            Bounds b = LocalBounds();
            float wantWidth = s.hullWidth + 2f * s.trackWidth - 2f * s.trackInset;
            if (s.modelScale > 0f) scale = s.modelScale * s.modelScaleMul;
            else if (b.size.x > 0.01f) scale = wantWidth / b.size.x * s.modelScaleMul;
            model.localScale = Vector3.one * scale;

            // Lage: auf dem Boden, mittig, Heck am Wannenheck
            b = LocalBounds();
            model.localPosition += new Vector3(-b.center.x, -b.min.y, -s.hullLength / 2f - b.min.z) + s.modelOffset;

            turretBone = Find(model, s.modelTurretBone);
            gunBone = Find(model, s.modelGunBone);
            var parts = tc.parts;

            if (turretBone != null)
            {
                // Unseren Turm-Drehpunkt unter das Turmgelenk des Modells schieben
                Transform hull = parts.turret.parent;
                Vector3 lp = hull.InverseTransformPoint(turretBone.position);
                parts.turret.localPosition = new Vector3(lp.x, parts.turret.localPosition.y, lp.z);
                turretOffset = Quaternion.Inverse(parts.turret.rotation) * turretBone.rotation;
                turretBoneLocalPos = parts.turret.InverseTransformPoint(turretBone.position);
            }
            else Debug.LogWarning($"Turmgelenk '{s.modelTurretBone}' im Modell nicht gefunden.");

            if (gunBone != null)
                gunOffset = Quaternion.Inverse(parts.gunPivot.rotation) * gunBone.rotation;
            else Debug.LogWarning($"Kanonengelenk '{s.modelGunBone}' im Modell nicht gefunden.");

            SetupWheels(tc);
            SetupTracks();

            // Panzerung aus der Modellform: die Kisten bleiben nur noch fuer Zusammenstoesse
            if (s.meshArmor)
            {
                meshArmor = ArmorMeshFollower.Build(tc, model);
                if (meshArmor != null)
                    foreach (var p in parts.plates)
                        if (p != null && !p.name.StartsWith("Kette")) p.gameObject.layer = ArmorPlate.PhysicsOnlyLayer;
            }
        }

        public void SetArmorView(bool on) { if (meshArmor != null) meshArmor.SetView(on); }

        void OnDestroy() { if (meshArmor != null) Destroy(meshArmor.gameObject); }

        void SetupWheels(TankController tc)
        {
            var s = tc.spec;
            if (string.IsNullOrEmpty(s.wheelBoneLeft) || s.wheelBoneCount < 3) return;
            Transform root = tc.transform;
            var points = new System.Collections.Generic.List<Vector3>();
            var ends = new System.Collections.Generic.List<Vector3>();
            var endHeights = new System.Collections.Generic.List<float>();
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < s.wheelBoneCount; i++)
                {
                    var bone = Find(model, (side == 0 ? s.wheelBoneLeft : s.wheelBoneRight) + i);
                    if (bone == null) continue;
                    var w = new Wheel
                    {
                        bone = bone,
                        left = side == 0,
                        suspended = i > 0 && i < s.wheelBoneCount - 1,
                        restLocal = root.InverseTransformPoint(bone.position),
                        restRot = Quaternion.Inverse(root.rotation) * bone.rotation
                    };
                    w.arm = Find(model, (side == 0 ? "armL" : "armR") + i);
                    if (w.arm != null) w.armRestRot = Quaternion.Inverse(root.rotation) * w.arm.rotation;
                    wheels.Add(w);
                    if (w.suspended) points.Add(w.restLocal);
                    else
                    {
                        // Unterkante der Kette am Antriebs-/Leitrad
                        bool front = i == s.wheelBoneCount - 1;
                        float r = (front ? s.sprocketRadius : s.idlerRadius) * scale + s.trackThickness;
                        ends.Add(w.restLocal + (front ? Vector3.forward : Vector3.back) * r * 0.5f);
                        endHeights.Add(Mathf.Max(0.05f, w.restLocal.y - r * 0.8f));
                    }
                }
            if (points.Count >= 8) tc.SetWheelPoints(points, ends, endHeights); // Physik federt an den echten Laufrollen
            if (s.linkTracks && wheels.Count >= 6) tracks = new TankTracks(tc);
        }

        void SetupTracks()
        {
            var s = tank.spec;
            if (string.IsNullOrEmpty(s.trackMaterialLeft)) return;
            foreach (var r in renderers)
                foreach (var m in r.materials) // eigene Kopien, damit nur dieser Panzer scrollt
                {
                    if (m.name.Contains(s.trackMaterialLeft)) trackL = m;
                    else if (m.name.Contains(s.trackMaterialRight)) trackR = m;
                }
            if (trackL != null) trackL.EnableKeyword("_TEXTURE_TRANSFORM");
            if (trackR != null) trackR.EnableKeyword("_TEXTURE_TRANSFORM");
            if (trackL == null || trackR == null) Debug.LogWarning("Kettenmaterialien im Modell nicht gefunden.");
        }

        void UpdateWheelsAndTracks()
        {
            if (wheels.Count == 0 && trackL == null) return;
            var s = tank.spec;
            Transform root = tank.transform;
            float dt = Time.deltaTime;

            // Kettengeschwindigkeit: beim Drehen laeuft die aeussere Kette schneller
            float halfTrack = (s.hullWidth + s.trackWidth) / 2f - s.trackInset;
            float v = tank.ForwardSpeed, w = tank.YawRate;
            float vL = v + w * halfTrack, vR = v - w * halfTrack;

            float radius = Mathf.Max(s.wheelRadius * scale, 0.05f);
            spinL += vL * dt / radius * Mathf.Rad2Deg;
            spinR += vR * dt / radius * Mathf.Rad2Deg;

            foreach (var wh in wheels)
            {
                Vector3 local = wh.restLocal;
                if (wh.suspended)
                {
                    // Boden unter der Rolle suchen, Rolle folgt dem Boden (begrenzter Federweg)
                    Vector3 o = root.TransformPoint(new Vector3(local.x, 1.2f, local.z));
                    float target = -0.12f;
                    if (tank.RaycastGround(o, -root.up, 2.5f, out RaycastHit hit))
                        target = Mathf.Clamp(root.InverseTransformPoint(hit.point).y, -0.12f, 0.15f);
                    wh.lift = Mathf.Lerp(wh.lift, target, 1f - Mathf.Exp(-20f * dt));
                    local.y += wh.lift;
                }
                float spin = wh.left ? spinL : spinR;
                wh.bone.SetPositionAndRotation(root.TransformPoint(local),
                    root.rotation * Quaternion.AngleAxis(spin, Vector3.right) * wh.restRot);
                if (wh.arm != null)
                    wh.arm.SetPositionAndRotation(root.TransformPoint(local), root.rotation * wh.armRestRot);
            }

            if (tracks != null) DrawLinkTracks(radius);

            float perUV = Mathf.Max(s.trackMetersPerUV * scale, 0.01f);
            scrollL += vL * dt / perUV * s.trackScrollSign;
            scrollR += vR * dt / perUV * s.trackScrollSign;
            scrollL -= Mathf.Floor(scrollL); scrollR -= Mathf.Floor(scrollR);
            SetScroll(trackL, scrollL);
            SetScroll(trackR, scrollR);
        }

        void DrawLinkTracks(float roadRadius)
        {
            var s = tank.spec;
            tracks.UpdateSag();
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                var road = left ? roadL : roadR;
                road.Clear();
                Wheel idler = null, sprocket = null;
                float xSum = 0f;
                foreach (var wh in wheels)
                {
                    if (wh.left != left) continue;
                    if (wh.suspended)
                    {
                        road.Add(new TankTracks.WheelInfo { center = wh.restLocal + Vector3.up * wh.lift, radius = roadRadius });
                        xSum += wh.restLocal.x;
                    }
                    else
                    {
                        if (idler == null || wh.restLocal.z < idler.restLocal.z) idler = wh;
                        if (sprocket == null || wh.restLocal.z > sprocket.restLocal.z) sprocket = wh;
                    }
                }
                if (road.Count < 2 || idler == null || sprocket == null || idler == sprocket) continue;
                var idl = new TankTracks.WheelInfo { center = idler.restLocal, radius = s.idlerRadius * scale };
                var spr = new TankTracks.WheelInfo { center = sprocket.restLocal, radius = s.sprocketRadius * scale };
                tracks.Draw(xSum / road.Count, idl, spr, road, left);
            }
        }

        static void SetScroll(Material m, float v)
        {
            if (m == null) return;
            foreach (var p in TexProps)
                if (m.HasProperty(p)) m.SetTextureOffset(p, new Vector2(0f, v));
        }

        void LateUpdate()
        {
            if (tank == null || tank.parts == null) return;
            var parts = tank.parts;
            if (turretBone != null && parts.turret != null)
            {
                turretBone.SetPositionAndRotation(parts.turret.TransformPoint(turretBoneLocalPos),
                                                  parts.turret.rotation * turretOffset);
            }
            if (gunBone != null && parts.gunPivot != null)
            {
                if (!gunRestSet) { gunRestLocal = gunBone.localPosition; gunRestSet = true; }
                gunBone.localPosition = gunRestLocal;
                gunBone.rotation = parts.gunPivot.rotation * gunOffset;
                if (tank.RecoilOffset > 0.0001f) gunBone.position -= parts.gunPivot.forward * tank.RecoilOffset;   // Rohrruecklauf
            }
            UpdateWheelsAndTracks();
        }

        public void SetVisible(bool on)
        {
            if (renderers == null) return;
            foreach (var r in renderers) if (r != null) r.enabled = on;
            if (tracks != null) tracks.visible = on;
        }

        Bounds LocalBounds()
        {
            Transform root = tank.transform;
            bool any = false;
            Bounds result = new Bounds();
            foreach (var r in renderers)
            {
                Bounds wb = r.bounds;
                Vector3 c = wb.center, e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    Vector3 lc = root.InverseTransformPoint(corner);
                    if (!any) { result = new Bounds(lc, Vector3.zero); any = true; }
                    else result.Encapsulate(lc);
                }
            }
            return result;
        }

        static Transform Find(Transform t, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                var f = Find(child, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
