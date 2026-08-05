using Reflex.Attributes;
using TMPro;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Writes the second line branded under the Continue nail: where the player left off and how
    /// long ago, or that there is nothing to go back to.
    ///
    /// The wording comes from <see cref="ResumeDescription"/>, the same helper the UI Toolkit
    /// Continue button uses, so the bench and the panel never describe the same save differently.
    /// </summary>
    public class NailContinueCaption : MonoBehaviour
    {
        [Header("Caption")]
        [Tooltip("Line to write. Taken from this GameObject when left empty.")]
        [SerializeField] private TMP_Text _caption;

        [Tooltip("Shown when there is no save to resume.")]
        [SerializeField] private string _noSaveText = "NOTHING TO GO BACK TO";

        private IGameSessionService _session;

        [Inject]
        private void Inject(IGameSessionService session) => _session = session;

        // OnEnable rather than Start: NailGroupController hides and re-shows these nails whenever a
        // panel opens, and a save written in between should be reflected on the way back.
        private void OnEnable()
        {
            if (_caption == null) _caption = GetComponent<TMP_Text>();
            if (_caption == null)
            {
                Debug.LogWarning($"{nameof(NailContinueCaption)} on '{name}' has no {nameof(TMP_Text)} — " +
                                 "the Continue nail will carry no caption.", this);
                return;
            }

            if (_session == null)
            {
                _caption.text = string.Empty;
                Debug.LogWarning($"{nameof(NailContinueCaption)} on '{name}' has no {nameof(IGameSessionService)} " +
                                 "injected — it cannot tell whether there is a save. Does this scene have a " +
                                 "ContainerScope?", this);
                return;
            }

            _caption.text = _session.HasSave
                ? ResumeDescription.For(_session.ResumeSceneName, _session.ResumeSavedAtUtc).ToUpperInvariant()
                : _noSaveText;
        }
    }
}
