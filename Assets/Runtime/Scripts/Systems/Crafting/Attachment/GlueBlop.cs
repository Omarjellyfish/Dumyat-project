using UnityEngine;

namespace DumyatRuntime
{
    public class GlueBlob : MonoBehaviour
    {
        [Header("Settings")]
        public float jointBreakForce = Mathf.Infinity;
        public float jointBreakTorque = Mathf.Infinity;

        [HideInInspector]
        public Rigidbody AttachedBodyA; // Set by the Glue Bottle

        private bool _hasBonded = false;

        private void OnTriggerEnter(Collider other)
        {
            // Stop if we've already glued something or if there's no base body
            if (_hasBonded || AttachedBodyA == null) return;

            Rigidbody bodyB = other.attachedRigidbody;

            // Ensure we hit a valid, different structural piece
            if (bodyB == null || bodyB == AttachedBodyA) return;
            if (bodyB.GetComponent<StructuralPiece>() == null) return;

            BondWith(bodyB);
        }

        private void BondWith(Rigidbody bodyB)
        {
            _hasBonded = true;

            // 1. Create the FixedJoint on Body A
            FixedJoint joint = AttachedBodyA.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = bodyB;

            // Anchor it exactly where the glue is
            joint.anchor = AttachedBodyA.transform.InverseTransformPoint(transform.position);
            joint.breakForce = jointBreakForce;
            joint.breakTorque = jointBreakTorque;

            // 2. Ignore collisions between the two glued pieces
            IgnoreCollisionsBetween(AttachedBodyA.gameObject, bodyB.gameObject);

            // 3. Trick the Hammer into thinking this is a Nail!
            // By adding the NailVisualMarker, the hammer's DestroyClosestNailVisual 
            // will find this glue blob and destroy it when detaching.
            NailVisualMarker marker = gameObject.AddComponent<NailVisualMarker>();
            marker.ConnectedBodyA = AttachedBodyA;
            marker.ConnectedBodyB = bodyB;

            // 4. Destroy the trigger collider so it doesn't keep detecting collisions
            Destroy(GetComponent<Collider>());
            
            Debug.Log($"[GlueBlob] Bonded '{AttachedBodyA.name}' ↔ '{bodyB.name}' at {transform.position}");
        }

        private void IgnoreCollisionsBetween(GameObject a, GameObject b)
        {
            Collider[] collidersA = a.GetComponentsInChildren<Collider>();
            Collider[] collidersB = b.GetComponentsInChildren<Collider>();
            foreach (Collider colA in collidersA)
            {
                foreach (Collider colB in collidersB)
                {
                    Physics.IgnoreCollision(colA, colB, true);
                }
            }
        }
    }
}