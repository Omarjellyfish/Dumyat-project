using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    // Serializable DTOs that mirror the shape of Google's Generative Language REST API so they can
    // be (de)serialized with Unity's built-in JsonUtility. Unknown JSON fields are ignored on read.

    #region Request

    [Serializable]
    public class GeminiRequest
    {
        public List<GeminiContent> contents = new List<GeminiContent>();
    }

    [Serializable]
    public class GeminiContent
    {
        public List<GeminiPart> parts = new List<GeminiPart>();
    }

    [Serializable]
    public class GeminiPart
    {
        public string text;
    }

    #endregion

    #region Response

    [Serializable]
    public class GeminiResponse
    {
        public List<GeminiCandidate> candidates;
        public GeminiPromptFeedback promptFeedback;
    }

    [Serializable]
    public class GeminiCandidate
    {
        public GeminiResponseContent content;
        public string finishReason;
    }

    [Serializable]
    public class GeminiResponseContent
    {
        public List<GeminiPart> parts;
        public string role;
    }

    [Serializable]
    public class GeminiPromptFeedback
    {
        public string blockReason;
    }

    #endregion
}
