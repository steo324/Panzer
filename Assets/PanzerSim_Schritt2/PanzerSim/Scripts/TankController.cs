using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Fahrphysik (Kettenantrieb mit Federung), Turm und Kanone, Besatzung und Schaden.
    /// Spieler-Panzer werden mit Tastatur gesteuert, Zielpanzer stehen einfach.
    /// </summary>
    public class TankController : MonoBehaviour
    {
        public TankSpec spec;
        public bool isPlayer;

        // Teams: 0 Blau, 1 Rot, 2 Zielpanzer (fuer alle Gegner). Name steht ueber dem Panzer.
        public const int TeamTarget = 2;
        public int team = TeamTarget;
        public string playerName = "";
        /// <summary>Team des eigenen Spielers (fuer Farben: blau = eigenes Team, rot = Gegner).</summary>
        public static int LocalTeam = 0;
        public bool IsFriendly => team != TeamTarget && team == LocalTeam;
        public string DisplayName => string.IsNullOrEmpty(playerName) ? spec.name : playerName;

        /// <summary>Besatzung steigt aus (Taste J): Panzer gilt als ausgeschaltet, die Leute rennen weg. Danach neu spawnen (N).</summary>
        public void Abandon()
        {
            if (destroyed) return;
            if (NetSync.IsClientOnly) { net?.RequestAbandon(); return; }   // Mehrspieler: Server entscheidet
            SetDestroyed("Besatzung ausgestiegen");
        }

        // Von der Kamera gesetzt
        [HideInInspector] public Vector3 aimPoint;
        [HideInInspector] public float sightRange = 500f;

        public bool destroyed { get; private set; }
        public string destroyedReason { get; private set; }
        public bool burning { get; private set; }
        public int ammoIndex;
        public float reloadLeft;
        public bool hasExtinguisher = true;

        public Rigidbody rb { get; private set; }
        public TankBuilder.Parts parts { get; private set; }

        // Besatzung: welcher Mann sitzt auf welchem Platz
        public readonly Dictionary<ModuleType, TankModule> seats = new Dictionary<ModuleType, TankModule>();
        TankModule swapCandidate;
        ModuleType swapRole;
        float swapTimer;

        readonly List<Vector3> wheelPoints = new List<Vector3>();
        // Hoehe ueber Grund, ab der ein Punkt den Boden beruehrt: 0 = Laufrolle,
        // > 0 = schraeges Kettenstueck am Antriebs-/Leitrad (greift erst an Rampen und Kanten)
        readonly List<float> contactHeights = new List<float>();
        float throttle, steer;
        bool brake;
        float fireTimer;
        GameObject fireFx;
        GameObject detachedTurret;
        float superElevationDeg;

        static readonly ModuleType[] SeatPriority =
            { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander, ModuleType.RadioOperator };

        public static int ViewMode;          // 0 = normal, 1 = Innenteile (Roentgen), 2 = Panzerplatten
        public static bool Xray => ViewMode == 1;
        public string debugInfo = "";
        string lastDriveInfo = "";             // Roentgenblick fuer alle Panzer
        public TankVisual visual;

        // ---- Mehrspieler ----
        /// <summary>Netzwerk-Objekt dieses Panzers (null = Einzelspieler).</summary>
        public NetTank net;
        /// <summary>Fremder Panzer im Mehrspieler: uebertragene Geschwindigkeit (fuer Ketten/Raeder).</summary>
        public Vector3? netVelocity;
        /// <summary>Zerstoerbare Anbauteile in fester Reihenfolge (gleich auf allen Rechnern).</summary>
        public readonly List<DestructiblePart> destructibles = new List<DestructiblePart>();

        public ShellData Ammo => AmmoLibrary.All[ammoIndex];
        /// <summary>Granaten, die dieser Panzer laden kann (ohne Angabe: nur die Startmunition).</summary>
        public int[] AmmoLoadout => spec.ammo != null && spec.ammo.Length > 0 ? spec.ammo : new[] { spec.ammoIndex };
        /// <summary>Tastennummer (1, 2, ...) der geladenen Munition.</summary>
        public int AmmoSlot => System.Array.IndexOf(AmmoLoadout, ammoIndex) + 1;
        public float SpeedKmh => Velocity.magnitude * 3.6f;
        public Vector3 SightPosition => parts.sight.position;
        public float SuperElevation => superElevationDeg;
        public float ForwardSpeed => Vector3.Dot(Velocity, transform.forward);
        public float YawRate => Vector3.Dot(rb.angularVelocity, transform.up);
        public float VerticalSpeed => Vector3.Dot(Velocity, transform.up);

        /// <summary>Federpunkte auf die echten Laufrollen des Modells setzen (lokale x/z).</summary>
        public void SetWheelPoints(List<Vector3> localPoints, List<Vector3> endPoints = null, List<float> endHeights = null)
        {
            if (localPoints == null || localPoints.Count < 4) return;
            wheelPoints.Clear();
            contactHeights.Clear();
            foreach (var p in localPoints) { wheelPoints.Add(new Vector3(p.x, 0.9f, p.z)); contactHeights.Add(0f); }
            if (endPoints != null)
                for (int i = 0; i < endPoints.Count; i++)
                {
                    wheelPoints.Add(new Vector3(endPoints[i].x, 0.9f, endPoints[i].z));
                    contactHeights.Add(endHeights != null && i < endHeights.Count ? endHeights[i] : 0.25f);
                }
        }

        Vector3 Velocity
        {
#if UNITY_6000_0_OR_NEWER
            get => rb.isKinematic && netVelocity.HasValue ? netVelocity.Value : rb.linearVelocity;
            set => rb.linearVelocity = value;
#else
            get => rb.isKinematic && netVelocity.HasValue ? netVelocity.Value : rb.velocity;
            set => rb.velocity = value;
#endif
        }

        public void Init(TankSpec s, Rigidbody body, TankBuilder.Parts p, bool player)
        {
            spec = s;
            rb = body;
            parts = p;
            isPlayer = player;
            ammoIndex = s.ammoIndex;
            reloadLeft = 0f;

            foreach (var m in p.modules)
            {
                m.owner = this;
                if (m.IsCrew) seats[m.type] = m;
            }

            // Federpunkte: 6 pro Kette
            float x = s.hullWidth / 2f + s.trackWidth / 2f - s.trackInset;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 6; i++)
                {
                    float z = Mathf.Lerp(-s.hullLength * 0.42f, s.hullLength * 0.42f, i / 5f);
                    wheelPoints.Add(new Vector3(side * x, 0.9f, z));
                    contactHeights.Add(0f);
                }
            // Kettenspitzen vorne und hinten (Antriebs- und Leitrad)
            for (int side = -1; side <= 1; side += 2)
                for (int e = -1; e <= 1; e += 2)
                {
                    wheelPoints.Add(new Vector3(side * x, 0.9f, e * s.hullLength * 0.5f));
                    contactHeights.Add(0.3f);
                }
            SetFrictionless();

            aimPoint = transform.position + transform.forward * 500f;
        }

        // =============== Eingabe, Turm, Besatzung ===============
        void Update()
        {
            if (spec == null) return;
            float dt = Time.deltaTime;

            foreach (var m in parts.modules)
                if (m != null && m.stun > 0f)
                {
                    m.stun -= dt;
                    if (m.stun <= 0f && !m.Destroyed && isPlayer) RangeLog.AddLocal($"{m.label} ist wieder bei Bewusstsein");
                }
            UpdateCrew(dt);
            UpdateFire(dt);
            UpdateRecoil(dt);
            UpdateRepair(dt);
            if (isPlayer && !destroyed && Inp.Down(KeyCode.G)) StartRepair();
            if (isPlayer && !destroyed && Inp.Down(KeyCode.J) && Cursor.lockState == CursorLockMode.Locked)
            {
                Abandon();
                RangeLog.AddLocal("Besatzung steigt aus - mit N neuen Panzer holen");
            }
            if (reloadLeft > 0f && seats.Count > 0) reloadLeft -= dt / ReloadMultiplier();

            // Turm-MG: rechte Maustaste (eigener Panzer), fremde Panzer zeigen das uebertragene Feuer
            bool mgFire = false;
            if (isPlayer) { mgFire = !destroyed && !spec.noGun && !spec.noMg && (Inp.MgHeld || BenchMg) && Cursor.lockState == CursorLockMode.Locked && Working(ModuleType.Gunner) && SeatActive(ModuleType.Gunner); net?.SetMg(mgFire); }
            else if (net != null) mgFire = !destroyed && net.MgFiring;
            MachineGun.Tick(this, mgFire, dt);
            UpdateExhaust(dt);

            if (destroyed) { throttle = 0f; steer = 0f; brake = true; return; }

            if (isPlayer && artilleryAim)
            {
                // Kartenmodus: Fahrzeug steht, Turm folgt dem Feuerleitrechner (ArtilleryMode setzt aimPoint), Feuer per Leertaste dort
                throttle = 0f; steer = 0f; brake = true;
                AimTurret(dt);
            }
            else if (isPlayer)
            {
                throttle = (Inp.Held(KeyCode.W) ? 1f : 0f) - (Inp.Held(KeyCode.S) ? 1f : 0f);
                steer = (Inp.Held(KeyCode.D) ? 1f : 0f) - (Inp.Held(KeyCode.A) ? 1f : 0f);
                brake = Inp.Held(KeyCode.Space);
                if (BenchDrive) { throttle = 0.6f; steer = Mathf.Sin(Time.time * 0.5f) * 0.5f; }   // Leistungstest: fahren

                // Nur die Granaten dieser Kanone: Taste 1 = erste Sorte, 2 = zweite ...
                int[] loadout = AmmoLoadout;
                for (int i = 0; i < loadout.Length && i < 9; i++)
                    if (Inp.Down(KeyCode.Alpha1 + i) && loadout[i] != ammoIndex)
                    {
                        ammoIndex = loadout[i];
                        reloadLeft = spec.reloadS; // Munition wechseln = neu laden
                    }

                if (Inp.Down(KeyCode.F)) Extinguish();
                if (Inp.FireDown && Cursor.lockState == CursorLockMode.Locked) TryFire();   // Klicks in Menues/Karte feuern nicht
                if (autoFire) AutoFireUpdate(dt);

                AimTurret(dt);
            }
            else
            {
                throttle = 0f; steer = 0f; brake = true;
            }
        }

        // Auspuff: laufender Motor qualmt leicht (mehr beim Gasgeben); ausgeschaltete Panzer nicht mehr - man sieht, dass der Motor aus ist
        float exhaustAcc, lastSpeed;
        void UpdateExhaust(float dt)
        {
            if (NetSync.Headless || destroyed || !Working(ModuleType.Engine)) return;
            float v = Velocity.magnitude;
            float load = Mathf.Clamp01(Mathf.Abs(throttle) + Mathf.Max(0f, v - lastSpeed) / Mathf.Max(dt, 0.001f) * 0.4f + v / 15f);
            lastSpeed = v;
            exhaustAcc += dt;
            float interval = Mathf.Lerp(0.3f, 0.09f, load);
            if (exhaustAcc < interval) return;
            exhaustAcc = 0f;
            if (Camera.main != null && (Camera.main.transform.position - transform.position).sqrMagnitude > 600f * 600f) return;   // weit weg: spart Partikel
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 p = transform.TransformPoint(new Vector3(side * spec.hullWidth * 0.25f, spec.clearance + spec.hullHeight * 0.85f, -spec.hullLength * 0.5f));
            Vector3 vel = -transform.forward * (0.8f + load) + Vector3.up * 0.7f + (rb != null ? rb.linearVelocity * 0.3f : Vector3.zero);
            Effects.SmokeOne(p, vel, 0.3f + 0.5f * load, Random.Range(1.4f, 2.4f), 0.3f - 0.12f * load, 0.12f + 0.3f * load);
        }

        void AimTurret(float dt)
        {
            if (parts.turret == null || parts.turret.parent == null) return; // Turm weggeflogen
            if (seats.Count > 0 && SeatOk(ModuleType.Gunner) && !SeatActive(ModuleType.Gunner)) return;   // Richtschuetze bewusstlos: Turm steht

            // Ueberhoehung: Rohr so weit anheben, dass die Granate auf Visier-Entfernung trifft
            if (Ammo.guided || artilleryAim) superElevationDeg = 0f;   // Lenkrakete fliegt der Visierlinie nach; Artillerie: Winkel kommt vom Feuerleitrechner
            else
            {
                Ballistics.SimulateFlat(Ammo, Mathf.Max(sightRange, 10f), out _, out float drop, out _);
                superElevationDeg = Mathf.Atan2(drop, sightRange) * Mathf.Rad2Deg;
            }

            float traverse = spec.traverseDegS * (Working(ModuleType.TurretDrive) ? 1f : 0.2f);
            // Stabilisator: Rohr haelt seine Richtung im Raum, die Wannenbewegung wird sofort ausgeglichen (nur mit intaktem Turmantrieb)
            bool stab = spec.stabilizer && hasLastGunDir && Working(ModuleType.TurretDrive);

            // Feuerleitrechner: Vorhalt fuer ein bewegtes Ziel (beim Lasern gemessen) - Rohr zielt vor das Fadenkreuz
            Vector3 aim = aimPoint;
            if (spec.fireControl && Time.time < leadUntil && !Ammo.guided && !artilleryAim)
            {
                Ballistics.SimulateFlat(Ammo, Mathf.Max(sightRange, 10f), out _, out _, out float tof);
                aim += leadVelocity * tof;
            }

            // Turm drehen (in Wannen-Koordinaten)
            Transform hull = parts.turret.parent;
            Vector3 local = hull.InverseTransformPoint(aim) - parts.turret.localPosition;
            float wantYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float yaw = parts.turret.localEulerAngles.y;
            if (stab) { Vector3 h = hull.InverseTransformDirection(lastGunDir); yaw = Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg; }
            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, traverse * dt);
            parts.turret.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // Kanone heben/senken (in Turm-Koordinaten)
            Vector3 g = parts.turret.InverseTransformPoint(aim) - parts.gunPivot.localPosition;
            float wantElev = Mathf.Atan2(g.y, new Vector2(g.x, g.z).magnitude) * Mathf.Rad2Deg + superElevationDeg;
            wantElev = Mathf.Clamp(wantElev, spec.minElevation, spec.maxElevation);
            float elev = -parts.gunPivot.localEulerAngles.x;
            elev = Mathf.DeltaAngle(0f, elev);
            if (stab)
            {
                Vector3 t = parts.turret.InverseTransformDirection(lastGunDir);
                elev = Mathf.Clamp(Mathf.Atan2(t.y, new Vector2(t.x, t.z).magnitude) * Mathf.Rad2Deg, spec.minElevation, spec.maxElevation);
            }
            elev = Mathf.MoveTowards(elev, wantElev, spec.elevationDegS * dt);
            parts.gunPivot.localRotation = Quaternion.Euler(-elev, 0f, 0f);
            lastGunDir = parts.gunPivot.forward; hasLastGunDir = true;
        }

        Vector3 lastGunDir; bool hasLastGunDir;
        /// <summary>Feuerleitrechner: beim Lasern gemessene Zielgeschwindigkeit (Vorhalt), gilt bis leadUntil.</summary>
        [HideInInspector] public Vector3 leadVelocity;
        [HideInInspector] public float leadUntil;

        void TryFire()
        {
            if (spec.noGun) { RangeLog.AddLocal($"{spec.name}: keine Waffe"); return; }
            if (activeMissile != null) return;   // Lenkrakete im Flug: Werfer haengt am Draht
            if (reloadLeft > 0f) return;
            if (!SeatActive(ModuleType.Gunner)) { RangeLog.Add(SeatOk(ModuleType.Gunner) ? "Richtschuetze bewusstlos!" : "Kein Richtschuetze!"); return; }
            if (Ammo.guided)
            {
                reloadLeft = spec.reloadS;
                activeMissile = GuidedMissile.Launch(this, ammoIndex);
                return;
            }
            if (!Working(ModuleType.Breech)) { RangeLog.Add("Kanonenverschluss zerstoert!"); return; }

            reloadLeft = spec.reloadS;   // zuerst: auch wenn danach ein Effekt scheitert, wird nachgeladen
            Vector3 origin = parts.muzzle.position, dir = parts.gunPivot.forward;
            float speed = artilleryAim ? artySpeed : 0f;                         // Artillerie: Treibladung bestimmt die Geschwindigkeit
            if (net != null && NetSync.Active)
            {
                net.RequestFire(ammoIndex, origin, dir, speed);                       // Server rechnet die echte Granate
                if (!NetSync.IsServer) Shell.Fire(Ammo, origin, dir, true, speed);     // eigene Geister-Granate sofort sehen
            }
            else Shell.Fire(Ammo, origin, dir, false, speed);
            // Rueckstoss: Granate + Treibgase (ca. 1,5x Geschossimpuls); die Rohrbremse daempft ihn ueber den Ruecklauf
            rb.AddForceAtPosition(-parts.gunPivot.forward * Ammo.massKg * Ammo.muzzleVelocity * 0.45f,
                                  parts.gunPivot.position, ForceMode.Impulse);
            try { PlayFireEffects(ammoIndex); } catch (System.Exception e) { Debug.LogException(e); }
        }

        // ---- Test: automatisch auf den naechsten Panzer schiessen (Startparameter -autofire) ----
        public bool autoFire;
        /// <summary>Leistungstest (-benchdrive): Spielerpanzer faehrt Schlangenlinien.</summary>
        public static bool BenchDrive;
        /// <summary>Leistungstest (-benchmg): MG feuert dauernd.</summary>
        public static bool BenchMg;
        /// <summary>Leistungstest: einen Schuss ausloesen.</summary>
        public void BenchFire() { reloadLeft = 0f; TryFire(); }

        // ---- Artillerie (Kartenmodus): Feuerleitrechner gibt Richtung und Treibladung vor ----
        [HideInInspector] public bool artilleryAim;
        [HideInInspector] public float artySpeed;
        /// <summary>Artillerie: Schuss ausloesen (Leertaste im Kartenmodus).</summary>
        public void ArtilleryFire() => TryFire();
        public bool ReadyToFire => reloadLeft <= 0f && activeMissile == null && !destroyed;
        /// <summary>Lenkrakete, die gerade fliegt (nur eine gleichzeitig: sie haengt am Draht).</summary>
        [HideInInspector] public GuidedMissile activeMissile;
        TankController autoTarget;
        float autoSearch;
        void AutoFireUpdate(float dt)
        {
            autoSearch -= dt;
            if (autoSearch <= 0f || autoTarget == null || autoTarget.destroyed)
            {
                autoSearch = 1f;
                autoTarget = null;
                float best = float.MaxValue;
                foreach (var t in FindObjectsByType<TankController>(FindObjectsSortMode.None))
                {
                    if (t == this || t.destroyed || t.spec == null) continue;
                    float d = (t.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; autoTarget = t; }
                }
            }
            if (autoTarget == null) return;
            aimPoint = autoTarget.transform.TransformPoint(0f, autoTarget.spec.clearance + autoTarget.spec.hullHeight * 0.6f, 0f);
            sightRange = Vector3.Distance(SightPosition, aimPoint);
            Vector3 want = aimPoint - parts.muzzle.position;
            float off = Vector3.Angle(new Vector3(parts.gunPivot.forward.x, 0f, parts.gunPivot.forward.z), new Vector3(want.x, 0f, want.z));
            if (reloadLeft <= 0f && off < 0.8f) TryFire();
        }

        /// <summary>Muendungsfeuer, Knall und Rohrruecklauf (auch fuer fremde Panzer im Mehrspieler).</summary>
        public void PlayFireEffects(int ammo)
        {
            var a = AmmoLibrary.All[Mathf.Clamp(ammo, 0, AmmoLibrary.All.Length - 1)];
            if (parts.muzzle != null && parts.gunPivot != null)
                Effects.Muzzle(parts.muzzle.position, parts.gunPivot.forward, a.caliberMm, spec.muzzleBrake);
            recoilT = 0f;
        }

        // =============== Ketten: Schaden und Reparatur ===============
        /// <summary>Eine Bruchstelle in einer Kette. leftSide = Kette auf der linken Seite (x &lt; 0).</summary>
        public class TrackBreak
        {
            public bool leftSide;
            public Vector2 zy;      // Lage im Panzer (z = vorne, y = oben)
            public float damage;    // Schadensmenge (bestimmt Lueckengroesse und Reparaturzeit)
            public float gap;       // Laenge der Luecke in m
        }
        public readonly List<TrackBreak> trackBreaks = new List<TrackBreak>();
        public bool repairing { get; private set; }
        public float repairLeft { get; private set; }
        public const float MaxRepairS = 40f;

        public bool TrackBroken(bool leftSide)
        {
            foreach (var b in trackBreaks) if (b.leftSide == leftSide) return true;
            return false;
        }

        /// <summary>Reparaturzeit nach Gesamtschaden: kleine Bruchstelle ca. 15 s, mehrere/Sprengtreffer bis 40 s.</summary>
        public float RepairTimeNeeded()
        {
            float d = 0f;
            foreach (var b in trackBreaks) d += b.damage;
            float t = 4f + d * 0.2f;
            if (HasSeat(ModuleType.Loader) && !SeatActive(ModuleType.Loader)) t *= 1.25f;   // weniger Haende
            return Mathf.Clamp(t, 8f, MaxRepairS);
        }

        /// <summary>
        /// Kette an der getroffenen Stelle beschaedigen. Liegt dort schon eine Bruchstelle, wird sie groesser.
        /// explosive = Sprengtreffer: Glieder fliegen mit Physik weg (von blastFrom aus).
        /// </summary>
        public string DamageTrack(Vector3 worldPoint, float damage, Vector3 blastFrom, bool explosive)
        {
            if (!NetSync.MayApplyDamage) return "";   // Geister-Granate / Mehrspieler-Client ohne Server-Auftrag
            if (NetSync.ShouldBroadcast(this)) net.SendTrackBreak(worldPoint, damage, blastFrom, explosive);
            Vector3 l = transform.InverseTransformPoint(worldPoint);
            bool left = l.x < 0f;
            var zy = new Vector2(l.z, l.y);
            TrackBreak b = null;
            foreach (var x in trackBreaks)
                if (x.leftSide == left && (x.zy - zy).magnitude < 0.6f) { b = x; break; }
            if (b == null) { b = new TrackBreak { leftSide = left, zy = zy }; trackBreaks.Add(b); }
            float before = b.gap;
            b.damage += damage;
            b.gap = Mathf.Max(b.gap, Mathf.Clamp(0.25f + b.damage * 0.006f, 0.3f, 1.4f));   // abgerollte Kette bleibt abgerollt
            if (repairing) { repairing = false; RangeLog.Add($"{spec.name}: Reparatur unterbrochen!"); }
            // Glieder, die neu herausgerissen wurden
            int links = Mathf.Max(1, Mathf.RoundToInt((b.gap - before) / Mathf.Max(spec.trackPitch, 0.05f)));
            TankTracks.SpawnLinks(this, worldPoint, explosive ? links : Mathf.Min(links, 2), blastFrom, explosive);
            return $"Kette {(left ? "links" : "rechts")} gerissen";
        }

        void StartRepair()
        {
            if (trackBreaks.Count == 0 || repairing || destroyed) return;
            if (!SeatActive(ModuleType.Driver) && !SeatActive(ModuleType.Loader) && !SeatActive(ModuleType.RadioOperator) && !SeatActive(ModuleType.Commander))
            { RangeLog.Add("Niemand da, der die Kette reparieren kann!"); return; }
            repairing = true;
            repairLeft = RepairTimeNeeded();
            RangeLog.Add($"{spec.name}: Kette wird repariert ({repairLeft:0} s, Panzer muss stehen)");
        }

        void UpdateRepair(float dt)
        {
            if (!repairing) return;
            if (destroyed) { repairing = false; return; }
            if (SpeedKmh > 3f) return;          // Besatzung arbeitet nur im Stand
            repairLeft -= dt;
            if (repairLeft > 0f) return;
            trackBreaks.Clear();
            repairing = false;
            RangeLog.Add($"{spec.name}: Kette repariert");
            if (net != null && NetSync.Active) net.ReportRepairDone();   // Server und alle anderen erfahren es
        }

        /// <summary>Mehrspieler: Reparatur fertig (vom Besitzer gemeldet).</summary>
        public void NetTrackRepaired()
        {
            trackBreaks.Clear();
            repairing = false;
        }

        // ---- Treffermarken im Mehrspieler: Bezugsteil (0 = Wanne, 1 = Turm, 2 = Kanone) ----
        public Transform DecalRef(int sel) => sel == 1 && parts.turret != null ? parts.turret : sel == 2 && parts.gunPivot != null ? parts.gunPivot : transform;

        ArmorMeshFollower armorFollower;
        /// <summary>Woran die Marke haengen soll: Teil der Mesh-Panzerung (folgt Turm/Kanone), sonst das Bezugsteil.</summary>
        public Transform DecalParent(int sel)
        {
            if (armorFollower == null)
                foreach (var f in FindObjectsByType<ArmorMeshFollower>(FindObjectsSortMode.None)) if (f.owner == this) { armorFollower = f; break; }
            if (armorFollower != null)
            {
                var t = armorFollower.transform.Find(sel == 1 ? "Turm" : sel == 2 ? "Kanone" : "Wanne");
                if (t != null) return t;
            }
            return DecalRef(sel);
        }

        // =============== Rohrruecklauf ===============
        float recoilT = 99f;
        /// <summary>Wie weit das Rohr gerade zurueckgefahren ist (m). Schnell zurueck, langsam wieder vor.</summary>
        public float RecoilOffset { get; private set; }
        void UpdateRecoil(float dt)
        {
            recoilT += dt;
            float len = Ammo.caliberMm * 0.0062f;            // 88 mm -> ca. 55 cm Ruecklauf
            float k = recoilT < 0.04f ? recoilT / 0.04f : 1f - Mathf.SmoothStep(0f, 1f, (recoilT - 0.08f) / 0.75f);
            RecoilOffset = len * Mathf.Clamp01(k);
            if (parts.barrel != null)
                parts.barrel.transform.localPosition = new Vector3(0f, 0f, spec.barrelLength / 2f - RecoilOffset);
        }

        public float ReloadMultiplier()
        {
            // Ohne Ladeschuetze laedt der Richtschuetze selbst (Fahrzeuge ohne Ladeschuetzen-Platz: schon in der Ladezeit enthalten)
            if (!HasSeat(ModuleType.Loader)) return 1f;
            return SeatActive(ModuleType.Loader) ? 1f : 2.5f;
        }

        // =============== Fahrphysik ===============
        void FixedUpdate()
        {
            if (spec == null) return;
            if (rb.isKinematic) return;   // fremder Panzer im Mehrspieler: folgt nur der uebertragenen Lage
            float dt = Time.fixedDeltaTime;
            int n = wheelPoints.Count;
            int roadCount = 0;
            foreach (float h in contactHeights) if (h <= 0.001f) roadCount++;
            float massPer = rb.mass / Mathf.Max(roadCount, 1);
            float k = massPer * 9.81f / 0.12f;                     // Federsteifigkeit: 12 cm Einfederung im Stand
            float c = 2f * 0.45f * Mathf.Sqrt(k * massPer);       // Daempfung
            const float rest = 1.02f;                             // Ruhelaenge der Feder
            const float rayLen = 1.5f;

            bool canDrive = !destroyed && SeatActive(ModuleType.Driver) && Working(ModuleType.Engine) && Working(ModuleType.Transmission);
            float vMax = spec.maxSpeedKmh / 3.6f;
            float vRev = spec.reverseKmh / 3.6f;
            float target = throttle >= 0f ? throttle * vMax : throttle * vRev;

            float speed = Velocity.magnitude;
            float power = spec.powerPS * 735.5f * 0.7f; // Watt am Kettenrad (70% Wirkungsgrad)

            int grounded = 0;
            var hits = new RaycastHit[n];
            var ok = new bool[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 o = transform.TransformPoint(wheelPoints[i]);
                ok[i] = RaycastGround(o, -transform.up, rayLen, out hits[i]);
                // nur Punkte zaehlen, die wirklich einfedern (Bodenkontakt)
                if (ok[i] && rest - contactHeights[i] - hits[i].distance > 0f) grounded++;
            }
            // Leistung = Kraft x Geschwindigkeit. Nur die Vorwaertsgeschwindigkeit zaehlt, nicht Wackeln/Federn.
            float vFwd = Mathf.Abs(Vector3.Dot(Velocity, transform.forward));
            float maxDrivePer = grounded > 0 ? power / Mathf.Max(vFwd, 1.0f) / grounded : 0f;

            float sumDrive = 0f, sumN = 0f; int contact = 0;
            for (int i = 0; i < n; i++)
            {
                if (!ok[i]) continue;
                RaycastHit h = hits[i];
                Vector3 pv = rb.GetPointVelocity(h.point);

                // Federung (Kettenspitzen: beginnen erst hoeher zu greifen, dafuer steifer)
                float ch = contactHeights[i];
                float compression = rest - ch - h.distance;
                if (ch > 0.001f) compression *= 2f;
                if (compression <= 0f) continue;
                float vN = Vector3.Dot(pv, h.normal);
                float fN = Mathf.Max(0f, k * compression - c * vN);
                rb.AddForceAtPosition(h.normal * fN, h.point);

                // Kette: vorwaerts/seitlich
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, h.normal).normalized;
                Vector3 right = Vector3.Cross(h.normal, fwd);
                // Geschwindigkeit der Wannenmitte: so bremst die Seitenreibung nicht die Drehung
                Vector3 vc = Velocity;
                float vF = Vector3.Dot(vc, fwd);
                float vR = Vector3.Dot(vc, right);

                // Hangabtrieb auf diesen Auflagepunkt (Anteil am Gewicht = Anteil an der Federkraft)
                Vector3 gT = Vector3.ProjectOnPlane(Physics.gravity, h.normal) * (fN / 9.81f);
                float gF = Vector3.Dot(gT, fwd), gR = Vector3.Dot(gT, right);

                float fF;
                if (TrackBroken(wheelPoints[i].x < 0f))
                    fF = -vF * massPer * 0.25f;                      // Kette gerissen: Laufrollen rollen nur, kein Antrieb, keine Bremse
                else if (throttle == 0f && (brake || Mathf.Abs(vF) < 1.0f))
                    fF = -vF * massPer * 6f - gF;                    // Bremse / Stillstand: Ketten halten (Haftreibung)
                else if (brake && throttle == 0f && steer == 0f || (!canDrive && brake))
                    fF = -vF * massPer * 4f;                         // Bremse
                else if (!canDrive)
                    fF = -vF * massPer * 0.15f;                      // rollt aus
                else
                {
                    float want = target;
                    fF = (want - vF) * massPer * 2.5f;
                    if (fF * want > 0f) fF = Mathf.Clamp(fF, -maxDrivePer, maxDrivePer); // Motorleistung begrenzt Beschleunigung
                }
                float fR = -vR * massPer * 6f - gR;                  // seitlich rutscht eine Kette nicht (bis zur Haftgrenze)

                // Haftung begrenzt alles (Reibwert 0,9)
                Vector3 f = fwd * fF + right * fR;
                float maxF = fN * 0.9f;
                if (f.magnitude > maxF) f = f.normalized * maxF;
                rb.AddForceAtPosition(f, h.point);
                sumDrive += Vector3.Dot(f, fwd); sumN += fN; contact++;
            }
            if (throttle != 0f) lastDriveInfo = $"GAS: Antrieb {sumDrive / 1000f:0} kN  maxPro {maxDrivePer / 1000f:0.0} kN  v {speed:0.0}  want {target:0.0}  Kontakt {contact}";
            debugInfo = lastDriveInfo + "\n" + $"FPS {1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):0}  dt {Time.fixedDeltaTime:0.000}  ts {Time.timeScale:0.0}\n" +
                        $"Punkte {n} Strahl {grounded} Kontakt {contact}  Antrieb {sumDrive / 1000f:0} kN  Last {sumN / 1000f:0} kN (Gewicht {rb.mass * 9.81f / 1000f:0})\n" +
                        $"v {speed:0.0} m/s  canDrive {canDrive}  throttle {throttle}  maxPro {maxDrivePer / 1000f:0.0} kN";

            // Lenken: Ketten laufen unterschiedlich schnell -> Drehmoment um die Hochachse.
            // Im Stand dreht der Panzer auf der Stelle, bei voller Fahrt nur noch ca. 60% so schnell.
            if (grounded > 0)
            {
                float yawRate = Vector3.Dot(rb.angularVelocity, transform.up);
                float speedFactor = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(speed / vMax));
                float wantRate = canDrive ? steer * spec.turnRateDegS * Mathf.Deg2Rad * speedFactor : 0f;
                if (throttle < 0f) wantRate = -wantRate; // rueckwaerts lenkt wie ein Auto
                // Eine Kette gerissen: nur die heile Kette treibt, der Panzer dreht um die kaputte Seite
                bool bl = TrackBroken(true), br = TrackBroken(false);
                if (bl || br)
                {
                    if (bl && br || !canDrive) wantRate = 0f;
                    else
                    {
                        float spacing = Mathf.Max(spec.hullWidth + spec.trackWidth - 2f * spec.trackInset, 1f);
                        float good = bl ? 1f : -1f;                                   // Seite der heilen Kette (+1 = rechts)
                        wantRate = -good * target / spacing;                           // rechte Kette vor -> Drehung nach links
                        float lim = spec.turnRateDegS * Mathf.Deg2Rad;
                        wantRate = Mathf.Clamp(wantRate, -lim, lim);
                    }
                }
                float inertia = rb.inertiaTensor.y;
                float torque = (wantRate - yawRate) * inertia * 6f;
                float maxTorque = rb.mass * 9.81f * 0.6f * (spec.hullLength * 0.25f) * grounded / n; // Haftung begrenzt
                torque = Mathf.Clamp(torque, -maxTorque, maxTorque);
                rb.AddTorque(transform.up * torque);
            }
        }

#if UNITY_6000_0_OR_NEWER
        static PhysicsMaterial MakeMaterial(string name, float dyn, float stat, bool max)
        {
            return new PhysicsMaterial(name)
            {
                dynamicFriction = dyn, staticFriction = stat,
                frictionCombine = max ? PhysicsMaterialCombine.Maximum : PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
#else
        static PhysicMaterial MakeMaterial(string name, float dyn, float stat, bool max)
        {
            return new PhysicMaterial(name)
            {
                dynamicFriction = dyn, staticFriction = stat,
                frictionCombine = max ? PhysicMaterialCombine.Maximum : PhysicMaterialCombine.Minimum,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
        }
#endif

        /// <summary>Panzerteile rutschen reibungsfrei: Bodenkontakt laeuft nur ueber Federn und Ketten.</summary>
        void SetFrictionless()
        {
#if UNITY_6000_0_OR_NEWER
            var pm = new PhysicsMaterial("Panzer reibungsfrei")
            {
                dynamicFriction = 0f, staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
            };
#else
            var pm = new PhysicMaterial("Panzer reibungsfrei")
            {
                dynamicFriction = 0f, staticFriction = 0f,
                frictionCombine = PhysicMaterialCombine.Minimum, bounceCombine = PhysicMaterialCombine.Minimum
            };
#endif
            foreach (var c in GetComponentsInChildren<Collider>())
                if (!c.isTrigger) c.sharedMaterial = pm;
        }

        public bool RaycastGround(Vector3 origin, Vector3 dir, float len, out RaycastHit best)
        {
            best = default;
            bool found = false;
            float bestDist = float.MaxValue;
            // Mesh-Panzerung (nur fuer Treffer) ist kein Boden
            foreach (var h in Physics.RaycastAll(origin, dir, len, ~(1 << ArmorPlate.MeshArmorLayer), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.attachedRigidbody == rb) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        /// <summary>Raycast, der den eigenen Panzer ignoriert (fuer Kamera und Zielmarker).</summary>
        public bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float len, out RaycastHit best)
        {
            best = default;
            bool found = false;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.RaycastAll(origin, dir, len, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.attachedRigidbody == rb) continue;
                if (detachedTurret != null && h.collider.transform.IsChildOf(detachedTurret.transform)) continue;
                var ap = h.collider.GetComponent<ArmorPlate>();
                if (ap != null && ap.ownerTank == this) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        // =============== Besatzung und Schaden ===============
        public bool SeatOk(ModuleType role) => seats.TryGetValue(role, out var m) && m != null && !m.Destroyed;
        /// <summary>Platz besetzt und der Mann ist bei Bewusstsein (kann fahren/richten/laden).</summary>
        public bool SeatActive(ModuleType role) => seats.TryGetValue(role, out var m) && m != null && !m.Destroyed && !m.Unconscious;
        /// <summary>0..1: wie stark der Spieler (Kommandant, sonst Richtschuetze) gerade weggetreten ist.</summary>
        public float PlayerDaze
        {
            get
            {
                seats.TryGetValue(ModuleType.Commander, out var c);
                if (c == null || c.Destroyed) seats.TryGetValue(ModuleType.Gunner, out c);
                return c != null && c.Unconscious ? Mathf.Clamp01(c.stun / 6f) : 0f;
            }
        }

        public bool Working(ModuleType t)
        {
            bool any = false;
            foreach (var m in parts.modules)
                if (m.type == t) { any = true; if (!m.Destroyed) return true; }
            return !any;
        }

        int Priority(ModuleType role) => System.Array.IndexOf(SeatPriority, role);

        /// <summary>Gibt es diesen Platz in diesem Fahrzeug? (Wiesel: kein Ladeschuetze, kein Funker)</summary>
        public bool HasSeat(ModuleType role) => spec.crew == null || System.Array.IndexOf(spec.crew, role) >= 0;

        void UpdateCrew(float dt)
        {
            if (destroyed) return;

            if (swapCandidate != null)
            {
                if (swapCandidate.Destroyed) { swapCandidate = null; return; }
                swapTimer -= dt;
                if (swapTimer > 0f) return;
                var keys = new List<ModuleType>(seats.Keys);
                foreach (var key in keys) if (seats[key] == swapCandidate) seats[key] = null;
                seats[swapRole] = swapCandidate;
                if (isPlayer) RangeLog.Add($"{swapCandidate.label} uebernimmt jetzt: {TankModule.RoleName(swapRole)}");
                swapCandidate = null;
                return;
            }

            // Wichtige Plaetze nachbesetzen: Richtschuetze, Fahrer, Ladeschuetze
            for (int r = 0; r < 3; r++)
            {
                ModuleType role = SeatPriority[r];
                if (SeatOk(role) || !HasSeat(role)) continue;
                // Spender: jemand auf einem weniger wichtigen Platz
                for (int d = SeatPriority.Length - 1; d > r; d--)
                {
                    ModuleType donor = SeatPriority[d];
                    if (!SeatOk(donor)) continue;
                    swapCandidate = seats[donor];
                    swapRole = role;
                    swapTimer = 6f;
                    if (isPlayer) RangeLog.Add($"{swapCandidate.label} wechselt in 6 s auf {TankModule.RoleName(role)}");
                    return;
                }
            }
        }

        public void OnModuleDamaged(TankModule m)
        {
            if (destroyed) return;
            if (NetSync.IsClientOnly) return;   // Mehrspieler: Server entscheidet (Zufall!) und schickt Brand/Explosion/Zerstoerung

            if (m.type == ModuleType.Ammo && Random.value < (m.Destroyed ? 0.7f : 0.25f))
            {
                DetonateAmmo();
                return;
            }
            if (m.type == ModuleType.Fuel && !burning && Random.value < (m.Destroyed ? 0.6f : 0.2f))
                StartFire();

            int alive = 0;
            foreach (var mod in parts.modules) if (mod.IsCrew && !mod.Destroyed) alive++;
            if (alive < 2) SetDestroyed("Besatzung ausgeschaltet");
        }

        /// <summary>Mehrspieler-Client: vom Server gemeldet.</summary>
        public void NetDetonate() { if (!destroyed) DetonateAmmo(); }
        public void NetSetDestroyed(string reason) => SetDestroyed(reason);
        public void NetFireState(bool on) { if (on && !burning) StartFire(); else if (!on && burning) StopFire(); }

        void DetonateAmmo()
        {
            if (NetSync.ShouldBroadcast(this)) net.SendDetonate();
            SetDestroyed("MUNITION EXPLODIERT");
            foreach (var mod in parts.modules)
                if (mod.IsCrew) { mod.hp = 0f; mod.UpdateColor(); }

            Effects.AmmoDetonation(transform.TransformPoint(0f, spec.clearance + spec.hullHeight, 0f), transform);

            // Turm fliegt weg
            if (parts.turret != null)
            {
                detachedTurret = parts.turret.gameObject;
                parts.turret.SetParent(null, true);
                var trb = detachedTurret.AddComponent<Rigidbody>();
                trb.mass = spec.massT * 250f;
                trb.AddForce(Vector3.up * trb.mass * 12f + Random.insideUnitSphere * trb.mass * 3f, ForceMode.Impulse);
                trb.AddTorque(Random.insideUnitSphere * trb.mass * 2f, ForceMode.Impulse);
                trb.angularDamping = 0.6f;
                // Der Turm liegt danach wie Stahl auf Erde: hohe Reibung, und sobald er ruht, bleibt er liegen
                var grip = MakeMaterial("Turm Stahl auf Boden", 0.6f, 0.8f, true);
                foreach (var col in detachedTurret.GetComponentsInChildren<Collider>())
                    if (!col.isTrigger) col.sharedMaterial = grip;
                detachedTurret.AddComponent<SettleBody>();
            }
        }

        void SetDestroyed(string reason)
        {
            if (destroyed) return;
            if (NetSync.ShouldBroadcast(this) && !reason.StartsWith("MUNITION")) net.SendDestroyed(reason);
            destroyed = true;
            destroyedReason = reason;
            if (!NetSync.IsClientOnly) RangeLog.Add($"*** {spec.name} ZERSTOERT: {reason} ***");   // Clients bekommen die Zeile vom Server
            // Nicht jedes Wrack brennt: Flammen nur, wenn Motor oder Kraftstoff schwer getroffen sind (Zustand kommt vom Server)
            bool ammo = reason.StartsWith("MUNITION");
            bool flames = ammo || burning;
            foreach (var m in parts.modules)
                if (m != null && (m.type == ModuleType.Fuel || m.type == ModuleType.Engine) && m.hp < m.maxHp * 0.5f) flames = true;
            if (!ammo && flames && !burning)
                Effects.Burn(transform, new Vector3(0f, spec.clearance + spec.hullHeight, -spec.hullLength * 0.3f), 60f, 0.6f);
            Wreck.Begin(this, flames);
            string why = ammo ? "Munition explodiert" : reason == "Besatzung ausgeschaltet" ? "Besatzung ausgefallen" : reason;
            if (reason == "Besatzung ausgestiegen")
                TankCamera.Notice(isPlayer ? "BESATZUNG AUSGESTIEGEN  -  N: neuer Panzer" : $"{DisplayName}: Besatzung ausgestiegen", isPlayer);
            else
                TankCamera.Notice(isPlayer ? $"DEIN PANZER IST AUSGESCHALTET  ({why})  -  N: neuer Panzer" : $"{DisplayName} AUSGESCHALTET  ({why})", isPlayer);
            foreach (var p in parts.plates)
                if (p != null) p.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.12f, 0.12f, 0.12f));
        }

        void StartFire()
        {
            if (NetSync.ShouldBroadcast(this)) net.SendFire(true);
            burning = true;
            fireTimer = 0f;
            if (!NetSync.IsClientOnly) RangeLog.Add($"{spec.name}: BRAND im Motorraum!");
            fireFx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fireFx.name = "Feuer";
            DestroyImmediate(fireFx.GetComponent<Collider>());
            fireFx.transform.SetParent(transform, false);
            fireFx.transform.localPosition = new Vector3(0f, spec.clearance + spec.hullHeight + 0.3f, -spec.hullLength * 0.33f);
            fireFx.GetComponent<Renderer>().enabled = false;           // sichtbar sind nur Flammen und Rauch
            Effects.Burn(fireFx.transform, Vector3.zero, 99999f, 0.8f);
        }

        void UpdateFire(float dt)
        {
            if (!burning) return;
            fireTimer += dt;


            // Feuer verletzt die Besatzung langsam
            foreach (var m in parts.modules)
                if (m.IsCrew && !m.Destroyed) m.ApplyDamage(3f * dt);

            // Computer-Besatzung loescht nach 12 s selbst (im Mehrspieler nur auf dem Server, nur bei Zielpanzern)
            if (!isPlayer && !destroyed && fireTimer > 12f && !NetSync.IsClientOnly && (net == null || net.isTarget.Value)) Extinguish();
        }

        public void Extinguish()
        {
            if (!burning) return;
            // Mehrspieler-Client: Wunsch an den Server (nur fuer den eigenen Panzer)
            if (NetSync.IsClientOnly) { if (net != null && net.IsOwner) net.RequestExtinguish(); return; }
            if (!hasExtinguisher) { if (isPlayer) RangeLog.Add("Kein Feuerloescher mehr!"); return; }
            hasExtinguisher = false;
            StopFire();
            RangeLog.Add($"{spec.name}: Feuer geloescht");
        }

        void StopFire()
        {
            if (NetSync.ShouldBroadcast(this)) net.SendFire(false);
            if (NetSync.IsClientOnly) hasExtinguisher = false;
            burning = false;
            if (fireFx != null) Destroy(fireFx);
        }

        /// <summary>Sichtbarkeit: Mit 3D-Modell sind Platten unsichtbar. Roentgen zeigt Innenteile statt Modell.</summary>
        static Material glassMat;
        readonly Dictionary<Renderer, Material> plateMats = new Dictionary<Renderer, Material>();

        /// <summary>Halb durchsichtiges Material fuer die Panzerplatten-Ansicht (URP Lit, transparent).</summary>
        static Material GlassMaterial()
        {
            if (glassMat != null) return glassMat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return null;
            glassMat = new Material(sh);
            glassMat.SetFloat("_Surface", 1f);   // transparent
            glassMat.SetFloat("_Blend", 0f);     // alpha
            glassMat.SetOverrideTag("RenderType", "Transparent");
            glassMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            glassMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glassMat.SetInt("_ZWrite", 0);
            glassMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glassMat.renderQueue = 3000;
            glassMat.color = new Color(0.2f, 0.75f, 1f, 0.3f);
            return glassMat;
        }

        public void ApplyXray()
        {
            bool hasModel = visual != null;
            bool platesOn = ViewMode == 2 || (ViewMode == 0 && !hasModel);
            bool modulesOn = ViewMode == 1 || (ViewMode == 0 && !hasModel);
            bool glass = ViewMode == 2 && hasModel;
            foreach (var p in parts.plates)
            {
                if (p == null) continue;
                var r = p.GetComponent<Renderer>();
                if (r == null) continue;
                r.enabled = platesOn && p.gameObject.layer != ArmorPlate.PhysicsOnlyLayer;
                if (!plateMats.ContainsKey(r)) plateMats[r] = r.sharedMaterial;
                var g = glass ? GlassMaterial() : null;
                r.sharedMaterial = g != null ? g : plateMats[r];
                r.shadowCastingMode = glass ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var m in parts.modules)
                if (m != null) { var r = m.GetComponent<Renderer>(); if (r != null) r.enabled = modulesOn; }
            if (parts.barrel != null) parts.barrel.enabled = !hasModel || ViewMode == 1;
            if (visual != null) { visual.SetVisible(ViewMode != 1); visual.SetArmorView(ViewMode == 2); }
        }

        public void Teleport(Vector3 pos, float yaw)
        {
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            rb.position = pos;
            rb.rotation = Quaternion.Euler(0f, yaw, 0f);
            Velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        public void ResetUpright()
        {
            Vector3 p = transform.position + Vector3.up * 2f;
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            Velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        void OnDestroy()
        {
            if (detachedTurret != null) Destroy(detachedTurret);
        }
    }
}
