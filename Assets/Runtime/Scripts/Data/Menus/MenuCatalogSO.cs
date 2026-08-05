using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Everything the menus need to know that is content rather than behaviour: which scenes they
    /// load, which scenes are worth resuming, what the loading screen says, and what the controls
    /// page shows. Kept on a ScriptableObject so a designer can repoint the start scene or rewrite
    /// a tip without a code change (CLAUDE.md ▸ "Expose data, not behaviour, to designers").
    /// </summary>
    [CreateAssetMenu(fileName = "MenuCatalog", menuName = "Dumyat/Menus/Menu Catalog")]
    public class MenuCatalogSO : ScriptableObject
    {
        [Header("Scenes")]
        [Tooltip("Scene containing the main menu. Must be in Build Settings.")]
        [SerializeField] private string _mainMenuSceneName = "MainMenu";

        [Tooltip("Scene a fresh game starts in. Must be in Build Settings.")]
        [SerializeField] private string _newGameSceneName = "First_room";

        [Tooltip("Scenes that must never become the Continue target — the main menu itself, test " +
                 "scenes, anything a player should not be dropped back into. The main menu scene is " +
                 "excluded automatically and does not need listing here.")]
        [SerializeField] private string[] _nonResumableScenes;

        [Header("Loading Screen")]
        [Tooltip("Shown one at a time while loading, in order, looping. Leave empty for no tips.")]
        [TextArea(2, 4)]
        [SerializeField] private string[] _loadingTips;

        [Tooltip("Seconds each tip stays up before the next one.")]
        [SerializeField] private float _tipRotationSeconds = 4f;

        [Header("Controls Page")]
        [Tooltip("Single image showing the control scheme. Read-only — the player cannot rebind.")]
        [SerializeField] private Sprite _controlsImage;

        public string MainMenuSceneName => _mainMenuSceneName;
        public string NewGameSceneName => _newGameSceneName;
        public IReadOnlyList<string> LoadingTips => _loadingTips ?? System.Array.Empty<string>();
        public float TipRotationSeconds => Mathf.Max(0.5f, _tipRotationSeconds);
        public Sprite ControlsImage => _controlsImage;

        /// <summary>
        /// True when finishing a load of <paramref name="sceneName"/> should be recorded as the
        /// Continue target. The main menu is always excluded — resuming into it would be a no-op
        /// loop — so it never needs to appear in the serialized list.
        /// </summary>
        public bool IsResumable(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            if (sceneName == _mainMenuSceneName) return false;

            if (_nonResumableScenes != null)
            {
                for (int i = 0; i < _nonResumableScenes.Length; i++)
                {
                    if (_nonResumableScenes[i] == sceneName) return false;
                }
            }

            return true;
        }
    }
}
