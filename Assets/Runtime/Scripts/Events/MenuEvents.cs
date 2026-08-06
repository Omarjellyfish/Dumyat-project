namespace DumyatRuntime
{
    public readonly struct CreditsPanelToggledEvent : IGameEvent
    {
        public readonly bool IsOpen;

        public CreditsPanelToggledEvent(bool isOpen)
        {
            IsOpen = isOpen;
        }
    }
}