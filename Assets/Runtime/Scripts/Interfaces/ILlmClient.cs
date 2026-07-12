using System;

namespace DumyatRuntime
{
    /// <summary>
    /// Contract for a text-in / text-out large-language-model client. Implementations own the
    /// transport, timeout and retry details and report results through callbacks so callers stay
    /// decoupled from any specific provider (Gemini, etc.).
    /// </summary>
    public interface ILlmClient
    {
        /// <summary>
        /// Sends <paramref name="prompt"/> to the model. On success <paramref name="onSuccess"/>
        /// receives the generated text; if every attempt fails (after the built-in retries)
        /// <paramref name="onFailure"/> receives a human-readable fallback message instead.
        /// </summary>
        void SendPrompt(string prompt, Action<string> onSuccess, Action<string> onFailure = null);
    }
}
