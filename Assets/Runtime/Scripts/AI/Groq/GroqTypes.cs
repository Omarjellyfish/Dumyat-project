using System;
using System.Collections.Generic;

namespace DumyatRuntime
{
    // Serializable DTOs that mirror Groq's OpenAI-compatible Chat Completions REST API so they can be
    // (de)serialized with Unity's built-in JsonUtility. Unknown JSON fields are ignored on read.

    #region Request

    [Serializable]
    public class GroqRequest
    {
        public string model;
        public List<GroqMessage> messages = new List<GroqMessage>();
        public GroqResponseFormat response_format = new GroqResponseFormat();
    }

    [Serializable]
    public class GroqResponseFormat
    {
        // "text" (default) or "json_object" to force a single structured JSON object.
        public string type = "text";
    }

    [Serializable]
    public class GroqMessage
    {
        public string role;
        public string content;
    }

    #endregion

    #region Response

    [Serializable]
    public class GroqResponse
    {
        public List<GroqChoice> choices;
        public GroqError error;
    }

    [Serializable]
    public class GroqChoice
    {
        public GroqMessage message;
        public string finish_reason;
    }

    [Serializable]
    public class GroqError
    {
        public string message;
        public string type;
        public string code;
    }

    #endregion
}
