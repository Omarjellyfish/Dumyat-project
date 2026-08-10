using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Marks a Rigidbody as something a <see cref="HoopScoreZone"/> will count. Empty by design — it is
    /// a capability tag, not behaviour.
    ///
    /// This exists instead of a layer or tag check because the project has no spare layer for balls and
    /// tags are stringly-typed; a component is checked by type, shows up in the Inspector, and lets a
    /// designer make any prop scoreable by adding it. Without one, every loose Rigidbody that fell
    /// through the hoop — a chair, a plank — would register a basket.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScoreableBall : MonoBehaviour
    {
    }
}
