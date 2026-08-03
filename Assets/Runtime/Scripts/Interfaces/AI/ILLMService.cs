using System.Threading.Tasks;

namespace DumyatRuntime
{
    /// <summary>
    /// Capability contract for the pooled large-language-model service. Injected in place of the
    /// former <c>LLMManager.Singleton</c>. Backed by <see cref="LLMManager"/>, which owns the
    /// <see cref="ILLMModel"/> pool and cross-provider fallback.
    /// </summary>
    public interface ILLMService
    {
        /// <summary>True when at least one registered model reports itself available.</summary>
        bool HasAvailableModel { get; }

        /// <summary>
        /// Generates text for <paramref name="prompt"/>, trying each available model in priority
        /// order until one succeeds. Returns null if every model is unavailable or fails.
        /// </summary>
        Task<string> GenerateTextAsync(string prompt, bool jsonObject = false);
    }
}
