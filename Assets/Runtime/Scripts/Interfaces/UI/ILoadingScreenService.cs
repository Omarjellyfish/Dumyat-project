namespace DumyatRuntime
{
    /// <summary>
    /// The loading overlay shown during scene transitions. Backed by
    /// <see cref="LoadingScreenPresenter"/>, which drives itself entirely from the
    /// <c>SceneLoad*</c> events — nothing needs to call this.
    ///
    /// The contract exists so the presenter can be registered as an eagerly-resolved binding
    /// (Reflex needs a contract type to bind against) and so a test can assert the overlay state.
    /// </summary>
    public interface ILoadingScreenService
    {
        bool IsVisible { get; }
    }
}
