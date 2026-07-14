using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "GroqConfig", menuName = "Dumyat/AI/Groq Config")]
    public class GroqConfig : ScriptableObject
    {
        [Header("Authentication")]
        [Tooltip("Groq API key used to authenticate requests (sent as a Bearer token).")]
        [SerializeField] private string _apiKey;

        [Header("Model")]
        [Tooltip("Groq model ID, e.g. 'llama-3.3-70b-versatile' or 'llama-3.1-8b-instant'.")]
        [SerializeField] private string _modelName = "llama-3.3-70b-versatile";

        public string ApiKey => _apiKey;
        public string ModelName => _modelName;
    }
}
