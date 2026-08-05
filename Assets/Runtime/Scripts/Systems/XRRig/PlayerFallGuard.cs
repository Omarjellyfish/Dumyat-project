using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Catches a player who has fallen out of the world and puts them back on solid ground.
    ///
    /// WHY A GUARD RATHER THAN ONLY COLLIDERS
    /// --------------------------------------
    /// Boundary colliders are the right FIRST line of defence and should still be authored per scene —
    /// but they only ever cover the gaps someone remembered. Quaysides, jetty edges, the lip of a
    /// terrain, a stairwell with no railing, a NavMesh-carved hole, or simply a new prop moved after
    /// the walls were placed will all let a player through eventually. This is the backstop: it needs
    /// no per-scene setup, so it protects scenes nobody has audited yet and scenes that do not exist
    /// yet.
    ///
    /// HOW IT DECIDES
    /// --------------
    /// While the player is standing on something, their position is remembered as the last safe spot,
    /// resampled at most every <see cref="_safePointInterval"/> so it trails slightly behind them
    /// rather than following them over the edge they just walked off. When they end up
    /// <see cref="_fallDistance"/> below that spot, or below <see cref="_killPlaneY"/> outright, they
    /// are blinked back to it. The blink goes through <see cref="IScreenFadeService"/> because a
    /// teleport the player can see is a comfort problem in VR — the same reason travel fades.
    ///
    /// Put this on the XR rig root, or anywhere persistent: it resolves the rig itself and re-resolves
    /// after every scene load, so one instance covers the whole game.
    /// </summary>
    public class PlayerFallGuard : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("How far BELOW the last safe spot the player must get before they count as fallen. " +
                 "Comfortably more than any step, ramp or drop the player is meant to be able to take.")]
        [SerializeField, Min(0.5f)] private float _fallDistance = 3f;

        [Tooltip("Absolute world Y below which the player is always considered lost, whatever the last " +
                 "safe spot was — the catch-all for a scene whose floor sits near y=0.")]
        [SerializeField] private float _killPlaneY = -25f;

        [Tooltip("How far below the rig to look for ground. Anything within this counts as standing on " +
                 "something; beyond it the player is in the air.")]
        [SerializeField, Min(0.2f)] private float _groundProbeDistance = 1.2f;

        [Tooltip("Layers that count as solid ground. Leave as Everything unless props/water are causing " +
                 "false 'safe' readings.")]
        [SerializeField] private LayerMask _groundLayers = ~0;

        [Header("Safe point")]
        [Tooltip("Seconds between samples of the last safe spot. A small delay is deliberate: sampling " +
                 "every frame would record the very edge tile the player is about to step off, and drop " +
                 "them straight back onto it.")]
        [SerializeField, Min(0f)] private float _safePointInterval = 0.75f;

        [Tooltip("Lifted slightly so the recovered player stands ON the floor rather than inside it.")]
        [SerializeField] private float _recoveryLift = 0.05f;

        [Header("Recovery")]
        [Tooltip("Seconds to black out before the move. Short — this is a blink, not a transition.")]
        [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.12f;

        [Tooltip("Seconds to fade back in once the player is standing safely again.")]
        [SerializeField, Min(0f)] private float _fadeInSeconds = 0.25f;

        [Tooltip("Ignore further falls for this long after a recovery, so a player dropped into a bad " +
                 "spot cannot ping-pong every frame.")]
        [SerializeField, Min(0.1f)] private float _recoveryCooldown = 1f;

        private IScreenFadeService _fade;

        private Transform _rig;
        private Vector3 _lastSafePosition;
        private bool _hasSafePosition;
        private float _safePointTimer;
        private float _cooldownTimer;

        [Inject]
        private void Inject(IScreenFadeService fade) => _fade = fade;

        /// <summary>
        /// Only one guard may run. It lives on the XR rig — the one object guaranteed to exist whenever
        /// there is a player to protect — because putting it on the app-wide ProjectScope did NOT work:
        /// pressing Play directly in a gameplay scene instantiates no ProjectScope at all, so the guard
        /// silently did not exist in exactly the sessions that needed it.
        /// </summary>
        private static PlayerFallGuard _active;

        private void OnEnable()
        {
            if (_active != null && _active != this)
            {
                enabled = false;
                return;
            }
            _active = this;
        }

        private void OnDisable()
        {
            if (_active == this) _active = null;
        }

        private void Update()
        {
            if (!EnsureRig()) return;

            _cooldownTimer -= Time.deltaTime;

            if (IsFallen())
            {
                Recover();
                return;
            }

            SampleSafePosition();
        }

        /// <summary>
        /// True once the player is somewhere they should not be: below the last safe spot by more than
        /// the allowed drop, under the kill plane, or STANDING ON a surface marked
        /// <see cref="NonWalkableSurface"/>.
        ///
        /// That last case is the one that matters most in practice. The water here is a solid plane, and
        /// Ezbet's quay is only 1.2 m above it, so walking off the edge is not a "fall" by any height
        /// measure — the player simply arrives on the water and keeps walking. No threshold catches
        /// that; the surface has to declare itself.
        /// </summary>
        private bool IsFallen()
        {
            if (_cooldownTimer > 0f) return false;

            Vector3 position = _rig.position;
            if (position.y <= _killPlaneY) return true;

            if (IsStandingOnForbiddenSurface()) return true;

            return _hasSafePosition && position.y < _lastSafePosition.y - _fallDistance;
        }

        // Records where the player is standing, at most every _safePointInterval. A forbidden surface is
        // never recorded, so the player is always sent back to real ground.
        private void SampleSafePosition()
        {
            _safePointTimer -= Time.deltaTime;
            if (_safePointTimer > 0f) return;

            if (!TryFindGround(out Vector3 ground, out Collider surface)) return;
            if (surface != null && surface.GetComponentInParent<NonWalkableSurface>() != null) return;

            _safePointTimer = _safePointInterval;
            _lastSafePosition = ground;
            _hasSafePosition = true;
        }

        private bool IsStandingOnForbiddenSurface()
        {
            if (!TryFindGround(out _, out Collider surface) || surface == null) return false;
            return surface.GetComponentInParent<NonWalkableSurface>() != null;
        }

        /// <summary>
        /// Ground directly under the rig, if it is close enough to be standing on, along with the
        /// collider it belongs to. Triggers are ignored so the many trigger volumes in these scenes —
        /// merchant detection ranges, tutorial zones — never read as floor.
        /// </summary>
        private bool TryFindGround(out Vector3 ground, out Collider surface)
        {
            ground = default;
            surface = null;

            // From slightly above the rig's feet, so a rig resting exactly on the floor still starts the
            // ray outside the collider it is standing on.
            Vector3 origin = _rig.position + Vector3.up * 0.2f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _groundProbeDistance + 0.2f,
                                 _groundLayers, QueryTriggerInteraction.Ignore))
                return false;

            ground = hit.point + Vector3.up * _recoveryLift;
            surface = hit.collider;
            return true;
        }

        // Blink out, put the player back, blink in. The move itself is instant and hidden by the fade.
        private void Recover()
        {
            _cooldownTimer = _recoveryCooldown;

            Vector3 target = _hasSafePosition ? _lastSafePosition : FindArrivalPoint();

            _fade?.FadeToBlack(_fadeOutSeconds);
            Teleport(target);
            _fade?.FadeFromBlack(_fadeInSeconds);

            Debug.Log($"{nameof(PlayerFallGuard)}: player fell out of the world — returned to " +
                      $"{target}. If this happens in the same place repeatedly, that edge needs a collider.",
                      this);
        }

        /// <summary>
        /// Moves the rig, defeating anything that would fight the move: a CharacterController writes the
        /// transform back from its own internal position unless it is switched off across the change,
        /// and a Rigidbody keeps the downward velocity the fall built up, so the player would resume
        /// plummeting the moment they arrived.
        /// </summary>
        private void Teleport(Vector3 position)
        {
            var controller = _rig.GetComponentInChildren<CharacterController>();
            bool hadController = controller != null && controller.enabled;
            if (hadController) controller.enabled = false;

            _rig.position = position;

            var body = _rig.GetComponentInChildren<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            if (hadController) controller.enabled = true;
        }

        // Last resort when the player fell before ever touching ground: the scene's authored spawn.
        private Vector3 FindArrivalPoint()
        {
            GameObject arrival = GameObject.FindWithTag("ArrivalPoint");
            return arrival != null ? arrival.transform.position : Vector3.zero;
        }

        /// <summary>
        /// Finds the player rig, re-resolving whenever the previous one has gone. Rigs are per-scene
        /// here — some scenes author their own, others have one spawned by
        /// <see cref="PlayerRigSpawner"/> — so this cannot be cached once.
        /// </summary>
        private bool EnsureRig()
        {
            if (_rig != null) return true;

            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) _rig = tagged.transform;
            else if (Camera.main != null) _rig = Camera.main.transform.root;

            if (_rig == null) return false;

            // A new rig means a new scene: the old scene's safe spot is meaningless here.
            _hasSafePosition = false;
            _safePointTimer = 0f;
            return true;
        }
    }
}
