using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for pooled floating world-space text. Injected in place of
    /// <c>FloatingUIManager.Instance</c>. Backed by the scene-bound <see cref="FloatingUIManager"/>.
    /// </summary>
    public interface IFloatingTextService
    {
        FloatingText Show(Vector3 worldPosition, string text);
        void Hide(FloatingText instance);
    }
}
