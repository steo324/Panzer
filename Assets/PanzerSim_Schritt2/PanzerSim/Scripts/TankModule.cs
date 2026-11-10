using UnityEngine;

namespace PanzerSim
{
    public enum ModuleType
    {
        // Besatzung (Reihenfolge wichtig: alles bis RadioOperator ist Besatzung)
        Gunner, Driver, Loader, Commander, RadioOperator,
        // Technik
        Engine, Transmission, Ammo, Fuel, Breech, TurretDrive
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

        public bool IsCrew => type <= ModuleType.RadioOperator;
        public bool Destroyed => hp <= 0f;

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
                default: return t.ToString();
            }
        }

        void Awake() { rend = GetComponent<Renderer>(); UpdateColor(); }

        /// <summary>Schaden anwenden. Gibt einen Text fuer das Protokoll zurueck (oder null).</summary>
        public string ApplyDamage(float dmg)
        {
            if (Destroyed || dmg <= 0f) return null;
            hp = Mathf.Max(0f, hp - dmg);
            UpdateColor();
            if (owner != null) owner.OnModuleDamaged(this);
            if (Destroyed) return IsCrew ? $"{label} getoetet" : $"{label} zerstoert";
            return $"{label} {(IsCrew ? "verwundet" : "beschaedigt")} ({hp / maxHp * 100f:0}%)";
        }

        public void UpdateColor()
        {
            if (rend == null) rend = GetComponent<Renderer>();
            if (rend == null) return;
            Color c = Destroyed ? new Color(0.1f, 0.1f, 0.1f) : Color.Lerp(Color.red, Color.green, hp / maxHp);
            rend.material.color = c;
        }
    }
}
