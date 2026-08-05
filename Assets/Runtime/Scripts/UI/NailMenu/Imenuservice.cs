namespace DumyatRuntime
{
    public interface IMenuService
    {
        void LoadLocation(string sceneName);

        void ToggleSettings();
        void OpenSettings();
        void CloseSettings();

        void ToggleCredits();
        void OpenCredits();
        void CloseCredits();
    }
}