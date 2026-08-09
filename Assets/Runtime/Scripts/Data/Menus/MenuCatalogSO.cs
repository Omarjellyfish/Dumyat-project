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
        [SerializeField] private string _newGameSceneName = "Home";

        [Tooltip("Scenes that must never become the Continue target — the main menu itself, test " +
                 "scenes, anything a player should not be dropped back into. The main menu scene is " +
                 "excluded automatically and does not need listing here.")]
        [SerializeField] private string[] _nonResumableScenes;

        [Header("Loading Screen")]
        [Tooltip("Shown one at a time while loading, in order, looping. Leave empty for no tips.")]
        [TextArea(2, 4)]
        [SerializeField] private string[] _loadingTips;

        [Tooltip("Arabic tips, shown when the player's language setting is Arabic. Leave empty and " +
                 "the loading screen falls back to the English list, so an untranslated build still " +
                 "shows tips rather than a blank line.")]
        [TextArea(2, 4)]
        [SerializeField] private string[] _loadingTipsArabic;

        [Tooltip("Seconds each tip stays up before the next one.")]
        [SerializeField] private float _tipRotationSeconds = 4f;

        [Tooltip("The travel destinations whose names the menus may reuse. Every place the bus goes " +
                 "already carries a bilingual player-facing name, and this is what lets the Continue " +
                 "button and the loading screen read it instead of keeping a second copy.\n\n" +
                 "List one destination per SCENE — several assets can name the same scene (the " +
                 "legacy CraftingScene and ToBuild_CraftingScene both point at the carpentry " +
                 "quarter) and the first match wins.")]
        [SerializeField] private TravelDestination[] _sceneDestinations;

        [Tooltip("Player-facing names for scenes NO destination covers — the main menu, the starting " +
                 "room before the bus is unlocked, test scenes. Checked after the destinations " +
                 "above. Without either the overlay falls back to the raw Build Settings scene name, " +
                 "which is an English identifier and never translates.")]
        [SerializeField] private SceneLabel[] _sceneLabels;

        [Header("Controls Page")]
        [Tooltip("Single image showing the control scheme. Read-only — the player cannot rebind.")]
        [SerializeField] private Sprite _controlsImage;

        public string MainMenuSceneName => _mainMenuSceneName;
        public string NewGameSceneName => _newGameSceneName;
        public IReadOnlyList<string> LoadingTips => _loadingTips ?? System.Array.Empty<string>();
        public float TipRotationSeconds => Mathf.Max(0.5f, _tipRotationSeconds);
        public Sprite ControlsImage => _controlsImage;

        /// <summary>
        /// The tip list for the current language, falling back to English whenever the Arabic list
        /// was never authored — a half-translated catalogue still reads rather than showing gaps.
        /// </summary>
        public IReadOnlyList<string> LoadingTipsFor(bool arabic)
        {
            if (arabic && _loadingTipsArabic != null && _loadingTipsArabic.Length > 0)
                return _loadingTipsArabic;

            return LoadingTips;
        }

        /// <summary>
        /// The player-facing name for <paramref name="sceneName"/> — what the Continue button and the
        /// loading screen announce — or the raw scene name when nothing names it. Never returns null.
        ///
        /// Destinations are consulted FIRST so a place is named in exactly one asset. Ras El Bar was
        /// spelled out in its TravelDestination, in this catalogue's label list, and (as
        /// 'El_manara') in the scene file itself; the two copies could disagree, and after the scenes
        /// were renamed the label list was the thing left pointing at a name that no longer existed.
        /// The bus already has to name every place it goes, so that asset is the one source worth
        /// keeping.
        ///
        /// The label list stays for the scenes no destination covers — the main menu is not a bus
        /// stop and never will be.
        /// </summary>
        public string SceneLabelFor(string sceneName, bool arabic)
        {
            if (string.IsNullOrEmpty(sceneName)) return string.Empty;

            if (_sceneDestinations != null)
            {
                for (int i = 0; i < _sceneDestinations.Length; i++)
                {
                    TravelDestination destination = _sceneDestinations[i];
                    if (destination == null || destination.SceneName != sceneName) continue;

                    string named = UiLanguage.Pick(arabic, destination.DisplayName,
                                                   destination.DisplayNameArabic);
                    if (!string.IsNullOrWhiteSpace(named)) return named;
                    break;   // named the scene but left the name blank — fall through to the labels
                }
            }

            if (_sceneLabels == null) return sceneName;

            for (int i = 0; i < _sceneLabels.Length; i++)
            {
                SceneLabel label = _sceneLabels[i];
                if (label == null || label.SceneName != sceneName) continue;

                string picked = UiLanguage.Pick(arabic, label.DisplayName, label.DisplayNameArabic);
                return string.IsNullOrWhiteSpace(picked) ? sceneName : picked;
            }

            return sceneName;
        }

        /// <summary>
        /// One scene's player-facing name, for a scene the bus does not serve.
        ///
        /// This used to be the ONLY source, on the reasoning that the loading screen announces scenes
        /// the bus never goes to and had no way to reach the destination assets — those sit on the
        /// bus station components inside the scenes being left. That second half is what changed:
        /// <see cref="_sceneDestinations"/> holds them directly, so the catalogue can now read the
        /// name the bus already publishes instead of keeping a duplicate that drifts.
        /// </summary>
        [System.Serializable]
        public class SceneLabel
        {
            [Tooltip("The exact scene name as it appears in Build Settings.")]
            public string SceneName;

            [Tooltip("Name shown on the loading screen.")]
            public string DisplayName;

            [Tooltip("Arabic name. Leave empty to fall back to DisplayName.")]
            public string DisplayNameArabic;
        }

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
