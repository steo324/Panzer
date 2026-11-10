using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Fuer lose Teile (weggeflogener Turm): bremst langsames Rutschen ab und friert das Teil ein,
    /// sobald es eine Weile ruhig liegt - sonst schiebt die Physik schwere Teile ewig ueber den Boden.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SettleBody : MonoBehaviour
    {
        Rigidbody rb;
        float calm, age;

        void Awake() { rb = GetComponent<Rigidbody>(); }

        void FixedUpdate()
        {
            if (rb == null || rb.isKinematic) return;
            age += Time.fixedDeltaTime;
            float v = rb.linearVelocity.magnitude, w = rb.angularVelocity.magnitude;
            bool touching = Physics.Raycast(rb.worldCenterOfMass, Vector3.down, 3f, ~((1 << ArmorPlate.MeshArmorLayer)), QueryTriggerInteraction.Ignore);

            // langsam und am Boden: kraeftig abbremsen (Reibung Stahl auf Erde)
            if (touching && v < 1.5f && age > 0.5f)
            {
                rb.linearVelocity *= Mathf.Clamp01(1f - 6f * Time.fixedDeltaTime);
                rb.angularVelocity *= Mathf.Clamp01(1f - 6f * Time.fixedDeltaTime);
            }
            calm = touching && v < 0.15f && w < 0.15f ? calm + Time.fixedDeltaTime : 0f;
            if (calm > 1f || age > 30f)
            {
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                enabled = false;
            }
        }
    }
}
