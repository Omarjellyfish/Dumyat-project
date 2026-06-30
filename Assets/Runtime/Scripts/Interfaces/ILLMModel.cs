using System.Threading.Tasks;

namespace DumyatRuntime
{
    /// <summary>
    /// A text-generating LLM model usable by <see cref="LLMManager"/>. The manager only needs raw
    /// text generation; prompt construction and structured-output parsing live above it (in
    /// <see cref="MerchantNegotiationLLM"/>). Concrete providers (e.g. Gemini) implement this.
    /// </summary>
    public interface ILLMModel : IAIModel
    {
        /// <summary>
        /// Generates a completion for <paramref name="prompt"/>. Returns the raw text, or null on
        /// failure so the manager can fall back to the next model in the pool.
        /// </summary>
        Task<string> GenerateTextAsync(string prompt);
    }
}
