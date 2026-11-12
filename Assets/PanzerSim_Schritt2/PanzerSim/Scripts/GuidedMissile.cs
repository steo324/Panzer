using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Drahtgelenkte Panzerabwehrrakete TOW (halbautomatische Lenkung, SACLOS):
    /// Die Rakete fliegt auf die Visierlinie des Zielgeraets - der Richtschuetze haelt nur das Fadenkreuz aufs Ziel,
    /// die Lenkung bringt die Rakete selbst dorthin. Ablauf wie beim Original:
    ///  - Startmotor stoesst die Rakete mit ~70 m/s aus dem Rohr, nach einigen Metern zuendet der Marschmotor
    ///    (1,6 s, bis ~320 m/s), danach wird sie langsamer (TOW-2: ~20 s fuer 3,75 km)
    ///  - hinten leuchtet der Leitstrahler (das Zielgeraet verfolgt ihn), zwei duenne Draehte fuehren zum Werfer
    ///  - Drahtende (3 / 3,75 km), Werfer zerstoert oder Richtschuetze ausgefallen -> ungelenkt
    ///  - Einschlag: Hohlladung ueber das normale Granatensystem (Server rechnet den Schaden)
    /// Ghost = Anzeige bei anderen Spielern im Mehrspieler (Position kommt vom Schuetzen).
    /// </summary>
    public class GuidedMissile : MonoBehaviour
    {
        public TankController owner;
        public ShellData data;
        public int ammoIndex;
        public bool ghost;
        public float flightTime;
        public float Distance => owner != null ? Vector3.Distance(launchPos, transform.position) : 0f;
        public bool Guided { get; private set; } = true;

        Vector3 vel, launchPos, ghostTarget;
        float speed;
        bool done;
        LineRenderer wire;
        float smokeTimer;
        static Material bodyMat, wireMat;

        const float EjectSpeed = 70f, BurnStart = 0.15f, BurnTime = 1.6f, PeakSpeed = 320f, Drag = 0.0003f;
        const float MaxTurnDeg = 28f;   // max. Kursaenderung pro Sekunde

        public static GuidedMissile Launch(TankController tc, int ammoIndex)
        {
            var d = AmmoLibrary.All[ammoIndex];
            var go = new GameObject("TOW " + d.name);
            var m = go.AddComponent<GuidedMissile>();
            m.owner = tc; m.data = d; m.ammoIndex = ammoIndex;
            Vector3 dir = tc.parts.gunPivot.forward;
            go.transform.SetPositionAndRotation(tc.parts.muzzle.position, Quaternion.LookRotation(dir));
            m.launchPos = go.transform.position;
            m.speed = EjectSpeed;
            m.vel = dir * EjectSpeed + (tc.rb != null ? tc.rb.linearVelocity : Vector3.zero);
            m.Setup();
            LaunchEffects(tc);
            return m;
        }

        /// <summary>Mehrspieler: Anzeige einer fremden Rakete (folgt der uebertragenen Position).</summary>
        public static GuidedMissile CreateGhost(TankController tc, Vector3 pos)
        {
            var go = new GameObject("TOW (Anzeige)");
            var m = go.AddComponent<GuidedMissile>();
            m.owner = tc; m.ghost = true; m.data = AmmoLibrary.All[16];
            go.transform.position = pos; m.ghostTarget = pos;
            m.launchPos = tc != null && tc.parts.muzzle != null ? tc.parts.muzzle.position : pos;
            m.Setup();
            if (tc != null) LaunchEffects(tc);
            return m;
        }

        public void SetGhostTarget(Vector3 p) => ghostTarget = p;
        public void Remove() { done = true; Destroy(gameObject); }

        static void LaunchEffects(TankController tc)
        {
            if (NetSync.Headless || tc.parts.muzzle == null) return;
            Vector3 p = tc.parts.muzzle.position, d = tc.parts.gunPivot.forward;
            Effects.Muzzle(p, d, 45f, false);                       // Startmotor vorne
            Effects.SmokePuff(p - d * 1.6f, 1.6f, 2.5f, 6);         // Rueckstrahl hinten aus dem Rohr
            Sfx.Cannon(p, 60f);
        }

        void Setup()
        {
            if (NetSync.Headless) return;
            // Rakete: Rumpf 1,17 m x 152 mm, vier Steuerfluegel hinten, vier Tragfluegel in der Mitte
            if (bodyMat == null) bodyMat = GameMap.Mat(new Color(0.30f, 0.33f, 0.24f));
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(0.152f, 0.585f, 0.152f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;
            var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(transform, false);
            nose.transform.localPosition = new Vector3(0f, 0f, 0.585f);
            nose.transform.localScale = new Vector3(0.15f, 0.15f, 0.2f);
            nose.GetComponent<Renderer>().sharedMaterial = bodyMat;
            for (int k = 0; k < 4; k++)
            {
                var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(fin.GetComponent<Collider>());
                fin.transform.SetParent(transform, false);
                fin.transform.localRotation = Quaternion.Euler(0f, 0f, 45f + k * 90f);
                fin.transform.localPosition = fin.transform.localRotation * new Vector3(0f, 0.16f, 0f) + new Vector3(0f, 0f, 0.05f);
                fin.transform.localScale = new Vector3(0.008f, 0.17f, 0.12f);
                fin.GetComponent<Renderer>().sharedMaterial = bodyMat;
            }
            // Draht vom Werfer zur Rakete
            if (wireMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                wireMat = new Material(sh) { color = new Color(0.05f, 0.05f, 0.05f, 0.9f) };
            }
            wire = gameObject.AddComponent<LineRenderer>();
            wire.sharedMaterial = wireMat;
            wire.positionCount = 2;
            wire.startWidth = wire.endWidth = 0.004f;
            wire.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wire.receiveShadows = false;
        }

        // ---------------- Flug (Schuetze rechnet, Anzeigen folgen) ----------------
        void FixedUpdate()
        {
            if (done || ghost) return;
            float dt = Time.fixedDeltaTime;
            flightTime += dt;
            if (flightTime > 25f) { Explode(transform.position, vel.normalized, false); return; }

            // Antrieb: Ausstoss -> Marschmotor -> Ausrollen (Luftwiderstand)
            if (flightTime < BurnStart) speed = EjectSpeed;
            else if (flightTime < BurnStart + BurnTime) speed = Mathf.Lerp(EjectSpeed, PeakSpeed, (flightTime - BurnStart) / BurnTime);
            else speed -= Drag * speed * speed * dt;

            // Lenkung: auf die Visierlinie (Zielgeraet -> Fadenkreuz) zufliegen
            Vector3 p = transform.position;
            Guided = owner != null && !owner.destroyed && owner.parts.gunPivot != null && owner.parts.turret != null &&
                     owner.parts.turret.parent != null && owner.Working(ModuleType.Gunner) && Distance < data.wireRange;
            Vector3 dir = vel.normalized;
            if (Guided && flightTime > 0.25f)
            {
                Vector3 o = owner.SightPosition, los = owner.parts.gunPivot.forward;
                float along = Vector3.Dot(p - o, los);
                Vector3 aimAt = o + los * (along + Mathf.Max(40f, speed * 0.5f));
                Vector3 want = (aimAt - p).normalized;
                dir = Vector3.RotateTowards(dir, want, MaxTurnDeg * Mathf.Deg2Rad * dt, 0f);
                vel = dir * speed;
            }
            else
            {
                // ungelenkt: faellt, bremst
                vel = vel.normalized * speed + Physics.gravity * dt;
                speed = vel.magnitude;
                dir = vel.normalized;
            }

            Vector3 step = vel * dt;
            if (Physics.Raycast(p, step.normalized, out RaycastHit hit, step.magnitude, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore))
            {
                var tank = hit.collider.GetComponentInParent<TankController>();
                if (tank != owner || flightTime > 0.5f) { Explode(hit.point, dir, true); return; }
            }
            transform.position = p + step;
            transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>Gefechtskopf: als Hohlladungsgranate kurz vor dem Ziel starten (normales Treffersystem).</summary>
        void Explode(Vector3 at, Vector3 dir, bool hit)
        {
            if (done) return;
            done = true;
            Vector3 start = at - dir * 0.6f;
            if (hit)
            {
                if (owner != null && owner.net != null && NetSync.Active) owner.net.RequestWarhead(ammoIndex, start, dir);
                else Shell.Fire(data, start, dir);
            }
            else Effects.Explosion(at, data.fillerKg * 0.3f, false);
            if (owner != null && owner.activeMissile == this) owner.activeMissile = null;
            Destroy(gameObject);
        }

        // ---------------- Anzeige ----------------
        void Update()
        {
            if (done) return;
            if (ghost)
            {
                // fremde Rakete: weich zur uebertragenen Position
                Vector3 d = ghostTarget - transform.position;
                if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
                transform.position = d.magnitude > 60f ? ghostTarget : Vector3.Lerp(transform.position, ghostTarget, 1f - Mathf.Exp(-20f * Time.deltaTime));
                flightTime += Time.deltaTime;
            }
            if (NetSync.Headless) return;
            Vector3 fwd = transform.forward, tail = transform.position - fwd * 0.6f;
            // Leitstrahler im Heck: grell weiss-gelb, auch auf weite Entfernung gut zu sehen
            Tracers.Streak(tail, fwd, 0.5f, 0.14f, new Color(3.2f, 2.8f, 2.0f, 1f));
            // Rauchspur, solange der Marschmotor brennt
            if (flightTime > BurnStart && flightTime < BurnStart + BurnTime + 0.3f)
            {
                smokeTimer -= Time.deltaTime;
                if (smokeTimer <= 0f) { smokeTimer = 0.03f; Effects.SmokePuff(tail, 0.35f, 1.6f, 1); }
            }
            if (wire != null)
            {
                Vector3 from = owner != null && owner.parts.muzzle != null ? owner.parts.muzzle.position : launchPos;
                wire.SetPosition(0, from); wire.SetPosition(1, tail);
                wire.enabled = ghost || Guided;
            }
        }
    }
}
