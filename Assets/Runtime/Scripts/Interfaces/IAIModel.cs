namespace DumyatRuntime
{
    /// <summary>
    /// Base contract every pluggable AI model (LLM / TTS / STT) exposes to its manager. Keeps the
    /// manager fully decoupled from concrete providers: it only needs to know a model's name, whether
    /// it can be used right now, and how it ranks against the other models in the pool. All
    /// provider-specific behaviour stays in the model's own file behind the specialised interfaces
    /// (<see cref="ILLMModel"/>, <see cref="ITTSModel"/>, <see cref="ISTTModel"/>).
    /// </summary>
    public interface IAIModel
    {
        /// <summary>Human-readable model name, for logging/diagnostics.</summary>
        string ModelName { get; }

        /// <summary>True when this model is configured and usable right now (e.g. has an API key).</summary>
        bool IsAvailable { get; }

        /// <summary>Selection priority within the pool — HIGHER is preferred; the manager tries
        /// available models in descending priority order.</summary>
        int Priority { get; }
    }
}
