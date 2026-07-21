using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "GeminiConfig", menuName = "Dumyat/AI/Gemini Config")]
    public class GeminiConfig : ScriptableObject
    {
        [Header("Authentication")]
        [Tooltip("Google AI / Gemini API key used to authenticate requests.")]
        [SerializeField] private string _apiKey;

        [Header("Model")]
        [Tooltip("Gemini model name, e.g. 'gemini-2.0-flash'.")]
        [SerializeField] private string _modelName = "gemini-2.0-flash";

        public string ApiKey => _apiKey;
        public string ModelName => _modelName;
    }
}
