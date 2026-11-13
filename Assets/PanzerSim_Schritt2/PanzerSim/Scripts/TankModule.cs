using UnityEngine;

namespace PanzerSim
{
    public enum ModuleType
    {
        // Besatzung (Reihenfolge wichtig: alles bis RadioOperator ist Besatzung)
        Gunner, Driver, Loader, Commander, RadioOperator,
        // Technik
        Engine, Transmission, Ammo, Fuel, Breech, TurretDrive, Barrel   // nur hinten anfuegen (Netz)
    }

    /// <summary>Ein Innenteil des Panzers: Besatzungsmitglied, Motor, Munition ... Collider ist ein Trigger.</summary>
    public class TankModule : MonoBehaviour
    {
        public ModuleType type;
        public string label;
        public float maxHp = 100f;
        public float hp = 100f;
        public float absorb = 0.05f;   // wie viel Geschwindigkeit eine Granate beim Durchfliegen verliert
        public TankController owner;

        Renderer rend;
        Material ownMat;

        void OnDestroy() { if (ownMat != null) Destroy(ownMat); }

        public bool IsCrew => type <= ModuleType.RadioOperator;
        public bool Destroyed => hp <= 0f;

        /// <summary>Besatzung: so viele Sekunden noch bewusstlos (Erschuetterung durch Sprengtreffer).</summary>
        public float stun;
        public bool Unconscious => stun > 0f && !Destroyed;

        /// <summary>
        /// Erschuetterung (nur Besatzung): severity 0..1 (1 = Explosion direkt daneben).
        /// Ab 0,12: bewusstlos 4-40 s und Gehirnerschuetterung (Schaden bis ~30 %). Gibt Text fuer das Protokoll zurueck.
        /// </summary>
        public string ApplyConcussion(float severity)
        {
            if (!IsCrew || Destroyed || severity < 0.06f) return null;
            if (!NetSync.MayApplyDamage) return null;
            severity = Mathf.Clamp01(severity * Random.Range(0.75f, 1.25f));
            float seconds = Mathf.Max(1.5f, 40f * Mathf.Pow(severity, 1.3f));   // 0,1 -> 2 s, 0,5 -> 16 s, 1 -> 40 s
            stun = Mathf.Max(stun, seconds);
            if (owner != null && owner.net != null && NetSync.ShouldBroadcast(owner)) owner.net.SendModuleStun(owner.parts.modules.IndexOf(this), stun);
            ApplyDamage(35f * Mathf.Pow(severity, 1.5f));   // Gehirnerschuetterung, Prellungen, Trommelfell
            if (Destroyed) return $"{label} an der Erschuetterung gestorben";
            return severity > 0.6f ? $"{label} bewusstlos ({seconds:0} s), schwere Gehirnerschuetterung"
                 : severity > 0.3f ? $"{label} bewusstlos ({seconds:0} s), Gehirnerschuetterung"
                 : $"{label} benommen ({seconds:0} s)";
        }

        public static string RoleName(ModuleType t)
        {
            switch (t)
            {
                case ModuleType.Gunner: return "Richtschuetze";
                case ModuleType.Driver: return "Fahrer";
                case ModuleType.Loader: return "Ladeschuetze";
                case ModuleType.Commander: return "Kommandant";
                case ModuleType.RadioOperator: return "Funker";
                case ModuleType.Engine: return "Motor";
                case ModuleType.Transmission: return "Getriebe";
                case ModuleType.Ammo: return "Munition";
                case ModuleType.Fuel: return "Kraftstofftank";
                case ModuleType.Breech: return "Kanonenverschluss";
                case ModuleType.TurretDrive: return "Turmantrieb";
                case ModuleType.Barrel: return "Rohr";
                default: return t.ToString();
            }
        }

        void Awake() { rend = GetComponent<Renderer>(); UpdateColor(); }

        /// <summary>Schaden anwenden. Gibt einen Text fuer das Protokoll zurueck (oder null).</summary>
        public string ApplyDamage(float dmg)
        {
            if (Destroyed || dmg <= 0f) return null;
            if (!NetSync.MayApplyDamage) return null;   // Mehrspieler-Client: Schaden kommt nur vom Server
            hp = Mathf.Max(0f, hp - dmg);
            UpdateColor();
            if (owner != null && NetSync.ShouldBroadcast(owner)) owner.net.SendModuleHp(owner.parts.modules.IndexOf(this), hp);
            if (owner != null) owner.OnModuleDamaged(this);
            if (Destroyed) return IsCrew ? $"{label} getoetet" : $"{label} zerstoert";
            return $"{label} {(IsCrew ? "verwundet" : "beschaedigt")} ({hp / maxHp * 100f:0}%)";
        }

        /// <summary>Mehrspieler-Client: Zustand vom Server uebernehmen.</summary>
        public void SetHpFromServer(float value)
        {
            hp = Mathf.Clamp(value, 0f, maxHp);
            UpdateColor();
        }

        public void UpdateColor()
        {
            if (rend == null) rend = GetComponent<Renderer>();
            if (rend == null) return;
            // gruen = heil, ueber gelb nach rot = beschaedigt, schwarz = zerstoert
            Color c = Destroyed ? new Color(0.1f, 0.1f, 0.1f) : Color.Lerp(Color.red, Color.green, hp / maxHp);
            if (ownMat == null) { ownMat = GameMap.NewMat(c); rend.sharedMaterial = ownMat; }
            ownMat.color = c;
        }
    }
}
