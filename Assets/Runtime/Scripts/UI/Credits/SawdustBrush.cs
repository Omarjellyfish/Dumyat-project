using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Marks the exact point on a tool that sweeps sawdust — the hammer's head, a fingertip.
    ///
    /// <see cref="SawdustReveal"/> works out where to clear from whatever enters its trigger, and a
    /// collider's own centre is a bad answer for anything but a fingertip: the hammer is one collider
    /// covering the whole tool, so its centre is halfway down the handle. Sweeping from there clears
    /// dust the head never touched and leaves dust it did.
    ///
    /// Put this on a child transform sitting at the business end, and the sweep uses that instead.
    /// </summary>
    public class SawdustBrush : MonoBehaviour
    {
        [Tooltip("Multiplies the drift's own brush radius. Above 1 for a broad tool like a hammer " +
                 "head, below 1 for a fingertip.")]
        [Range(0.25f, 3f)]
        [SerializeField] private float _radiusScale = 1f;

        public float RadiusScale => _radiusScale;
    }
}
