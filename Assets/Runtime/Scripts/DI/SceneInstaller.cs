using Reflex.Core;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Scene-container installer for scene-bound MonoBehaviour services (things that need the active
    /// rig's references, an AudioSource pool, or coroutines). Place this under the scene's
    /// <see cref="ContainerScope"/> and assign the in-scene manager instances; they are registered
    /// against their capability interfaces so scene components can inject them.
    /// </summary>
    public sealed class SceneInstaller : MonoBehaviour, IInstaller
    {
        [Header("Scene Services")]
        [Tooltip("Haptics is scene-scoped: it references this rig's controller impulse players.")]
        [SerializeField] private HapticFeedback _hapticFeedback;
        [SerializeField] private SoundManager _soundManager;
        [SerializeField] private WorldAudioManager _worldAudioManager;
        [SerializeField] private FloatingUIManager _floatingUIManager;

        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            if (_hapticFeedback != null)
                containerBuilder.RegisterValue(_hapticFeedback, new[] { typeof(IHapticService) });
            else
                Debug.LogWarning("SceneInstaller: HapticFeedback not assigned; IHapticService will be unresolved in this scene.");

            if (_soundManager != null)
                containerBuilder.RegisterValue(_soundManager, new[] { typeof(ISoundService) });
            else
                Debug.LogWarning("SceneInstaller: SoundManager not assigned; ISoundService will be unresolved in this scene.");

            if (_worldAudioManager != null)
                containerBuilder.RegisterValue(_worldAudioManager, new[] { typeof(IWorldAudioService) });
            else
                Debug.LogWarning("SceneInstaller: WorldAudioManager not assigned; IWorldAudioService will be unresolved in this scene.");

            if (_floatingUIManager != null)
                containerBuilder.RegisterValue(_floatingUIManager, new[] { typeof(IFloatingTextService) });
            else
                Debug.LogWarning("SceneInstaller: FloatingUIManager not assigned; IFloatingTextService will be unresolved in this scene.");
        }
    }
}
