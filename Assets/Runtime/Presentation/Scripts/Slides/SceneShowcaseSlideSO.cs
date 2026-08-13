using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// A slide that shows a real place from a real camera. One asset per angle — nineteen of them plus
    /// the shader spotlights — which is why the asset holds only an id pair and a caption: everything
    /// expensive lives in the stage.
    /// </summary>
    [CreateAssetMenu(fileName = "Slide_Scene", menuName = "Dumyat/Presentation/Scene Showcase Slide")]
    public sealed class SceneShowcaseSlideSO : SlideSO
    {
        [Header("What to show")]
        [SerializeField, Tooltip("SceneStage.StageId, e.g. 'Shipyard'.")]
        private string _stageId;

        [SerializeField, Tooltip("CameraAngleSet angle id, e.g. 'wide'.")]
        private string _angleId;

        [Header("Lockup — no panel ground over live 3D, just a rule and type")]
        [SerializeField, Tooltip("Place name, e.g. \"Ras El Bar · the lighthouse\". Falls back to the " +
                                 "slide Title when empty.")]
        private string _placeName;

        [SerializeField, Tooltip("Arabic place name, shown under the Latin one. Optional.")]
        private string _placeNameArabic;

        [SerializeField, TextArea(2, 4), Tooltip("One or two lines about the shot. Falls back to the " +
                                                 "slide Kicker when empty.")]
        private string _caption;

        [Header("Angle pips")]
        [SerializeField, Tooltip("Show the 'ANGLE 2 / 4' pips.\n\nOff by default: the deck deliberately " +
                                 "does not tell the audience which camera angle they are looking at — " +
                                 "the place is the subject, the angle is an authoring detail. Leave it " +
                                 "off unless you specifically want to count a series out loud.")]
        private bool _showAnglePips;

        public string StageId => _stageId;
        public string AngleId => _angleId;
        public string PlaceName => string.IsNullOrEmpty(_placeName) ? Title : _placeName;
        public string PlaceNameArabic => _placeNameArabic;
        public string Caption => string.IsNullOrEmpty(_caption) ? Kicker : _caption;
        public bool ShowAnglePips => _showAnglePips;

        /// <summary>Never — the backdrop would cover the stage render.</summary>
        public override bool WantsBackdrop => false;
    }
}
