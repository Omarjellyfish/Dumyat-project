namespace DumyatRuntime
{
    /// <summary>
    /// When the microphone listens.
    ///
    /// Deliberately NOT <see cref="VoiceCaptureController.CaptureMode"/>, even though the two carry
    /// the same two meanings. <see cref="SettingsService"/> is a root-container service that must be
    /// constructible on the main menu, long before any scene has a capture controller; binding its
    /// contract to a MonoBehaviour's nested enum would drag the whole AI/Voice layer into that
    /// dependency. The two are also declared in opposite order, so a cast between them would be
    /// silently wrong — <see cref="VoiceCaptureSettingsBinder"/> maps them explicitly instead.
    /// </summary>
    public enum MicCaptureMode
    {
        /// <summary>Opens on speech, commits on silence. The default.</summary>
        VoiceActivity,

        /// <summary>Opens only while <see cref="PushToTalkButton"/> is held.</summary>
        PushToTalk
    }

    /// <summary>
    /// Which controller button holds the mic open.
    ///
    /// A PICKER, not an open rebind, and only two entries because only two buttons are free: the
    /// stick walks and turns, the trigger points and uses, the grip picks up, and the menu button
    /// pauses. An open rebind screen in VR answers "that one is taken" to almost every press, and a
    /// player who binds talk to the grip breaks picking things up — inside a menu they would then
    /// have to re-find with a broken grip.
    ///
    /// Neither path names a hand, so <c>&lt;XRController&gt;</c> matches both controllers from one
    /// binding: the player talks with whichever hand is free.
    /// </summary>
    public enum PushToTalkButton
    {
        /// <summary>A on the right controller, X on the left.</summary>
        Primary,

        /// <summary>B on the right controller, Y on the left.</summary>
        Secondary
    }

    /// <summary>
    /// The one place the <see cref="PushToTalkButton"/> choice becomes an input path or a label.
    ///
    /// Both the settings UI (drawing the picker) and <see cref="VoiceCaptureController"/> (applying
    /// the binding override) need this mapping, so it lives here rather than in either of them.
    /// </summary>
    public static class PushToTalkBinding
    {
        /// <summary>
        /// Binding group on the XR binding this override targets. Must match the group string in
        /// <c>InputSystem_Actions.inputactions</c>, or the override silently applies to nothing —
        /// <c>ApplyBindingOverride</c> matching zero bindings is not an error.
        /// </summary>
        public const string XRGroup = "XR";

        /// <summary>The control path for a choice, e.g. <c>&lt;XRController&gt;/primaryButton</c>.</summary>
        public static string PathFor(PushToTalkButton button) =>
            button == PushToTalkButton.Secondary
                ? "<XRController>/secondaryButton"
                : "<XRController>/primaryButton";

        /// <summary>
        /// What the player sees on the button. Both hands are listed because the same physical
        /// button carries a different letter per controller, and the binding covers both.
        /// </summary>
        public static string LabelFor(PushToTalkButton button) =>
            button == PushToTalkButton.Secondary ? "B / Y" : "A / X";
    }
}
