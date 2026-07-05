using Reflex.Core;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Scene-container installer for scene-bound MonoBehaviour services (things that need the active
    /// rig's references, an AudioSource pool, or coroutines). Drop the <c>SceneScope</c> prefab into
    /// any gameplay scene: each service is taken from its serialized field, or — if left empty —
    /// auto-discovered in the scene, then registered against its capability interface. A scene that
    /// simply doesn't contain a given manager just won't offer that capability (its consumers are
    /// injected null and null-guard themselves).
    ///
    /// Note: the scene lookup here is deliberate — an installer is the composition root, the one
    /// sanctioned place to reach across the scene. Regular components must still inject, not Find.
    /// </summary>
    public sealed class SceneInstaller : MonoBehaviour, IInstaller
    {
        [Header("Scene Services (optional — auto-found in the scene if left empty)")]
        [Tooltip("Haptics is scene-scoped: it references this rig's controller impulse players.")]
        [SerializeField] private HapticFeedback _hapticFeedback;
        [SerializeField] private SoundManager _soundManager;
        [SerializeField] private WorldAudioManager _worldAudioManager;
        [SerializeField] private FloatingUIManager _floatingUIManager;

        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            Register<HapticFeedback, IHapticService>(containerBuilder, _hapticFeedback);
            Register<SoundManager, ISoundService>(containerBuilder, _soundManager);
            Register<WorldAudioManager, IWorldAudioService>(containerBuilder, _worldAudioManager);
            Register<FloatingUIManager, IFloatingTextService>(containerBuilder, _floatingUIManager);
        }

        // Registers the serialized instance, or the first one found in the scene, under TContract.
        private static void Register<TImpl, TContract>(ContainerBuilder builder, TImpl serialized)
            where TImpl : MonoBehaviour
        {
            TImpl impl = serialized != null
                ? serialized
                : Object.FindFirstObjectByType<TImpl>(FindObjectsInactive.Include);

            if (impl != null)
                builder.RegisterValue(impl, new[] { typeof(TContract) });
        }
    }
}
