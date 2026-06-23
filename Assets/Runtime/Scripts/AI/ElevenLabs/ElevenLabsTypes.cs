using System;

namespace DumyatRuntime
{
    // Serializable DTOs that mirror the ElevenLabs text-to-speech REST API so they can be
    // serialized with Unity's built-in JsonUtility.

    [Serializable]
    internal class ElevenLabsTtsRequest
    {
        public string text;
        public string model_id;
        public ElevenLabsVoiceSettings voice_settings = new ElevenLabsVoiceSettings();
    }

    [Serializable]
    internal class ElevenLabsVoiceSettings
    {
        public float stability = 0.5f;
        public float similarity_boost = 0.75f;
        public float style = 0f;
        public float speed = 1f;
    }
}
