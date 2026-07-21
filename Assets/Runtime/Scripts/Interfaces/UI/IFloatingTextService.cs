using UnityEngine;

namespace DumyatRuntime
{
    public interface IFloatingTextService
    {
        FloatingText Show(Vector3 worldPosition, string text);
        void Hide(FloatingText instance);
    }
}
