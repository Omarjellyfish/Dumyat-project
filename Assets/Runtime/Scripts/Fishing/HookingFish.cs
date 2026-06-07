using UnityEngine;

namespace DumyatRuntime
{
    public class HookingFish : MonoBehaviour
    {
        [SerializeField] private HingeJoint hingeJoint;

        private void OnTriggerEnter(Collider collision)
        {
            if (collision.CompareTag("Fish"))
            {
                hingeJoint.connectedBody = collision.attachedRigidbody;
                collision.attachedRigidbody.useGravity = true;
                Debug.Log("Fish hooked");
            }
        }
    }
}
