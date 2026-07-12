using System.Threading.Tasks;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Manages the pool of <see cref="ILLMModel"/> providers and exposes a single text-generation
    /// API. Uses ACTIVE + AUTO-FALLBACK: it tries available models in descending priority order and
    /// returns the first non-empty result, so a misconfigured or failing provider transparently
    /// falls through to the next.
    /// </summary>
    public class LLMManager : AIModelManager<LLMManager, ILLMModel>, ILLMService
    {
        /// <summary>
        /// Generates text for <paramref name="prompt"/>, trying each available model in priority
        /// order until one succeeds. Returns null if every model is unavailable or fails.
        /// </summary>
        public async Task<string> GenerateTextAsync(string prompt, bool jsonObject = false)
        {
            foreach (ILLMModel model in AvailableModels())
            {
                string result = await model.GenerateTextAsync(prompt, jsonObject);
                if (!string.IsNullOrEmpty(result)) return result;
                Debug.LogWarning($"LLMManager: model '{model.ModelName}' returned no result; trying next.");
            }

            Debug.LogWarning("LLMManager: no LLM model produced a result.");
            return null;
        }
    }
}
