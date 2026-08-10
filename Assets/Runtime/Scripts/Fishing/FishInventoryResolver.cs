using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Finds the scene's <see cref="IFishInventory"/>: the reference a designer assigned, else any
    /// component in the scene that implements it. Caches the first success.
    ///
    /// <see cref="IFishInventory"/> is not a DI binding — it is whichever MonoBehaviour happens to be
    /// in the scene (<see cref="CaughtFishInventory"/> normally, the random test driver while
    /// play-testing), so every consumer has to go looking. Three of them were doing it with identical
    /// copies of this method; <c>MerchantApproachCoordinator</c> even carried a note saying to lift it
    /// out if a third caller appeared. This is that third caller.
    ///
    /// Held as a field rather than made static, so each consumer keeps its own cache and one scene's
    /// inventory cannot leak into the next.
    /// </summary>
    public sealed class FishInventoryResolver
    {
        private IFishInventory _cached;

        /// <summary>
        /// The inventory, or null if the scene has none.
        /// </summary>
        /// <param name="serializedSource">
        /// The designer-assigned component, if any. Checked before the scene scan so an explicit
        /// choice always beats whatever the scan happens to find first — which matters in a scene
        /// holding both a real inventory and the test driver.
        /// </param>
        public IFishInventory Resolve(Object serializedSource)
        {
            if (_cached != null) return _cached;

            if (serializedSource is IFishInventory fromField)
            {
                _cached = fromField;
                return _cached;
            }

            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is IFishInventory inv)
                {
                    _cached = inv;
                    return _cached;
                }
            }

            return null;
        }
    }
}
