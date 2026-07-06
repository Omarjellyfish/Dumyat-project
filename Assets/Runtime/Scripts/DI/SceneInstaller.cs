using Reflex.Core;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Scene-container installer for scene-bound MonoBehaviour services.
    /// Drop the <c>SceneScope</c> prefab into any gameplay scene: each service is taken from its
    /// serialized field, or — if left empty — auto-discovered in the scene, then registered against
    /// its capability interface.
    ///
    /// Global services (SoundManager, WorldAudioManager, HapticFeedback) are registered in
    /// <see cref="GameInstaller"/> on the ProjectScope prefab.
    /// </summary>
    public sealed class SceneInstaller : MonoBehaviour, IInstaller
    {
        [Header("Scene Services (optional — auto-found in the scene if left empty)")]
        [SerializeField] private FloatingUIManager _floatingUIManager;

        public void InstallBindings(ContainerBuilder containerBuilder)
        {
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
