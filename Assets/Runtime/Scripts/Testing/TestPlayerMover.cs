using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// TEST-ONLY keyboard mover for the "Player (Test Stand-in)" capsule, so merchant
    /// approaches can be exercised in the Editor without VR locomotion: WASD / arrow keys
    /// move on the XZ plane, hold Left Shift to move faster. Drives a DYNAMIC (non-kinematic)
    /// Rigidbody by setting its horizontal <see cref="Rigidbody.linearVelocity"/> so the capsule
    /// is blocked by, and slides along, scene colliders (market stalls, walls) instead of passing
    /// through them — and still fires trigger volumes (e.g. <see cref="MerchantProximityTrigger"/>).
    /// The Rigidbody freezes its Y position and pitch/roll (so it stays upright at a constant height
    /// without needing a ground collider) but is free to YAW: each step the body is rotated to match
    /// the main camera's yaw, so the player faces where the camera looks. Not used by the shipping
    /// game — the real player is the XR rig.
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
            ConfigureBody();
        }

        // Make the capsule a DYNAMIC physics body so it is blocked by, and slides along, scene
        // colliders — while staying upright at a constant height (Y position + pitch/roll frozen,
        // gravity off) so it needs no ground collider. YAW is left free so the body can be turned to
        // match the camera. Movement is applied as horizontal velocity.
        private void ConfigureBody()
        {
            if (_body == null) return;
            _body.isKinematic = false;
            _body.useGravity = false;
            _body.constraints = RigidbodyConstraints.FreezePositionY
                              | RigidbodyConstraints.FreezeRotationX
                              | RigidbodyConstraints.FreezeRotationZ;
            _body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void FixedUpdate()
        {
            Transform reference = ResolveDirectionReference();

            // Rotate the body to face the same yaw as the main camera, so the player turns with the
            // camera and always faces where it looks.
            if (reference != null && _body != null && !_body.isKinematic)
            {
                Vector3 lookForward = reference.forward;
                lookForward.y = 0f;
                if (lookForward.sqrMagnitude > 0.0001f)
                    _body.MoveRotation(Quaternion.LookRotation(lookForward.normalized, Vector3.up));
            }

            Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));

            // Camera-relative desired velocity: W moves where the camera looks, A/D strafe.
            Vector3 horizontalVelocity = Vector3.zero;
            if (input.sqrMagnitude >= 0.01f)
            {
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
                horizontalVelocity = direction * speed;
            }

            // Drive the dynamic body by velocity so the physics engine resolves collisions and
            // slides the capsule along walls/props. Preserve any vertical velocity (gravity, if the
            // body is left un-frozen). Fall back to kinematic MovePosition / transform if needed.
            if (_body != null && !_body.isKinematic)
            {
                Vector3 velocity = _body.linearVelocity;
                velocity.x = horizontalVelocity.x;
                velocity.z = horizontalVelocity.z;
                _body.linearVelocity = velocity;
            }
            else if (_body != null)
            {
                _body.MovePosition(_body.position + horizontalVelocity * Time.fixedDeltaTime);
            }
            else
            {
                transform.position += horizontalVelocity * Time.fixedDeltaTime;
            }
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
