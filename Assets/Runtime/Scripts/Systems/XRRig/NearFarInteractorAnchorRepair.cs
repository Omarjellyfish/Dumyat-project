using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace DumyatRuntime
{
    /// <summary>
    /// Rebuilds the helper transforms a <see cref="NearFarInteractor"/> depends on if something
    /// destroys them mid-session, so the interactor keeps working instead of throwing every frame.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// A Near-Far Interactor owns two GameObjects that XRI creates at runtime and parents under the
    /// XR Origin:
    ///   • "[hand InteractionAttachController] Attach" (+ "Attach Child") — the grab anchor, cached
    ///     by the interactor as <c>attachTransform</c>.
    ///   • "[hand CurveInteractionCaster] Stabilization Cast Origin" — the smoothed ray origin,
    ///     returned by the caster's <c>effectiveCastOrigin</c>.
    ///
    /// Both are created ONCE behind a one-shot guard and neither is ever rebuilt. If they are
    /// destroyed while the interactor survives — which happens when toggling the XR Device Simulator
    /// during Play, because the rig gets swapped between its controller and hand-tracking branches —
    /// the interactor is left holding destroyed references and spams MissingReferenceException from
    /// <c>InteractionAttachController.SyncAnchorParent</c> and
    /// <c>CurveInteractionCaster.UpdateUIModel</c>. Grabbing and UI rays stop working for the rest
    /// of the session.
    ///
    /// The two need different treatment, because their guards differ:
    ///   • The attach controller guards on <c>m_AnchorParent == null</c>, which a destroyed object
    ///     already satisfies — asking it for the anchor again is enough to rebuild it.
    ///   • The caster guards on a private <c>bool</c> that stays true after its anchor dies, so it
    ///     can only be rebuilt by clearing that flag. There is no public API for it, hence the
    ///     reflection below. It is resolved once and null-checked, so an XRI upgrade that renames
    ///     the field degrades to one warning and the untouched stock behaviour rather than throwing.
    ///
    /// Costs nothing when nothing is broken: the common path is a single Unity null check per frame.
    /// </summary>
    [RequireComponent(typeof(NearFarInteractor))]
    [AddComponentMenu("Dumyat/XR Rig/Near-Far Interactor Anchor Repair")]
    public class NearFarInteractorAnchorRepair : MonoBehaviour
    {
        private const string StabilizationFlagField = "m_InitializedStabilizationOrigin";
        private const string StabilizationAnchorField = "m_StabilizationAnchor";

        private NearFarInteractor _interactor;
        private CurveInteractionCaster _farCaster;

        private static FieldInfo _flagField;
        private static FieldInfo _anchorField;
        private static bool _reflectionResolved;

        private void Awake()
        {
            _interactor = GetComponent<NearFarInteractor>();
            ResolveReflection();
        }

        /// <summary>
        /// Repairs the moment this interactor is switched back on, which is when the damage actually
        /// starts to matter: a disabled interactor is never preprocessed, so a destroyed anchor sits
        /// harmless until the rig swaps back to controllers. Doing it here rather than waiting for
        /// the first <see cref="LateUpdate"/> is what keeps that swap completely silent — otherwise
        /// <c>XRInteractionManager.Update</c> runs first and throws once before the repair lands.
        /// </summary>
        private void OnEnable()
        {
            Repair();
        }

        // Also polled, for damage inflicted while the interactor is already active and running.
        // LateUpdate rather than Update: XRI drives the interactor from XRInteractionManager.Update,
        // so repairing here puts the rebuilt anchors in place before the next frame reads them.
        private void LateUpdate()
        {
            Repair();
        }

        private void Repair()
        {
            if (_interactor == null) return;

            RepairAttachAnchor();
            RepairStabilizationAnchor();
        }

        /// <summary>
        /// Re-asks the attach controller for its anchor when the cached one has been destroyed.
        /// </summary>
        private void RepairAttachAnchor()
        {
            // Unity's overloaded == reports a destroyed object as null, which is exactly the state
            // being repaired. A never-assigned attachTransform reads the same and rebuilding it is
            // equally correct, so the two cases do not need telling apart.
            if (_interactor.attachTransform != null) return;

            IInteractionAttachController controller = _interactor.interactionAttachController;
            if (controller == null) return;

            Transform rebuilt = controller.GetOrCreateAnchorTransform(true);
            if (rebuilt == null) return;

            _interactor.attachTransform = rebuilt;
        }

        /// <summary>
        /// Clears the caster's one-shot stabilization guard so it recreates its cast origin. Until it
        /// does, <c>effectiveCastOrigin</c> falls back to the plain cast origin, so the ray keeps
        /// working through the gap rather than blinking out.
        /// </summary>
        private void RepairStabilizationAnchor()
        {
            if (_flagField == null || _anchorField == null) return;

            // Resolved here rather than in Awake: XRI assigns farInteractionCaster during its own
            // initialisation, which is not guaranteed to have happened by the time this component
            // wakes up.
            if (_farCaster == null)
                _farCaster = _interactor.farInteractionCaster as CurveInteractionCaster;

            if (_farCaster == null) return;

            // Only act once the anchor is genuinely destroyed: clearing the flag while the caster is
            // healthy would make it rebuild a perfectly good anchor every frame.
            if (!(_anchorField.GetValue(_farCaster) is Transform anchor)) return;
            if (anchor != null) return;

            _anchorField.SetValue(_farCaster, null);
            _flagField.SetValue(_farCaster, false);
        }

        private static void ResolveReflection()
        {
            if (_reflectionResolved) return;
            _reflectionResolved = true;

            // The fields live on InteractionCasterBase rather than CurveInteractionCaster itself.
            // Walked rather than read off BaseType directly so an XRI release that inserts another
            // class between the two still finds them.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            for (System.Type t = typeof(CurveInteractionCaster); t != null; t = t.BaseType)
            {
                _flagField ??= t.GetField(StabilizationFlagField, flags);
                _anchorField ??= t.GetField(StabilizationAnchorField, flags);

                if (_flagField != null && _anchorField != null) break;
            }

            // Both must be the expected types, or writing to them would corrupt the caster rather
            // than repair it.
            if (_flagField != null && _flagField.FieldType != typeof(bool)) _flagField = null;
            if (_anchorField != null && _anchorField.FieldType != typeof(Transform)) _anchorField = null;

            if (_flagField == null || _anchorField == null)
                Debug.LogWarning($"{nameof(NearFarInteractorAnchorRepair)}: could not find " +
                                 $"'{StabilizationFlagField}'/'{StabilizationAnchorField}' on " +
                                 "InteractionCasterBase. XRI has probably renamed them — the ray's " +
                                 "stabilized cast origin will no longer self-repair. The grab " +
                                 "anchor repair is unaffected.");
        }
    }
}
