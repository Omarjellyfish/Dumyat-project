using UnityEngine;

namespace DumyatRuntime
{
    public class HookingFish : MonoBehaviour
    {
        private bool isFishHooked = false;

        private void OnTriggerEnter(Collider collision)
        {
            if (isFishHooked) return;
            if (!collision.CompareTag("Fish")) return;

            Fish fish = collision.GetComponent<Fish>();
            Rigidbody fishBody = collision.attachedRigidbody;
            Rigidbody hookColliderRigidbody = GetComponentInParent<Rigidbody>();

            if (fish == null || fishBody == null || hookColliderRigidbody == null)
                return;

            // Transform mouth = fish.MouthPoint != null ? fish.MouthPoint : fishBody.transform;

            fishBody.linearVelocity = Vector3.zero;
            fishBody.angularVelocity = Vector3.zero;

            FixedJoint joint = fish.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = hookColliderRigidbody;
            joint.autoConfigureConnectedAnchor = false;
            fish.StopAnimation();

            // anchor: local to the fish (this object the joint sits on)
            // joint.anchor = fishBody.transform.InverseTransformPoint(mouth.position);

            // connectedAnchor: local to the hook, i.e. where on the hook the mouth should sit
            // joint.connectedAnchor = hookColliderRigidbody.transform.InverseTransformPoint(mouth.position);

            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;

            isFishHooked = true;

            fish.OnCaught();
            Debug.Log("Fish hooked");
        }
    }
}