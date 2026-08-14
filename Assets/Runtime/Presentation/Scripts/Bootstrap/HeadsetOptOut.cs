using System.Reflection;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Shuts XR down for the presentation build.
    ///
    /// WHY THIS RATHER THAN UNCHECKING OPENXR IN PROJECT SETTINGS:
    /// XR plug-in settings are per BUILD TARGET, not per build profile, and Dumyat itself needs OpenXR
    /// on Windows Standalone. Turning it off to suit the deck would break the game's own build. So the
    /// deck opts out at runtime instead: OpenXR initialises as the project says it should, and this
    /// immediately stops it.
    ///
    /// Without it the presentation hunts for a headset at launch, stalls while it fails, renders through
    /// the XR pipeline, and reports the wrong display resolution — on a machine that may have no headset
    /// attached at all.
    ///
    /// WHY REFLECTION:
    /// Project.Runtime does not reference Unity.XR.Management, and there is no UNITY_XR_MANAGEMENT define
    /// in this project — an #if on one would compile to nothing and this component would silently do
    /// nothing at all. Reflection keeps XR an optional dependency for a single shutdown call, rather than
    /// adding an assembly reference the rest of the deck has no use for.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class HeadsetOptOut : MonoBehaviour
    {
        [SerializeField, Tooltip("Force the frame rate and vSync suited to a projector. Smooth camera " +
                                 "moves matter more here than headroom.")]
        private bool _applyDisplaySettings = true;

        [SerializeField, Range(30, 240)] private int _targetFrameRate = 60;

        [SerializeField, Tooltip("Log what happened. Worth leaving on — 'XR stopped' in the player log is " +
                                 "how you confirm the build is not talking to a headset.")]
        private bool _verbose = true;

        private void Awake()
        {
            StopXr();

            if (_applyDisplaySettings)
            {
                // vSync on, 60 target: a deck that tears on a camera move looks worse than one at 60.
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = _targetFrameRate;
            }

            // A stray Paused state from the gameplay root container would freeze the deck: transitions
            // run on unscaled time, but the video player does not.
            if (!Mathf.Approximately(Time.timeScale, 1f))
            {
                Debug.LogWarning($"[HeadsetOptOut] timeScale was {Time.timeScale}; forcing 1 so " +
                                 "transitions and video cannot stall.", this);
                Time.timeScale = 1f;
            }
        }

        private void StopXr()
        {
            System.Type settingsType = System.Type.GetType(
                "UnityEngine.XR.Management.XRGeneralSettings, Unity.XR.Management");
            if (settingsType == null)
            {
                if (_verbose) Debug.Log("[HeadsetOptOut] XR Management not present — nothing to stop.", this);
                return;
            }

            PropertyInfo instanceProp = settingsType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            object settings = instanceProp?.GetValue(null);
            if (settings == null) return;

            object manager = settingsType.GetProperty("Manager")?.GetValue(settings);
            if (manager == null) return;

            System.Type managerType = manager.GetType();
            object activeLoader = managerType.GetProperty("activeLoader")?.GetValue(manager);
            if (activeLoader == null)
            {
                // The normal case in the Editor and on a machine with no XR runtime. Silent by design:
                // a warning here would train people to ignore warnings.
                if (_verbose) Debug.Log("[HeadsetOptOut] no active XR loader; already screen-only.", this);
                return;
            }

            string loaderName = (activeLoader as Object)?.name ?? activeLoader.GetType().Name;
            managerType.GetMethod("StopSubsystems")?.Invoke(manager, null);
            managerType.GetMethod("DeinitializeLoader")?.Invoke(manager, null);

            if (_verbose)
                Debug.Log($"[HeadsetOptOut] stopped XR loader '{loaderName}' — the deck renders to the screen.", this);
        }
    }
}
