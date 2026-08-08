public interface IPresentationCustomAction
{
    /// <summary>
    /// Trigger a custom action on the current location.
    /// This can be wired up via UI buttons passing a string parameter.
    /// </summary>
    /// <param name="actionId">The identifier for the action (e.g., "PlayVideo", "ShowStats").</param>
    void TriggerAction(string actionId);
}
