using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// TEST-ONLY keyboard mover for the "Player (Test Stand-in)" capsule, so merchant
    /// approaches can be exercised in the Editor without VR locomotion: WASD / arrow keys
    /// move on the XZ plane, hold Left Shift to move faster. Drives the kinematic Rigidbody
    /// via <see cref="Rigidbody.MovePosition"/> so trigger volumes (e.g.
    /// <see cref="MerchantProximityTrigger"/>) fire reliably. Not used by the shipping game —
    /// the real player is the XR rig.
    /// </summary>
    public class TestPlayerMover : MonoBehaviour
    {
        [Tooltip("Walk speed in metres per second.")]
        [SerializeField, Min(0f)] private float _moveSpeed = 2.5f;

        [Tooltip("Speed multiplier while Left Shift is held.")]
        [SerializeField, Min(1f)] private float _runMultiplier = 2f;

        [Tooltip("Movement is relative to this transform's forward/right (flattened onto XZ). " +
                 "Empty = the main camera, so W always walks away from the camera.")]
        [SerializeField] private Transform _directionReference;

        private Rigidbody _body;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude < 0.01f) return;

            // Camera-relative: W moves where the camera looks, A/D strafe across the view.
            Transform reference = ResolveDirectionReference();
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            if (reference != null)
            {
                forward = reference.forward; forward.y = 0f; forward.Normalize();
                right = reference.right; right.y = 0f; right.Normalize();
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward; // camera looking straight down
            }
            Vector3 direction = (right * input.x + forward * input.z).normalized;

            float speed = _moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? _runMultiplier : 1f);
            Vector3 delta = direction * (speed * Time.fixedDeltaTime);

            if (_body != null) _body.MovePosition(_body.position + delta);
            else transform.position += delta;
        }

        private Transform ResolveDirectionReference()
        {
            if (_directionReference != null) return _directionReference;
            Camera cam = Camera.main;
            if (cam != null) _directionReference = cam.transform;
            return _directionReference;
        }
    }
}
