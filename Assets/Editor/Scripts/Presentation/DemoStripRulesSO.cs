#if UNITY_EDITOR
using UnityEngine;

namespace DumyatEditor
{
    /// <summary>
    /// What to tear out of a copied scene to turn it into a presentation demo stage: no gameplay,
    /// no XR, no audio, no UI, no tutorial — just the art and its baked lighting.
    ///
    /// WHY THIS IS DATA AND NOT A HARD-CODED LIST:
    /// six scenes get stripped, and each one taught us something about what the previous one broke.
    /// Shipyard alone carries 1110 colliders and 376 MonoBehaviours, so a mistake here is invisible
    /// until a camera happens to look at the thing that went missing. Keeping the rules in an asset
    /// means fixing a rule once and re-running it on all six, rather than editing a script and hoping
    /// the earlier scenes were done the same way.
    ///
    /// The keep-list matters more than the removal lists. Some objects LOOK like scenery but are
    /// driven by a script — the Home fan, the water surfaces, the god-ray volumes — and those are
    /// exactly the things the art showcase exists to show.
    /// </summary>
    [CreateAssetMenu(fileName = "DemoStripRules", menuName = "Dumyat/Presentation/Demo Strip Rules")]
    public sealed class DemoStripRulesSO : ScriptableObject
    {
        [Header("Delete whole objects")]
        [SerializeField, Tooltip("Any GameObject whose name CONTAINS one of these is deleted with its " +
                                 "children. Case-insensitive. Checked before anything else.")]
        private string[] _deleteObjectsNameContains =
        {
            "XR Origin", "XR Rig", "XRRig", "XR Interaction Manager",
            "ProjectScope", "SceneScope", "Scene Scope",
            "EventSystem", "Event System",
            "Tutorial", "Canvas", "UIDocument", "UI Document",
            "Player", "GameManager", "Game Manager",
        };

        [Header("Keep, whatever the other rules say")]
        [SerializeField, Tooltip("An object whose name CONTAINS one of these is never deleted, and its " +
                                 "components are never stripped. This is where visual-but-scripted " +
                                 "things go: the fan, water surfaces, god rays, VFX.")]
        private string[] _keepObjectsNameContains =
        {
            "Fan", "Water", "Ocean", "Nile", "Sea",
            "GodRay", "God Ray", "LightShaft", "Light Shaft", "Volumetric",
            "Sky", "Lightmap", "Reflection Probe", "Light Probe",
        };

        [SerializeField, Tooltip("MonoBehaviour type names kept even when 'Remove all MonoBehaviours' " +
                                 "is on. Use the class name only, no namespace.\n\n" +
                                 "Note: anything in a UnityEngine.Rendering namespace (Volume, " +
                                 "ProbeVolume) and anything from Cinemachine is already protected by " +
                                 "rule and does not need listing here.")]
        private string[] _keepComponentTypeNames =
        {
            "WaterSurface", "WaterAnimator", "FanRotator", "Rotator", "Spinner",
            "LightProbeGroup", "ReflectionProbe",
            // Listed as well as rule-protected, so the intent is visible in the Inspector.
            "Volume", "ProbeVolume",
        };

        [Header("Remove components by type name")]
        [SerializeField, Tooltip("Component type names to remove wherever found. Class name only.")]
        private string[] _removeComponentTypeNames =
        {
            "AudioSource", "AudioListener", "AudioReverbZone",
            "Canvas", "CanvasScaler", "GraphicRaycaster", "UIDocument",
            "NavMeshAgent", "NavMeshObstacle",
            "ContainerScope", "SceneInstaller", "GenericSceneInstaller",
        };

        [Header("Blanket removals")]
        [SerializeField, Tooltip("Remove every MonoBehaviour not in the keep lists. This is the big " +
                                 "one — it is what makes a demo scene inert.")]
        private bool _removeAllMonoBehaviours = true;

        [SerializeField, Tooltip("Colliders serve no purpose in a scene nothing walks through, and " +
                                 "Shipyard has over a thousand.")]
        private bool _removeColliders = true;

        [SerializeField] private bool _removeRigidbodies = true;

        [SerializeField, Tooltip("Off by default: particles are often the art (dust, spray, smoke).")]
        private bool _removeParticleSystems = false;

        [SerializeField, Tooltip("Off by default: an Animator may be driving scenery.")]
        private bool _removeAnimators = false;

        [SerializeField, Tooltip("Components whose script reference is missing show as 'Missing (Mono " +
                                 "Script)'. Clearing them removes console noise on every load.")]
        private bool _removeMissingScripts = true;

        [Header("Cameras and lights")]
        [SerializeField, Tooltip("Demo stages get their camera angles from Cinemachine in Phase 3, so " +
                                 "the scene's own gameplay cameras are removed. Cinemachine cameras " +
                                 "and the presentation angle root are never touched.")]
        private bool _removeCameras = true;

        [SerializeField, Tooltip("Leave lights alone — they are half the reason a scene looks right, " +
                                 "and Phase 0 proved the bakes carry over intact.")]
        private bool _removeLights = false;

        [Header("Baked data")]
        [SerializeField, Tooltip("Occlusion culling data is baked in world space and went stale the " +
                                 "moment the stage was offset (Phase 0 measured 203920 bytes of it). " +
                                 "Demo stages do not need it.")]
        private bool _clearOcclusionData = true;

        public string[] DeleteObjectsNameContains => _deleteObjectsNameContains;
        public string[] KeepObjectsNameContains => _keepObjectsNameContains;
        public string[] KeepComponentTypeNames => _keepComponentTypeNames;
        public string[] RemoveComponentTypeNames => _removeComponentTypeNames;
        public bool RemoveAllMonoBehaviours => _removeAllMonoBehaviours;
        public bool RemoveColliders => _removeColliders;
        public bool RemoveRigidbodies => _removeRigidbodies;
        public bool RemoveParticleSystems => _removeParticleSystems;
        public bool RemoveAnimators => _removeAnimators;
        public bool RemoveMissingScripts => _removeMissingScripts;
        public bool RemoveCameras => _removeCameras;
        public bool RemoveLights => _removeLights;
        public bool ClearOcclusionData => _clearOcclusionData;

        /// <summary>True if this object is protected by the keep-list.</summary>
        public bool IsKept(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return false;
            foreach (string k in _keepObjectsNameContains)
            {
                if (!string.IsNullOrEmpty(k) &&
                    objectName.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>True if this component type is protected by the keep-list.</summary>
        public bool IsComponentKept(string typeName)
        {
            foreach (string k in _keepComponentTypeNames)
            {
                if (!string.IsNullOrEmpty(k) &&
                    string.Equals(k, typeName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool ShouldDeleteObject(string objectName)
        {
            if (IsKept(objectName)) return false;
            if (string.IsNullOrEmpty(objectName)) return false;
            foreach (string d in _deleteObjectsNameContains)
            {
                if (!string.IsNullOrEmpty(d) &&
                    objectName.IndexOf(d, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public bool ShouldRemoveComponentType(string typeName)
        {
            if (IsComponentKept(typeName)) return false;
            foreach (string r in _removeComponentTypeNames)
            {
                if (!string.IsNullOrEmpty(r) &&
                    string.Equals(r, typeName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
#endif
