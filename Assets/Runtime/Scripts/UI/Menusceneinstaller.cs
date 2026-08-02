using Reflex.Core;
using UnityEngine;

namespace DumyatRuntime
{
    public class MenuSceneInstaller : GenericSceneInstaller
    {
        [SerializeField] private MenuManager _menuManager;
        [SerializeField] private SettingsManager _settingsManager;

        public override void InstallBindings(ContainerBuilder containerBuilder)
        {
            base.InstallBindings(containerBuilder);

            Register<MenuManager, IMenuService>(containerBuilder, _menuManager);
            Register<SettingsManager, ISettingsService>(containerBuilder, _settingsManager);
        }
    }
}