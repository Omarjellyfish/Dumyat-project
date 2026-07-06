using UnityEngine;

namespace DumyatRuntime
{
    public class HookingFish : MonoBehaviour
    {
        [SerializeField] private HingeJoint hingeJoint;

        private void OnTriggerEnter(Collider collision)
        {
            if (!collision.CompareTag("Fish"))
                return;

            if (hingeJoint.connectedBody != null)
                return;

            Fish fish = collision.GetComponent<Fish>();
            Rigidbody fishBody = collision.attachedRigidbody;

            hingeJoint.connectedBody = fishBody;
            hingeJoint.autoConfigureConnectedAnchor = false;

            // Convert mouth's world position into fish rigidbody's LOCAL space
            Transform mouth = fish != null ? fish.MouthPoint : fishBody.transform;
            hingeJoint.connectedAnchor = fishBody.transform.InverseTransformPoint(mouth.position);

            fishBody.useGravity = true;
            fishBody.isKinematic = false;

            fish?.OnCaught();

            Debug.Log("Fish hooked");
        }
    }
}