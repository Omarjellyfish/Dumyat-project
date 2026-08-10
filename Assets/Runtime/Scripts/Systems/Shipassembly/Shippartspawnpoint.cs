using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DumyatRuntime
{
    /// <summary>
    /// Put this on each ship-part object (alongside its XRGrabInteractable). It records the
    /// part's starting position/rotation/parent as its "spawn point" so that
    /// <see cref="ShipPartSocket"/> can snap it back there if the player drops it on a
    /// locked or otherwise wrong socket.
    ///
    /// By default the spawn point is captured automatically in Awake, i.e. wherever you've
    /// placed the part in the scene/prefab is what it returns to. If the part gets moved to
    /// its "real" starting spot by some other system after Awake (e.g. spawned/positioned at
    /// runtime), call <see cref="CaptureSpawnPoint"/> yourself once it's in that spot.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class ShipPartSpawnPoint : MonoBehaviour
    {
        public Vector3 SpawnPosition { get; private set; }
        public Quaternion SpawnRotation { get; private set; }
        public Transform SpawnParent { get; private set; }

        private void Awake() => CaptureSpawnPoint();

        /// <summary>Re-records the current transform as this part's spawn point.</summary>
        public void CaptureSpawnPoint()
        {
            SpawnPosition = transform.position;
            SpawnRotation = transform.rotation;
            SpawnParent = transform.parent;
        }
    }
}