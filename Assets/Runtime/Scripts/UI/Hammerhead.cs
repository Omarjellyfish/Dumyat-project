using UnityEngine;

namespace DumyatRuntime
{
    [RequireComponent(typeof(Rigidbody))]
    public class HammerHead : MonoBehaviour
    {
        [SerializeField] private LayerMask _nailLayer;

        private void OnCollisionEnter(Collision collision)
        {
            if (((1 << collision.gameObject.layer) & _nailLayer) == 0) return;

            NailAction nail = collision.gameObject.GetComponentInParent<NailAction>();
            if (nail == null) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            nail.Hit(impactSpeed);
        }
    }
}