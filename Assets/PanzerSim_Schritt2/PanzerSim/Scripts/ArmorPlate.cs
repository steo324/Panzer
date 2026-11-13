using UnityEngine;

namespace PanzerSim
{
    public enum ArmorMaterial
    {
        RHA,          // gewalzter homogener Stahl (Referenz)
        Cast,         // Gussstahl, etwas schwaecher
        FaceHardened, // oberflaechengehaertet: stark gegen Granaten OHNE Kappe
        Composite     // Verbundpanzerung (Stahl, Keramik, Zwischenschichten): mm = Schutz gegen Pfeile (KE), gegen Hohlladungen viel staerker
    }

    /// <summary>Eine Panzerplatte. Die Dicke der Box wird automatisch an die mm angepasst.</summary>
    public class ArmorPlate : MonoBehaviour
    {
        public float thicknessMm = 50f;
        public ArmorMaterial material = ArmorMaterial.RHA;
        public string label = "Platte";
        public TankController ownerTank;   // gesetzt, wenn die Platte nicht im Panzer-Objekt haengt

        public const int PhysicsOnlyLayer = 29;   // Kisten nur fuer Zusammenstoesse, Granaten ignorieren sie
        public const int MeshArmorLayer = 30;     // Panzerung aus Modellform, nur fuer Treffer
        public const int ShotMask = ~(1 << PhysicsOnlyLayer);

        /// <summary>Dicke, Material und Name an der Trefferstelle.</summary>
        public virtual void GetArmor(RaycastHit hit, out float mm, out ArmorMaterial mat, out string lab)
        {
            mm = thicknessMm; mat = material; lab = label;
        }

        public TankController Tank => ownerTank != null ? ownerTank : GetComponentInParent<TankController>();

        public float QualityFactor(ShellData s) => Quality(material, s);

        public static float Quality(ArmorMaterial material, ShellData s)
        {
            switch (material)
            {
                case ArmorMaterial.Cast: return 0.92f;
                case ArmorMaterial.FaceHardened: return s.IsCapped ? 1.0f : 1.2f;
                default: return 1f;
            }
        }

        public static string MaterialName(ArmorMaterial m)
        {
            switch (m)
            {
                case ArmorMaterial.Cast: return "Gussstahl";
                case ArmorMaterial.FaceHardened: return "oberflaechengehaertet";
                case ArmorMaterial.Composite: return "Verbundpanzerung";
                default: return "Walzstahl (RHA)";
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying) return;
            Vector3 sc = transform.localScale;
            transform.localScale = new Vector3(sc.x, sc.y, Mathf.Max(thicknessMm, 1f) / 1000f);
        }
    }
}
