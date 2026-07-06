using Reflex.Core;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Installer for services that only exist in the Crafting scene.
    /// Attach this alongside the ContainerScope component in your crafting scene.
    /// </summary>
    public sealed class CraftingSceneInstaller : GenericSceneInstaller
    {
        [Header("Crafting Scene Services")]


        [Tooltip("The crafting station manager for this scene.")]
        [SerializeField] private CraftingStation _craftingStation;

        [Tooltip("Scene-bound services (optional — auto-found if left empty)")]
        [SerializeField] private FloatingUIManager _floatingUIManager;

        public override void InstallBindings(ContainerBuilder containerBuilder)
        {
            base.InstallBindings(containerBuilder);

            Register<FloatingUIManager, IFloatingTextService>(containerBuilder, _floatingUIManager);

            // Example: Registering the CraftingStation if it implements an interface (e.g. ICraftingStation)
            // Register<CraftingStation, ICraftingStation>(containerBuilder, _craftingStation);

            // If it doesn't have an interface and you just want to inject the concrete class:
            // if (_craftingStation != null)
            //     containerBuilder.RegisterValue(_craftingStation);

        }
    }
}
