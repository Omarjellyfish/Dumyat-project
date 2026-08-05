using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace DumyatRuntime
{
    /// <summary>
    /// TEMPORARY. Reports why a ray can or cannot click a world-space UI panel, from inside the
    /// running player. Delete once the UI-click bug is closed.
    ///
    /// It exists because the question cannot be answered from the Editor. Reading input state from an
    /// editor-invoked command returns the EDITOR's Input System buffer, which is permanently zero for
    /// XR devices, so every reading taken that way is a false negative. Running inside the player loop
    /// is the only way to see what the interactor actually sees.
    ///
    /// Installs itself — no scene or prefab wiring, which matters when the person reproducing the bug
    /// is wearing a headset and cannot reach the Inspector.
    /// </summary>
    public class UiRayDiagnostics : MonoBehaviour
    {
        /// <summary>Seconds between reports while nothing changes. Keeps the console readable.</summary>
        private const float HeartbeatSeconds = 2f;

        private readonly List<Collider> _hits = new List<Collider>();
        private readonly List<RaycastHit> _raycastHits = new List<RaycastHit>();
        private readonly StringBuilder _line = new StringBuilder();

        private string _lastReport;
        private float _nextHeartbeat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var host = new GameObject("[UiRayDiagnostics]");
            host.AddComponent<UiRayDiagnostics>();
            DontDestroyOnLoad(host);
        }

        private void Update()
        {
            string report = Describe();
            bool changed = report != _lastReport;

            if (!changed && Time.unscaledTime < _nextHeartbeat) return;

            _lastReport = report;
            _nextHeartbeat = Time.unscaledTime + HeartbeatSeconds;
            Debug.Log("[UiRayDiag] " + report);
        }

        private string Describe()
        {
            _line.Clear();
            _line.Append("scene=").Append(gameObject.scene.name)
                 .Append(" cam=").Append(Camera.main == null ? "NULL" : Camera.main.name);

            var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _line.Append(" eventSystems=").Append(systems.Length);

            AppendPanels();

            foreach (var interactor in FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                AppendInteractor(interactor);

            return _line.ToString();
        }

        /// <summary>
        /// Only panels that are actually showing can be clicked — a hidden UI Toolkit document lays out
        /// to a zero rect and so never gets the collider the ray needs.
        /// </summary>
        private void AppendPanels()
        {
            _line.Append(" | panels:");
            foreach (var document in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                VisualElement root = document.rootVisualElement;
                if (root == null || root.resolvedStyle.display == DisplayStyle.None) continue;

                var panelCollider = document.GetComponent<Collider>();
                _line.Append(' ').Append(document.name)
                     .Append(panelCollider == null
                         ? "(NO COLLIDER)"
                         : "(col,trigger=" + panelCollider.isTrigger + ",layer=" + LayerMask.LayerToName(panelCollider.gameObject.layer) + ")");

                // A panel sitting at a NaN position is invisible to every raycast, and is what the
                // "Invalid AABB" / "IsFinite(distanceForSort)" assertions in the console are reporting.
                if (!IsFinite(document.transform.position) || !IsFinite(document.transform.lossyScale))
                    _line.Append("!!NaN-TRANSFORM!!");
            }
        }

        private static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        private void AppendInteractor(NearFarInteractor interactor)
        {
            _line.Append(" | ").Append(interactor.transform.parent == null ? interactor.name : interactor.transform.parent.name);
            _line.Append(" uiEnabled=").Append(interactor.enableUIInteraction);

            // The decisive reading: does the trigger reach the interactor at all on this hardware?
            _line.Append(" uiPress=").Append(interactor.uiPressInput.ReadIsPerformed());
            _line.Append(" select=").Append(interactor.isSelectActive);

            if (interactor.farInteractionCaster is CurveInteractionCaster caster)
            {
                _hits.Clear();
                _raycastHits.Clear();
                caster.TryGetColliderTargets(interactor.interactionManager, _hits, _raycastHits);

                _line.Append(" hits=[");
                for (int i = 0; i < _hits.Count; i++)
                {
                    if (_hits[i] == null) continue;
                    if (i > 0) _line.Append(',');
                    _line.Append(_hits[i].name);
                }
                _line.Append(']');

                _line.Append(" triggerMode=").Append(caster.raycastTriggerInteraction);
            }

            _line.Append(" uiModel=").Append(interactor.TryGetUIModel(out var model) ? "ok(select=" + model.select + ")" : "NONE");
        }
    }
}
